using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Core.Vision;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Vérifie que le rejeu vise ce qu'il reconnaît et non la position enregistrée.
/// C'est ce qui permet à une même macro de fonctionner sur des personnages dont
/// l'interface n'est pas dans le même état — liste défilée, panneau décalé.
/// </summary>
public class AnchoredReplayTests
{
    private const int PatchSize = 32;
    private const int PatchOffset = PatchSize / 2;

    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildTeam()
    {
        var windows = new FakeWindowManager
        {
            Screen = VirtualScreen.Single(1920, 1080),
            Surface = Noise(1920, 1080),
        };

        windows.AddWindow(1, "Meneur", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        windows.AddWindow(2, "Second", new ClientBounds(new ScreenPoint(800, 0), 800, 600));

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static PixelBuffer Noise(int width, int height)
    {
        var image = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image.SetPixel(x, y, (byte)((x * 5 + y) % 200), (byte)((y * 7) % 200), (byte)((x * 3) % 200));
            }
        }
        return image;
    }

    /// <summary>Dessine un motif reconnaissable dans l'écran simulé et le renvoie.</summary>
    private static PixelBuffer Draw(PixelBuffer surface, int x, int y)
    {
        var patch = new PixelBuffer(PatchSize, PatchSize);
        var shape = new Random(4242);

        for (int dy = 0; dy < PatchSize; dy++)
        {
            for (int dx = 0; dx < PatchSize; dx++)
            {
                byte red = (byte)shape.Next(200, 256);
                byte green = (byte)shape.Next(0, 60);
                byte blue = (byte)shape.Next(120, 200);
                patch.SetPixel(dx, dy, red, green, blue);
                surface.SetPixel(x + dx, y + dy, red, green, blue);
            }
        }

        return patch;
    }

