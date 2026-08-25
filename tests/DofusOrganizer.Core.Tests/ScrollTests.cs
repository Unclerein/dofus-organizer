using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Une liste défile à la vitesse où on la fait tourner : il n'y a ni panneau à ouvrir ni
/// contenu à charger. La molette a donc son délai propre, court, et surtout pas celui du rejeu
/// sur l'équipe — 600 ms par cran, multipliés par le nombre de crans et de personnages,
/// rendaient le moindre défilement interminable.
/// </summary>
public class ScrollTests
{
    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildSolo()
    {
        var windows = new FakeWindowManager();
        windows.AddWindow(1, "Meneur", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        windows.Foreground = 1;

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static Macro MacroOf(params MacroStep[] steps)
    {
        var macro = new Macro { Name = "Défiler", RestoreInitialWindow = false, RestoreCursorPosition = false };
        foreach (var step in steps) macro.Steps.Add(step);
        return macro;
    }

    private static async Task<List<RecordedAction>> RunAsync(
        Macro macro, FakeWindowManager windows, CharacterRoster roster, Profile profile,
        int? actionDelayOverride = null)
    {
        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        var result = await runner.RunAsync(
            macro, roster, profile.Settings, CancellationToken.None, actionDelayOverride);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);
        return actions;
    }

    [Fact]
    public async Task La_molette_suit_son_propre_delai()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.ScrollDelayMs = 40;
        profile.Settings.ActionDelayMs = 500;

        var actions = await RunAsync(
            MacroOf(new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Down, Notches = 3 }),
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
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.ScrollDelayMs = 40;

        var actions = await RunAsync(
            MacroOf(new ScrollStep { Fx = 0.5, Fy = 0.5, Notches = 2 }),
            windows, roster, profile, actionDelayOverride: 600);

        Assert.Equal(new RecordedAction.Delay(40), Assert.Single(actions.OfType<RecordedAction.Delay>()));
    }

    [Fact]
    public async Task Le_sens_de_defilement_donne_le_signe_des_crans()
    {
        var (windows, roster, profile) = BuildSolo();

        var actions = await RunAsync(
            MacroOf(
                new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Up, Notches = 4 },
                new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Down, Notches = 4 }),
            windows, roster, profile);

        var wheels = actions.OfType<RecordedAction.Wheel>().Select(w => w.Notches).ToList();
        Assert.Equal([4, -4], wheels);
    }
}
