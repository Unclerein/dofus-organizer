using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class MacroRunnerTests
{
    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildTeam(int count)
    {
        var windows = new FakeWindowManager();
        for (int i = 0; i < count; i++)
        {
            // Fenêtres côte à côte de 800x600 pour que chaque personnage ait des coordonnées distinctes.
            windows.AddWindow(i + 1, $"Perso{i + 1}", new ClientBounds(new ScreenPoint(i * 100, 0), 800, 600));
        }

        var profile = new Profile();
        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static Macro HealMacro() => new()
    {
        Name = "Soin",
        RestoreInitialWindow = false,
        RestoreCursorPosition = false,
        Steps =
        {
            new ForEachCharacterStep
            {
                Steps =
                {
                    new MouseClickStep { Fx = 0.25, Fy = 0.5 },
                    new MouseClickStep { Fx = 0.75, Fy = 0.5 },
                },
            },
        },
    };

    [Fact]
    public async Task La_macro_de_soin_passe_sur_chaque_personnage_dans_l_ordre()
    {
        var (windows, roster, profile) = BuildTeam(4);
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        var result = await runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);

        // Attendu : focus 1, deux clics, focus 2, deux clics, ... pour les quatre personnages.
        var focusOrder = actions.OfType<RecordedAction.Focus>().Select(f => f.Handle).ToArray();
        Assert.Equal(new nint[] { 1, 2, 3, 4 }, focusOrder);
        Assert.Equal(8, actions.OfType<RecordedAction.Click>().Count());

        // Et chaque paire de clics doit suivre le focus qui la précède, pas rester sur la fenêtre précédente.
        var sequence = actions.Where(a => a is RecordedAction.Focus or RecordedAction.Click).ToList();
        for (int i = 0; i < 4; i++)
        {
            Assert.IsType<RecordedAction.Focus>(sequence[i * 3]);
            Assert.IsType<RecordedAction.Click>(sequence[i * 3 + 1]);
            Assert.IsType<RecordedAction.Click>(sequence[i * 3 + 2]);
        }
    }

    [Fact]
    public async Task Chaque_clic_vise_la_fenetre_du_personnage_en_cours()
    {
        var (windows, roster, profile) = BuildTeam(2);
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);

        var clicks = actions.OfType<RecordedAction.Click>().ToList();
        var screen = windows.GetVirtualScreen();

        // Personnage 1 : fenêtre en x=0 ; personnage 2 : fenêtre en x=100. Les coordonnées
        // absolues doivent refléter ce décalage, preuve que la cible a bien changé.
        var expectedFirst = CoordinateMapper.ToAbsolute(
            new NormalizedPoint(0.25, 0.5), new ClientBounds(new ScreenPoint(0, 0), 800, 600), screen);
        var expectedThird = CoordinateMapper.ToAbsolute(
            new NormalizedPoint(0.25, 0.5), new ClientBounds(new ScreenPoint(100, 0), 800, 600), screen);

        Assert.Equal(expectedFirst, clicks[0].Point);
        Assert.Equal(expectedThird, clicks[2].Point);
        Assert.NotEqual(clicks[0].Point, clicks[2].Point);
    }

    [Fact]
    public async Task Un_personnage_decoche_est_saute()
    {
        var (windows, roster, profile) = BuildTeam(3);
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        profile.Characters[1].Enabled = false;
        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);

        Assert.Equal(new nint[] { 1, 3 }, actions.OfType<RecordedAction.Focus>().Select(f => f.Handle).ToArray());
    }

    [Fact]
    public async Task Un_personnage_qu_on_ne_parvient_pas_a_activer_ne_recoit_pas_les_clics()
    {
        // Sans cette protection, les clics destinés au personnage 2 partiraient
        // sur la fenêtre du personnage 1, encore au premier plan.
        var (windows, roster, profile) = BuildTeam(3);
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        windows.ActivationFailures.Add(2);
        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);

        Assert.Equal(4, actions.OfType<RecordedAction.Click>().Count());
    }

    [Fact]
    public async Task La_touche_panique_interrompt_la_macro_en_cours()
    {
        var (windows, roster, profile) = BuildTeam(4);
        var actions = windows.Actions;
        using var cts = new CancellationTokenSource();
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new CancellingClock(cts, cancelAfter: 3));

        var result = await runner.RunAsync(HealMacro(), roster, profile.Settings, cts.Token);

        Assert.Equal(MacroOutcome.Cancelled, result.Outcome);
        Assert.True(actions.OfType<RecordedAction.Focus>().Count() < 4);
    }

    [Fact]
    public async Task Le_curseur_revient_a_sa_place_meme_apres_une_interruption()
    {
        var (windows, roster, profile) = BuildTeam(3);
        var actions = windows.Actions;
        var input = new FakeInputSender(actions) { Cursor = new ScreenPoint(1500, 900) };
        using var cts = new CancellationTokenSource();
        var runner = new MacroRunner(windows, input, new CancellingClock(cts, cancelAfter: 2));

        var macro = HealMacro();
        macro.RestoreCursorPosition = true;
        await runner.RunAsync(macro, roster, profile.Settings, cts.Token);

        Assert.Equal(new ScreenPoint(1500, 900), input.Cursor);
    }

    [Fact]
    public async Task La_fenetre_de_depart_reprend_le_focus_a_la_fin()
    {
        var (windows, roster, profile) = BuildTeam(3);
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        windows.Foreground = 1;
        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        var macro = HealMacro();
        macro.RestoreInitialWindow = true;
        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal(1, actions.OfType<RecordedAction.Focus>().Last().Handle);
    }

    [Fact]
    public async Task Deux_macros_ne_peuvent_pas_s_executer_en_meme_temps()
    {
        // Deux séquences entrelacées enverraient des clics à la mauvaise fenêtre :
        // la seconde demande doit être refusée, pas mise en file.
        var (windows, roster, profile) = BuildTeam(2);
        var actions = windows.Actions;
        var gate = new TaskCompletionSource();
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new BlockingClock(gate.Task));

        var first = runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);
        await Task.Yield();

        var second = await runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);
        Assert.Equal(MacroOutcome.Busy, second.Outcome);

        gate.SetResult();
        Assert.Equal(MacroOutcome.Completed, (await first).Outcome);
    }

    [Fact]
    public async Task Les_delais_configures_sont_respectes()
    {
        var (windows, roster, profile) = BuildTeam(2);
        profile.Settings.FocusSettleDelayMs = 120;
        profile.Settings.ActionDelayMs = 30;
        var actions = windows.Actions;
        var clock = new FakeClock(actions);
        var runner = new MacroRunner(windows, new FakeInputSender(actions), clock);

        await runner.RunAsync(HealMacro(), roster, profile.Settings, CancellationToken.None);

        // Deux personnages : 2 attentes après focus (120) + 4 attentes après clic (30).
        Assert.Equal(2 * 120 + 4 * 30, clock.TotalDelay);
    }

    private sealed class BlockingClock(Task gate) : DofusOrganizer.Core.Abstractions.IClock
    {
        public Task DelayAsync(int milliseconds, CancellationToken cancellationToken) => gate;
    }
}
