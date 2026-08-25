using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Une main qui fait tourner la molette produit un événement par cran. Non regroupés, ces crans
/// devenaient autant d'étapes que le rejeu sépare chacune de son délai : un geste instantané se
/// rejouait au ralenti, et d'autant plus qu'il y a de personnages.
/// </summary>
public class ScrollMergingTests
{
    private static readonly ScreenPoint Position = new(400, 300);

    [Fact]
    public void Les_crans_d_un_meme_geste_se_regroupent()
    {
        // Cadence d'une molette réelle : plusieurs crans par dixième de seconde.
        Assert.True(ScrollMerging.ContinuesScroll(
            new RecordedScroll(ScrollDirection.Down, Position, 1000),
            ScrollDirection.Down, Position, 1030, alreadyCounted: 1, adding: 1));
    }

    [Fact]
    public void Un_changement_de_sens_ouvre_un_nouveau_geste()
    {
        // Remonter après être descendu est une intention nouvelle, pas la suite du geste.
        Assert.False(ScrollMerging.ContinuesScroll(
            new RecordedScroll(ScrollDirection.Down, Position, 1000),
            ScrollDirection.Up, Position, 1030, alreadyCounted: 3, adding: 1));
    }

    [Fact]
    public void Deux_defilements_espaces_restent_distincts()
    {
        Assert.False(ScrollMerging.ContinuesScroll(
            new RecordedScroll(ScrollDirection.Down, Position, 1000),
            ScrollDirection.Down, Position, 1000 + ScrollMerging.GestureGapMs + 1,
            alreadyCounted: 2, adding: 1));
    }

    [Fact]
    public void Un_defilement_ailleurs_dans_la_fenetre_reste_distinct()
    {
        var ailleurs = new ScreenPoint(Position.X + ScrollMerging.PositionSlop + 1, Position.Y);

        Assert.False(ScrollMerging.ContinuesScroll(
            new RecordedScroll(ScrollDirection.Down, Position, 1000),
            ScrollDirection.Down, ailleurs, 1030, alreadyCounted: 2, adding: 1));
    }

    [Fact]
    public void Un_leger_mouvement_de_la_main_ne_coupe_pas_le_geste()
    {
        // La souris bouge un peu pendant qu'on fait tourner la molette : exiger le pixel près
        // découperait le geste en morceaux.
        var fremissement = new ScreenPoint(Position.X + 3, Position.Y - 2);

        Assert.True(ScrollMerging.ContinuesScroll(
            new RecordedScroll(ScrollDirection.Down, Position, 1000),
            ScrollDirection.Down, fremissement, 1020, alreadyCounted: 5, adding: 1));
    }

    [Fact]
    public void Le_regroupement_s_arrete_avant_de_perdre_des_crans()
    {
        // ScrollStep plafonne ses crans : dépasser ferait disparaître le surplus en silence.
        Assert.False(ScrollMerging.ContinuesScroll(
            new RecordedScroll(ScrollDirection.Down, Position, 1000),
            ScrollDirection.Down, Position, 1020,
            alreadyCounted: ScrollMerging.MaximumNotches, adding: 1));

        // Et le plafond correspond bien à celui du modèle.
        var step = new ScrollStep { Notches = ScrollMerging.MaximumNotches + 10 };
        Assert.Equal(ScrollMerging.MaximumNotches, step.Notches);
    }

    [Fact]
    public void Sans_defilement_precedent_il_n_y_a_rien_a_prolonger()
    {
        Assert.False(ScrollMerging.ContinuesScroll(
            null, ScrollDirection.Down, Position, 1000, alreadyCounted: 0, adding: 1));
    }

    [Fact]
    public async Task Un_geste_regroupe_part_en_une_seule_injection()
    {
        // Le bout qui compte pour l'utilisateur : douze crans regroupés donnent une molette et
        // une seule attente, là où douze étapes en donnaient douze.
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.ScrollDelayMs = 40;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(new ScrollStep { Fx = 0.5, Fy = 0.5, Notches = 12 }),
            windows, roster, profile, actionDelayOverride: 600);

        Assert.Equal(-12, Assert.Single(actions.OfType<RecordedAction.Wheel>()).Notches);
        Assert.Equal(new RecordedAction.Delay(40), Assert.Single(actions.OfType<RecordedAction.Delay>()));
    }
}
