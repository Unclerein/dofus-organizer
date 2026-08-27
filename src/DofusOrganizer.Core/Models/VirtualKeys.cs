namespace DofusOrganizer.Core.Models;

/// <summary>
/// Les codes virtuels Windows utiles à l'organizer, et leur libellé d'affichage.
/// Les codes des lettres et des chiffres sont ceux du clavier US : Windows les
/// conserve quelle que soit la disposition, seul le caractère produit change.
/// </summary>
public static class VirtualKeys
{
    public const int None = 0x00;
    public const int Cancel = 0x03;
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;      // Alt
    public const int Pause = 0x13;
    public const int Capital = 0x14;
    public const int Escape = 0x1B;

    // Les trois lettres des raccourcis universels. Nommées parce que le moteur les envoie
    // lui-même, et qu'un 0x43 nu au milieu d'une méthode ne dit rien à personne.
    public const int A = 0x41;
    public const int C = 0x43;
    public const int V = 0x56;
    public const int Space = 0x20;
    public const int Prior = 0x21;     // Page précédente
    public const int Next = 0x22;      // Page suivante
    public const int End = 0x23;
    public const int Home = 0x24;
    public const int Left = 0x25;
    public const int Up = 0x26;
    public const int Right = 0x27;
    public const int Down = 0x28;
    public const int Insert = 0x2D;
    public const int Delete = 0x2E;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int NumPad0 = 0x60;
    public const int Multiply = 0x6A;
    public const int Add = 0x6B;
    public const int Subtract = 0x6D;
    public const int Decimal = 0x6E;
    public const int Divide = 0x6F;
    public const int F1 = 0x70;
    public const int F2 = 0x71;
    public const int F3 = 0x72;
    public const int F4 = 0x73;
    public const int F5 = 0x74;
    public const int F6 = 0x75;
    public const int F7 = 0x76;
    public const int F8 = 0x77;
    public const int F9 = 0x78;
    public const int F10 = 0x79;
    public const int F11 = 0x7A;
    public const int F12 = 0x7B;
    public const int F24 = 0x87;
    public const int NumLock = 0x90;
    public const int Scroll = 0x91;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;

    private static readonly Dictionary<int, string> Names = new()
    {
        [Cancel] = "Pause/Attn", [Back] = "Retour arrière", [Tab] = "Tab", [Return] = "Entrée",
        [Shift] = "Maj", [Control] = "Ctrl", [Menu] = "Alt", [Pause] = "Pause", [Capital] = "Verr. Maj",
        [Escape] = "Échap", [Space] = "Espace", [Prior] = "Page préc.", [Next] = "Page suiv.",
        [End] = "Fin", [Home] = "Origine", [Left] = "Gauche", [Up] = "Haut", [Right] = "Droite",
        [Down] = "Bas", [Insert] = "Inser", [Delete] = "Suppr", [LWin] = "Win gauche", [RWin] = "Win droite",
        [Multiply] = "Pavé *", [Add] = "Pavé +", [Subtract] = "Pavé -", [Decimal] = "Pavé .",
        [Divide] = "Pavé /", [NumLock] = "Verr. Num", [Scroll] = "Arrêt défil.",
        [LShift] = "Maj gauche", [RShift] = "Maj droite", [LControl] = "Ctrl gauche",
        [RControl] = "Ctrl droite", [LMenu] = "Alt gauche", [RMenu] = "Alt droite",
        [0xBA] = ";  :", [0xBB] = "=  +", [0xBC] = ",  <", [0xBD] = "-  _", [0xBE] = ".  >",
        [0xBF] = "/  ?", [0xC0] = "`  ~", [0xDB] = "[  {", [0xDC] = "\\  |", [0xDD] = "]  }",
        [0xDE] = "'  \"", [0xDF] = "OEM 8", [0xE2] = "OEM 102",
        // Boutons souris latéraux, exposés comme des touches par le répartiteur de raccourcis.
        [MouseButton4] = "Souris 4", [MouseButton5] = "Souris 5", [MouseMiddle] = "Souris milieu",
    };

    /// <summary>
    /// Codes synthétiques pour les boutons souris. Ils vivent au-delà de 0xFF, hors de
    /// la plage des vrais codes virtuels, pour pouvoir cohabiter avec eux dans une
    /// <see cref="Hotkey"/> sans risque de collision.
    /// </summary>
    public const int MouseMiddle = 0x100;
    public const int MouseButton4 = 0x101;
    public const int MouseButton5 = 0x102;

    public static bool IsMouseButton(int virtualKey) => virtualKey is >= MouseMiddle and <= MouseButton5;

    /// <summary>Vrai pour les touches qui ne servent que de modificateur : elles ne peuvent pas être le corps d'un raccourci.</summary>
    public static bool IsModifierKey(int virtualKey) => virtualKey
        is Shift or Control or Menu or LShift or RShift or LControl or RControl or LMenu or RMenu or LWin or RWin;

    public static string GetName(int virtualKey)
    {
        if (Names.TryGetValue(virtualKey, out var name)) return name;
        if (virtualKey is >= 0x30 and <= 0x39) return ((char)virtualKey).ToString();          // 0-9
        if (virtualKey is >= 0x41 and <= 0x5A) return ((char)virtualKey).ToString();          // A-Z
        if (virtualKey is >= NumPad0 and <= 0x69) return "Pavé " + (virtualKey - NumPad0);
        if (virtualKey is >= F1 and <= F24) return "F" + (virtualKey - F1 + 1);
        return $"0x{virtualKey:X2}";
    }

    /// <summary>Les touches proposées dans les listes déroulantes de l'éditeur de macros.</summary>
    public static IEnumerable<int> CommonKeys()
    {
        for (int vk = F1; vk <= F12; vk++) yield return vk;
        for (int vk = 0x30; vk <= 0x39; vk++) yield return vk;
        for (int vk = 0x41; vk <= 0x5A; vk++) yield return vk;
        yield return Space; yield return Return; yield return Escape; yield return Tab;
        yield return Left; yield return Up; yield return Right; yield return Down;
        yield return Delete; yield return Insert; yield return Home; yield return End;
        for (int vk = NumPad0; vk <= 0x69; vk++) yield return vk;
    }
}
