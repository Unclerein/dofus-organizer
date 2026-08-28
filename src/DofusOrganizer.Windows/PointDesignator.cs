using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Input;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Windows.Hooks;

namespace DofusOrganizer.Windows;

/// <summary>
/// Recueille des points désignés à la souris dans une fenêtre du jeu, sans que le jeu les voie.
///
/// C'est le tout de la différence avec l'enregistreur de macros, qui laisse les clics suivre
/// leur route : ici, cliquer un item de coffre ouvrirait une boîte de quantité à chaque
/// désignation, et il faudrait la refermer entre chaque. Les clics sont donc avalés — ils
/// servent à pointer, pas à jouer.
///
/// Classe à part plutôt qu'un second mode dans <see cref="MacroRecorder"/> : celui-ci fusionne
/// les clics en double-clics, retient les appuis pour reconnaître un glisser, mesure les temps
/// morts. Rien de tout cela n'a de sens pour désigner, et le mélange aurait donné deux logiques
/// entrelacées dans les mêmes méthodes.
/// </summary>
/// <param name="slotIndexOf">
/// Position du personnage occupant une fenêtre donnée, ou -1 si la fenêtre n'est pas un client
/// suivi. C'est ce qui laisse passer les clics destinés à l'organizer lui-même — sans quoi le
/// bouton « Terminer » serait inaccessible.
/// </param>
public sealed class PointDesignator(IWindowManager windows, Func<nint, int> slotIndexOf) : IDisposable
{
    private readonly MouseHook _mouse = new();
    private readonly List<NormalizedPoint> _points = [];
    private readonly DesignationSequencer _sequencer = new();

    private TaskCompletionSource<NormalizedPoint>? _capture;

    public bool IsDesignating => _sequencer.IsDesignating;

    public IReadOnlyList<NormalizedPoint> Points => _points;

    /// <summary>Levé à chaque point recueilli, pour que la liste se remplisse sous les yeux.</summary>
    public event Action<NormalizedPoint>? PointDesignated;

    public void Start()
    {
        if (_sequencer.IsDesignating) return;

        _points.Clear();
        Open(singleShot: false);
    }

    public IReadOnlyList<NormalizedPoint> Stop()
    {
        _sequencer.Close();

        // Une capture encore en attente est rompue plutôt qu'abandonnée : la laisser pendre
        // ferait patienter son demandeur jusqu'à l'expiration du délai, hook déjà retiré, sans
        // que rien ne puisse plus la servir.
        var capture = _capture;
        _capture = null;
        capture?.TrySetCanceled();

        _mouse.Uninstall();
        return _points.ToList();
    }

    /// <summary>
    /// Ouvre la désignation, ou se greffe sur celle qui court déjà.
    ///
    /// Reposer le hook alors qu'il est déjà en place le laisserait installé deux fois : le
    /// séquenceur sait dire si la désignation était déjà ouverte, et c'est lui qui tranche.
    /// </summary>
    private void Open(bool singleShot)
    {
        bool wasOpen = _sequencer.IsDesignating;
        _sequencer.Open(singleShot);
        if (wasOpen) return;

        _mouse.MouseEventReceived = OnMouse;
        _mouse.Install();
    }

    /// <summary>
    /// Recueille un seul point, puis s'arrête.
    ///
    /// Sert à renseigner la position d'une étape de macro depuis le jeu plutôt qu'en tapant des
    /// pourcentages — personne ne sait à quel pourcentage correspond une case d'inventaire.
    /// Même mécanique que la capture d'un raccourci dans <c>HotkeyDispatcher</c> : une promesse
    /// que le premier événement utile dénoue, et qu'une annulation rompt.
    /// </summary>
    public Task<NormalizedPoint> CaptureNextAsync(CancellationToken cancellationToken)
    {
        var capture = new TaskCompletionSource<NormalizedPoint>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => capture.TrySetCanceled());

        // La capture est posée avant d'ouvrir : le premier clic peut arriver aussitôt le hook
        // installé, et il ne doit pas trouver la place vide.
        _capture = capture;
        Open(singleShot: true);
        return capture.Task;
    }

    /// <summary>Interrompt une capture en cours, sans rien rendre.</summary>
    public void CancelCapture()
    {
        // Refermer rompt la promesse en attente : il n'y a rien de plus à faire ici.
        if (_sequencer.IsDesignating) Stop();
    }

    private bool OnMouse(MouseEvent e)
    {
        if (!_sequencer.IsDesignating || e.IsInjected) return false;

        // Seul le clic gauche désigne. Le reste passe : il faut pouvoir faire tourner la
        // molette pour atteindre un item plus bas dans le coffre.
        if (e.Button is not MouseButton.Left) return false;

        if (!e.IsDown)
        {
            // Le hook reste posé le temps d'avaler le relâchement qui suit un appui pris : le
            // retirer plus tôt laisserait le jeu recevoir un bouton relâché qu'il n'a jamais
            // vu s'enfoncer. Une capture posée entre-temps — le calibrage en enchaîne deux —
            // garde la désignation ouverte, et c'est le séquenceur qui le sait.
            var release = _sequencer.OnRelease(captureAwaiting: _capture is not null);
            if (release is DesignationVerdict.SwallowAndClose) Stop();

            return release is not DesignationVerdict.LetThrough;
        }

        var point = Locate(e.Point);
        if (_sequencer.OnPress(onTarget: point is not null) is DesignationVerdict.LetThrough) return false;

        // Une capture à l'unité se dénoue ici et rend la main à son demandeur, qui reprend
        // aussitôt : d'où la possibilité qu'une seconde capture soit posée avant même que le
        // bouton ne soit relâché.
        var capture = _capture;
        if (capture is not null)
        {
            _capture = null;
            capture.TrySetResult(point!.Value);
            return true;
        }

        _points.Add(point!.Value);
        PointDesignated?.Invoke(point.Value);
        return true;
    }

    /// <summary>
    /// Ramène un point d'écran aux proportions de la fenêtre cliquée, ou rien si ce n'est pas
    /// une fenêtre suivie.
    ///
    /// La fenêtre sous le curseur, et non celle au premier plan : le hook se déclenche avant que
    /// le focus ne change, donc cliquer sur un client pour l'activer rapporterait le point aux
    /// dimensions du client précédent.
    /// </summary>
    private NormalizedPoint? Locate(ScreenPoint screen)
    {
        nint target = windows.WindowUnder(screen);
        if (slotIndexOf(target) < 0) return null;
        if (!windows.TryGetClientBounds(target, out var bounds) || bounds.IsEmpty) return null;

        return CoordinateMapper.ToNormalized(screen, bounds);
    }

    public void Dispose() => _mouse.Dispose();
}
