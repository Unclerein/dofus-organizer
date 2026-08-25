using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>Une action observée pendant l'exécution d'une macro, pour comparer la séquence obtenue à celle attendue.</summary>
public abstract record RecordedAction
{
    public sealed record Focus(nint Handle, bool Succeeded) : RecordedAction;
    public sealed record Click(AbsolutePoint Point, MouseButton Button) : RecordedAction;
    public sealed record Move(AbsolutePoint Point) : RecordedAction;
    public sealed record Key(int VirtualKey, KeyModifiers Modifiers, KeyAction Action) : RecordedAction;
    public sealed record Wheel(AbsolutePoint Point, int Notches) : RecordedAction;
    public sealed record ButtonPress(AbsolutePoint Point, MouseButton Which, bool Down) : RecordedAction;
    public sealed record Delay(int Milliseconds) : RecordedAction;
    public sealed record Cursor(ScreenPoint Point) : RecordedAction;
}

public sealed class FakeWindowManager : IWindowManager
{
    private readonly Dictionary<nint, ClientBounds> _bounds = [];

    public List<RecordedAction> Actions { get; } = [];
    public List<GameWindow> Windows { get; } = [];
    public nint Foreground { get; set; }
    public VirtualScreen Screen { get; set; } = VirtualScreen.Single(1920, 1080);

    /// <summary>Fenêtres que Activate doit refuser, pour simuler un refus de premier plan par Windows.</summary>
    public HashSet<nint> ActivationFailures { get; } = [];

    public GameWindow AddWindow(nint handle, string character, ClientBounds bounds)
    {
        var window = new GameWindow(handle, 1000 + (int)handle, $"{character} - Dofus", "Dofus", "UnityWndClass")
        {
            CharacterName = character,
        };
        Windows.Add(window);
        _bounds[handle] = bounds;
        return window;
    }

    public IReadOnlyList<GameWindow> EnumerateGameWindows(AppSettings settings) => Windows;

    public nint GetForegroundWindow() => Foreground;

    public nint WindowUnder(ScreenPoint point)
    {
        foreach (var (handle, bounds) in _bounds)
        {
            if (point.X >= bounds.Origin.X && point.X < bounds.Origin.X + bounds.Width
                && point.Y >= bounds.Origin.Y && point.Y < bounds.Origin.Y + bounds.Height)
            {
                return handle;
            }
        }
        return 0;
    }

    public bool Activate(nint handle)
    {
        bool ok = !ActivationFailures.Contains(handle);
        Actions.Add(new RecordedAction.Focus(handle, ok));
        if (ok) Foreground = handle;
        return ok;
    }

    public bool IsWindow(nint handle) => _bounds.ContainsKey(handle);

    public bool TryGetClientBounds(nint handle, out ClientBounds bounds) => _bounds.TryGetValue(handle, out bounds);

    public VirtualScreen GetVirtualScreen() => Screen;
}

public sealed class FakeInputSender(List<RecordedAction> actions) : IInputSender
{
    public ScreenPoint Cursor { get; set; } = new(640, 400);

    public void MoveMouse(AbsolutePoint point) => actions.Add(new RecordedAction.Move(point));

    public void Click(AbsolutePoint point, MouseButton button)
        => actions.Add(new RecordedAction.Click(point, button));

    public void SendKey(int virtualKey, KeyModifiers modifiers, KeyAction action, bool useScanCodes)
        => actions.Add(new RecordedAction.Key(virtualKey, modifiers, action));

    public void Scroll(AbsolutePoint point, int notches) => actions.Add(new RecordedAction.Wheel(point, notches));

    public void PressButton(AbsolutePoint point, MouseButton button, bool down)
        => actions.Add(new RecordedAction.ButtonPress(point, button, down));

    public ScreenPoint GetCursorPosition() => Cursor;

    public void SetCursorPosition(ScreenPoint point)
    {
        Cursor = point;
        actions.Add(new RecordedAction.Cursor(point));
    }
}

/// <summary>Horloge qui note les attentes sans jamais dormir, pour que les tests restent instantanés.</summary>
public sealed class FakeClock(List<RecordedAction> actions) : IClock
{
    public int TotalDelay { get; private set; }

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (milliseconds > 0)
        {
            TotalDelay += milliseconds;
            actions.Add(new RecordedAction.Delay(milliseconds));
        }
        return Task.CompletedTask;
    }
}

/// <summary>Horloge qui déclenche une annulation après un nombre donné d'attentes.</summary>
public sealed class CancellingClock(CancellationTokenSource source, int cancelAfter) : IClock
{
    private int _count;

    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (++_count >= cancelAfter) source.Cancel();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Le montage commun aux séries qui vérifient le rythme d'une macro sur un seul personnage —
/// multi-clic, molette, touches lentes. Les trois le recopiaient mot pour mot, y compris la
/// surcharge de délai ajoutée après coup à <see cref="MacroRunner.RunAsync"/> : trois endroits
/// à corriger le jour où cette signature bougera encore.
/// </summary>
public static class MacroHarness
{
    /// <summary>Un personnage, une fenêtre, et aucune temporisation autre que celles qu'on mesure.</summary>
    public static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildSolo()
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

    public static Macro MacroOf(params MacroStep[] steps)
    {
        var macro = new Macro { Name = "Séquence", RestoreInitialWindow = false, RestoreCursorPosition = false };
        foreach (var step in steps) macro.Steps.Add(step);
        return macro;
    }

    /// <summary>Exécute la macro et rend la suite d'actions observées, dans l'ordre.</summary>
    public static async Task<List<RecordedAction>> RunAsync(
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
}