    [Fact]
    public async Task Le_clic_ancre_suit_l_image_meme_quand_elle_a_bouge()
    {
        var (windows, roster, profile) = BuildTeam();

        // Position enregistrée : le centre de la zone client. Chez le meneur, l'image y est
        // exactement ; chez le second, elle est 40 pixels plus bas — le cas d'une liste qui
        // n'a pas défilé pareil.
        var leaderCorner = new ScreenPoint(400 - PatchOffset, 300 - PatchOffset);
        var secondCorner = new ScreenPoint(1200 - PatchOffset, 340 - PatchOffset);

        var patch = Draw(windows.Surface, leaderCorner.X, leaderCorner.Y);
        Draw(windows.Surface, secondCorner.X, secondCorner.Y);

        var macro = new Macro
        {
            Name = "Ancré",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new ForEachCharacterStep
                {
                    Steps =
                    {
                        new MouseClickStep
                        {
                            Fx = 0.5,
                            Fy = 0.5,
                            Anchor = ImageAnchor.FromPixelBuffer(patch, PatchOffset, PatchOffset),
                        },
                    },
                },
            },
        };

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        var result = await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);

        var clicks = actions.OfType<RecordedAction.Click>().ToList();
        Assert.Equal(2, clicks.Count);

        var screen = windows.GetVirtualScreen();
        Assert.Equal(CoordinateMapper.ToAbsolute(new ScreenPoint(400, 300), screen), clicks[0].Point);

        // Le point qui compte : le second clic vise 340 et non 300, c'est-à-dire l'image
        // retrouvée et non la position enregistrée.
        Assert.Equal(CoordinateMapper.ToAbsolute(new ScreenPoint(1200, 340), screen), clicks[1].Point);
        Assert.NotEqual(CoordinateMapper.ToAbsolute(new ScreenPoint(1200, 300), screen), clicks[1].Point);
    }

    [Fact]
    public async Task Une_image_introuvable_fait_retomber_le_clic_sur_sa_position()
    {
        // Renoncer laisserait la macro à moitié faite sans que rien ne le dise ; cliquer à
        // l'ancienne place peut rater, mais le journal l'indique.
        var (windows, roster, profile) = BuildTeam();

        var absent = new PixelBuffer(PatchSize, PatchSize);
        for (int y = 0; y < PatchSize; y++)
        {
            for (int x = 0; x < PatchSize; x++) absent.SetPixel(x, y, 0, 255, 0);
        }

        var log = new CollectingLog();
        var macro = new Macro
        {
            Name = "Ancre absente",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new FocusStep { Target = FocusTarget.Slot, SlotIndex = 1 },
                new MouseClickStep
                {
                    Fx = 0.5,
                    Fy = 0.5,
                    Anchor = ImageAnchor.FromPixelBuffer(absent, PatchOffset, PatchOffset),
                },
            },
        };

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions), log);

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        var click = Assert.Single(actions.OfType<RecordedAction.Click>());
        Assert.Equal(
            CoordinateMapper.ToAbsolute(new ScreenPoint(1200, 300), windows.GetVirtualScreen()),
            click.Point);
        Assert.Contains(log.Messages, message => message.Contains("non retrouvée"));
    }

    [Fact]
    public async Task L_attente_sur_image_rend_la_main_des_que_l_image_est_la()
    {
        var (windows, roster, profile) = BuildTeam();
        var patch = Draw(windows.Surface, 400 - PatchOffset, 300 - PatchOffset);

        var macro = new Macro
        {
            Name = "Attente",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new FocusStep { Target = FocusTarget.Slot, SlotIndex = 0 },
                new WaitForImageStep
                {
                    Fx = 0.5,
                    Fy = 0.5,
                    TimeoutMs = 5000,
                    Anchor = ImageAnchor.FromPixelBuffer(patch, PatchOffset, PatchOffset),
                },
            },
        };

        var actions = windows.Actions;
        var clock = new FakeClock(actions);
        var runner = new MacroRunner(windows, new FakeInputSender(actions), clock);

        var result = await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);
        Assert.Equal(0, clock.TotalDelay);
    }

    [Fact]
    public async Task L_attente_sur_image_abandonne_au_bout_du_delai_sans_bloquer_la_macro()
    {
        var (windows, roster, profile) = BuildTeam();

        var absent = new PixelBuffer(PatchSize, PatchSize);
        for (int y = 0; y < PatchSize; y++)
        {
            for (int x = 0; x < PatchSize; x++) absent.SetPixel(x, y, 255, 0, 255);
        }

        var log = new CollectingLog();
        var macro = new Macro
        {
            Name = "Attente vaine",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new FocusStep { Target = FocusTarget.Slot, SlotIndex = 0 },
                new WaitForImageStep
                {
                    Fx = 0.5,
                    Fy = 0.5,
                    TimeoutMs = 300,
                    Anchor = ImageAnchor.FromPixelBuffer(absent, PatchOffset, PatchOffset),
                },
                new MouseClickStep { Fx = 0.25, Fy = 0.25 },
            },
        };

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions), log);

        var result = await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);
        Assert.Contains(log.Messages, message => message.Contains("expirée"));

        // La macro se poursuit malgré l'attente déçue : rester bloqué serait pire.
        Assert.Single(actions.OfType<RecordedAction.Click>());
    }

    [Fact]
    public async Task Le_rejeu_sur_l_equipe_saute_le_meneur()
    {
        // Mécanisme sur lequel repose « Refaire sur l'équipe » : la séquence capturée est
        // enveloppée dans une boucle qui ignore la fenêtre au premier plan, puisque le
        // meneur vient de faire l'action lui-même.
        var (windows, roster, profile) = BuildTeam();
        windows.Foreground = 1;

        var macro = new Macro
        {
            Name = "Refaire sur l'équipe",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new ForEachCharacterStep
                {
                    SkipCurrentWindow = true,
                    Steps = { new MouseClickStep { Fx = 0.5, Fy = 0.5 } },
                },
            },
        };

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        Assert.Equal([(nint)2], actions.OfType<RecordedAction.Focus>().Select(f => f.Handle));
        Assert.Single(actions.OfType<RecordedAction.Click>());
    }

    [Fact]
    public async Task Le_rejeu_sur_l_equipe_utilise_son_propre_delai()
    {
        // Les quelques dizaines de millisecondes qui enchaînent des clics de sort ne
        // laissent pas à une liste le temps de s'ouvrir : ce rejeu a son propre délai.
        var (windows, roster, profile) = BuildTeam();
        profile.Settings.ActionDelayMs = 30;
        windows.Foreground = 1;

        var macro = new Macro
        {
            Name = "Refaire sur l'équipe",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new ForEachCharacterStep
                {
                    SkipCurrentWindow = true,
                    Steps = { new MouseClickStep { Fx = 0.5, Fy = 0.5 } },
                },
            },
        };

        var actions = windows.Actions;
        var clock = new FakeClock(actions);
        var runner = new MacroRunner(windows, new FakeInputSender(actions), clock);

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None, actionDelayOverride: 600);

        Assert.Equal(600, clock.TotalDelay);
    }

    [Fact]
    public async Task La_molette_est_envoyee_au_point_demande()
    {
        var (windows, roster, profile) = BuildTeam();

        var macro = new Macro
        {
            Name = "Défilement",
            RestoreInitialWindow = false,
            RestoreCursorPosition = false,
            Steps =
            {
                new FocusStep { Target = FocusTarget.Slot, SlotIndex = 0 },
                new ScrollStep { Fx = 0.5, Fy = 0.5, Direction = ScrollDirection.Down, Notches = 4 },
            },
        };

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        await runner.RunAsync(macro, roster, profile.Settings, CancellationToken.None);

        var wheel = Assert.Single(actions.OfType<RecordedAction.Wheel>());
        Assert.Equal(-4, wheel.Notches);
        Assert.Equal(
            CoordinateMapper.ToAbsolute(new ScreenPoint(400, 300), windows.GetVirtualScreen()),
            wheel.Point);
    }
}

public sealed class CollectingLog : DofusOrganizer.Core.Abstractions.ILogSink
{
    public List<string> Messages { get; } = [];

    public void Log(string message) => Messages.Add(message);
}
