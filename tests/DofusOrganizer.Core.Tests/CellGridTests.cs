using DofusOrganizer.Core.Geometry;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// La grille du coffre : neuf lignes de cinq colonnes. Deux points de calibrage la décrivent
/// exactement, et c'est ce qui permet de ne cliquer que le premier et le dernier item. Une case
/// mal calculée ferait déplacer autre chose, sans que rien ne le signale.
/// </summary>
public class CellGridTests
{
    /// <summary>Un coffre : centres des cases extrêmes à 10 % et 50 %, cinq colonnes, neuf lignes.</summary>
    private static readonly CellGrid Coffre = new(
        TopLeft: new NormalizedPoint(0.10, 0.10),
        BottomRight: new NormalizedPoint(0.50, 0.50),
        Rows: 9,
        Columns: 5);

    // Pas : 0.40 / 4 = 0.10 en horizontal, 0.40 / 8 = 0.05 en vertical.
    private const double PasX = 0.10;
    private const double PasY = 0.05;

    // ---------------------------------------------------------------- Repérer une case

    [Fact]
    public void Le_centre_d_une_case_se_calcule_a_partir_des_deux_extremes()
    {
        Assert.Equal(new NormalizedPoint(0.10, 0.10), Coffre.CenterOf(new Cell(0, 0)));
        Assert.Equal(new NormalizedPoint(0.50, 0.50), Coffre.CenterOf(new Cell(8, 4)));

        var milieu = Coffre.CenterOf(new Cell(4, 2));
        Assert.Equal(0.30, milieu.Fx, 9);
        Assert.Equal(0.30, milieu.Fy, 9);
    }

    [Fact]
    public void Un_clic_au_centre_retrouve_sa_case()
        => Assert.Equal(new Cell(3, 2), Coffre.Locate(Coffre.CenterOf(new Cell(3, 2))));

    [Fact]
    public void Un_clic_pres_d_un_bord_appartient_encore_a_sa_case()
    {
        // C'est le cas courant : personne ne vise le centre exact d'une case.
        var presDuBord = new NormalizedPoint(0.30 + PasX * 0.4, 0.30 + PasY * 0.4);

        Assert.Equal(new Cell(4, 2), Coffre.Locate(presDuBord));
    }

    [Fact]
    public void Un_clic_ramene_au_centre_de_sa_case_ne_derive_plus()
    {
        // Un clic brut dérivait tel quel dans la macro, et le glisser partait de travers sur
        // les personnages dont la fenêtre n'a pas exactement la même taille.
        var approximatif = new NormalizedPoint(0.30 + PasX * 0.3, 0.30 - PasY * 0.3);

        var recale = Coffre.Snap(approximatif);

        Assert.Equal(0.30, recale.Fx, 9);
        Assert.Equal(0.30, recale.Fy, 9);
    }

    [Theory]
    [InlineData(-0.20, 0.30)]   // largement à gauche
    [InlineData(0.30, 0.90)]    // sous la dernière ligne
    [InlineData(0.90, 0.30)]    // à droite de la dernière colonne
    public void Un_clic_hors_grille_n_appartient_a_aucune_case(double fx, double fy)
    {
        Assert.Null(Coffre.Locate(new NormalizedPoint(fx, fy)));

        // Et il ressort inchangé plutôt que ramené de force sur un bord.
        var dehors = new NormalizedPoint(fx, fy);
        Assert.Equal(dehors, Coffre.Snap(dehors));
    }

    // ---------------------------------------------------------------- La plage

    [Fact]
    public void Une_plage_sur_une_meme_ligne_donne_les_cases_entre_les_deux()
    {
        var plage = Coffre.Range(Coffre.CenterOf(new Cell(2, 1)), Coffre.CenterOf(new Cell(2, 3)));

        Assert.Equal(3, plage.Count);
        Assert.Equal([Coffre.CenterOf(new Cell(2, 1)), Coffre.CenterOf(new Cell(2, 2)),
                      Coffre.CenterOf(new Cell(2, 3))], plage);
    }

    [Fact]
    public void Une_plage_passe_a_la_ligne_comme_une_lecture()
    {
        // De la dernière case d'une ligne à la deuxième de la suivante : quatre cases, et non
        // trois — c'est tout l'intérêt de compter en ordre de lecture plutôt qu'en rectangle.
        var plage = Coffre.Range(Coffre.CenterOf(new Cell(0, 4)), Coffre.CenterOf(new Cell(1, 2)));

        Assert.Equal(4, plage.Count);
        Assert.Equal([Coffre.CenterOf(new Cell(0, 4)), Coffre.CenterOf(new Cell(1, 0)),
                      Coffre.CenterOf(new Cell(1, 1)), Coffre.CenterOf(new Cell(1, 2))], plage);
    }

    [Fact]
    public void L_ordre_des_deux_clics_ne_compte_pas()
    {
        // Personne ne devrait avoir à se souvenir dans quel sens désigner.
        var premier = Coffre.CenterOf(new Cell(1, 3));
        var dernier = Coffre.CenterOf(new Cell(3, 1));

        Assert.Equal(Coffre.Range(premier, dernier), Coffre.Range(dernier, premier));
    }

    [Fact]
    public void Deux_clics_sur_la_meme_case_donnent_une_seule_case()
        => Assert.Single(Coffre.Range(Coffre.CenterOf(new Cell(5, 2)), Coffre.CenterOf(new Cell(5, 2))));

    [Fact]
    public void Une_plage_dont_un_bout_tombe_hors_grille_ne_designe_rien()
    {
        // Mieux vaut ne rien désigner que désigner à côté : les items déplacés ne reviennent pas.
        Assert.Empty(Coffre.Range(Coffre.CenterOf(new Cell(0, 0)), new NormalizedPoint(0.95, 0.95)));
    }

    // ---------------------------------------------------------------- Calibrages douteux

    [Fact]
    public void Un_calibrage_aux_deux_points_confondus_ne_divise_pas_par_zero()
    {
        var degeneree = new CellGrid(new NormalizedPoint(0.3, 0.3), new NormalizedPoint(0.3, 0.3), 9, 5);

        Assert.False(degeneree.IsUsable);
        Assert.Null(degeneree.Locate(new NormalizedPoint(0.3, 0.3)));
        Assert.Empty(degeneree.Range(new NormalizedPoint(0.3, 0.3), new NormalizedPoint(0.3, 0.3)));
    }

    [Fact]
    public void Une_grille_d_une_seule_ligne_reste_utilisable()
    {
        // Le pas vertical vaut alors zéro, ce qui ne doit pas rendre la grille inexploitable.
        var ligne = new CellGrid(new NormalizedPoint(0.1, 0.2), new NormalizedPoint(0.5, 0.2), Rows: 1, Columns: 5);

        Assert.True(ligne.IsUsable);
        Assert.Equal(new Cell(0, 2), ligne.Locate(new NormalizedPoint(0.3, 0.2)));
        Assert.Equal(5, ligne.Range(ligne.CenterOf(new Cell(0, 0)), ligne.CenterOf(new Cell(0, 4))).Count);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(9, 0)]
    public void Une_grille_sans_case_n_est_pas_utilisable(int rows, int columns)
        => Assert.False(new CellGrid(new NormalizedPoint(0.1, 0.1), new NormalizedPoint(0.5, 0.5), rows, columns).IsUsable);
}
