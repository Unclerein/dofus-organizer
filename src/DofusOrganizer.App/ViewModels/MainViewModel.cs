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

        AssignNextHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.Next));
        AssignPreviousHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.Previous));
        AssignPanicHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.Panic));
        AssignToggleRecordingHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.ToggleRecording));
        AssignRepeatOnTeamHotkeyCommand = new RelayCommand(async () => await AssignSettingHotkeyAsync(SettingHotkey.RepeatOnTeam));
        SaveCommand = new RelayCommand(() => { _service.ApplyBindings(); Status = $"Profil enregistré dans {_service.ProfilePath}"; });
        RefreshCommand = new RelayCommand(_service.Refresh);

        foreach (var macro in Profile.Macros) Macros.Add(new MacroEditorViewModel(macro));
        SelectedMacro = Macros.FirstOrDefault();

        RefreshCharacters();
    }

    public Profile Profile => _service.Profile;

    public AppSettings Settings => _service.Profile.Settings;

    public ObservableCollection<CharacterRowViewModel> Characters { get; } = [];

    public ObservableCollection<MacroEditorViewModel> Macros { get; } = [];

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
    public RelayCommand AssignNextHotkeyCommand { get; }
    public RelayCommand AssignPreviousHotkeyCommand { get; }
    public RelayCommand AssignPanicHotkeyCommand { get; }
    public RelayCommand AssignToggleRecordingHotkeyCommand { get; }
    public RelayCommand AssignRepeatOnTeamHotkeyCommand { get; }
    public RelayCommand SaveCommand { get; }
    public RelayCommand RefreshCommand { get; }

    private string _rosterSignature = "";

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
        if (step is not MouseClickStep and not WaitForImageStep)
        {
            Status = "Sélectionnez d'abord une étape de clic ou d'attente sur image.";
            return;
        }

        Status = "Cliquez sur l'élément à reconnaître, dans une fenêtre Dofus.";

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var anchor = await _service.CaptureAnchorAsync(timeout.Token);
            if (anchor is null) return;

            switch (step)
            {
                case MouseClickStep click: click.Anchor = anchor; break;
                case WaitForImageStep wait: wait.Anchor = anchor; break;
            }

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
        if (SelectedMacro?.SelectedStep is MouseClickStep click)
        {
            click.Anchor = null;
            _service.Save();
            Status = "Ancrage retiré : le clic visera sa position enregistrée.";
        }
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
        ];

        foreach (var command in commands) command?.RaiseCanExecuteChanged();
    }
}

public enum StepKind { Clic, Deplacement, Touche, Attente, AttenteImage, Molette, Focus, PourChaquePersonnage }
