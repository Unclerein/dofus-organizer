using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Une liste défile à la vitesse où on la fait tourner : il n'y a ni panneau à ouvrir ni
/// contenu à charger. La molette a donc son délai propre, court, et surtout pas celui du rejeu
/// sur l'équipe, qui faisait attendre une demi-seconde après chaque défilement, sur chaque
/// personnage.
/// </summary>
public class ScrollTests
{
    [Fact]
    public async Task La_molette_suit_son_propre_delai()
    {
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.ScrollDelayMs = 40;
        profile.Settings.ActionDelayMs = 500;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Down, Notches = 3 }),
            windows, roster, profile);

        Assert.Equal(new RecordedAction.Wheel(
            actions.OfType<RecordedAction.Wheel>().Single().Point, -3),
            actions.OfType<RecordedAction.Wheel>().Single());

        // Et non les 500 ms du délai entre actions.
        Assert.Equal(new RecordedAction.Delay(40), Assert.Single(actions.OfType<RecordedAction.Delay>()));
    }

    [Fact]
    public async Task Le_rejeu_sur_l_equipe_ne_ralentit_pas_la_molette()
    {
        // Le cas qui rendait le défilement pénible : la surcharge du rejeu s'appliquait aussi
        // à la molette, alors qu'elle n'attend rien.
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.ScrollDelayMs = 40;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(new ScrollStep { Fx = 0.5, Fy = 0.5, Notches = 2 }),
            windows, roster, profile, actionDelayOverride: 600);

        Assert.Equal(new RecordedAction.Delay(40), Assert.Single(actions.OfType<RecordedAction.Delay>()));
    }

    [Fact]
    public async Task Le_sens_de_defilement_donne_le_signe_des_crans()
    {
        var (windows, roster, profile) = MacroHarness.BuildSolo();

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(
                new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Up, Notches = 4 },
                new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Down, Notches = 4 }),
            windows, roster, profile);

        var wheels = actions.OfType<RecordedAction.Wheel>().Select(w => w.Notches).ToList();
        Assert.Equal([4, -4], wheels);
    }
}
