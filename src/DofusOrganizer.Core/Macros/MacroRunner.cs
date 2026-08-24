using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Core.Vision;

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

    /// <param name="actionDelayOverride">
    /// Remplace le délai entre actions le temps de cette exécution. Le rejeu sur l'équipe
    /// enchaîne des interactions d'interface, qui demandent bien plus de temps que des clics
    /// de sort.
    /// </param>
    public async Task<MacroResult> RunAsync(
        Macro macro, CharacterRoster roster, AppSettings settings, CancellationToken cancellationToken,
        int? actionDelayOverride = null)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return new MacroResult(MacroOutcome.Busy, 0, "Une macro est déjà en cours.");
        }

        RunningChanged?.Invoke(true);
        var state = new RunState(windows.GetForegroundWindow(), input.GetCursorPosition(),
            actionDelayOverride ?? settings.ActionDelayMs);
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
                if (TryResolveTarget(click.Point, click.Anchor, state, out var clickPoint))
                {
                    input.Click(clickPoint, click.Button, click.Clicks);
                    await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);
                }
                break;

            case MouseDragStep drag:
                await DragAsync(drag, state, ct).ConfigureAwait(false);
                break;

            case WaitForImageStep wait:
                await WaitForImageAsync(wait, state, ct).ConfigureAwait(false);
                break;

            case ScrollStep scroll:
                if (TryResolvePoint(scroll.Point, state, out var scrollPoint))
                {
                    input.Scroll(scrollPoint, scroll.Direction == ScrollDirection.Up ? scroll.Notches : -scroll.Notches);
                    await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);
                }
                break;

            case MouseMoveStep move:
                if (TryResolvePoint(move.Point, state, out var movePoint))
                {
                    input.MoveMouse(movePoint);
                    await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);
                }
                break;

            case KeyStep key:
                input.SendKey(key.VirtualKey, key.Modifiers, key.Action, settings.UseScanCodes);
                await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);
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

            _log.Log($"→ {entry.Slot.DisplayName}");
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
        if (!TryGetTargetBounds(state, out var bounds)) return false;

        absolute = CoordinateMapper.ToAbsolute(point, bounds, windows.GetVirtualScreen());
        return true;
    }

    private bool TryGetTargetBounds(RunState state, out ClientBounds bounds)
    {
        bounds = default;
        nint handle = state.CurrentTarget != 0 ? state.CurrentTarget : windows.GetForegroundWindow();
        if (handle == 0 || !windows.TryGetClientBounds(handle, out bounds) || bounds.IsEmpty)
        {
            _log.Log("Étape ignorée : aucune fenêtre cible valide.");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Situe le point à viser : par reconnaissance d'image si l'étape porte un ancrage,
    /// par ses coordonnées sinon.
    ///
    /// Un ancrage introuvable ne fait pas échouer l'étape mais la fait retomber sur les
    /// coordonnées, en le signalant. C'est le compromis retenu : cliquer à l'ancienne place
    /// peut rater, mais renoncer laisserait la macro à moitié faite sans que rien ne le dise.
    /// </summary>
    private bool TryResolveTarget(NormalizedPoint point, ImageAnchor? anchor, RunState state, out AbsolutePoint absolute)
    {
        if (!TryResolvePoint(point, state, out absolute)) return false;
        if (anchor is null || anchor.IsEmpty) return true;

        if (!TryGetTargetBounds(state, out var bounds)) return true;

        var expected = CoordinateMapper.ToScreen(point, bounds);
        var located = Locate(anchor, expected, bounds);

        if (located is null)
        {
            _log.Log("Image non retrouvée : l'étape retombe sur sa position enregistrée.");
            return true;
        }

        absolute = CoordinateMapper.ToAbsolute(located.Value, windows.GetVirtualScreen());
        return true;
    }

    /// <summary>Cherche l'image d'un ancrage autour d'une position, et rend le point à cliquer.</summary>
    private ScreenPoint? Locate(ImageAnchor anchor, ScreenPoint expected, ClientBounds bounds)
    {
        var needle = anchor.ToPixelBuffer();
        if (needle is null) return null;

        var area = ScreenRect.Around(expected, anchor.SearchRadius, bounds);
        if (area.IsEmpty) return null;

        var haystack = windows.CaptureScreen(area);
        if (haystack is null) return null;

        var match = TemplateMatcher.Find(haystack, needle, anchor.MinimumScore);
        if (match is null) return null;

        // La position trouvée désigne le coin du fragment ; le clic vise l'endroit qui
        // avait été cliqué à l'intérieur de celui-ci.
        return new ScreenPoint(
            area.X + match.Value.X + anchor.OffsetX,
            area.Y + match.Value.Y + anchor.OffsetY);
    }

    /// <summary>
    /// Saisit un point, déplace en maintenant le bouton, puis relâche.
    ///
    /// Le trajet est parcouru par petits pas plutôt qu'en un saut : beaucoup d'interfaces
    /// n'entament un déplacement qu'en voyant le curseur bouger, et ignorent une position
    /// qui change d'un coup. Le bouton est relâché même si une étape intermédiaire échoue,
    /// sans quoi il resterait enfoncé et l'utilisateur se retrouverait à traîner un panneau.
    /// </summary>
    private async Task DragAsync(MouseDragStep step, RunState state, CancellationToken ct)
    {
        if (!TryResolveTarget(step.Point, step.Anchor, state, out var from)) return;
        if (!TryResolvePoint(step.Destination, state, out var to)) return;

        input.MoveMouse(from);
        await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);

        input.PressButton(from, step.Button, down: true);

        try
        {
            await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);

            for (int move = 1; move <= step.IntermediateMoves; move++)
            {
                double progress = (double)move / (step.IntermediateMoves + 1);
                input.MoveMouse(new AbsolutePoint(
                    (int)Math.Round(from.X + (to.X - from.X) * progress),
                    (int)Math.Round(from.Y + (to.Y - from.Y) * progress)));

                await clock.DelayAsync(DragMoveIntervalMs, ct).ConfigureAwait(false);
            }

            input.MoveMouse(to);
            await clock.DelayAsync(state.ActionDelayMs, ct).ConfigureAwait(false);
        }
        finally
        {
            input.PressButton(to, step.Button, down: false);
        }
    }

    /// <summary>Attente entre deux positions d'un glisser, pour que le trajet reste suivi.</summary>
    private const int DragMoveIntervalMs = 15;

    private async Task WaitForImageAsync(WaitForImageStep step, RunState state, CancellationToken ct)
    {
        if (step.Anchor is not { IsEmpty: false } anchor)
        {
            _log.Log("Attente sur image ignorée : aucune image capturée.");
            return;
        }

        if (!TryGetTargetBounds(state, out var bounds)) return;
        var expected = CoordinateMapper.ToScreen(step.Point, bounds);

        // Le temps écoulé est compté en tours de boucle et non sur l'heure réelle, pour que
        // l'attente dépende de l'horloge injectée et reste donc vérifiable en test. La
        // recherche d'image elle-même prend quelques millisecondes, ce qui allonge un peu
        // l'attente réelle par rapport au délai annoncé : dépasser un maximum est sans
        // conséquence, l'écourter en aurait.
        int elapsed = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            bool present = Locate(anchor, expected, bounds) is not null;
            if (present != step.WaitUntilGone) return;

            if (elapsed >= step.TimeoutMs)
            {
                // Poursuivre plutôt que s'arrêter : les personnages suivants ont peut-être
                // encore une chance d'aboutir, et l'utilisateur voit ce qui a échoué.
                _log.Log($"Attente sur image expirée après {step.TimeoutMs} ms.");
                return;
            }

            await clock.DelayAsync(PollIntervalMs, ct).ConfigureAwait(false);
            elapsed += PollIntervalMs;
        }
    }

    /// <summary>Intervalle entre deux vérifications d'une attente sur image.</summary>
    private const int PollIntervalMs = 60;

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

    private sealed class RunState(nint initialWindow, ScreenPoint initialCursor, int actionDelayMs)
    {
        public nint InitialWindow { get; } = initialWindow;
        public ScreenPoint InitialCursor { get; } = initialCursor;
        public int ActionDelayMs { get; } = actionDelayMs;
        public nint CurrentTarget { get; set; }
        public int StepsExecuted { get; set; }
    }
}
