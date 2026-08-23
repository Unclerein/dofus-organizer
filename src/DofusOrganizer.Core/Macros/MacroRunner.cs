using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;

namespace DofusOrganizer.Core.Macros;

public enum MacroOutcome { Completed, Cancelled, Failed, Busy }

public sealed record MacroResult(MacroOutcome Outcome, int StepsExecuted, string? Message = null);

/// <summary>
/// Exécute une macro : enchaîne les changements de fenêtre et les entrées injectées.
/// Une seule macro tourne à la fois — deux séquences de clics entrelacées produiraient
/// des actions envoyées à la mauvaise fenêtre.
/// </summary>
public sealed class MacroRunner(IWindowManager windows, IInputSender input, IClock clock, ILogSink? log = null)
{
    private readonly ILogSink _log = log ?? NullLogSink.Instance;
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>Levé à chaque changement d'état, pour que l'interface affiche « macro en cours ».</summary>
    public event Action<bool>? RunningChanged;

    public async Task<MacroResult> RunAsync(Macro macro, CharacterRoster roster, AppSettings settings, CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return new MacroResult(MacroOutcome.Busy, 0, "Une macro est déjà en cours.");
        }

        RunningChanged?.Invoke(true);
        var state = new RunState(windows.GetForegroundWindow(), input.GetCursorPosition());
        state.CurrentTarget = state.InitialWindow;

