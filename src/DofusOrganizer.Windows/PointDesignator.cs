using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
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

    private bool _designating;
    private bool _swallowedPress;

    public bool IsDesignating => _designating;

    public IReadOnlyList<NormalizedPoint> Points => _points;

    /// <summary>Levé à chaque point recueilli, pour que la liste se remplisse sous les yeux.</summary>
    public event Action<NormalizedPoint>? PointDesignated;

    public void Start()
    {
        if (_designating) return;

        _points.Clear();
        _swallowedPress = false;
        _designating = true;

        _mouse.MouseEventReceived = OnMouse;
        _mouse.Install();
    }

    public IReadOnlyList<NormalizedPoint> Stop()
    {
        _designating = false;
        _mouse.Uninstall();
        return _points.ToList();
    }

    private bool OnMouse(MouseEvent e)
    {
        if (!_designating || e.IsInjected) return false;

        // Seul le clic gauche désigne. Le reste passe : il faut pouvoir faire tourner la
        // molette pour atteindre un item plus bas dans le coffre.
        if (e.Button is not MouseButton.Left) return false;

        if (!e.IsDown)
        {
            // Le relâchement suit le sort de son appui. Avaler l'un sans l'autre laisserait
            // le jeu croire à un bouton resté enfoncé.
            bool swallow = _swallowedPress;
            _swallowedPress = false;
            return swallow;
        }

        // La fenêtre sous le curseur, et non celle au premier plan : le hook se déclenche
        // avant que le focus ne change, donc cliquer sur un client pour l'activer rapporterait
        // le point aux dimensions du client précédent.
        nint target = windows.WindowUnder(e.Point);
        if (slotIndexOf(target) < 0) return false;
        if (!windows.TryGetClientBounds(target, out var bounds) || bounds.IsEmpty) return false;

        var point = CoordinateMapper.ToNormalized(e.Point, bounds);
        _points.Add(point);
        PointDesignated?.Invoke(point);

        _swallowedPress = true;
        return true;
    }

    public void Dispose() => _mouse.Dispose();
}
