using System.Windows.Threading;
using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Config;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Windows;

namespace DofusOrganizer.App.Services;

/// <summary>
/// Assemble les briques de l'organizer : détection des fenêtres, raccourcis, exécution
/// des macros et sauvegarde du profil. L'interface ne parle qu'à cette classe.
/// </summary>
public sealed class OrganizerService : IDisposable, ILogSink
{
    private readonly ProfileStore _store;
    private readonly Win32WindowManager _windows = new();
    private readonly SendInputSender _input = new();
    private readonly MacroRunner _runner;
    private readonly HotkeyDispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly Dispatcher _uiDispatcher;

    private CancellationTokenSource? _macroCancellation;

    public OrganizerService(Dispatcher uiDispatcher, string? profilePath = null)
    {
        _uiDispatcher = uiDispatcher;
        _store = new ProfileStore(profilePath ?? ProfileStore.DefaultPath);
        Profile = _store.Load();

        _runner = new MacroRunner(_windows, _input, new SystemClock(), this);
        _runner.RunningChanged += running => _uiDispatcher.BeginInvoke(() => MacroRunningChanged?.Invoke(running));

        _dispatcher = new HotkeyDispatcher(_windows.GetForegroundWindow, IsGameWindow);
        _dispatcher.ActionTriggered += OnHotkey;

        Recorder = new MacroRecorder(_windows, SlotIndexOf);

        // Une seconde suffit : ouvrir un client prend plus de temps que ça, et un
        // intervalle plus court ferait tourner une énumération de fenêtres pour rien.
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, uiDispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) => Refresh();
    }

    public Profile Profile { get; private set; }

    public CharacterRoster Roster { get; } = new();

    public MacroRecorder Recorder { get; }

    public bool IsMacroRunning => _runner.IsRunning;

    public event Action? RosterChanged;
    public event Action<bool>? MacroRunningChanged;
    public event Action<string>? LogMessage;

    /// <summary>Signalé quand un client Dofus tourne avec plus de privilèges que l'organizer.</summary>
    public event Action<string>? ElevationWarning;

    public void Start()
    {
        // Les hooks bas niveau doivent être posés depuis un fil disposant d'une boucle
        // de messages : c'est le cas du fil d'interface, d'où l'appel depuis ici.
        _dispatcher.Install();
        _dispatcher.Update(Profile);
        Refresh();
        _refreshTimer.Start();
    }

    /// <summary>Relit la liste des fenêtres Dofus et met la liste de personnages à jour.</summary>
    public void Refresh()
    {
        var windows = _windows.EnumerateGameWindows(Profile.Settings);
        int before = Profile.Characters.Count;

        Roster.Sync(windows, Profile.Characters);

        // Les processus disparus ne doivent pas faire enfler le cache indéfiniment.
        if (_elevationByProcess.Count > 0)
        {
            var alive = windows.Select(w => w.ProcessId).ToHashSet();
            foreach (int pid in _elevationByProcess.Keys.Where(pid => !alive.Contains(pid)).ToList())
                _elevationByProcess.Remove(pid);
        }

        if (Profile.Characters.Count != before) Save();
        RosterChanged?.Invoke();
        CheckElevation(windows);
    }

    private string? _lastElevationWarning;
    private readonly Dictionary<int, bool> _elevationByProcess = [];

    private void CheckElevation(IReadOnlyList<Core.Models.GameWindow> windows)
    {
        foreach (var window in windows)
        {
            // Le résultat est mis en cache par processus : le privilège d'un client ne
            // change pas en cours de route, et le test s'exécute à chaque seconde.
            if (!_elevationByProcess.TryGetValue(window.ProcessId, out bool elevated))
            {
                elevated = Win32WindowManager.LooksElevated(window.ProcessId);
                _elevationByProcess[window.ProcessId] = elevated;
            }
            if (!elevated) continue;

            string message =
                $"Le client « {window.CharacterName} » semble lancé en administrateur. " +
                "Ni les raccourcis ni les macros ne l'atteindront tant que Dofus Organizer " +
                "ne l'est pas aussi — relancez l'un ou l'autre au même niveau de privilèges.";

            if (message == _lastElevationWarning) return;
            _lastElevationWarning = message;
            ElevationWarning?.Invoke(message);
            return;
        }
        _lastElevationWarning = null;
    }

    public void ApplyBindings()
    {
        _dispatcher.Update(Profile);
        Save();
    }

    public void Save() => _store.Save(Profile);

    public string ProfilePath => _store.Path;

    /// <summary>
    /// Démarre une capture de macro. Les raccourcis sont mis en sommeil le temps de
    /// l'enregistrement, pour que les touches frappées finissent dans la macro plutôt
    /// que de déclencher une autre macro.
    /// </summary>
    public void StartRecording()
    {
        _dispatcher.Enabled = false;
        Recorder.Start();
    }

    public IReadOnlyList<MacroStep> StopRecording()
    {
        var steps = Recorder.Stop();
        _dispatcher.Enabled = true;
        return steps;
    }

    public Task<Hotkey> CaptureHotkeyAsync(CancellationToken cancellationToken)
        => _dispatcher.CaptureNextAsync(cancellationToken);

    public void CancelHotkeyCapture() => _dispatcher.CancelCapture();

    public bool IsGameWindow(nint handle) => Roster.ByHandle(handle) is not null;

    private int SlotIndexOf(nint handle)
    {
        for (int i = 0; i < Roster.Entries.Count; i++)
        {
            if (Roster.Entries[i].Window?.Handle == handle) return i;
        }
        return -1;
    }

    public void FocusNext() => Activate(Roster.Next(_windows.GetForegroundWindow()));

    public void FocusPrevious() => Activate(Roster.Previous(_windows.GetForegroundWindow()));

    public void FocusSlot(CharacterSlot slot)
        => Activate(Roster.Entries.FirstOrDefault(e => ReferenceEquals(e.Slot, slot)));

    private void Activate(RosterEntry? entry)
    {
        if (entry?.Window is null) return;
        _windows.Activate(entry.Window.Handle);
    }

    public async Task RunMacroAsync(Macro macro)
    {
        // La macro précédente est annulée avant d'en lancer une autre : c'est le
        // comportement attendu quand on presse la mauvaise touche et qu'on se reprend.
        _macroCancellation?.Cancel();
        _macroCancellation?.Dispose();
        _macroCancellation = new CancellationTokenSource();

        var result = await _runner.RunAsync(macro, Roster, Profile.Settings, _macroCancellation.Token)
            .ConfigureAwait(false);

        if (result.Outcome == MacroOutcome.Failed) Log($"Échec : {result.Message}");
    }

    public void CancelMacro()
    {
        if (_macroCancellation is null) return;
        _macroCancellation.Cancel();
        Log("Arrêt d'urgence : macro interrompue.");
    }

    private void OnHotkey(HotkeyAction action)
    {
        // Le fil de travail du répartiteur n'a pas le droit de toucher à l'interface.
        _uiDispatcher.BeginInvoke(() =>
        {
            switch (action.Kind)
            {
                case HotkeyActionKind.FocusNext: FocusNext(); break;
                case HotkeyActionKind.FocusPrevious: FocusPrevious(); break;
                case HotkeyActionKind.FocusSlot when action.Slot is not null: FocusSlot(action.Slot); break;
                case HotkeyActionKind.Panic: CancelMacro(); break;
                case HotkeyActionKind.RunMacro when action.Macro is not null:
                    _ = RunMacroAsync(action.Macro);
                    break;
            }
        });
    }

    public void Log(string message)
        => _uiDispatcher.BeginInvoke(() => LogMessage?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}"));

    public void Dispose()
    {
        _refreshTimer.Stop();
        _macroCancellation?.Cancel();
        _macroCancellation?.Dispose();
        _dispatcher.Dispose();
        Recorder.Dispose();
    }
}
