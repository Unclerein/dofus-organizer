using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.Json.Serialization;
using DofusOrganizer.Core.Geometry;

namespace DofusOrganizer.Core.Models;

public enum MouseButton { Left, Right, Middle }

public enum KeyAction { Press, Down, Up }

/// <summary>Ce que vise une étape de focus.</summary>
public enum FocusTarget
{
    /// <summary>Le personnage à la position <see cref="FocusStep.SlotIndex"/> dans la liste.</summary>
    Slot,
    Next,
    Previous,
    First,
    /// <summary>La fenêtre qui avait le focus au lancement de la macro.</summary>
    Initial,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FocusStep), "focus")]
[JsonDerivedType(typeof(ForEachCharacterStep), "foreach")]
[JsonDerivedType(typeof(MouseClickStep), "click")]
[JsonDerivedType(typeof(MouseMoveStep), "move")]
[JsonDerivedType(typeof(KeyStep), "key")]
[JsonDerivedType(typeof(DelayStep), "delay")]
[JsonDerivedType(typeof(ScrollStep), "scroll")]
[JsonDerivedType(typeof(MouseDragStep), "drag")]
public abstract class MacroStep : NotifyBase
{
    /// <summary>Libellé affiché dans la liste d'étapes de l'éditeur.</summary>
    [JsonIgnore]
    public abstract string Description { get; }

    /// <summary>À rappeler quand une propriété change pour rafraîchir le libellé affiché.</summary>
    protected void RaiseDescription() => Raise(nameof(Description));
}

public sealed class FocusStep : MacroStep
{
    private FocusTarget _target = FocusTarget.Next;
    private int _slotIndex;

    public FocusTarget Target
    {
        get => _target;
        set { if (Set(ref _target, value)) RaiseDescription(); }
    }

    /// <summary>Position (base 0) du personnage visé quand <see cref="Target"/> vaut <see cref="FocusTarget.Slot"/>.</summary>
    public int SlotIndex
    {
        get => _slotIndex;
        set { if (Set(ref _slotIndex, value)) RaiseDescription(); }
    }

    public override string Description => Target switch
    {
        FocusTarget.Slot => $"Aller sur le personnage {SlotIndex + 1}",
        FocusTarget.Next => "Aller sur le personnage suivant",
        FocusTarget.Previous => "Aller sur le personnage précédent",
        FocusTarget.First => "Aller sur le premier personnage",
        _ => "Revenir sur la fenêtre de départ",
    };
}

/// <summary>
/// Répète ses sous-étapes sur chaque personnage présent, dans l'ordre de la liste,
/// en donnant le focus à chacun avant de commencer. C'est l'étape qui permet
/// d'écrire « soigner toute l'équipe » une seule fois au lieu d'une fois par personnage.
/// </summary>
public sealed class ForEachCharacterStep : MacroStep
{
    private bool _skipCurrentWindow;
    private ObservableCollection<MacroStep> _steps = [];

    public ForEachCharacterStep() => Watch(_steps);

    /// <summary>
    /// Collection observable et non simple liste : l'éditeur affiche les sous-étapes
    /// en retrait sous la boucle, et doit voir un enregistrement les remplacer.
    /// </summary>
    public ObservableCollection<MacroStep> Steps
    {
        get => _steps;
        set
        {
            _steps.CollectionChanged -= OnStepsChanged;
            _steps = value ?? [];
            Watch(_steps);
            RaiseDescription();
        }
    }

    private void Watch(ObservableCollection<MacroStep> steps) => steps.CollectionChanged += OnStepsChanged;

    private void OnStepsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RaiseDescription();

    /// <summary>Ignorer le personnage qui avait déjà le focus au moment du déclenchement.</summary>
    public bool SkipCurrentWindow
    {
        get => _skipCurrentWindow;
        set { if (Set(ref _skipCurrentWindow, value)) RaiseDescription(); }
    }

    public override string Description =>
        $"Pour chaque personnage{(SkipCurrentWindow ? " (sauf l'actuel)" : "")} : {Steps.Count} étape(s)";
}

/// <summary>Base des étapes visant un point de la zone client de la fenêtre courante.</summary>
public abstract class PointerStep : MacroStep
{
    private double _fx = 0.5;
    private double _fy = 0.5;

    /// <summary>Position horizontale, en fraction de la largeur de la zone client (0 = bord gauche, 1 = bord droit).</summary>
    public double Fx
    {
        get => _fx;
        set { if (Set(ref _fx, value)) RaiseDescription(); }
    }

    /// <summary>Position verticale, en fraction de la hauteur de la zone client.</summary>
    public double Fy
    {
        get => _fy;
        set { if (Set(ref _fy, value)) RaiseDescription(); }
    }

    [JsonIgnore]
    public NormalizedPoint Point => new(Fx, Fy);

