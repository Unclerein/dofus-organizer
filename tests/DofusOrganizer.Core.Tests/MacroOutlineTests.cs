using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// L'aplatissement décide de ce que l'utilisateur voit dans l'éditeur. Une étape qui n'en
/// ressort pas disparaît de la liste sans un mot — elle continue de s'exécuter, invisible et
/// impossible à corriger.
/// </summary>
public class MacroOutlineTests
{
    private static ForEachCharacterStep Boucle(params MacroStep[] steps)
    {
        var loop = new ForEachCharacterStep();
        foreach (var step in steps) loop.Steps.Add(step);
        return loop;
    }

    private static List<MacroStep> Macro(params MacroStep[] steps) => [.. steps];

    // ---------------------------------------------------------------- Aplatir

    [Fact]
    public void Les_sous_etapes_apparaissent_en_retrait_sous_leur_boucle()
    {
        var clic = new MouseClickStep();
        var touche = new KeyStep();
        var focus = new FocusStep();
        var steps = Macro(focus, Boucle(clic, touche));

        var rows = MacroOutline.Flatten(steps);

        Assert.Equal(4, rows.Count);
        Assert.Equal((focus, 0), (rows[0].Step, rows[0].Depth));
        Assert.Equal(0, rows[1].Depth);                       // la boucle elle-même
        Assert.Equal((clic, 1), (rows[2].Step, rows[2].Depth));
        Assert.Equal((touche, 1), (rows[3].Step, rows[3].Depth));
    }

    [Fact]
    public void Une_macro_sans_boucle_donne_une_ligne_par_etape()
    {
        var steps = Macro(new MouseClickStep(), new DelayStep(), new KeyStep());

        var rows = MacroOutline.Flatten(steps);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, row => Assert.Equal(0, row.Depth));
    }

    [Fact]
    public void Une_boucle_vide_garde_sa_ligne()
    {
        // Sans elle, on ne pourrait plus la sélectionner pour y ajouter quoi que ce soit.
        var rows = MacroOutline.Flatten(Macro(Boucle()));

        Assert.Equal(0, Assert.Single(rows).Depth);
    }

    // ---------------------------------------------------------------- Réordonner

    [Fact]
    public void Une_sous_etape_se_deplace_dans_sa_boucle()
    {
        var premier = new MouseClickStep();
        var second = new KeyStep();
        var loop = Boucle(premier, second);
        var steps = Macro(loop);

        // Lignes : 0 la boucle, 1 le premier, 2 le second.
        Assert.True(MacroOutline.Reorder(steps, from: 2, to: 1));

        Assert.Equal([second, premier], loop.Steps);
    }

    [Fact]
    public void Une_etape_de_premier_niveau_se_deplace_parmi_ses_pareilles()
    {
        var focus = new FocusStep();
        var loop = Boucle(new KeyStep());
        var steps = Macro(focus, loop);

        Assert.True(MacroOutline.Reorder(steps, from: 0, to: 1));

        Assert.Equal([loop, focus], steps);
    }

    [Fact]
    public void Un_depot_qui_traverse_la_frontiere_d_une_boucle_est_refuse()
    {
        // Sortir une étape de sa boucle changerait quand elle s'exécute — une fois au lieu
        // d'une fois par personnage. Cela ne doit pas tenir à la précision d'un lâcher.
        var focus = new FocusStep();
        var interne = new KeyStep();
        var loop = Boucle(interne);
        var steps = Macro(focus, loop);

        // Ligne 2 est l'étape interne, ligne 0 le focus à la racine.
        Assert.False(MacroOutline.Reorder(steps, from: 2, to: 0));

        Assert.Equal([focus, loop], steps);
        Assert.Equal([interne], loop.Steps);
    }

    [Fact]
    public void Deposer_une_boucle_sur_sa_propre_sous_etape_est_refuse()
    {
        var interne = new KeyStep();
        var loop = Boucle(interne);
        var steps = Macro(loop);

        Assert.False(MacroOutline.Reorder(steps, from: 0, to: 1));

        Assert.Equal([loop], steps);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 99)]
    public void Un_depot_sans_effet_ou_hors_liste_ne_change_rien(int from, int to)
    {
        var steps = Macro(new MouseClickStep(), new KeyStep());

        Assert.False(MacroOutline.Reorder(steps, from, to));
    }

    // ---------------------------------------------------------------- Conteneur

    [Fact]
    public void Le_conteneur_d_une_etape_est_la_liste_qui_la_porte()
    {
        var interne = new KeyStep();
        var loop = Boucle(interne);
        var racine = new FocusStep();
        var steps = Macro(racine, loop);

        Assert.Same(steps, MacroOutline.ContainerOf(steps, racine));
        Assert.Same(loop.Steps, MacroOutline.ContainerOf(steps, interne));
        Assert.Null(MacroOutline.ContainerOf(steps, new DelayStep()));
    }
}
