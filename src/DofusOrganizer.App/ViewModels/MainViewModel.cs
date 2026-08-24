using System.Collections.ObjectModel;
using System.Windows;
using DofusOrganizer.App.Services;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly OrganizerService _service;
    private CharacterRowViewModel? _selectedCharacter;
    private MacroEditorViewModel? _selectedMacro;
    private BroadcastKey? _selectedBroadcast;
    private SlowKey? _selectedSlowKey;
    private string _status = "Prêt.";
    private bool _macroRunning;
    private bool _recording;

    public MainViewModel(OrganizerService service)
    {
        _service = service;

        _service.RosterChanged += RefreshCharacters;
        _service.LogMessage += message => Status = message;
        _service.ElevationWarning += message => Status = message;
        _service.MacroRunningChanged += running =>
        {
            MacroRunning = running;
            if (!running) RefreshCommands();
        };

        _service.MacrosChanged += SyncMacros;
        _service.RecordingChanged += OnRecordingChanged;
        _service.RecordingFinished += OnRecordingFinished;

        // La description de l'étape, et non un simple compteur : l'utilisateur voit en
        // direct la position capturée et repère immédiatement une valeur aberrante.
        _service.Recorder.StepRecorded += step =>
            Status = $"Capturé ({_service.Recorder.Steps.Count}) : {step.Description}";

        // Les commandes sont créées avant toute affectation de sélection : les
        // accesseurs de SelectedMacro et SelectedCharacter appellent RefreshCommands(),
        // qui les parcourt. Les remplir plus tard laisserait des références nulles.
        MoveCharacterUpCommand = new RelayCommand(() => MoveCharacter(-1), () => SelectedCharacter is not null);
        MoveCharacterDownCommand = new RelayCommand(() => MoveCharacter(+1), () => SelectedCharacter is not null);
        AssignCharacterHotkeyCommand = new RelayCommand(async () => await AssignCharacterHotkeyAsync(), () => SelectedCharacter is not null);
        ClearCharacterHotkeyCommand = new RelayCommand(ClearCharacterHotkey, () => SelectedCharacter is not null);
        FocusCharacterCommand = new RelayCommand(FocusCharacter, () => SelectedCharacter?.IsPresent == true);
        ForgetCharacterCommand = new RelayCommand(ForgetCharacter, () => SelectedCharacter?.IsPresent == false);

        AddMacroCommand = new RelayCommand(AddMacro);
        DeleteMacroCommand = new RelayCommand(DeleteMacro, () => SelectedMacro is not null);
        AssignMacroHotkeyCommand = new RelayCommand(async () => await AssignMacroHotkeyAsync(), () => SelectedMacro is not null);
        ClearMacroHotkeyCommand = new RelayCommand(ClearMacroHotkey, () => SelectedMacro is not null);
        RunMacroCommand = new RelayCommand(async () => await RunMacroAsync(), () => SelectedMacro is not null && !MacroRunning);
        StopMacroCommand = new RelayCommand(_service.CancelMacro, () => MacroRunning);

        AddStepCommand = new RelayCommand(AddStep, () => SelectedMacro is not null);
        RemoveStepCommand = new RelayCommand(() => SelectedMacro?.RemoveSelected(), () => SelectedMacro?.HasSelection == true);
        MoveStepUpCommand = new RelayCommand(() => SelectedMacro?.MoveSelected(-1), () => SelectedMacro?.HasSelection == true);
        MoveStepDownCommand = new RelayCommand(() => SelectedMacro?.MoveSelected(+1), () => SelectedMacro?.HasSelection == true);

        ToggleRecordingCommand = new RelayCommand(ToggleRecording, () => SelectedMacro is not null);
        CaptureAnchorCommand = new RelayCommand(async () => await CaptureAnchorAsync(), () => SelectedMacro?.HasSelection == true);
        ClearAnchorCommand = new RelayCommand(ClearAnchor, () => SelectedMacro?.HasSelection == true);

        AddBroadcastCommand = new RelayCommand(AddBroadcast);
        DeleteBroadcastCommand = new RelayCommand(DeleteBroadcast, () => SelectedBroadcast is not null);
        AssignBroadcastTriggerCommand = new RelayCommand(async () => await AssignBroadcastHotkeyAsync(trigger: true), () => SelectedBroadcast is not null);
        AssignBroadcastKeyCommand = new RelayCommand(async () => await AssignBroadcastHotkeyAsync(trigger: false), () => SelectedBroadcast is not null);
        RunBroadcastCommand = new RelayCommand(async () => await RunBroadcastAsync(), () => SelectedBroadcast is not null && !MacroRunning);

        AddSlowKeyCommand = new RelayCommand(AddSlowKey);
        DeleteSlowKeyCommand = new RelayCommand(DeleteSlowKey, () => SelectedSlowKey is not null);
        AssignSlowKeyCommand = new RelayCommand(async () => await AssignSlowKeyAsync(), () => SelectedSlowKey is not null);

        AssignNextHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.Next));
        AssignPreviousHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.Previous));
        AssignPanicHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.Panic));
        AssignToggleRecordingHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.ToggleRecording));
        AssignRepeatOnTeamHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.RepeatOnTeam));
        SaveCommand = new RelayCommand(() => { _service.ApplyBindings(); Status = $"Profil enregistré dans {_service.ProfilePath}"; });
        RefreshCommand = new RelayCommand(_service.Refresh);

        SyncMacros();
        SelectedMacro = Macros.FirstOrDefault();

        foreach (var broadcast in Profile.Broadcasts) Broadcasts.Add(broadcast);
        SelectedBroadcast = Broadcasts.FirstOrDefault();

        foreach (var slow in Settings.SlowKeys) SlowKeys.Add(slow);
        SelectedSlowKey = SlowKeys.FirstOrDefault();

        RefreshCharacters();
    }

    public Profile Profile => _service.Profile;

    public AppSettings Settings => _service.Profile.Settings;

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public ObservableCollection<MacroEditorViewModel> Macros { get; } = [];

    /// <summary>
    /// Reflet observable de <see cref="Profile.Broadcasts"/>, qui est une simple liste : les
    /// deux sont modifiées ensemble, l'une pour l'affichage, l'autre pour ce qui est écrit
    /// dans le profil.
    /// </summary>
    public ObservableCollection<BroadcastKey> Broadcasts { get; } = [];

    /// <summary>Reflet observable de <see cref="AppSettings.SlowKeys"/>, modifié de pair avec elle.</summary>
    public ObservableCollection<SlowKey> SlowKeys { get; } = [];

    private StepKind _newStepKind = StepKind.Clic;

    public StepKind NewStepKind
    {
        get => _newStepKind;
        set => Set(ref _newStepKind, value);
    }

    public CharacterRowViewModel? SelectedCharacter
    {
        get => _selectedCharacter;
        set { if (Set(ref _selectedCharacter, value)) RefreshCommands(); }
    }

    public MacroEditorViewModel? SelectedMacro
    {
        get => _selectedMacro;
        set
        {
            if (_selectedMacro is not null) _selectedMacro.PropertyChanged -= OnMacroChanged;
            if (Set(ref _selectedMacro, value))
            {
                if (_selectedMacro is not null) _selectedMacro.PropertyChanged += OnMacroChanged;
                RefreshCommands();
            }
        }
    }

    private void OnMacroChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RefreshCommands();

    public BroadcastKey? SelectedBroadcast
    {
        get => _selectedBroadcast;
        set
        {
            if (!Set(ref _selectedBroadcast, value)) return;
            Raise(nameof(HasBroadcast));
            RefreshCommands();
        }
    }

    public bool HasBroadcast => _selectedBroadcast is not null;

    public SlowKey? SelectedSlowKey
    {
        get => _selectedSlowKey;
        set
        {
            if (!Set(ref _selectedSlowKey, value)) return;
            Raise(nameof(HasSlowKey));
            RefreshCommands();
        }
    }

    public bool HasSlowKey => _selectedSlowKey is not null;

    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    public bool MacroRunning
    {
        get => _macroRunning;
        private set { if (Set(ref _macroRunning, value)) RefreshCommands(); }
    }

    public bool IsRecording
    {
        get => _recording;
        private set
        {
            if (!Set(ref _recording, value)) return;
            Raise(nameof(RecordingLabel));
        }
    }

    public string RecordingLabel => IsRecording ? "Arrêter l'enregistrement" : "Enregistrer";

    public RelayCommand MoveCharacterUpCommand { get; }
    public RelayCommand MoveCharacterDownCommand { get; }
    public RelayCommand AssignCharacterHotkeyCommand { get; }
    public RelayCommand ClearCharacterHotkeyCommand { get; }
    public RelayCommand FocusCharacterCommand { get; }
    public RelayCommand ForgetCharacterCommand { get; }
    public RelayCommand AddMacroCommand { get; }
    public RelayCommand DeleteMacroCommand { get; }
    public RelayCommand AssignMacroHotkeyCommand { get; }
    public RelayCommand ClearMacroHotkeyCommand { get; }
    public RelayCommand RunMacroCommand { get; }
    public RelayCommand StopMacroCommand { get; }
    public RelayCommand AddStepCommand { get; }
    public RelayCommand RemoveStepCommand { get; }
    public RelayCommand MoveStepUpCommand { get; }
    public RelayCommand MoveStepDownCommand { get; }
    public RelayCommand ToggleRecordingCommand { get; }
    public RelayCommand CaptureAnchorCommand { get; }
    public RelayCommand ClearAnchorCommand { get; }
    public RelayCommand AddBroadcastCommand { get; }
    public RelayCommand DeleteBroadcastCommand { get; }
    public RelayCommand AssignBroadcastTriggerCommand { get; }
    public RelayCommand AssignBroadcastKeyCommand { get; }
    public RelayCommand RunBroadcastCommand { get; }
    public RelayCommand AddSlowKeyCommand { get; }
    public RelayCommand DeleteSlowKeyCommand { get; }
    public RelayCommand AssignSlowKeyCommand { get; }
    public RelayCommand AssignNextHotkeyCommand { get; }
    public RelayCommand AssignPreviousHotkeyCommand { get; }
    public RelayCommand AssignPanicHotkeyCommand { get; }
    public RelayCommand AssignToggleRecordingHotkeyCommand { get; }
    public RelayCommand AssignRepeatOnTeamHotkeyCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private string _rosterSignature = "";

    /// <summary>
    /// Aligne la liste affichée sur les macros du profil. Nécessaire parce que le rejeu sur
    /// l'équipe y range sa dernière capture sans passer par l'interface : sans cette
    /// synchronisation, la macro existerait dans le fichier mais resterait invisible.
    /// </summary>
    private void SyncMacros()
    {
        foreach (var editor in Macros.Where(e => !Profile.Macros.Contains(e.Macro)).ToList())
        {
            Macros.Remove(editor);
        }

        for (int index = 0; index < Profile.Macros.Count; index++)
        {
            var macro = Profile.Macros[index];
            var editor = Macros.FirstOrDefault(e => ReferenceEquals(e.Macro, macro));

            if (editor is null) Macros.Insert(Math.Min(index, Macros.Count), new MacroEditorViewModel(macro));
            else if (Macros.IndexOf(editor) != index && index < Macros.Count) Macros.Move(Macros.IndexOf(editor), index);
        }

        // Une capture qui vient de remplacer la macro sélectionnée doit rester sélectionnée,
        // sinon l'utilisateur perd de vue ce qu'il vient d'enregistrer.
        SelectedMacro ??= Macros.FirstOrDefault();
        if (SelectedMacro is not null && !Macros.Contains(SelectedMacro)) SelectedMacro = Macros.FirstOrDefault();
    }

    private void RefreshCharacters()
    {
        // La détection tourne chaque seconde. Reconstruire la liste à chaque passage
        // annulerait la saisie d'un nom en cours et ferait sauter la sélection sans
        // arrêt : on ne la rebâtit que si sa composition a réellement changé.
        string signature = string.Join('|', _service.Roster.Entries.Select(
            e => $"{e.Slot.Key}:{e.Window?.Handle ?? 0}"));

        if (signature == _rosterSignature)
        {
            Raise(nameof(PresentCount));
            return;
        }
        _rosterSignature = signature;

        var selected = SelectedCharacter?.Slot;

        Characters.Clear();
        for (int i = 0; i < _service.Roster.Entries.Count; i++)
        {
            Characters.Add(new CharacterRowViewModel(_service.Roster.Entries[i], i + 1));
        }

        SelectedCharacter = Characters.FirstOrDefault(c => ReferenceEquals(c.Slot, selected));
        Raise(nameof(PresentCount));
    }

    public string PresentCount
    {
        get
        {
            int present = _service.Roster.Entries.Count(e => e.IsPresent);
            return present switch
            {
                0 => "Aucun client Dofus détecté",
                1 => "1 client Dofus détecté",
                _ => $"{present} clients Dofus détectés",
            };
        }
    }

    private void MoveCharacter(int delta)
    {
        if (SelectedCharacter is null) return;
        _service.Roster.Move(SelectedCharacter.Slot, delta, Profile.Characters);
        _service.Refresh();
        _service.Save();
    }

    private void FocusCharacter()
    {
        if (SelectedCharacter is not null) _service.FocusSlot(SelectedCharacter.Slot);
    }

    private void ForgetCharacter()
    {
        if (SelectedCharacter is null) return;
        Profile.Characters.Remove(SelectedCharacter.Slot);
        _service.Refresh();
        _service.ApplyBindings();
    }

    private async Task AssignCharacterHotkeyAsync()
    {
        if (SelectedCharacter is null) return;
        var hotkey = await CaptureAsync($"Appuyez sur la touche pour « {SelectedCharacter.DisplayName} »");
        if (hotkey is null) return;
        SelectedCharacter.Slot.Hotkey = hotkey;
        SelectedCharacter.RefreshHotkey();
        _service.ApplyBindings();
        InvalidateCharacters();
    }

    private void ClearCharacterHotkey()
    {
        if (SelectedCharacter is null) return;
        SelectedCharacter.Slot.Hotkey = null;
        SelectedCharacter.RefreshHotkey();
        _service.ApplyBindings();
        InvalidateCharacters();
    }

    /// <summary>Force la reconstruction de la liste au prochain rafraîchissement.</summary>
    private void InvalidateCharacters()
    {
        _rosterSignature = "";
        RefreshCharacters();
    }

    private void AddMacro()
    {
        var macro = new Macro { Name = $"Macro {Profile.Macros.Count + 1}" };
        Profile.Macros.Add(macro);
        var editor = new MacroEditorViewModel(macro);
        Macros.Add(editor);
        SelectedMacro = editor;
        _service.ApplyBindings();
    }

    private void DeleteMacro()
    {
        if (SelectedMacro is null) return;
        if (MessageBox.Show($"Supprimer la macro « {SelectedMacro.Macro.Name} » ?", "Dofus Organizer",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Profile.Macros.Remove(SelectedMacro.Macro);
        Macros.Remove(SelectedMacro);
        SelectedMacro = Macros.FirstOrDefault();
        _service.ApplyBindings();
    }

    private async Task AssignMacroHotkeyAsync()
    {
        if (SelectedMacro is null) return;
        var hotkey = await CaptureAsync($"Appuyez sur la touche pour « {SelectedMacro.Macro.Name} »");
        if (hotkey is null) return;
        SelectedMacro.Macro.Hotkey = hotkey;
        _service.ApplyBindings();
    }

    private void ClearMacroHotkey()
    {
        if (SelectedMacro is null) return;
        SelectedMacro.Macro.Hotkey = null;
        _service.ApplyBindings();
    }

    private async Task RunMacroAsync()
    {
        if (SelectedMacro is null) return;
        Status = $"Exécution de « {SelectedMacro.Macro.Name} »…";
        await _service.RunMacroAsync(SelectedMacro.Macro);
        Status = $"Macro « {SelectedMacro.Macro.Name} » terminée.";
    }

    private void AddStep()
    {
        MacroStep step = NewStepKind switch
        {
            StepKind.Clic => new MouseClickStep(),
            StepKind.Glisser => new MouseDragStep(),
            StepKind.Deplacement => new MouseMoveStep(),
            StepKind.Touche => new KeyStep(),
            StepKind.Attente => new DelayStep(),
            StepKind.AttenteImage => new WaitForImageStep(),
            StepKind.Molette => new ScrollStep(),
            StepKind.Focus => new FocusStep(),
            _ => new ForEachCharacterStep(),
        };
        SelectedMacro?.Add(step);
    }

    private void ToggleRecording()
    {
        // Le raccourci clavier peut être pressé alors qu'aucune macro n'est sélectionnée :
        // prendre la première est plus utile que d'ignorer la demande, l'utilisateur étant
        // alors dans le jeu et hors de portée de la barre d'état.
        SelectedMacro ??= Macros.FirstOrDefault();
        if (SelectedMacro is null)
        {
            Status = "Créez d'abord une macro avant d'enregistrer.";
            return;
        }

        _service.ToggleRecording();
    }

    private void OnRecordingChanged(bool recording)
    {
        IsRecording = recording;
        if (!recording) return;

        var settings = _service.Profile.Settings;

        if (_service.IsTeamRepeatCapture)
        {
            Status = settings.RepeatOnTeamHotkey is { IsEmpty: false } teamKey
                ? $"Faites l'action sur ce personnage, puis {teamKey} pour la refaire sur les autres."
                : "Capture pour l'équipe en cours.";
            return;
        }

        Status = settings.ToggleRecordingHotkey is { IsEmpty: false } hotkey
            ? $"Enregistrement en cours — {hotkey} pour arrêter."
            : "Enregistrement en cours — cliquez de nouveau sur le bouton pour arrêter.";
    }

    private void OnRecordingFinished(IReadOnlyList<MacroStep> steps)
    {
        ApplyRecordedSteps(steps);
        _service.Save();
    }

    private void ApplyRecordedSteps(IReadOnlyList<MacroStep> steps)
    {
        if (SelectedMacro is null) return;
        if (steps.Count == 0) { Status = "Aucune action capturée."; return; }

        // Si la macro contient déjà une boucle sur les personnages, les étapes
        // enregistrées sur un seul personnage y sont déposées : c'est la façon
        // naturelle d'écrire un soin d'équipe, une fois pour tous. Aucune confirmation
        // n'est demandée — la capture s'arrête souvent au clavier depuis le jeu en plein
        // écran, où une boîte de dialogue serait au mieux gênante.
        var loop = SelectedMacro.FindLoop();
        if (loop is not null)
        {
            loop.Steps.Clear();
            foreach (var step in steps) loop.Steps.Add(step);
            SelectedMacro.SelectedStep = loop;
            Status = $"{steps.Count} action(s) placée(s) dans la boucle « pour chaque personnage ».";
            return;
        }

        SelectedMacro.Replace(steps);
        Status = $"{steps.Count} action(s) enregistrée(s).";
    }

    /// <summary>
    /// Relève l'image que l'étape sélectionnée devra reconnaître, au prochain clic dans un
    /// client Dofus. Passer par un vrai clic plutôt que par des coordonnées à saisir : on
    /// désigne ce qu'on voit.
    /// </summary>
    private async Task CaptureAnchorAsync()
    {
        var step = SelectedMacro?.SelectedStep;
        if (step is not PointerStep)
        {
            Status = "Sélectionnez d'abord une étape qui vise un point à l'écran.";
            return;
        }

        Status = "Cliquez sur l'élément à reconnaître, dans une fenêtre Dofus.";

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var anchor = await _service.CaptureAnchorAsync(timeout.Token);
            if (anchor is null) return;

            ((PointerStep)step).Anchor = anchor;

            _service.Save();
            Status = $"Image de {anchor.Width}×{anchor.Height} px capturée.";
        }
        catch (OperationCanceledException)
        {
            _service.CancelHotkeyCapture();
            Status = "Capture d'image annulée.";
        }
    }

    private void ClearAnchor()
    {
        if (SelectedMacro?.SelectedStep is PointerStep pointer)
        {
            pointer.Anchor = null;
            _service.Save();
            Status = "Ancrage retiré : le clic visera sa position enregistrée.";
        }
    }

    private void AddBroadcast()
    {
        var broadcast = new BroadcastKey { Name = $"Diffusion {Profile.Broadcasts.Count + 1}" };
        Profile.Broadcasts.Add(broadcast);
        Broadcasts.Add(broadcast);
        SelectedBroadcast = broadcast;
        _service.ApplyBindings();
    }

    private void DeleteBroadcast()
    {
        if (SelectedBroadcast is null) return;

        // Aucune confirmation : une diffusion tient en trois champs et se refait en dix
        // secondes, là où une macro représente une capture entière.
        Profile.Broadcasts.Remove(SelectedBroadcast);
        Broadcasts.Remove(SelectedBroadcast);
        SelectedBroadcast = Broadcasts.FirstOrDefault();
        _service.ApplyBindings();
    }

    /// <summary>
    /// Assigne l'un des deux raccourcis d'une diffusion : celui qui la déclenche, ou la touche
    /// qui part dans le jeu. À la création, choisir le déclencheur remplit aussi la touche
    /// envoyée — c'est le cas courant, et laisser une diffusion sans rien à envoyer ne servirait
    /// personne.
    /// </summary>
    private async Task AssignBroadcastHotkeyAsync(bool trigger)
    {
        if (SelectedBroadcast is null) return;

        string label = trigger
            ? $"déclencher « {SelectedBroadcast.Name} »"
            : $"envoyer à l'équipe pour « {SelectedBroadcast.Name} »";

        var hotkey = await CaptureAsync($"Appuyez sur la touche pour {label}");
        if (hotkey is null) return;

        if (trigger)
        {
            SelectedBroadcast.Trigger = hotkey;
            SelectedBroadcast.Sent ??= hotkey;
        }
        else
        {
            SelectedBroadcast.Sent = hotkey;
        }

        _service.ApplyBindings();
    }

    private async Task RunBroadcastAsync()
    {
        if (SelectedBroadcast is null) return;
        await _service.BroadcastAsync(SelectedBroadcast);
    }

    private void AddSlowKey()
    {
        var slow = new SlowKey();
        Settings.SlowKeys.Add(slow);
        SlowKeys.Add(slow);
        SelectedSlowKey = slow;
        _service.Save();
    }

    private void DeleteSlowKey()
    {
        if (SelectedSlowKey is null) return;

        Settings.SlowKeys.Remove(SelectedSlowKey);
        SlowKeys.Remove(SelectedSlowKey);
        SelectedSlowKey = SlowKeys.FirstOrDefault();
        _service.Save();
    }

    private async Task AssignSlowKeyAsync()
    {
        if (SelectedSlowKey is null) return;

        var hotkey = await CaptureAsync("Appuyez sur la touche dont l'effet tarde");
        if (hotkey is null) return;

        SelectedSlowKey.Key = hotkey;
        _service.Save();
    }

    private enum SettingHotkey { Next, Previous, Panic, ToggleRecording, RepeatOnTeam }

    private async Task AssignSettingHotkeyAsync(SettingHotkey which)
    {
        string label = which switch
        {
            SettingHotkey.Next => "passer au personnage suivant",
            SettingHotkey.Previous => "revenir au personnage précédent",
            SettingHotkey.ToggleRecording => "démarrer et arrêter l'enregistrement",
            SettingHotkey.RepeatOnTeam => "refaire l'action sur le reste de l'équipe",
            _ => "l'arrêt d'urgence",
        };

        var hotkey = await CaptureAsync($"Appuyez sur la touche pour {label}");
        if (hotkey is null) return;

        switch (which)
        {
            case SettingHotkey.Next: Settings.NextCharacterHotkey = hotkey; break;
            case SettingHotkey.Previous: Settings.PreviousCharacterHotkey = hotkey; break;
            case SettingHotkey.ToggleRecording: Settings.ToggleRecordingHotkey = hotkey; break;
            case SettingHotkey.RepeatOnTeam: Settings.RepeatOnTeamHotkey = hotkey; break;
            default: Settings.PanicHotkey = hotkey; break;
        }

        _service.ApplyBindings();
    }

    /// <summary>Attend la prochaine frappe pour en faire un raccourci. Échap annule.</summary>
    private async Task<Hotkey?> CaptureAsync(string prompt)
    {
        Status = prompt + " — Échap pour annuler.";
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var hotkey = await _service.CaptureHotkeyAsync(timeout.Token);
            Status = $"Raccourci assigné : {hotkey}";
            return hotkey;
        }
        catch (OperationCanceledException)
        {
            _service.CancelHotkeyCapture();
            Status = "Assignation annulée.";
            return null;
        }
    }

    private void RefreshCommands()
    {
        // Les éléments nuls sont tolérés : cette méthode est déclenchée par les
        // accesseurs de sélection, qui peuvent être atteints avant la fin du
        // constructeur. Planter là rendrait l'application impossible à ouvrir.
        RelayCommand?[] commands =
        [
            MoveCharacterUpCommand, MoveCharacterDownCommand, AssignCharacterHotkeyCommand,
            ClearCharacterHotkeyCommand, FocusCharacterCommand, ForgetCharacterCommand,
            DeleteMacroCommand, AssignMacroHotkeyCommand, ClearMacroHotkeyCommand,
            RunMacroCommand, StopMacroCommand, AddStepCommand, RemoveStepCommand,
            MoveStepUpCommand, MoveStepDownCommand, ToggleRecordingCommand,
            CaptureAnchorCommand, ClearAnchorCommand,
            DeleteBroadcastCommand, AssignBroadcastTriggerCommand, AssignBroadcastKeyCommand,
            RunBroadcastCommand, DeleteSlowKeyCommand, AssignSlowKeyCommand,
        ];

        foreach (var command in commands) command?.RaiseCanExecuteChanged();
    }
}

public enum StepKind { Clic, Glisser, Deplacement, Touche, Attente, AttenteImage, Molette, Focus, PourChaquePersonnage }
