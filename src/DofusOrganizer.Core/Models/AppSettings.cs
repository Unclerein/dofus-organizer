namespace DofusOrganizer.Core.Models;

public sealed class AppSettings : NotifyBase
{
    private string _titlePattern = DefaultTitlePattern;
    private string _windowClassFilter = "";
    private Hotkey? _nextCharacterHotkey;
    private Hotkey? _previousCharacterHotkey;
    private Hotkey? _panicHotkey = new(VirtualKeys.Pause);
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
