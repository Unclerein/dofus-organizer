using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Vision;

namespace DofusOrganizer.Core.Tests;

/// <summary>Une action observée pendant l'exécution d'une macro, pour comparer la séquence obtenue à celle attendue.</summary>
public abstract record RecordedAction
{
    public sealed record Focus(nint Handle, bool Succeeded) : RecordedAction;
    public sealed record Click(AbsolutePoint Point, MouseButton Button, int Clicks) : RecordedAction;
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

    /// <summary>
    /// Écran simulé, dans lequel les captures découpent. Les tests y dessinent ce qu'ils
    /// veulent voir trouvé, ce qui rend le rejeu ancré à l'image vérifiable sans Windows.
    /// </summary>
    public PixelBuffer Surface { get; set; } = new(0, 0);

    public List<ScreenRect> Captures { get; } = [];

    public PixelBuffer? CaptureScreen(ScreenRect area)
    {
        Captures.Add(area);
        if (area.IsEmpty || Surface.IsEmpty) return null;
        return Surface.Crop(area.X, area.Y, area.Width, area.Height);
    }
}

public sealed class FakeInputSender(List<RecordedAction> actions) : IInputSender
{
    public ScreenPoint Cursor { get; set; } = new(640, 400);

    public void MoveMouse(AbsolutePoint point) => actions.Add(new RecordedAction.Move(point));

    public void Click(AbsolutePoint point, MouseButton button, int clicks)
        => actions.Add(new RecordedAction.Click(point, button, clicks));

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
