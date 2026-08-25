using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Le glisser-déposer sert à replacer un panneau dessiné par le jeu, que le système ne
/// connaît pas comme une fenêtre et ne peut donc pas déplacer. Ce qui compte : le bouton
/// reste tenu pendant tout le trajet, et il est relâché quoi qu'il arrive.
/// </summary>
public class DragTests
{
    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildTeam()
    {
        var windows = new FakeWindowManager { Screen = VirtualScreen.Single(1920, 1080) };

        windows.AddWindow(1, "Meneur", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        windows.AddWindow(2, "Second", new ClientBounds(new ScreenPoint(800, 0), 800, 600));

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static Macro DragMacro(MouseDragStep drag) => new()
    {
        Name = "Recentrer",
        RestoreInitialWindow = false,
        RestoreCursorPosition = false,
        Steps = { new FocusStep { Target = FocusTarget.Slot, SlotIndex = 0 }, drag },
    };

    [Fact]
    public async Task Le_bouton_reste_tenu_du_depart_a_l_arrivee()
    {
        var (windows, roster, profile) = BuildTeam();

        var macro = DragMacro(new MouseDragStep
        {
            Fx = 0.25, Fy = 0.25,
            ToFx = 0.75, ToFy = 0.75,
            IntermediateMoves = 3,
        });

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        var screen = windows.GetVirtualScreen();
        var from = CoordinateMapper.ToAbsolute(new ScreenPoint(200, 150), screen);
        var to = CoordinateMapper.ToAbsolute(new ScreenPoint(600, 450), screen);

        // Séquence attendue : aller au départ, enfoncer, traverser, arriver, relâcher.
        var pointer = actions.Where(a => a is RecordedAction.Move or RecordedAction.ButtonPress).ToList();

        Assert.Equal(from, Assert.IsType<RecordedAction.Move>(pointer[0]).Point);

        var press = Assert.IsType<RecordedAction.ButtonPress>(pointer[1]);
        Assert.True(press.Down);
        Assert.Equal(from, press.Point);

        // Trois positions intermédiaires puis le point d'arrivée.
        Assert.Equal(4, pointer.Skip(2).Take(4).OfType<RecordedAction.Move>().Count());
        Assert.Equal(to, Assert.IsType<RecordedAction.Move>(pointer[5]).Point);

        var release = Assert.IsType<RecordedAction.ButtonPress>(pointer[6]);
        Assert.False(release.Down);
        Assert.Equal(to, release.Point);
    }

    [Fact]
    public async Task Le_trajet_progresse_du_depart_vers_l_arrivee()
    {
        // Un saut direct est souvent ignoré : l'interface n'entame le déplacement qu'en
        // voyant le curseur bouger. Les positions doivent donc réellement s'échelonner.
        var (windows, roster, profile) = BuildTeam();

        var macro = DragMacro(new MouseDragStep
        {
            Fx = 0.1, Fy = 0.1,
            ToFx = 0.9, ToFy = 0.9,
            IntermediateMoves = 5,
        });

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        var moves = actions.OfType<RecordedAction.Move>().Select(m => m.Point.X).ToList();
        Assert.Equal(7, moves.Count);                      // départ + 5 intermédiaires + arrivée
        Assert.Equal(moves.OrderBy(x => x), moves);        // strictement croissant vers la droite
        Assert.True(moves.Distinct().Count() == moves.Count);
    }

    [Fact]
    public async Task Le_bouton_est_relache_meme_si_la_macro_est_interrompue()
    {
        // Un bouton laissé enfoncé ferait traîner le panneau derrière le curseur bien après
        // l'arrêt de la macro : le relâchement doit survivre à une annulation.
        var (windows, roster, profile) = BuildTeam();
        using var cts = new CancellationTokenSource();

        var macro = DragMacro(new MouseDragStep { IntermediateMoves = 10 });

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new CancellingClock(cts, cancelAfter: 3));

        var result = await runner.RunAsync(macro, roster, profile.Settings, cts.Token);

        Assert.Equal(MacroOutcome.Cancelled, result.Outcome);
        Assert.Contains(actions.OfType<RecordedAction.ButtonPress>(), b => b.Down);
        Assert.Contains(actions.OfType<RecordedAction.ButtonPress>(), b => !b.Down);
    }
}
