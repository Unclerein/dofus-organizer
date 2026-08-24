using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Core.Vision;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Le glisser-déposer sert à replacer un panneau dessiné par le jeu, que le système ne
/// connaît pas comme une fenêtre et ne peut donc pas déplacer. Ce qui compte : le bouton
/// reste tenu pendant tout le trajet, et le point de saisie suit l'image quand le panneau
/// s'est ouvert ailleurs.
/// </summary>
public class DragTests
{
    private const int PatchSize = 32;
    private const int PatchOffset = PatchSize / 2;

    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildTeam()
    {
        var windows = new FakeWindowManager
        {
            Screen = VirtualScreen.Single(1920, 1080),
            Surface = new PixelBuffer(1920, 1080),
        };

        for (int y = 0; y < 1080; y++)
        {
            for (int x = 0; x < 1920; x++)
            {
                windows.Surface.SetPixel(x, y, (byte)((x * 5 + y) % 200), (byte)((y * 7) % 200), (byte)((x * 3) % 200));
            }
        }

        windows.AddWindow(1, "Meneur", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        windows.AddWindow(2, "Second", new ClientBounds(new ScreenPoint(800, 0), 800, 600));

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static PixelBuffer Draw(PixelBuffer surface, int x, int y)
    {
        var patch = new PixelBuffer(PatchSize, PatchSize);
        var shape = new Random(77);

        for (int dy = 0; dy < PatchSize; dy++)
        {
            for (int dx = 0; dx < PatchSize; dx++)
            {
                byte red = (byte)shape.Next(200, 256);
                byte green = (byte)shape.Next(0, 50);
                byte blue = (byte)shape.Next(0, 50);
                patch.SetPixel(dx, dy, red, green, blue);
                surface.SetPixel(x + dx, y + dy, red, green, blue);
            }
        }

        return patch;
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
    public async Task Le_point_de_saisie_suit_l_image_quand_le_panneau_s_est_ouvert_ailleurs()
    {
        // Le cas d'usage : replacer un panneau identiquement sur tous les personnages alors
        // qu'il ne s'ouvre pas au même endroit chez chacun.
        var (windows, roster, profile) = BuildTeam();

        var patch = Draw(windows.Surface, 200 - PatchOffset, 150 - PatchOffset);
        Draw(windows.Surface, 1100 - PatchOffset, 260 - PatchOffset);

        var macro = new Macro
        {
            Name = "Recentrer sur l'équipe",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new ForEachCharacterStep
                {
                    Steps =
                    {
                        new MouseDragStep
                        {
                            Fx = 0.25, Fy = 0.25,
                            ToFx = 0.5, ToFy = 0.5,
                            IntermediateMoves = 0,
                            Anchor = ImageAnchor.FromPixelBuffer(patch, PatchOffset, PatchOffset),
                        },
                    },
                },
            },
        };

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        var screen = windows.GetVirtualScreen();
        var presses = actions.OfType<RecordedAction.ButtonPress>().Where(b => b.Down).ToList();
        Assert.Equal(2, presses.Count);

        // Chez le second, la saisie vise 1100/260 — l'image retrouvée — et non 1000/150,
        // qui est la position enregistrée transposée dans sa fenêtre.
        Assert.Equal(CoordinateMapper.ToAbsolute(new ScreenPoint(200, 150), screen), presses[0].Point);
        Assert.Equal(CoordinateMapper.ToAbsolute(new ScreenPoint(1100, 260), screen), presses[1].Point);

        // L'arrivée, elle, reste la même position dans chaque fenêtre : c'est ce qui aligne
        // le panneau identiquement d'un personnage à l'autre.
        var releases = actions.OfType<RecordedAction.ButtonPress>().Where(b => !b.Down).ToList();
        Assert.Equal(CoordinateMapper.ToAbsolute(new ScreenPoint(400, 300), screen), releases[0].Point);
        Assert.Equal(CoordinateMapper.ToAbsolute(new ScreenPoint(1200, 300), screen), releases[1].Point);
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
