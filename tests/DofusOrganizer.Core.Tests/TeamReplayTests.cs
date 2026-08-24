using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Le rejeu sur l'équipe part d'une séquence capturée chez le meneur. L'enregistreur y place
/// en tête une étape « aller sur le personnage N » : indispensable pour une macro ordinaire,
/// ruineuse une fois enfermée dans une boucle par personnage, où elle ramène chaque tour sur
/// le meneur — c'est le bug que ces tests verrouillent.
/// </summary>
public class TeamReplayTests
{
    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildTeam(int count)
    {
        var windows = new FakeWindowManager();
        for (int i = 0; i < count; i++)
        {
            windows.AddWindow(i + 1, $"Perso{i + 1}", new ClientBounds(new ScreenPoint(i * 900, 0), 800, 600));
        }

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    /// <summary>Ce que l'enregistreur produisait : focus sur le meneur, puis les actions.</summary>
    private static List<MacroStep> CapturedOnLeader() =>
    [
        new FocusStep { Target = FocusTarget.Slot, SlotIndex = 0 },
        new MouseClickStep { Fx = 0.4, Fy = 0.6 },
        new MouseClickStep { Fx = 0.5, Fy = 0.7 },
    ];

    [Fact]
    public void L_etape_de_focus_capturee_est_ecartee()
    {
        var macro = TeamReplay.BuildMacro(CapturedOnLeader());

        var loop = Assert.IsType<ForEachCharacterStep>(Assert.Single(macro.Steps));
        Assert.True(loop.SkipCurrentWindow);
        Assert.Equal(2, loop.Steps.Count);
        Assert.All(loop.Steps, step => Assert.IsType<MouseClickStep>(step));
    }

    [Fact]
    public void Une_boucle_imbriquee_est_ecartee()
    {
        // Une boucle dans une boucle rejouerait toute l'équipe pour chaque personnage.
        var captured = new List<MacroStep>
        {
            new ForEachCharacterStep { Steps = { new MouseClickStep() } },
            new MouseClickStep { Fx = 0.2, Fy = 0.2 },
        };

        var loop = Assert.IsType<ForEachCharacterStep>(Assert.Single(TeamReplay.BuildMacro(captured).Steps));
        Assert.IsType<MouseClickStep>(Assert.Single(loop.Steps));
    }

    [Fact]
    public void Une_capture_sans_action_utile_est_reconnue_comme_vide()
    {
        // Un appui immédiatement suivi d'un second ne capture que l'étape de focus :
        // il n'y a rien à rejouer, et mieux vaut le dire que lancer une macro vide.
        Assert.False(TeamReplay.HasReplayableSteps([new FocusStep()]));
        Assert.True(TeamReplay.HasReplayableSteps([new FocusStep(), new MouseClickStep()]));
    }

    [Fact]
    public async Task Le_rejeu_agit_sur_chaque_autre_personnage_et_jamais_sur_le_meneur()
    {
        // Le test qui aurait évité le bug : avant correction, la séquence se rejouait
        // quatre fois sur le meneur sans qu'aucune fenêtre ne change.
        var (windows, roster, profile) = BuildTeam(4);
        windows.Foreground = 1;

        var macro = TeamReplay.BuildMacro(CapturedOnLeader());
        macro.RestoreInitialWindow = false;
        macro.RestoreCursorPosition = false;

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        var result = await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);

        // Trois personnages visités, le meneur sauté, et aucun retour vers lui en cours de route.
        var focused = actions.OfType<RecordedAction.Focus>().Select(f => f.Handle).ToList();
        Assert.Equal([(nint)2, (nint)3, (nint)4], focused);
        Assert.DoesNotContain((nint)1, focused);

        // Deux clics par personnage, chacun dans la fenêtre du personnage courant.
        var clicks = actions.OfType<RecordedAction.Click>().ToList();
        Assert.Equal(6, clicks.Count);

        var screen = windows.GetVirtualScreen();
        for (int character = 0; character < 3; character++)
        {
            var bounds = new ClientBounds(new ScreenPoint((character + 1) * 900, 0), 800, 600);
            Assert.Equal(
                CoordinateMapper.ToAbsolute(new NormalizedPoint(0.4, 0.6), bounds, screen),
                clicks[character * 2].Point);
        }
    }

    [Fact]
    public async Task Le_meneur_est_saute_meme_s_il_n_est_pas_le_premier_de_la_liste()
    {
        var (windows, roster, profile) = BuildTeam(3);
        windows.Foreground = 2;

        var macro = TeamReplay.BuildMacro([new MouseClickStep { Fx = 0.5, Fy = 0.5 }]);
        macro.RestoreInitialWindow = false;
        macro.RestoreCursorPosition = false;

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal([(nint)1, (nint)3], actions.OfType<RecordedAction.Focus>().Select(f => f.Handle));
    }
}