    protected string PositionText => $"{Fx * 100:0.#} % / {Fy * 100:0.#} %";
}

public sealed class MouseClickStep : PointerStep
{
    private MouseButton _button = MouseButton.Left;
    private int _clicks = 1;

    public MouseButton Button
    {
        get => _button;
        set { if (Set(ref _button, value)) RaiseDescription(); }
    }

    public int Clicks
    {
        get => _clicks;
        set { if (Set(ref _clicks, Math.Clamp(value, 1, 3))) RaiseDescription(); }
    }

    public override string Description
    {
        get
        {
            string button = Button switch
            {
                MouseButton.Right => "Clic droit",
                MouseButton.Middle => "Clic milieu",
                _ => "Clic gauche",
            };
            string repeat = Clicks > 1 ? $" ×{Clicks}" : "";
            return $"{button}{repeat} à {PositionText}";
        }
    }
}

public sealed class MouseMoveStep : PointerStep
{
    public override string Description => $"Déplacer la souris à {PositionText}";
}

/// <summary>
/// Saisit un point, déplace en maintenant le bouton, puis relâche.
///
/// Sert à déplacer un panneau dessiné par le jeu, que le système ne connaît pas comme une
/// fenêtre et ne peut donc pas déplacer lui-même. Les deux points étant fixes, le panneau
/// atterrit au même endroit chez tous les personnages — à condition qu'il se soit ouvert au
/// même endroit chez chacun.
/// </summary>
public sealed class MouseDragStep : PointerStep
{
    private double _toFx = 0.5;
    private double _toFy = 0.5;
    private MouseButton _button = MouseButton.Left;
    private int _intermediateMoves = 8;

    /// <summary>Position horizontale d'arrivée, en fraction de la largeur de la zone client.</summary>
    public double ToFx
    {
        get => _toFx;
        set { if (Set(ref _toFx, value)) RaiseDescription(); }
    }

    /// <summary>Position verticale d'arrivée, en fraction de la hauteur de la zone client.</summary>
    public double ToFy
    {
        get => _toFy;
        set { if (Set(ref _toFy, value)) RaiseDescription(); }
    }

    [JsonIgnore]
    public NormalizedPoint Destination => new(ToFx, ToFy);

    public MouseButton Button
    {
        get => _button;
        set { if (Set(ref _button, value)) RaiseDescription(); }
    }

    /// <summary>
    /// Nombre de positions traversées entre le départ et l'arrivée. Un saut direct est souvent
    /// ignoré : beaucoup d'interfaces n'entament un déplacement qu'en voyant le curseur bouger.
    /// </summary>
    public int IntermediateMoves
    {
        get => _intermediateMoves;
        set { if (Set(ref _intermediateMoves, Math.Clamp(value, 0, 60))) RaiseDescription(); }
    }

    public override string Description
    {
        get => $"Glisser depuis {PositionText} jusqu'à {ToFx * 100:0.#} % / {ToFy * 100:0.#} %";
    }
}

public sealed class KeyStep : MacroStep
{
    private int _virtualKey = VirtualKeys.F1;
    private KeyModifiers _modifiers = KeyModifiers.None;
    private KeyAction _action = KeyAction.Press;

    public int VirtualKey
    {
        get => _virtualKey;
        set { if (Set(ref _virtualKey, value)) RaiseDescription(); }
    }

    public KeyModifiers Modifiers
    {
        get => _modifiers;
        set { if (Set(ref _modifiers, value)) RaiseDescription(); }
    }

    public KeyAction Action
    {
        get => _action;
        set { if (Set(ref _action, value)) RaiseDescription(); }
    }

    public override string Description
    {
        get
        {
            string verb = Action switch
            {
                KeyAction.Down => "Enfoncer",
                KeyAction.Up => "Relâcher",
                _ => "Touche",
            };
            return $"{verb} {new Hotkey(VirtualKey, Modifiers)}";
        }
    }
}

public sealed class DelayStep : MacroStep
{
    private int _milliseconds = 200;

    public int Milliseconds
    {
        get => _milliseconds;
        set { if (Set(ref _milliseconds, Math.Max(0, value))) RaiseDescription(); }
    }

    public override string Description => $"Attendre {Milliseconds} ms";
}

public enum ScrollDirection { Up, Down }

/// <summary>Molette, nécessaire pour parcourir une liste avant d'y cliquer.</summary>
public sealed class ScrollStep : PointerStep
{
    private ScrollDirection _direction = ScrollDirection.Down;
    private int _notches = 3;

    public ScrollDirection Direction
    {
        get => _direction;
        set { if (Set(ref _direction, value)) RaiseDescription(); }
    }

    /// <summary>Nombre de crans de molette.</summary>
    public int Notches
    {
        get => _notches;
        set { if (Set(ref _notches, Math.Clamp(value, 1, 50))) RaiseDescription(); }
    }

    public override string Description =>
        $"Molette {(Direction == ScrollDirection.Up ? "vers le haut" : "vers le bas")} ×{Notches} à {PositionText}";
}
