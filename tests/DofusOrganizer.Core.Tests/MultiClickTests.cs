using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Core.Vision;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Un double-clic partait en une seule salve d'injection : les deux clics arrivaient au même
/// instant, un client Unity n'interrogeant l'entrée qu'une fois par image n'en voyait qu'un, et
/// la téléportation ne partait pas. L'étape affichait pourtant « ×2 » — le défaut était
/// entièrement du côté de l'émission, donc invisible à la lecture de la macro.
///
/// Ces tests vérifient le rythme réellement produit : combien de clics, séparés de combien, et
/// tous au même endroit.
/// </summary>
public class MultiClickTests
{
    private const int Interval = 80;

    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildSolo()
    {
        var windows = new FakeWindowManager();
        windows.AddWindow(1, "Meneur", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        windows.Foreground = 1;

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        profile.Settings.MultiClickIntervalMs = Interval;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static Macro MacroOf(params MacroStep[] steps)
    {
        var macro = new Macro { Name = "Clic", RestoreInitialWindow = false, RestoreCursorPosition = false };
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
    public async Task Un_double_clic_produit_deux_clics_separes_par_l_intervalle()
    {
        var (windows, roster, profile) = BuildSolo();

        var actions = await RunAsync(
            MacroOf(new MouseClickStep { Fx = 0.5, Fy = 0.5, Clicks = 2 }), windows, roster, profile);

        // Le point décisif : une attente *entre* les deux clics, et non deux clics collés.
        var rhythm = actions.Where(a => a is RecordedAction.Click or RecordedAction.Delay).ToList();
        Assert.Collection(rhythm,
            a => Assert.IsType<RecordedAction.Click>(a),
            a => Assert.Equal(new RecordedAction.Delay(Interval), a),
            a => Assert.IsType<RecordedAction.Click>(a));

        // Et les deux au même endroit : un double-clic dont les moitiés se séparent n'en est plus un.
        var clicks = actions.OfType<RecordedAction.Click>().ToList();
        Assert.Equal(clicks[0].Point, clicks[1].Point);
        Assert.Equal(MouseButton.Left, clicks[0].Button);
    }

    [Fact]
    public async Task Un_clic_simple_n_attend_pas()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.ActionDelayMs = 0;

        var actions = await RunAsync(
            MacroOf(new MouseClickStep { Fx = 0.5, Fy = 0.5 }), windows, roster, profile);

        // Le cas courant ne doit pas ralentir : aucune attente ajoutée pour un clic seul.
        Assert.Single(actions.OfType<RecordedAction.Click>());
        Assert.DoesNotContain(actions, a => a is RecordedAction.Delay);
    }

    [Fact]
    public async Task Un_triple_clic_produit_trois_clics_et_deux_intervalles()
    {
        var (windows, roster, profile) = BuildSolo();

        var actions = await RunAsync(
            MacroOf(new MouseClickStep { Fx = 0.5, Fy = 0.5, Clicks = 3 }), windows, roster, profile);

        Assert.Equal(3, actions.OfType<RecordedAction.Click>().Count());
        Assert.Equal(2, actions.OfType<RecordedAction.Delay>().Count(d => d.Milliseconds == Interval));
    }

    [Fact]
    public async Task L_intervalle_est_distinct_du_delai_entre_actions()
    {
        var (windows, roster, profile) = BuildSolo();
        profile.Settings.ActionDelayMs = 500;

        var actions = await RunAsync(
            MacroOf(new MouseClickStep { Fx = 0.5, Fy = 0.5, Clicks = 2 }), windows, roster, profile);

        // L'écart entre les deux clics vaut l'intervalle, pas le délai entre actions, qui ne
        // s'applique qu'une fois l'étape terminée.
        var rhythm = actions.Where(a => a is RecordedAction.Click or RecordedAction.Delay).ToList();
        Assert.Equal(new RecordedAction.Delay(Interval), rhythm[1]);
        Assert.Equal(new RecordedAction.Delay(500), rhythm[3]);
    }

    [Fact]
    public async Task Le_rejeu_sur_l_equipe_ne_ralentit_pas_l_intervalle()
    {
        var (windows, roster, profile) = BuildSolo();

        // C'est le bug d'origine sous sa forme la plus dangereuse : le rejeu sur l'équipe impose
        // 600 ms entre actions, très au-delà du seuil de double-clic du système. Appliqué entre
        // les deux moitiés d'un même geste, il redonnerait deux clics indépendants.
        var actions = await RunAsync(
            MacroOf(new MouseClickStep { Fx = 0.5, Fy = 0.5, Clicks = 2 }),
            windows, roster, profile, actionDelayOverride: 600);

        // Le geste garde son rythme propre ; le délai du rejeu ne s'applique qu'une fois l'étape
        // finie, où il a son sens — laisser au jeu le temps d'ouvrir ce que le clic a demandé.
        var rhythm = actions.Where(a => a is RecordedAction.Click or RecordedAction.Delay).ToList();
        Assert.Collection(rhythm,
            a => Assert.IsType<RecordedAction.Click>(a),
            a => Assert.Equal(new RecordedAction.Delay(Interval), a),
            a => Assert.IsType<RecordedAction.Click>(a),
            a => Assert.Equal(new RecordedAction.Delay(600), a));
    }

    [Fact]
    public async Task Une_etape_ancree_ne_cherche_l_image_qu_une_fois()
    {
        var windows = new FakeWindowManager
        {
            Screen = VirtualScreen.Single(1920, 1080),
            Surface = new PixelBuffer(1920, 1080),
        };
        windows.AddWindow(1, "Meneur", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        windows.Foreground = 1;

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;
        profile.Settings.MultiClickIntervalMs = Interval;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);

        // Un motif reconnaissable, décalé de 40 px sous la position enregistrée.
        var patch = new PixelBuffer(24, 24);
        var shape = new Random(7);
        for (int y = 0; y < 24; y++)
        {
            for (int x = 0; x < 24; x++)
            {
                byte r = (byte)shape.Next(200, 256), g = (byte)shape.Next(0, 60), b = (byte)shape.Next(120, 200);
                patch.SetPixel(x, y, r, g, b);
                windows.Surface.SetPixel(400 - 12 + x, 340 - 12 + y, r, g, b);
            }
        }

        var actions = await RunAsync(
            MacroOf(new MouseClickStep
            {
                Fx = 0.5,
                Fy = 0.5,
                Clicks = 2,
                Anchor = ImageAnchor.FromPixelBuffer(patch, 12, 12),
            }),
            windows, roster, profile);

        // Une seule capture d'écran : rechercher l'image entre les deux clics relancerait la
        // reconnaissance sur une ligne dont l'aspect vient de changer, et le second clic
        // pourrait atterrir ailleurs.
        Assert.Single(windows.Captures);

        var clicks = actions.OfType<RecordedAction.Click>().ToList();
        Assert.Equal(2, clicks.Count);
        Assert.Equal(clicks[0].Point, clicks[1].Point);
        Assert.Equal(
            CoordinateMapper.ToAbsolute(new ScreenPoint(400, 340), windows.GetVirtualScreen()),
            clicks[0].Point);
    }

    [Fact]
    public async Task L_arret_d_urgence_interrompt_pendant_l_intervalle()
    {
        var (windows, roster, profile) = BuildSolo();

        // L'attente entre deux clics ne doit pas être un angle mort de l'arrêt d'urgence.
        using var source = new CancellationTokenSource();
        var runner = new MacroRunner(windows, new FakeInputSender(windows.Actions), new CancellingClock(source, 1));

        var result = await runner.RunAsync(
            MacroOf(new MouseClickStep { Fx = 0.5, Fy = 0.5, Clicks = 3 }),
            roster, profile.Settings, source.Token);

        Assert.Equal(MacroOutcome.Cancelled, result.Outcome);
        Assert.True(windows.Actions.OfType<RecordedAction.Click>().Count() < 3);
    }
}
