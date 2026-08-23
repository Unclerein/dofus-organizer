namespace DofusOrganizer.Core.Geometry;

/// <summary>
/// Conversions entre les trois espaces de coordonnées manipulés par l'organizer :
/// la fraction de zone client (ce qu'on stocke dans les macros), le pixel écran
/// (ce que rapportent les hooks souris) et l'absolu 0..65535 (ce qu'attend SendInput).
/// </summary>
public static class CoordinateMapper
{
    /// <summary>Borne haute des coordonnées absolues de SendInput.</summary>
    public const int AbsoluteMax = 65535;

    /// <summary>
    /// Pixel écran -> fraction de la zone client.
    /// On vise le *centre* du pixel ((p + 0.5) / taille) : combiné à la troncature de
    /// <see cref="ToScreen"/>, un clic enregistré retombe exactement sur le même pixel
    /// quand la fenêtre n'a pas bougé, et se replace proportionnellement sinon.
    /// </summary>
    public static NormalizedPoint ToNormalized(ScreenPoint point, ClientBounds bounds)
    {
        if (bounds.IsEmpty) return new NormalizedPoint(0, 0);
        double fx = (point.X - bounds.Origin.X + 0.5) / bounds.Width;
        double fy = (point.Y - bounds.Origin.Y + 0.5) / bounds.Height;
        return new NormalizedPoint(fx, fy).Clamped();
    }

    /// <summary>Fraction de la zone client -> pixel écran.</summary>
    public static ScreenPoint ToScreen(NormalizedPoint point, ClientBounds bounds)
    {
        if (bounds.IsEmpty) return bounds.Origin;
        var p = point.Clamped();
        int dx = Math.Clamp((int)Math.Floor(p.Fx * bounds.Width), 0, bounds.Width - 1);
        int dy = Math.Clamp((int)Math.Floor(p.Fy * bounds.Height), 0, bounds.Height - 1);
        return new ScreenPoint(bounds.Origin.X + dx, bounds.Origin.Y + dy);
    }

    /// <summary>
    /// Pixel écran -> absolu 0..65535 sur le bureau virtuel.
    /// Windows fait correspondre 0 au premier pixel et 65535 au dernier, d'où le
    /// diviseur (largeur - 1) et non la largeur : utiliser la largeur décale d'un pixel
    /// en bord d'écran.
    /// </summary>
    public static AbsolutePoint ToAbsolute(ScreenPoint point, VirtualScreen screen)
    {
        return new AbsolutePoint(
            Normalize(point.X - screen.Left, screen.Width),
            Normalize(point.Y - screen.Top, screen.Height));

        static int Normalize(int offset, int extent)
        {
            if (extent <= 1) return 0;
            double scaled = (double)offset * AbsoluteMax / (extent - 1);
            return Math.Clamp((int)Math.Round(scaled, MidpointRounding.AwayFromZero), 0, AbsoluteMax);
        }
    }

    /// <summary>Raccourci fraction -> absolu, le chemin emprunté à chaque clic de macro.</summary>
    public static AbsolutePoint ToAbsolute(NormalizedPoint point, ClientBounds bounds, VirtualScreen screen)
        => ToAbsolute(ToScreen(point, bounds), screen);
}
