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
    private bool _disposed;

    public OrganizerService(Dispatcher uiDispatcher, string? profilePath = null)
    {
        _uiDispatcher = uiDispatcher;
        _store = new ProfileStore(profilePath ?? ProfileStore.DefaultPath);
        Profile = _store.Load();

        _runner = new MacroRunner(_windows, _input, new SystemClock(), this);
        _dispatcher = new HotkeyDispatcher(_windows.GetForegroundWindow, IsGameWindow);
        _dispatcher.ActionTriggered += OnHotkey;

        Recorder = new MacroRecorder(_windows, SlotIndexOf, _dispatcher.IsRecordingControl);

        // Une seconde suffit : ouvrir un client prend plus de temps que ça, et un
        // intervalle plus court ferait tourner une énumération de fenêtres pour rien.
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, uiDispatcher)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _refreshTimer.Tick += (_, _) =>
        {
            ReleaseStuckKeys();
            Refresh();
        };

        // Abonnement après la création de la minuterie, que la réaction manipule : l'inverse
        // laisserait une capture non encore affectée, du même genre que celle qui rendait
        // autrefois l'application impossible à ouvrir.
        _runner.RunningChanged += running => _uiDispatcher.BeginInvoke(() =>
        {
            // La macro lit la liste de personnages depuis un fil de travail. La rafraîchir
            // pendant ce temps la ferait passer par un état où chaque emplacement a perdu sa
            // fenêtre, et un personnage serait sauté sans que rien ne le signale. Rien n'a
            // besoin d'être détecté pendant qu'une macro se déroule.
            if (running) _refreshTimer.Stop();
            else if (!_disposed) _refreshTimer.Start();

            MacroRunningChanged?.Invoke(running);
        });
    }

    public Profile Profile { get; private set; }

    public CharacterRoster Roster { get; } = new();

    public MacroRecorder Recorder { get; }

    public bool IsMacroRunning => _runner.IsRunning;

    public event Action? RosterChanged;
    public event Action<bool>? MacroRunningChanged;

    /// <summary>
    /// Signale un changement d'état de la capture. Indispensable depuis que le raccourci
    /// clavier peut l'arrêter : le bouton et la barre d'état doivent suivre sans que
    /// l'utilisateur soit repassé par la fenêtre.
    /// </summary>
    public event Action<bool>? RecordingChanged;

    /// <summary>
    /// Levé à la fin d'une capture destinée à une macro, avec les étapes obtenues.
    /// Une capture destinée au rejeu sur l'équipe ne passe pas par là : elle part
    /// directement au moteur.
    /// </summary>
    public event Action<IReadOnlyList<MacroStep>>? RecordingFinished;

    /// <summary>Ce à quoi sert la capture en cours.</summary>
    private enum CapturePurpose { Macro, TeamRepeat }

    private CapturePurpose _purpose = CapturePurpose.Macro;

    /// <summary>Levé quand la liste des macros du profil a changé en dehors de l'interface.</summary>
    public event Action? MacrosChanged;

    /// <summary>Vrai si la capture en cours alimentera un rejeu sur l'équipe.</summary>
    public bool IsTeamRepeatCapture => Recorder.IsRecording && _purpose == CapturePurpose.TeamRepeat;
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

    public bool IsRecording => Recorder.IsRecording;

    /// <summary>
    /// Démarre une capture de macro. Les raccourcis sont mis en sommeil le temps de
    /// l'enregistrement, pour que les touches frappées finissent dans la macro plutôt
    /// que de déclencher une autre macro — la bascule d'enregistrement et l'arrêt
    /// d'urgence restant seuls actifs.
    /// </summary>
    public void StartRecording() => StartCapture(CapturePurpose.Macro);

    public void StopRecording()
    {
        if (!Recorder.IsRecording) return;

        var steps = StopCapture();
        if (_purpose == CapturePurpose.Macro)
        {
            _uiDispatcher.BeginInvoke(() => RecordingFinished?.Invoke(steps));
        }
    }

    public void ToggleRecording()
    {
        if (Recorder.IsRecording) StopRecording();
        else StartRecording();
    }

    /// <summary>
    /// Capture une séquence sur le personnage meneur, puis la rejoue sur tous les autres.
    /// Répond au besoin « les autres font la même chose que moi » sans exiger une macro par
    /// destination : l'outil n'a pas à savoir de quel zaap ni de quel dialogue il s'agit.
    /// </summary>
    public void ToggleTeamRepeat()
    {
        if (Recorder.IsRecording && _purpose == CapturePurpose.TeamRepeat)
        {
            var steps = StopCapture();
            _ = RepeatOnTeamAsync(steps);
            return;
        }

        if (Recorder.IsRecording) StopRecording();
        StartCapture(CapturePurpose.TeamRepeat);
    }

    private void StartCapture(CapturePurpose purpose)
    {
        if (Recorder.IsRecording) return;

        _purpose = purpose;
        ApplyRecorderSettings();

        // Les raccourcis sont mis en sommeil le temps de la capture, pour que les touches
        // frappées finissent dans la séquence plutôt que de déclencher autre chose. Seuls
        // l'arrêt d'urgence et les touches de capture restent actifs.
        _dispatcher.Enabled = false;
        Recorder.Start();
        Notify(recording: true);
    }

    private IReadOnlyList<MacroStep> StopCapture()
    {
        var steps = Recorder.Stop();
        _dispatcher.Enabled = true;
        Notify(recording: false);
        return steps;
    }

    private void ApplyRecorderSettings()
    {
        var settings = Profile.Settings;
        Recorder.CaptureDelays = settings.RecordDelays;

        // Un changement de fenêtre n'a pas de sens dans une séquence rejouée personnage par
        // personnage : l'étape ramènerait chaque tour sur celui qui a été enregistré.
        Recorder.RecordWindowChanges = _purpose != CapturePurpose.TeamRepeat;
    }

    private async Task RepeatOnTeamAsync(IReadOnlyList<MacroStep> steps)
    {
        if (!TeamReplay.HasReplayableSteps(steps))
        {
            Log("Aucune action capturée : rien à refaire sur l'équipe.");
            return;
        }

        var macro = StoreLastTeamCapture(TeamReplay.BuildMacro(steps));

        Log($"Rejeu de {macro.Steps.Count} étape(s) sur le reste de l'équipe…");
        await RunMacroAsync(macro, Profile.Settings.TeamReplayDelayMs).ConfigureAwait(false);
    }

    /// <summary>
    /// Range la séquence capturée dans le profil, sous un nom réservé et remplacée à chaque
    /// usage. Sans cela elle serait exécutée puis jetée, et il n'y aurait rien à inspecter
    /// quand le rejeu déçoit — ni les images capturées, ni l'enchaînement obtenu.
    /// </summary>
    private Macro StoreLastTeamCapture(Macro macro)
    {
        var existing = Profile.Macros.FirstOrDefault(m => m.Name == TeamReplay.MacroName);
        if (existing is not null)
        {
            // Le raccourci éventuellement assigné à cette macro est conservé.
            macro.Id = existing.Id;
            macro.Hotkey = existing.Hotkey;
            Profile.Macros[Profile.Macros.IndexOf(existing)] = macro;
        }
        else
        {
            Profile.Macros.Add(macro);
        }

        Save();
        _uiDispatcher.BeginInvoke(() => MacrosChanged?.Invoke());
        return macro;
    }

    private void Notify(bool recording)
    {
        if (Profile.Settings.RecordingFeedbackSound) PlayFeedback(recording);
        _uiDispatcher.BeginInvoke(() => RecordingChanged?.Invoke(recording));
    }

    /// <summary>
    /// Un bip au démarrage, deux à l'arrêt. Déclenchée depuis le jeu en plein écran, la
    /// capture ne donne aucun signe visible : le son est le seul retour possible.
    /// </summary>
    private static void PlayFeedback(bool recording)
    {
        try
        {
            System.Media.SystemSounds.Beep.Play();
            if (recording) return;

            Task.Delay(140).ContinueWith(_ => System.Media.SystemSounds.Beep.Play(),
                TaskScheduler.Default);
        }
        catch
        {
            // Une machine sans périphérique audio ne doit pas empêcher d'enregistrer.
        }
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

    /// <summary>
    /// Bascule vers un personnage. La fenêtre visée est lue ici, sur le fil d'interface, mais
    /// l'activation part sur un fil de travail.
    ///
    /// <see cref="Win32WindowManager.Activate"/> attend que le client soit réellement prêt, ce
    /// qui peut prendre jusqu'à deux secondes pour un client lourd — au sortir d'un combat, par
    /// exemple. Sur le fil d'interface, cette attente bloque aussi les rappels des hooks bas
    /// niveau, qu'il est le seul à servir : le relâchement de la touche qui vient de déclencher
    /// la bascule s'y perdait, et cette touche restait considérée comme tenue pour toujours.
    /// </summary>
    private void Activate(RosterEntry? entry)
    {
        if (entry?.Window is null) return;

        nint handle = entry.Window.Handle;
        _ = Task.Run(() => _windows.Activate(handle));
    }

    /// <summary>
    /// Libère les touches que l'organizer croit tenues alors que le clavier les dit relâchées.
    /// Un relâchement perdu condamnerait sinon son raccourci jusqu'au redémarrage, pendant que
    /// tous les autres continueraient de répondre.
    /// </summary>
    private void ReleaseStuckKeys()
    {
        int released = _dispatcher.ReleaseStuckKeys();
        if (released > 0) Log($"{released} touche(s) restée(s) bloquée(s) ont été libérées.");
    }

    public Task RunMacroAsync(Macro macro) => RunMacroAsync(macro, actionDelayOverride: null);

    public async Task RunMacroAsync(Macro macro, int? actionDelayOverride)
    {
        // La macro précédente est annulée avant d'en lancer une autre : c'est le
        // comportement attendu quand on presse la mauvaise touche et qu'on se reprend.
        _macroCancellation?.Cancel();
        _macroCancellation?.Dispose();
        _macroCancellation = new CancellationTokenSource();

        var token = _macroCancellation.Token;

        // Hors du fil d'interface : le moteur y enchaîne des activations de fenêtre, des captures
        // d'écran et des recherches d'image, toutes synchrones et coûteuses. Le laisser là
        // bloquerait les rappels des hooks, dont ce fil est le seul serviteur.
        var result = await Task
            .Run(() => _runner.RunAsync(macro, Roster, Profile.Settings, token, actionDelayOverride), token)
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
                case HotkeyActionKind.ToggleRecording: ToggleRecording(); break;
                case HotkeyActionKind.RepeatOnTeam: ToggleTeamRepeat(); break;
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
        _disposed = true;
        _refreshTimer.Stop();
        _macroCancellation?.Cancel();
        _macroCancellation?.Dispose();
        _dispatcher.Dispose();
        Recorder.Dispose();
    }
}
