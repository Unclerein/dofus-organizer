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
[JsonDerivedType(typeof(DistributeQuantityStep), "distribute")]
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

/// <summary>
/// Lit la quantité que le jeu propose dans une boîte de saisie, la divise, et pose le résultat
/// à sa place.
///
/// C'est la seule étape qui lit quelque chose : toutes les autres injectent à l'aveugle. Elle
/// passe par le presse-papiers, seul canal par lequel un nombre affiché par le jeu peut
/// revenir jusqu'ici — Ctrl+C pour lire, Ctrl+V pour écrire.
///
/// Coller plutôt que taper les chiffres n'est pas un raccourci de paresse. Les codes virtuels
/// des chiffres désignent une touche et non un caractère : sur un clavier AZERTY, la rangée du
/// haut donne « &amp; » là où un clavier US donne « 1 ». Le presse-papiers ignore la disposition,
/// et c'est de toute façon le canal dont l'étape dépend déjà pour lire.
/// </summary>
public sealed class DistributeQuantityStep : MacroStep
{
    /// <summary>Un par personnage d'une équipe ordinaire.</summary>
    public const int DefaultDivisor = 4;

    /// <summary>Le temps qu'une boîte de quantité met à s'ouvrir, sur un client ordinaire.</summary>
    public const int DefaultOpenDelayMs = 100;

    /// <summary>Le temps que le jeu met à déplacer les items et à refermer la boîte.</summary>
    public const int DefaultTransferDelayMs = 100;

    private int _divisor = DefaultDivisor;
    private int _openDelayMs = DefaultOpenDelayMs;
    private int _transferDelayMs = DefaultTransferDelayMs;

    /// <summary>
    /// Par combien diviser la quantité lue.
    ///
    /// Le même nombre pour tous les personnages : ce que le premier prend, le dernier le prend
    /// aussi. La division étant entière, ce qui ne tombe pas juste reste au coffre — un ou deux
    /// items oubliés valent mieux qu'un nombre qui change d'un personnage à l'autre sans être
    /// écrit nulle part.
    ///
    /// Jamais zéro : diviser par rien n'est pas une intention exprimable.
    /// </summary>
    public int Divisor
    {
        get => _divisor;
        set { if (Set(ref _divisor, Math.Max(1, value))) RaiseDescription(); }
    }

    /// <summary>
    /// Attente avant de copier, le temps que la boîte de quantité s'ouvre.
    ///
    /// Sans elle le Ctrl+C part avant que la boîte n'existe : il ne copie rien, et l'étape
    /// s'arrête à juste titre plutôt que de coller un nombre venu d'ailleurs. C'est ce qui
    /// faisait échouer une répartition au premier item.
    ///
    /// Portée par l'étape et non par une attente voisine : elle fait partie du geste que
    /// l'étape exécute d'un bloc, et une étape d'attente séparée se supprimerait par mégarde
    /// en cassant celle qui la suit. Réglable, parce que cent millisecondes sont une
    /// supposition sur la vitesse d'un client.
    /// </summary>
    public int OpenDelayMs
    {
        get => _openDelayMs;
        set => Set(ref _openDelayMs, Math.Clamp(value, 0, 5000));
    }

    /// <summary>
    /// Attente après la validation, le temps que le transfert soit enregistré.
    ///
    /// Le pendant de <see cref="OpenDelayMs"/>, à l'autre bout du geste : sans elle, la macro
    /// attrape l'item suivant pendant que le jeu déplace encore le précédent, et le glisser
    /// part dans une fenêtre qui n'est plus dans l'état attendu.
    /// </summary>
    public int TransferDelayMs
    {
        get => _transferDelayMs;
        set => Set(ref _transferDelayMs, Math.Clamp(value, 0, 5000));
    }

    public override string Description => $"Répartir la quantité proposée (diviser par {Divisor})";
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
