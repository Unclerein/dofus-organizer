namespace DofusOrganizer.Core.Models;

public sealed class AppSettings : NotifyBase
{
    private string _titlePattern = DefaultTitlePattern;
    private string _windowClassFilter = "";
    private Hotkey? _nextCharacterHotkey;
    private Hotkey? _previousCharacterHotkey;
    private Hotkey? _panicHotkey = new(VirtualKeys.Pause);
    private Hotkey? _toggleRecordingHotkey;
    private Hotkey? _repeatOnTeamHotkey;
    private int _teamReplayDelayMs = 600;
    private bool _recordDelays;
    private bool _recordingFeedbackSound = true;
    private bool _hotkeysOnlyWhenGameFocused = true;
    private bool _swallowBoundKeys = true;
    private int _focusSettleDelayMs = 120;
    private int _actionDelayMs = 30;
    private int _multiClickIntervalMs = 80;
    private int _scrollDelayMs = 40;
    private bool _useScanCodes = true;

    /// <summary>
    /// Motif d'extraction du nom de personnage depuis le titre de la fenêtre.
    /// Le groupe nommé « name » est celui qui est retenu. Réglable car le format
    /// des titres de Dofus varie d'une version à l'autre.
    /// </summary>
    public const string DefaultTitlePattern = @"^(?<name>.+?)\s*-\s*Dofus";

    /// <summary>Noms de processus considérés comme des clients Dofus (sans .exe, comparaison insensible à la casse).</summary>
    public List<string> ProcessNames { get; set; } = ["Dofus"];

    public string TitlePattern
    {
        get => _titlePattern;
        set => Set(ref _titlePattern, value);
    }

    /// <summary>Classe de fenêtre exigée, vide pour ne pas filtrer. Les clients Unity utilisent « UnityWndClass ».</summary>
    public string WindowClassFilter
    {
        get => _windowClassFilter;
        set => Set(ref _windowClassFilter, value ?? "");
    }

    public Hotkey? NextCharacterHotkey
    {
        get => _nextCharacterHotkey;
        set => Set(ref _nextCharacterHotkey, value);
    }

    public Hotkey? PreviousCharacterHotkey
    {
        get => _previousCharacterHotkey;
        set => Set(ref _previousCharacterHotkey, value);
    }

    /// <summary>Touche d'arrêt d'urgence, active même hors du jeu pour pouvoir toujours reprendre la main.</summary>
    public Hotkey? PanicHotkey
    {
        get => _panicHotkey;
        set => Set(ref _panicHotkey, value);
    }

    /// <summary>
    /// Démarre et arrête l'enregistrement d'une macro sans repasser par la fenêtre de
    /// l'organizer. Sans valeur par défaut : en imposer une risquerait de voler une
    /// touche utilisée en jeu.
    /// </summary>
    public Hotkey? ToggleRecordingHotkey
    {
        get => _toggleRecordingHotkey;
        set => Set(ref _toggleRecordingHotkey, value);
    }

    /// <summary>
    /// Capture une séquence sur le personnage meneur puis la rejoue immédiatement sur tous
    /// les autres. C'est la réponse à « les autres font la même chose que moi » sans avoir
    /// à écrire une macro par destination.
    /// </summary>
    public Hotkey? RepeatOnTeamHotkey
    {
        get => _repeatOnTeamHotkey;
        set => Set(ref _repeatOnTeamHotkey, value);
    }

    /// <summary>
    /// Délai entre deux actions lors d'un rejeu sur l'équipe. Distinct du délai ordinaire :
    /// les quelques dizaines de millisecondes qui enchaînent des clics de sort ne laissent
    /// pas à une carte ou à une liste le temps de s'ouvrir.
    /// </summary>
    public int TeamReplayDelayMs
    {
        get => _teamReplayDelayMs;
        set => Set(ref _teamReplayDelayMs, Math.Clamp(value, 0, 10000));
    }

