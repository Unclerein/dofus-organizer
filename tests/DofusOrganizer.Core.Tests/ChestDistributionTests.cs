using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// La répartition du coffre est la seule chose que l'organizer fasse qui soit irréversible en
/// jeu : des items changent de mains et rien ne les ramène. Ces tests portent moins sur ce
/// qu'elle réussit que sur ce qu'elle refuse de faire à l'aveugle.
/// </summary>
public class ChestDistributionTests
{
    private static readonly NormalizedPoint Coffre = new(0.10, 0.20);
    private static readonly NormalizedPoint Depot = new(0.80, 0.60);

    private static Macro RepartitionDe(params MacroStep[] steps)
    {
        var loop = new ForEachCharacterStep { SkipCurrentWindow = false };
        foreach (var step in steps) loop.Steps.Add(step);
        return MacroHarness.MacroOf(loop);
    }

    // ---------------------------------------------------------------- La lecture

    [Fact]
    public async Task Le_diviseur_ne_bouge_pas_d_un_personnage_a_l_autre()
    {
        // Le test qui verrouille la demande. Le stock diminue à mesure que chacun se sert —
        // 100, puis 75, 50, 25 — et pourtant la part ne bouge pas : elle est calculée à la
        // première lecture puis reprise. Rediviser le stock restant à chaque tour donnerait
        // 25, 18, 12, 6, et laisserait un tiers du coffre derrière.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux", "Trois", "Quatre");
        var clipboard = new FakeClipboard();
        foreach (string reste in new[] { "100", "75", "50", "25" }) clipboard.Answers.Enqueue(reste);

        await MacroHarness.RunAsync(
            RepartitionDe(new DistributeQuantityStep { Divisor = 4 }), windows, roster, profile,
            clipboard: clipboard);

        Assert.Equal(["25", "25", "25", "25"], clipboard.Written);
    }

    [Fact]
    public async Task Ce_qui_ne_tombe_pas_juste_reste_au_coffre()
    {
        // Dix items en quatre parts : deux chacun, et deux restent. C'est assumé.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux", "Trois", "Quatre");
        var clipboard = new FakeClipboard();
        foreach (string reste in new[] { "10", "8", "6", "4" }) clipboard.Answers.Enqueue(reste);

        await MacroHarness.RunAsync(
            RepartitionDe(new DistributeQuantityStep { Divisor = 4 }), windows, roster, profile,
            clipboard: clipboard);

        Assert.Equal(["2", "2", "2", "2"], clipboard.Written);
    }

    [Fact]
    public async Task On_ne_reclame_jamais_plus_qu_il_n_y_a()
    {
        // Un diviseur plus petit que l'équipe, ou un coffre déjà entamé : la part mémorisée
        // dépasse ce qui reste, et c'est ce qui reste qui est pris.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux", "Trois");
        var clipboard = new FakeClipboard();
        foreach (string reste in new[] { "100", "50", "10" }) clipboard.Answers.Enqueue(reste);

        await MacroHarness.RunAsync(
            RepartitionDe(new DistributeQuantityStep { Divisor = 2 }), windows, roster, profile,
            clipboard: clipboard);

        Assert.Equal(["50", "50", "10"], clipboard.Written);
    }

    [Fact]
    public async Task Le_presse_papiers_est_vide_avant_chaque_copie()
    {
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux");
        var clipboard = new FakeClipboard { AnswerOnCopy = "40" };

        await MacroHarness.RunAsync(RepartitionDe(new DistributeQuantityStep()), windows, roster, profile,
            clipboard: clipboard);

        Assert.Equal(2, clipboard.Clears);
    }

    // ---------------------------------------------------------------- Les refus

    [Fact]
    public async Task Une_copie_qui_n_a_pas_lieu_arrete_la_macro()
    {
        // Le piège que tout le reste sert à éviter : la boîte de saisie ne s'ouvre pas, le
        // Ctrl+C ne copie rien, et le presse-papiers garde le nombre du personnage précédent.
        // Sans le vidage préalable, la macro le prendrait pour la réponse du jeu.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux");
        var clipboard = new FakeClipboard("9999") { AnswerOnCopy = null };

        var result = await RunRawAsync(
            RepartitionDe(new DistributeQuantityStep()), windows, roster, profile, clipboard);

        Assert.Equal(MacroOutcome.Failed, result.Outcome);
        Assert.Empty(clipboard.Written);
        Assert.Contains("Ctrl+C", result.Message);
    }

    [Theory]
    [InlineData("beaucoup")]
    [InlineData("12 items")]
    [InlineData("-4")]
    public async Task Un_texte_qui_n_est_pas_une_quantite_arrete_la_macro(string copie)
    {
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux");
        var clipboard = new FakeClipboard { AnswerOnCopy = copie };

        var result = await RunRawAsync(
            RepartitionDe(new DistributeQuantityStep()), windows, roster, profile, clipboard);

        Assert.Equal(MacroOutcome.Failed, result.Outcome);
        Assert.Empty(clipboard.Written);
    }

