using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// La touche du havre-sac ouvre un panneau qui met parfois du temps à apparaître, et le clic
/// suivant part alors dans le vide. L'attente lui est accordée par une règle portant sur la
/// touche : la séquence de « Refaire sur l'équipe » étant reconstruite à chaque téléportation,
/// une étape d'attente ajoutée à la main serait effacée au voyage suivant.
///
/// Une attente qui ne s'applique pas ne se voit pas — la macro se contente de rater un clic de
/// temps en temps — d'où ces tests sur la pause réellement observée.
/// </summary>
public class SlowKeysTests
{
    private const int Extra = 1500;

    private static readonly Hotkey Havresac = new(VirtualKeys.F7);

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
        var macro = new Macro { Name = "Voyage", RestoreInitialWindow = false, RestoreCursorPosition = false };
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
    public async Task La_touche_listee_recoit_son_attente()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.SlowKeys.Add(new SlowKey { Key = Havresac, ExtraDelayMs = Extra });

        var actions = await RunAsync(
            MacroOf(new KeyStep { VirtualKey = VirtualKeys.F7 }), windows, roster, profile);

        Assert.Equal(new RecordedAction.Delay(Extra), Assert.Single(actions.OfType<RecordedAction.Delay>()));
    }

    [Fact]
    public async Task Une_touche_absente_de_la_liste_ne_change_rien()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.SlowKeys.Add(new SlowKey { Key = Havresac, ExtraDelayMs = Extra });

        var actions = await RunAsync(
            MacroOf(new KeyStep { VirtualKey = VirtualKeys.F1 }), windows, roster, profile);

        Assert.DoesNotContain(actions, a => a is RecordedAction.Delay);
    }

    [Fact]
    public async Task Les_modificateurs_font_partie_de_la_comparaison()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.SlowKeys.Add(new SlowKey { Key = Havresac, ExtraDelayMs = Extra });

        // « H » et « Ctrl + H » ne font pas la même chose en jeu, donc n'ouvrent pas le même
        // panneau : l'attente de l'une n'a pas à s'appliquer à l'autre.
        var actions = await RunAsync(
            MacroOf(new KeyStep { VirtualKey = VirtualKeys.F7, Modifiers = KeyModifiers.Control }),
            windows, roster, profile);

        Assert.DoesNotContain(actions, a => a is RecordedAction.Delay);
    }

    [Fact]
    public async Task Le_supplement_s_ajoute_au_delai_du_rejeu_sur_l_equipe()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.SlowKeys.Add(new SlowKey { Key = Havresac, ExtraDelayMs = Extra });

        // Le point qui compte : les 600 ms du rejeu restent nécessaires pour tout le reste de la
        // séquence. Le supplément vient par-dessus, il ne les remplace pas.
        var actions = await RunAsync(
            MacroOf(new KeyStep { VirtualKey = VirtualKeys.F7 }),
            windows, roster, profile, actionDelayOverride: 600);

        Assert.Equal(new RecordedAction.Delay(600 + Extra), Assert.Single(actions.OfType<RecordedAction.Delay>()));
    }

    [Fact]
    public async Task Seules_les_etapes_de_touche_sont_concernees()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.ActionDelayMs = 30;
        profile.Settings.SlowKeys.Add(new SlowKey { Key = Havresac, ExtraDelayMs = Extra });

        var actions = await RunAsync(
            MacroOf(
                new KeyStep { VirtualKey = VirtualKeys.F7 },
                new MouseClickStep { Fx = 0.5, Fy = 0.5 },
                new MouseMoveStep { Fx = 0.2, Fy = 0.2 }),
            windows, roster, profile);

        var delays = actions.OfType<RecordedAction.Delay>().Select(d => d.Milliseconds).ToList();
        Assert.Equal([30 + Extra, 30, 30], delays);
    }

    [Fact]
    public void Une_entree_sans_touche_ne_correspond_a_rien()
    {
        // Sans ce garde-fou, une entrée à peine créée ralentirait toutes les frappes de
        // toutes les macros — le temps d'aller lui assigner une touche.
        var slowKeys = new List<SlowKey> { new() { ExtraDelayMs = Extra } };

        Assert.Equal(0, SlowKeys.ExtraDelayFor(new KeyStep { VirtualKey = VirtualKeys.F7 }, slowKeys));
        Assert.Equal(0, SlowKeys.ExtraDelayFor(new KeyStep { VirtualKey = VirtualKeys.F7 }, []));
        Assert.Equal(0, SlowKeys.ExtraDelayFor(new KeyStep { VirtualKey = VirtualKeys.F7 }, null));
    }

    [Fact]
    public void Les_touches_lentes_survivent_a_un_aller_retour_sur_disque()
    {
        // Une touche lente perdue au redémarrage ne se verrait pas : la macro se remettrait
        // simplement à rater un clic de temps en temps.
        string directory = Directory.CreateTempSubdirectory("dofus-organizer-slowkeys").FullName;
        try
        {
            var store = new Core.Config.ProfileStore(Path.Combine(directory, "profile.json"));
            var profile = new Profile();
            profile.Settings.SlowKeys.Add(new SlowKey
            {
                Key = new Hotkey(VirtualKeys.F7, KeyModifiers.Shift),
                ExtraDelayMs = Extra,
            });

            store.Save(profile);
            var loaded = Assert.Single(store.Load().Settings.SlowKeys);

            Assert.Equal(new Hotkey(VirtualKeys.F7, KeyModifiers.Shift), loaded.Key);
            Assert.Equal(Extra, loaded.ExtraDelayMs);
            Assert.True(loaded.IsUsable);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
