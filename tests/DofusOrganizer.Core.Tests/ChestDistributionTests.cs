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
    public async Task Une_quantite_lue_est_divisee_puis_recollee()
    {
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux", "Trois", "Quatre");
        var clipboard = new FakeClipboard { AnswerOnCopy = "100" };

        await MacroHarness.RunAsync(RepartitionDe(new DistributeQuantityStep()), windows, roster, profile,
            clipboard: clipboard);

        // Quatre personnages : 100/4, puis le stock n'ayant pas bougé dans ce faux, 100/3, 100/2, 100/1.
        // Ce qui compte ici est que la division suive le nombre de personnages restants.
        Assert.Equal(["25", "33", "50", "100"], clipboard.Written);
    }

    [Fact]
    public async Task Le_stock_qui_diminue_se_repartit_en_entier()
    {
        // Le cas réel : chaque personnage relit ce qu'il reste au coffre. Dix items sur quatre
        // donnent 2, 2, 3, 3 — dix distribués, rien d'abandonné. Un quart figé aurait donné
        // 2, 2, 2, 2 et laissé deux items derrière.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux", "Trois", "Quatre");
        var clipboard = new FakeClipboard();
        foreach (string reste in new[] { "10", "8", "6", "3" }) clipboard.Answers.Enqueue(reste);

        await MacroHarness.RunAsync(RepartitionDe(new DistributeQuantityStep()), windows, roster, profile,
            clipboard: clipboard);

        Assert.Equal(["2", "2", "3", "3"], clipboard.Written);
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

        var result = await RunRawAsync(RepartitionDe(new DistributeQuantityStep()), windows, roster, profile, clipboard);

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

        var result = await RunRawAsync(RepartitionDe(new DistributeQuantityStep()), windows, roster, profile, clipboard);

        Assert.Equal(MacroOutcome.Failed, result.Outcome);
        Assert.Empty(clipboard.Written);
    }

    [Fact]
    public async Task Hors_d_une_boucle_la_repartition_echoue_plutot_que_de_deviner()
    {
        // Sans boucle, il n'y a personne à servir : diviser par un reste oublié donnerait
        // n'importe quoi. Mieux vaut le dire à l'auteur de la macro.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un");
        var clipboard = new FakeClipboard { AnswerOnCopy = "100" };

        var result = await RunRawAsync(
            MacroHarness.MacroOf(new DistributeQuantityStep()), windows, roster, profile, clipboard);

        Assert.Equal(MacroOutcome.Failed, result.Outcome);
        Assert.Empty(clipboard.Written);
    }

    [Fact]
    public async Task Un_diviseur_explicite_se_passe_de_boucle()
    {
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
        // Trois items pour quatre personnages : taper zéro ferait n'importe quoi selon le jeu.
        var (windows, roster, profile) = MacroHarness.BuildTeam("Un", "Deux", "Trois", "Quatre");
        var clipboard = new FakeClipboard();
        foreach (string reste in new[] { "3", "3", "3", "3" }) clipboard.Answers.Enqueue(reste);

        var actions = await MacroHarness.RunAsync(RepartitionDe(new DistributeQuantityStep()), windows, roster,
            profile, clipboard: clipboard);

        // Le premier n'a rien (3/4), les suivants se partagent : 3/3, puis le faux rend
        // toujours 3, donc 3/2 puis 3/1.
        Assert.Equal(["1", "1", "3"], clipboard.Written);
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