        try
        {
            await ExecuteAsync(macro.Steps, roster, settings, state, cancellationToken).ConfigureAwait(false);
            await RestoreAsync(macro, settings, state, cancellationToken).ConfigureAwait(false);
            return new MacroResult(MacroOutcome.Completed, state.StepsExecuted);
        }
        catch (OperationCanceledException)
        {
            _log.Log($"Macro « {macro.Name} » interrompue après {state.StepsExecuted} étape(s).");
            // Rendre la main proprement compte plus que finir : on repose le curseur
            // sans consommer le jeton annulé, sinon l'utilisateur le retrouve au hasard de l'écran.
            TryRestoreCursor(macro, state);
            return new MacroResult(MacroOutcome.Cancelled, state.StepsExecuted);
        }
        catch (Exception ex)
        {
            _log.Log($"Macro « {macro.Name} » en échec : {ex.Message}");
            TryRestoreCursor(macro, state);
            return new MacroResult(MacroOutcome.Failed, state.StepsExecuted, ex.Message);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            RunningChanged?.Invoke(false);
        }
    }

    private async Task ExecuteAsync(IReadOnlyList<MacroStep> steps, CharacterRoster roster, AppSettings settings, RunState state, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            ct.ThrowIfCancellationRequested();
            await ExecuteStepAsync(step, roster, settings, state, ct).ConfigureAwait(false);
            state.StepsExecuted++;
        }
    }

    private async Task ExecuteStepAsync(MacroStep step, CharacterRoster roster, AppSettings settings, RunState state, CancellationToken ct)
    {
        switch (step)
        {
            case FocusStep focus:
                await FocusAsync(ResolveTarget(focus, roster, state), settings, state, ct).ConfigureAwait(false);
                break;

            case ForEachCharacterStep loop:
                await RunLoopAsync(loop, roster, settings, state, ct).ConfigureAwait(false);
                break;

            case MouseClickStep click:
                if (TryResolvePoint(click.Point, state, out var clickPoint))
                {
                    input.Click(clickPoint, click.Button, click.Clicks);
                    await clock.DelayAsync(settings.ActionDelayMs, ct).ConfigureAwait(false);
                }
                break;

            case MouseMoveStep move:
                if (TryResolvePoint(move.Point, state, out var movePoint))
                {
                    input.MoveMouse(movePoint);
                    await clock.DelayAsync(settings.ActionDelayMs, ct).ConfigureAwait(false);
                }
                break;

            case KeyStep key:
                input.SendKey(key.VirtualKey, key.Modifiers, key.Action, settings.UseScanCodes);
                await clock.DelayAsync(settings.ActionDelayMs, ct).ConfigureAwait(false);
                break;

            case DelayStep delay:
                await clock.DelayAsync(delay.Milliseconds, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task RunLoopAsync(ForEachCharacterStep loop, CharacterRoster roster, AppSettings settings, RunState state, CancellationToken ct)
    {
        // La liste est figée à l'entrée de la boucle : un client fermé en cours de route
        // ne doit pas décaler les personnages restants.
        var targets = roster.ActiveEntries;
        foreach (var entry in targets)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.Window is null) continue;
            if (loop.SkipCurrentWindow && entry.Window.Handle == state.InitialWindow) continue;

            await FocusAsync(entry, settings, state, ct).ConfigureAwait(false);
            if (state.CurrentTarget != entry.Window.Handle)
            {
                // Sans le focus, les clics partiraient sur la fenêtre précédente.
                _log.Log($"Impossible d'activer « {entry.Slot.DisplayName} », personnage ignoré.");
                continue;
            }

            await ExecuteAsync(loop.Steps, roster, settings, state, ct).ConfigureAwait(false);
        }
    }

    private async Task FocusAsync(RosterEntry? entry, AppSettings settings, RunState state, CancellationToken ct)
    {
        if (entry?.Window is null) return;
        await FocusHandleAsync(entry.Window.Handle, settings, state, ct).ConfigureAwait(false);
    }

    private async Task FocusHandleAsync(nint handle, AppSettings settings, RunState state, CancellationToken ct)
    {
        if (handle == 0) return;
        if (windows.Activate(handle)) state.CurrentTarget = handle;
        await clock.DelayAsync(settings.FocusSettleDelayMs, ct).ConfigureAwait(false);
    }

    private RosterEntry? ResolveTarget(FocusStep step, CharacterRoster roster, RunState state) => step.Target switch
    {
        FocusTarget.Slot => roster.BySlotIndex(step.SlotIndex),
        FocusTarget.Next => roster.Next(state.CurrentTarget),
        FocusTarget.Previous => roster.Previous(state.CurrentTarget),
        FocusTarget.First => roster.First(),
        FocusTarget.Initial => roster.ByHandle(state.InitialWindow),
        _ => null,
    };

    /// <summary>Convertit une position de macro en coordonnée absolue, à partir de la fenêtre visée à cet instant.</summary>
    private bool TryResolvePoint(NormalizedPoint point, RunState state, out AbsolutePoint absolute)
    {
        absolute = default;
        nint handle = state.CurrentTarget != 0 ? state.CurrentTarget : windows.GetForegroundWindow();
        if (handle == 0 || !windows.TryGetClientBounds(handle, out var bounds) || bounds.IsEmpty)
        {
            _log.Log("Étape ignorée : aucune fenêtre cible valide.");
            return false;
        }

        absolute = CoordinateMapper.ToAbsolute(point, bounds, windows.GetVirtualScreen());
        return true;
    }

    private async Task RestoreAsync(Macro macro, AppSettings settings, RunState state, CancellationToken ct)
    {
        if (macro.RestoreInitialWindow && state.InitialWindow != 0 && state.CurrentTarget != state.InitialWindow)
        {
            await FocusHandleAsync(state.InitialWindow, settings, state, ct).ConfigureAwait(false);
        }
        if (macro.RestoreCursorPosition) input.SetCursorPosition(state.InitialCursor);
    }

    private void TryRestoreCursor(Macro macro, RunState state)
    {
        if (!macro.RestoreCursorPosition) return;
        try { input.SetCursorPosition(state.InitialCursor); }
        catch { /* Sur un arrêt d'urgence, échouer ici ne doit rien empêcher. */ }
    }

    private sealed class RunState(nint initialWindow, ScreenPoint initialCursor)
    {
        public nint InitialWindow { get; } = initialWindow;
        public ScreenPoint InitialCursor { get; } = initialCursor;
        public nint CurrentTarget { get; set; }
        public int StepsExecuted { get; set; }
    }
}