    /// <summary>
    /// Transformer les temps morts de l'enregistrement en étapes d'attente. Décoché par
    /// défaut : les hésitations humaines encombrent la macro, et l'attente juste est celle
    /// sur image.
    /// </summary>
    public bool RecordDelays
    {
        get => _recordDelays;
        set => Set(ref _recordDelays, value);
    }

    /// <summary>
    /// Émettre un bip au début et à la fin d'un enregistrement. En plein écran, c'est le
    /// seul moyen de savoir que la capture tourne.
    /// </summary>
    public bool RecordingFeedbackSound
    {
        get => _recordingFeedbackSound;
        set => Set(ref _recordingFeedbackSound, value);
    }

    /// <summary>N'activer les raccourcis que lorsqu'une fenêtre Dofus est au premier plan.</summary>
    public bool HotkeysOnlyWhenGameFocused
    {
        get => _hotkeysOnlyWhenGameFocused;
        set => Set(ref _hotkeysOnlyWhenGameFocused, value);
    }

    /// <summary>Empêcher la touche déclenchée d'atteindre le jeu. À décocher si un raccourci doit aussi agir en jeu.</summary>
    public bool SwallowBoundKeys
    {
        get => _swallowBoundKeys;
        set => Set(ref _swallowBoundKeys, value);
    }

    /// <summary>Pause après un changement de fenêtre : le client a besoin de quelques frames avant d'accepter un clic.</summary>
    public int FocusSettleDelayMs
    {
        get => _focusSettleDelayMs;
        set => Set(ref _focusSettleDelayMs, Math.Clamp(value, 0, 5000));
    }

    /// <summary>Pause entre deux actions d'une macro.</summary>
    public int ActionDelayMs
    {
        get => _actionDelayMs;
        set => Set(ref _actionDelayMs, Math.Clamp(value, 0, 5000));
    }

    /// <summary>
    /// Touches dont l'effet tarde, avec l'attente à leur accorder en plus de la pause ordinaire.
    ///
    /// Vide par défaut : personne ne subit un ralentissement qu'il n'a pas demandé. On y ajoute
    /// typiquement la touche du havre-sac, dont le panneau met parfois un moment à s'ouvrir.
    /// </summary>
    public List<SlowKey> SlowKeys { get; set; } = [];

    /// <summary>
    /// Pause après un cran de molette.
    ///
    /// Court, et distinct du délai entre actions : une liste défile à la vitesse où on la fait
    /// tourner, il n'y a ni panneau à ouvrir ni contenu à charger. Lui appliquer le délai du
    /// rejeu sur l'équipe rendrait le moindre défilement interminable, multiplié par le nombre
    /// de crans et de personnages.
    /// </summary>
    public int ScrollDelayMs
    {
        get => _scrollDelayMs;
        set => Set(ref _scrollDelayMs, Math.Clamp(value, 0, 2000));
    }

    /// <summary>
    /// Écart entre deux clics d'un même geste — un double-clic, par exemple.
    ///
    /// Cette valeur tient dans la seule fenêtre où un double-clic existe. Trop courte, les deux
    /// clics tombent dans la même image du jeu, qui n'interroge l'entrée qu'une fois par image :
    /// il n'en voit qu'un. Trop longue, elle dépasse le seuil de double-clic du système
    /// (une demi-seconde par défaut) et il voit deux clics indépendants.
    ///
    /// 80 ms laisse quelques images d'écart tout en restant le rythme d'une vraie main. La borne
    /// basse dépendant du nombre d'images par seconde de la machine, c'est le premier réglage à
    /// monter si un double-clic n'aboutit pas.
    /// </summary>
    public int MultiClickIntervalMs
    {
        get => _multiClickIntervalMs;
        set => Set(ref _multiClickIntervalMs, Math.Clamp(value, 0, 1000));
    }

    /// <summary>
    /// Joindre le code de balayage aux touches envoyées. Les moteurs de jeu qui lisent
    /// l'entrée brute ignorent souvent les frappes dépourvues de code de balayage.
    /// </summary>
    public bool UseScanCodes
    {
        get => _useScanCodes;
        set => Set(ref _useScanCodes, value);
    }
}
