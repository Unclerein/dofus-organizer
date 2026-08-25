namespace DofusOrganizer.Core.Geometry;

/// <summary>Un point en pixels dans l'espace écran (peut être négatif sur un écran secondaire à gauche).</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// Position d'un clic exprimée en fraction de la zone client d'une fenêtre.
/// C'est sous cette forme que les macros sont enregistrées : elles restent valides
/// si la fenêtre Dofus est déplacée ou redimensionnée entre l'enregistrement et le rejeu.
/// </summary>
public readonly record struct NormalizedPoint(double Fx, double Fy)
{
    public NormalizedPoint Clamped() => new(Math.Clamp(Fx, 0d, 1d), Math.Clamp(Fy, 0d, 1d));
}

/// <summary>Un rectangle en pixels dans l'espace écran, tel qu'on le capture.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;

}

/// <summary>Zone client d'une fenêtre : sa taille, et l'écran où se trouve son coin haut-gauche.</summary>
public readonly record struct ClientBounds(ScreenPoint Origin, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Le bureau virtuel, c'est-à-dire le rectangle englobant tous les écrans.
/// L'origine n'est pas (0,0) dès qu'un écran est placé à gauche ou au-dessus du principal.
/// </summary>
public readonly record struct VirtualScreen(int Left, int Top, int Width, int Height)
{
    public static VirtualScreen Single(int width, int height) => new(0, 0, width, height);
}

/// <summary>
/// Coordonnée absolue normalisée sur 0..65535 attendue par SendInput
/// avec MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
/// </summary>
public readonly record struct AbsolutePoint(int X, int Y);
