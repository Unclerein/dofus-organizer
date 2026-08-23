using DofusOrganizer.Core.Geometry;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class CoordinateMapperTests
{
    private static readonly ClientBounds Window = new(new ScreenPoint(100, 200), 800, 600);

    [Fact]
    public void Centre_de_la_zone_client_tombe_au_centre_de_la_fenetre()
    {
        var screen = CoordinateMapper.ToScreen(new NormalizedPoint(0.5, 0.5), Window);
        Assert.Equal(new ScreenPoint(100 + 400, 200 + 300), screen);
    }

    [Fact]
    public void Les_bords_restent_dans_la_zone_client()
    {
        Assert.Equal(new ScreenPoint(100, 200), CoordinateMapper.ToScreen(new NormalizedPoint(0, 0), Window));
        Assert.Equal(new ScreenPoint(899, 799), CoordinateMapper.ToScreen(new NormalizedPoint(1, 1), Window));
    }

    [Fact]
    public void Un_point_hors_bornes_est_ramene_dans_la_fenetre()
    {
        Assert.Equal(new ScreenPoint(100, 200), CoordinateMapper.ToScreen(new NormalizedPoint(-3, -0.5), Window));
        Assert.Equal(new ScreenPoint(899, 799), CoordinateMapper.ToScreen(new NormalizedPoint(4, 2), Window));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(399, 299)]
    [InlineData(799, 599)]
    public void Un_clic_enregistre_retombe_sur_le_meme_pixel(int dx, int dy)
    {
        var original = new ScreenPoint(Window.Origin.X + dx, Window.Origin.Y + dy);

        var normalized = CoordinateMapper.ToNormalized(original, Window);
        var replayed = CoordinateMapper.ToScreen(normalized, Window);

        Assert.Equal(original, replayed);
    }

    [Fact]
    public void Un_clic_suit_la_fenetre_quand_elle_est_deplacee_et_redimensionnee()
    {
        // Le clic est enregistré au quart de la largeur et à la moitié de la hauteur,
        // puis rejoué sur une fenêtre deux fois plus grande et déplacée : il doit rester
        // au quart et à la moitié, sans quoi les macros casseraient au moindre changement.
        var recorded = new ScreenPoint(Window.Origin.X + 200, Window.Origin.Y + 300);
        var normalized = CoordinateMapper.ToNormalized(recorded, Window);

        var moved = new ClientBounds(new ScreenPoint(1000, 50), 1600, 1200);
        var replayed = CoordinateMapper.ToScreen(normalized, moved);

        // Un pixel de tolérance : on vise le centre du pixel enregistré, ce qui garantit
        // le retour exact à taille identique et laisse au plus un pixel d'écart à l'échelle.
        Assert.InRange(replayed.X, 1000 + 400, 1000 + 401);
        Assert.InRange(replayed.Y, 50 + 600, 50 + 601);
    }

    [Fact]
    public void Les_coins_du_bureau_virtuel_correspondent_aux_bornes_absolues()
    {
        var screen = VirtualScreen.Single(1920, 1080);

        Assert.Equal(new AbsolutePoint(0, 0), CoordinateMapper.ToAbsolute(new ScreenPoint(0, 0), screen));
        Assert.Equal(new AbsolutePoint(65535, 65535), CoordinateMapper.ToAbsolute(new ScreenPoint(1919, 1079), screen));
    }

    [Fact]
    public void Un_ecran_secondaire_a_gauche_donne_des_coordonnees_absolues_positives()
    {
        // Deux écrans 1920x1080, le secondaire à gauche : le bureau virtuel commence en -1920.
        // Sans soustraction de l'origine, tout l'écran de gauche donnerait des valeurs
        // négatives et les clics partiraient dans le décor.
        var screen = new VirtualScreen(-1920, 0, 3840, 1080);

        Assert.Equal(0, CoordinateMapper.ToAbsolute(new ScreenPoint(-1920, 0), screen).X);
        Assert.Equal(65535, CoordinateMapper.ToAbsolute(new ScreenPoint(1919, 0), screen).X);

        var middle = CoordinateMapper.ToAbsolute(new ScreenPoint(0, 0), screen);
        Assert.InRange(middle.X, 32700, 32800);
    }

    [Fact]
    public void Une_fenetre_sans_surface_ne_provoque_pas_de_division_par_zero()
    {
        var empty = new ClientBounds(new ScreenPoint(10, 20), 0, 0);

        Assert.True(empty.IsEmpty);
        Assert.Equal(new ScreenPoint(10, 20), CoordinateMapper.ToScreen(new NormalizedPoint(0.5, 0.5), empty));
        Assert.Equal(new NormalizedPoint(0, 0), CoordinateMapper.ToNormalized(new ScreenPoint(50, 50), empty));
    }
}
