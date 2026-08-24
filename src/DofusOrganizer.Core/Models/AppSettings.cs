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
    private bool _anchorClicksToImages = true;
    private int _anchorPatchWidth = 160;
    private int _anchorPatchHeight = 48;
    private bool _recordingFeedbackSound = true;
    private bool _hotkeysOnlyWhenGameFocused = true;
    private bool _swallowBoundKeys = true;
    private int _focusSettleDelayMs = 120;
    private int _actionDelayMs = 30;
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
    /// Capturer, à chaque clic enregistré, le fragment d'écran qui l'entoure. C'est ce qui
    /// permet au rejeu de retrouver la cible quand elle a bougé chez un autre personnage.
    /// </summary>
    public bool AnchorClicksToImages
    {
        get => _anchorClicksToImages;
        set => Set(ref _anchorClicksToImages, value);
    }

    /// <summary>
    /// Largeur du fragment capturé. Une ligne d'interface est large et courte : un fragment
    /// à sa forme capture le libellé entier plutôt que quelques caractères, ce qui évite de
    /// confondre deux lignes voisines. Plus grand, il est plus reconnaissable mais plus long
    /// à chercher et plus sensible à ce qui change autour de la cible.
    /// </summary>
    public int AnchorPatchWidth
    {
        get => _anchorPatchWidth;
        set => Set(ref _anchorPatchWidth, Math.Clamp(value, 16, 600));
    }

    /// <summary>Hauteur du fragment capturé.</summary>
    public int AnchorPatchHeight
    {
        get => _anchorPatchHeight;
        set => Set(ref _anchorPatchHeight, Math.Clamp(value, 16, 600));
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
    /// Joindre le code de balayage aux touches envoyées. Les moteurs de jeu qui lisent
    /// l'entrée brute ignorent souvent les frappes dépourvues de code de balayage.
    /// </summary>
    public bool UseScanCodes
    {
        get => _useScanCodes;
        set => Set(ref _useScanCodes, value);
    }
}
