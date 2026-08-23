using System.Diagnostics;
using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Windows.Hooks;

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
public sealed class MacroRecorder(IWindowManager windows, Func<nint, int> slotIndexOf) : IDisposable
{
    private readonly KeyboardHook _keyboard = new();
    private readonly MouseHook _mouse = new();
    private readonly List<MacroStep> _steps = [];
    private readonly Stopwatch _sinceLastStep = new();

    private nint _lastWindow;
    private bool _recording;

    /// <summary>Insérer les temps d'attente réels entre deux actions.</summary>
    public bool CaptureDelays { get; set; } = true;

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
        _lastWindow = windows.GetForegroundWindow();
        _sinceLastStep.Restart();
        _recording = true;

        // Le personnage de départ est noté explicitement : sans cette première étape,
        // le rejeu commencerait sur la fenêtre qui se trouve au premier plan ce jour-là.
        int startIndex = slotIndexOf(_lastWindow);
        if (startIndex >= 0) Add(new FocusStep { Target = FocusTarget.Slot, SlotIndex = startIndex });

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
        if (!_recording || !e.IsDown || e.IsInjected || e.Button is null) return false;

        // Le clic est rapporté à la fenêtre qui le reçoit, c'est-à-dire celle au premier plan.
        nint target = windows.GetForegroundWindow();

        // Sans ce filtre, le clic sur le bouton « Arrêter l'enregistrement » de
        // l'organizer serait lui aussi capturé et terminerait chaque macro enregistrée.
        if (slotIndexOf(target) < 0) return false;

        if (!windows.TryGetClientBounds(target, out var bounds) || bounds.IsEmpty) return false;

        TrackWindowChange();
        AddDelay();

        var normalized = CoordinateMapper.ToNormalized(e.Point, bounds);
        Add(new MouseClickStep { Fx = normalized.Fx, Fy = normalized.Fy, Button = e.Button.Value });
        return false;
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
