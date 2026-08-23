using System.Collections.Concurrent;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Windows.Hooks;

namespace DofusOrganizer.Windows;

/// <summary>
/// Écoute le clavier et la souris, décide si une frappe correspond à un raccourci et,
/// le cas échéant, l'absorbe puis fait exécuter l'action ailleurs.
///
/// Le rappel du hook doit rendre la main en quelques millisecondes : au-delà du délai
/// que Windows tolère, le système désinstalle le hook sans prévenir et tous les
/// raccourcis cessent de fonctionner. Tout le travail réel part donc dans une file
/// traitée par un fil dédié.
/// </summary>
public sealed class HotkeyDispatcher : IDisposable
{
    private readonly KeyboardHook _keyboard = new();
    private readonly MouseHook _mouse = new();
    private readonly BlockingCollection<HotkeyAction> _queue = new();
    private readonly Thread _worker;
    private readonly Func<nint, bool> _isGameWindow;
    private readonly Func<nint> _getForegroundWindow;

    /// <summary>
    /// Mis à faux pendant un enregistrement de macro : sans cela, une touche assignée
    /// pressée au cours de la capture déclencherait la macro correspondante au lieu
    /// d'être simplement enregistrée.
    /// </summary>
    public bool Enabled { get; set; } = true;

    private volatile HotkeyBindings _bindings = HotkeyBindings.Build(new Profile());
    private volatile AppSettings _settings = new();
    private TaskCompletionSource<Hotkey>? _capture;

    /// <summary>Touches actuellement maintenues, pour ne déclencher qu'au premier appui et non à la répétition automatique.</summary>
    private readonly HashSet<int> _heldKeys = [];

    /// <summary>
    /// Touches dont l'appui a été absorbé. Leur relâchement doit l'être aussi :
    /// un jeu qui reçoit un relâchement sans l'appui correspondant peut se retrouver
    /// avec une touche considérée comme coincée.
    /// </summary>
    private readonly HashSet<int> _swallowed = [];

    public HotkeyDispatcher(Func<nint> getForegroundWindow, Func<nint, bool> isGameWindow)
    {
        _getForegroundWindow = getForegroundWindow;
        _isGameWindow = isGameWindow;

        _keyboard.KeyEventReceived = OnKey;
        _mouse.MouseEventReceived = OnMouse;

        _worker = new Thread(ProcessQueue) { IsBackground = true, Name = "DofusOrganizer.Hotkeys" };
        _worker.Start();
    }

    /// <summary>Action déclenchée par un raccourci, levée sur le fil de travail et non sur celui du hook.</summary>
    public event Action<HotkeyAction>? ActionTriggered;

    /// <summary>Doit être appelé depuis le fil d'interface : les hooks bas niveau exigent une boucle de messages.</summary>
    public void Install()
    {
        _keyboard.Install();
        _mouse.Install();
    }

    public void Update(Profile profile)
    {
        _bindings = HotkeyBindings.Build(profile);
        _settings = profile.Settings;
    }

    /// <summary>
    /// Met le répartiteur en écoute de la prochaine touche pour l'assigner à un raccourci.
    /// Pendant la capture toutes les frappes sont absorbées, afin que la touche choisie
    /// ne parte pas aussi dans le jeu.
    /// </summary>
    public Task<Hotkey> CaptureNextAsync(CancellationToken cancellationToken)
    {
        var capture = new TaskCompletionSource<Hotkey>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancellationToken.Register(() => capture.TrySetCanceled());
        _capture = capture;
        return capture.Task;
    }

    public void CancelCapture()
    {
        _capture?.TrySetCanceled();
        _capture = null;
    }

    private bool OnKey(KeyEvent e)
    {
        if (e.IsInjected) return false;

        if (!e.IsDown)
        {
            _heldKeys.Remove(e.VirtualKey);
            return _swallowed.Remove(e.VirtualKey);
        }

        var capture = _capture;
        if (capture is not null)
        {
            // Échap annule, un modificateur seul ne peut pas être le corps d'un raccourci.
            if (e.VirtualKey == VirtualKeys.Escape) { capture.TrySetCanceled(); _capture = null; return true; }
            if (VirtualKeys.IsModifierKey(e.VirtualKey)) return true;
            capture.TrySetResult(new Hotkey(e.VirtualKey, e.Modifiers));
            _capture = null;
            return true;
        }

        // La répétition automatique du clavier renverrait la macro en boucle tant que la touche est tenue.
        if (!_heldKeys.Add(e.VirtualKey)) return _swallowed.Contains(e.VirtualKey);

        return Remember(e.VirtualKey, Dispatch(e.VirtualKey, e.Modifiers));
    }

    /// <summary>Note qu'une touche a été absorbée, pour absorber également son relâchement.</summary>
    private bool Remember(int virtualKey, bool swallowed)
    {
        if (swallowed) _swallowed.Add(virtualKey);
        return swallowed;
    }

    private bool OnMouse(MouseEvent e)
    {
        if (e.IsInjected || e.ExtraButton is null) return false;

        if (!e.IsDown)
        {
            _heldKeys.Remove(e.ExtraButton.Value);
            return _swallowed.Remove(e.ExtraButton.Value);
        }

        var capture = _capture;
        if (capture is not null)
        {
            capture.TrySetResult(new Hotkey(e.ExtraButton.Value, ModifierState.Current()));
            _capture = null;
            return true;
        }

        if (!_heldKeys.Add(e.ExtraButton.Value)) return _swallowed.Contains(e.ExtraButton.Value);

        return Remember(e.ExtraButton.Value, Dispatch(e.ExtraButton.Value, ModifierState.Current()));
    }

    private bool Dispatch(int virtualKey, KeyModifiers modifiers)
    {
        if (!Enabled) return false;

        var action = _bindings.Resolve(virtualKey, modifiers);
        if (action is null) return false;

        var settings = _settings;

        // L'arrêt d'urgence doit rester joignable même quand le jeu n'a pas le focus :
        // c'est le seul moyen de reprendre la main sur une macro partie de travers.
        if (action.Kind != HotkeyActionKind.Panic
            && settings.HotkeysOnlyWhenGameFocused
            && !_isGameWindow(_getForegroundWindow()))
        {
            return false;
        }

        _queue.Add(action);
        return settings.SwallowBoundKeys || action.Kind == HotkeyActionKind.Panic;
    }

    private void ProcessQueue()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            try { ActionTriggered?.Invoke(action); }
            catch { /* Une action en échec ne doit pas emporter le fil de traitement. */ }
        }
    }

    public void Dispose()
    {
        CancelCapture();
        _keyboard.Dispose();
        _mouse.Dispose();
        _queue.CompleteAdding();
        _worker.Join(TimeSpan.FromSeconds(1));
        _queue.Dispose();
    }
}
