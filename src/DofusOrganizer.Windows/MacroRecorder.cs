using System.Diagnostics;
using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Windows.Hooks;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows;

/// <summary>
/// Capture les clics et les frappes réels pour les transformer en étapes de macro.
///
/// Chaque clic est converti en position relative à la zone client de la fenêtre Dofus
/// visée, et non en pixels écran : c'est ce qui rend l'enregistrement rejouable après
/// un déplacement ou un redimensionnement des fenêtres.
/// </summary>
/// <param name="slotIndexOf">
/// Position du personnage occupant une fenêtre donnée, ou -1 si la fenêtre n'est pas un client suivi.
/// </param>
/// <param name="isRecordingToggle">
/// Reconnaît la combinaison qui pilote l'enregistrement, pour ne pas la capturer.
/// </param>
public sealed class MacroRecorder(
    IWindowManager windows,
    Func<nint, int> slotIndexOf,
    Func<int, KeyModifiers, bool> isRecordingToggle) : IDisposable
{
    private readonly KeyboardHook _keyboard = new();
    private readonly MouseHook _mouse = new();
    private readonly List<MacroStep> _steps = [];
    private readonly Stopwatch _sinceLastStep = new();

    private nint _lastWindow;
    private bool _recording;

    /// <summary>Dernier appui enregistré, le temps de savoir si le suivant le prolonge.</summary>
    private RecordedClick? _lastClick;

    /// <summary>Étape produite par cet appui, à compléter au relâchement si c'était un glisser.</summary>
    private MouseClickStep? _pendingClick;
    private ScreenPoint _pressedAt;
    private ClientBounds _pressedIn;

    /// <summary>
    /// Insérer les temps d'attente réels entre deux actions. Désactivé par défaut : les
    /// hésitations humaines encombrent la macro sans rien lui apporter, et l'attente juste
    /// est celle sur image.
    /// </summary>
    public bool CaptureDelays { get; set; }

    /// <summary>
    /// Capturer le fragment d'écran autour de chaque clic, pour que le rejeu retrouve la
    /// cible même si elle a bougé chez un autre personnage.
    /// </summary>
    public bool AnchorClicks { get; set; } = true;

    /// <summary>Largeur du fragment capturé autour d'un clic.</summary>
    public int AnchorPatchWidth { get; set; } = 160;

    /// <summary>Hauteur du fragment capturé autour d'un clic.</summary>
    public int AnchorPatchHeight { get; set; } = 48;

    /// <summary>
    /// Transformer les changements de fenêtre en étapes de focus. À laisser faux pour une
    /// capture destinée au rejeu sur l'équipe : une telle étape, enfermée dans une boucle
    /// par personnage, ramènerait chaque tour sur le personnage enregistré.
    /// </summary>
    public bool RecordWindowChanges { get; set; } = true;

    /// <summary>
    /// Ressemblance exigée au rejeu. Un peu plus tolérante que le réglage par défaut de la
    /// reconnaissance : au moment du clic l'élément est survolé, donc souvent surligné, ce
    /// qu'il ne sera pas quand on le cherchera chez le personnage suivant.
    /// </summary>
    public double AnchorMinimumScore { get; set; } = 0.85;

    /// <summary>Attentes plus courtes que ce seuil : elles n'apportent rien et alourdissent la macro.</summary>
    public int MinimumDelayMs { get; set; } = 60;

    /// <summary>Attentes plus longues que ce plafond : personne ne veut rejouer une pause de 40 secondes.</summary>
    public int MaximumDelayMs { get; set; } = 3000;

    public bool IsRecording => _recording;

    public IReadOnlyList<MacroStep> Steps => _steps;

    /// <summary>Levé à chaque étape capturée, pour que la liste se remplisse sous les yeux de l'utilisateur.</summary>
    public event Action<MacroStep>? StepRecorded;

    public void Start()
    {
        if (_recording) return;

        _steps.Clear();
        _lastClick = null;
        _pendingClick = null;
        _lastWindow = windows.GetForegroundWindow();
        _sinceLastStep.Restart();
        _recording = true;

        // Le personnage de départ est noté explicitement : sans cette première étape, une
        // macro rejouée plus tard commencerait sur la fenêtre au premier plan ce jour-là.
        if (RecordWindowChanges)
        {
            int startIndex = slotIndexOf(_lastWindow);
            if (startIndex >= 0) Add(new FocusStep { Target = FocusTarget.Slot, SlotIndex = startIndex });
        }

        _keyboard.KeyEventReceived = OnKey;
        _mouse.MouseEventReceived = OnMouse;
        _keyboard.Install();
        _mouse.Install();
    }

    /// <summary>Arrête la capture et rend les étapes enregistrées.</summary>
    public IReadOnlyList<MacroStep> Stop()
    {
        _recording = false;
        _keyboard.Uninstall();
        _mouse.Uninstall();
        _sinceLastStep.Stop();
        return _steps.ToList();
    }

    private bool OnKey(KeyEvent e)
    {
        // On n'enregistre que les appuis réels de l'utilisateur, jamais nos propres injections.
        if (!_recording || !e.IsDown || e.IsInjected) return false;
        if (VirtualKeys.IsModifierKey(e.VirtualKey)) return false;

        // La touche qui arrête la capture ne doit pas en faire partie.
        if (isRecordingToggle(e.VirtualKey, e.Modifiers)) return false;

        // Seules les frappes destinées à un client Dofus font partie de la macro :
        // ce qu'on tape dans un navigateur ou dans l'organizer lui-même n'a rien à y faire.
        if (slotIndexOf(windows.GetForegroundWindow()) < 0) return false;

        TrackWindowChange();
        AddDelay();
        Add(new KeyStep { VirtualKey = e.VirtualKey, Modifiers = e.Modifiers });
        return false;
    }

    private bool OnMouse(MouseEvent e)
    {
        if (!_recording || e.IsInjected) return false;

        if (e.WheelNotches != 0) return OnWheel(e);
        if (e.Button is null) return false;
        if (!e.IsDown) return OnRelease(e);

        // Le clic est rapporté à la fenêtre réellement située sous le curseur, et non à
        // celle au premier plan : le hook se déclenche avant que le focus ne change, donc
        // cliquer sur un client pour l'activer rapporterait le clic aux dimensions du
        // client précédent.
        nint target = windows.WindowUnder(e.Point);

        // Et seules les fenêtres suivies comptent : un clic dans l'organizer ou ailleurs
        // n'a rien à faire dans une macro.
        if (slotIndexOf(target) < 0) return false;

        if (!windows.TryGetClientBounds(target, out var bounds) || bounds.IsEmpty) return false;

        TrackWindowChange();
        AddDelay();

        long now = Environment.TickCount64;

        // Un double-clic arrive comme deux appuis distincts. Non reconnu, il devient deux
        // étapes que le rejeu espace du délai configuré — bien au-delà du seuil de Windows —
        // et le jeu ne voit alors que deux clics isolés.
        if (_pendingClick is null
            && _steps.Count > 0 && _steps[^1] is MouseClickStep previous
            && ClickMerging.ContinuesClick(_lastClick, e.Button.Value, e.Point, now, previous.Clicks, Thresholds()))
        {
            previous.Clicks++;
            _lastClick = new RecordedClick(e.Button.Value, e.Point, now);
            RememberPress(previous, e.Point, bounds);
            StepRecorded?.Invoke(previous);
            return false;
        }

        var normalized = CoordinateMapper.ToNormalized(e.Point, bounds);
        var step = new MouseClickStep
        {
            Fx = normalized.Fx,
            Fy = normalized.Fy,
            Button = e.Button.Value,
            Anchor = AnchorClicks ? CaptureAnchor(e.Point, bounds) : null,
        };

        _lastClick = new RecordedClick(e.Button.Value, e.Point, now);
        RememberPress(step, e.Point, bounds);
        Add(step);
        return false;
    }

    private void RememberPress(MouseClickStep step, ScreenPoint point, ClientBounds bounds)
    {
        _pendingClick = step;
        _pressedAt = point;
        _pressedIn = bounds;
    }

    /// <summary>
    /// Au relâchement, un curseur qui s'est déplacé signe un glisser et non un clic :
    /// l'étape déjà ajoutée est remplacée. C'est ce qui permet de capturer le déplacement
    /// d'un panneau que le système ne connaît pas comme une fenêtre.
    /// </summary>
    private bool OnRelease(MouseEvent e)
    {
        var pending = _pendingClick;
        _pendingClick = null;

        if (pending is null || e.Button != pending.Button) return false;
        if (!ClickMerging.IsDrag(_pressedAt, e.Point, Thresholds())) return false;

        int index = _steps.IndexOf(pending);
        if (index < 0) return false;

        var destination = CoordinateMapper.ToNormalized(e.Point, _pressedIn);
        var drag = new MouseDragStep
        {
            Fx = pending.Fx,
            Fy = pending.Fy,
            ToFx = destination.Fx,
            ToFy = destination.Fy,
            Button = pending.Button,
            Anchor = pending.Anchor,
        };

        // Le panneau saisi peut s'ouvrir loin de là chez un autre personnage : la recherche
        // doit porter bien plus large que pour un clic sur un élément à sa place habituelle.
        if (drag.Anchor is not null) drag.Anchor.SearchRadius = DragSearchRadius;

        _steps[index] = drag;

        // Un glisser n'est pas un appui susceptible d'être prolongé en double-clic.
        _lastClick = null;

        StepRecorded?.Invoke(drag);
        return false;
    }

    /// <summary>Rayon de recherche donné à l'ancrage d'un glisser.</summary>
    private const int DragSearchRadius = 500;

    /// <summary>
    /// Seuils de double-clic et de glisser tels que l'utilisateur les a réglés. Les figer
    /// produirait une capture qui ne correspond pas à sa façon de cliquer.
    /// </summary>
    private static InputThresholds Thresholds() => new(
        (int)GetDoubleClickTime(),
        GetSystemMetrics(SM_CXDOUBLECLK),
        GetSystemMetrics(SM_CYDOUBLECLK),
        GetSystemMetrics(SM_CXDRAG),
        GetSystemMetrics(SM_CYDRAG));

    /// <summary>
    /// Relève le fragment d'écran entourant le point cliqué. La capture a lieu au moment de
    /// l'appui, donc avant que le jeu ne réagisse : c'est bien la cible qui est photographiée,
    /// pas ce qu'elle devient une fois cliquée.
    /// </summary>
    private ImageAnchor? CaptureAnchor(ScreenPoint point, ClientBounds bounds)
    {
        var area = AnchorArea(point, bounds, AnchorPatchWidth, AnchorPatchHeight);
        if (area.IsEmpty) return null;

        var patch = windows.CaptureScreen(area);
        if (patch is null) return null;

        var anchor = ImageAnchor.FromPixelBuffer(patch, point.X - area.X, point.Y - area.Y);
        anchor.MinimumScore = AnchorMinimumScore;
        return anchor;
    }

    private bool OnWheel(MouseEvent e)
    {
        nint target = windows.WindowUnder(e.Point);
        if (slotIndexOf(target) < 0) return false;
        if (!windows.TryGetClientBounds(target, out var bounds) || bounds.IsEmpty) return false;

        TrackWindowChange();
        AddDelay();

        var normalized = CoordinateMapper.ToNormalized(e.Point, bounds);
        Add(new ScrollStep
        {
            Fx = normalized.Fx,
            Fy = normalized.Fy,
            Direction = e.WheelNotches > 0 ? ScrollDirection.Up : ScrollDirection.Down,
            Notches = Math.Abs(e.WheelNotches),
        });
        return false;
    }

    /// <summary>
    /// Rectangle du fragment à capturer : large et court, à la forme d'une ligne d'interface.
    /// Un carré étroit centré sur un clic peut ne contenir que quelques caractères, voire du
    /// fond vide si le clic tombe après la fin d'un libellé court — et ressemble alors à
    /// toutes les autres lignes.
    /// </summary>
    public static ScreenRect AnchorArea(ScreenPoint point, ClientBounds bounds, int width, int height)
    {
        int left = Math.Max(bounds.Origin.X, point.X - Math.Max(8, width / 2));
        int top = Math.Max(bounds.Origin.Y, point.Y - Math.Max(8, height / 2));
        int right = Math.Min(bounds.Origin.X + bounds.Width, point.X + Math.Max(8, width / 2));
        int bottom = Math.Min(bounds.Origin.Y + bounds.Height, point.Y + Math.Max(8, height / 2));

        return new ScreenRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>
    /// Un changement de fenêtre pendant l'enregistrement devient une étape de focus,
    /// pour que le rejeu reproduise le même parcours entre les personnages.
    /// </summary>
    private void TrackWindowChange()
    {
        nint current = windows.GetForegroundWindow();
        if (current == _lastWindow) return;
        _lastWindow = current;

        if (!RecordWindowChanges) return;

        int index = slotIndexOf(current);
        if (index < 0) return;

        AddDelay();
        Add(new FocusStep { Target = FocusTarget.Slot, SlotIndex = index });
    }

    private void AddDelay()
    {
        if (!CaptureDelays) { _sinceLastStep.Restart(); return; }

        int elapsed = (int)_sinceLastStep.ElapsedMilliseconds;
        _sinceLastStep.Restart();

        if (elapsed < MinimumDelayMs) return;
        Add(new DelayStep { Milliseconds = Math.Min(elapsed, MaximumDelayMs) });
    }

    private void Add(MacroStep step)
    {
        _steps.Add(step);
        StepRecorded?.Invoke(step);
    }

    public void Dispose()
    {
        _keyboard.Dispose();
        _mouse.Dispose();
    }
}