    [Fact]
    public async Task Une_repartition_se_passe_de_boucle()
    {
        // Un diviseur écrit dans l'étape ne dépend de rien d'autre : l'étape est utilisable
        // seule, ce que l'ancien diviseur déduit de la boucle interdisait.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un");
        var clipboard = new FakeClipboard { AnswerOnCopy = "100" };

        await MacroHarness.RunAsync(
            MacroHarness.MacroOf(new DistributeQuantityStep { Divisor = 5 }), windows, roster, profile,
            clipboard: clipboard);

        Assert.Equal("20", Assert.Single(clipboard.Written));
    }

    [Fact]
    public async Task Une_part_nulle_referme_la_boite_sans_rien_ecrire()
    {
        // Trois items en quatre parts : la part vaut zéro, et taper zéro ferait n'importe
        // quoi selon le jeu.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux");
        var clipboard = new FakeClipboard { AnswerOnCopy = "3" };

        var actions = await MacroHarness.RunAsync(
            RepartitionDe(new DistributeQuantityStep { Divisor = 4 }), windows, roster, profile,
            clipboard: clipboard);

        Assert.Empty(clipboard.Written);
        Assert.Contains(actions, a => a is RecordedAction.Key(VirtualKeys.Escape, KeyModifiers.None, _));
    }

    // ---------------------------------------------------------------- La construction

    [Fact]
    public void Les_items_sont_traites_du_dernier_designe_au_premier()
    {
        // Quand une pile se vide, les items qui la suivaient remontent d'une case et les points
        // désignés après elle tombent à côté. En partant de la fin, ce qui disparaît est
        // toujours derrière ce qu'il reste à traiter.
        var items = new List<NormalizedPoint> { new(0.1, 0.1), new(0.2, 0.2), new(0.3, 0.3) };

        var macro = ChestDistribution.BuildMacro(Coffre, Depot, items);

        var drags = Assert.IsType<ForEachCharacterStep>(Assert.Single(macro.Steps))
            .Steps.OfType<MouseDragStep>().ToList();
        Assert.Equal([0.3, 0.2, 0.1], drags.Select(d => d.Fx));
    }

    [Fact]
    public void Chaque_item_donne_un_glisser_vers_le_depot_puis_une_repartition()
    {
        var items = new List<NormalizedPoint> { new(0.1, 0.1), new(0.2, 0.2) };

        var loop = Assert.IsType<ForEachCharacterStep>(
            Assert.Single(ChestDistribution.BuildMacro(Coffre, Depot, items).Steps));

        // Le coffre s'ouvre, on attend, puis deux paires glisser/répartition, puis Échap.
        Assert.Collection(loop.Steps,
            s => Assert.Equal(Coffre, PointOf(Assert.IsType<MouseClickStep>(s))),
            s => Assert.Equal(ChestDistribution.ChestOpenDelayMs, Assert.IsType<DelayStep>(s).Milliseconds),
            s => Assert.Equal(Depot, Assert.IsType<MouseDragStep>(s).Destination),
            s => Assert.IsType<DistributeQuantityStep>(s),
            s => Assert.Equal(Depot, Assert.IsType<MouseDragStep>(s).Destination),
            s => Assert.IsType<DistributeQuantityStep>(s),
            s => Assert.Equal(VirtualKeys.Escape, Assert.IsType<KeyStep>(s).VirtualKey));
    }

    [Fact]
    public void Le_diviseur_demande_se_retrouve_sur_chaque_etape()
    {
        var items = new List<NormalizedPoint> { new(0.1, 0.1), new(0.2, 0.2) };

        var loop = Assert.IsType<ForEachCharacterStep>(
            Assert.Single(ChestDistribution.BuildMacro(Coffre, Depot, items, divisor: 6).Steps));

        Assert.All(loop.Steps.OfType<DistributeQuantityStep>(), step => Assert.Equal(6, step.Divisor));
    }

    [Fact]
    public void Le_meneur_prend_sa_part_comme_les_autres()
    {
        // Il a tout déposé : le sauter le laisserait les mains vides.
        var loop = Assert.IsType<ForEachCharacterStep>(
            Assert.Single(ChestDistribution.BuildMacro(Coffre, Depot, [new(0.1, 0.1)]).Steps));

        Assert.False(loop.SkipCurrentWindow);
    }

    private static NormalizedPoint PointOf(PointerStep step) => step.Point;

    private static async Task<MacroResult> RunRawAsync(
        Macro macro, FakeWindowManager windows, CharacterRoster roster, Profile profile, IClipboard clipboard)
    {
        var runner = new MacroRunner(
            windows, new FakeInputSender(windows.Actions), new FakeClock(windows.Actions), log: null, clipboard);

        return await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);
    }
}
