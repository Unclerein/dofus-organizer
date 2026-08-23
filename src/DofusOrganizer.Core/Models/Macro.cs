using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace DofusOrganizer.Core.Models;

public sealed class Macro : NotifyBase
{
    private string _name = "Nouvelle macro";
    private Hotkey? _hotkey;
    private bool _restoreInitialWindow = true;
    private bool _restoreCursorPosition = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    /// <summary>Raccourci qui déclenche la macro. Null tant qu'aucune touche n'est assignée.</summary>
    public Hotkey? Hotkey
    {
        get => _hotkey;
        set { if (Set(ref _hotkey, value)) Raise(nameof(Summary)); }
    }

    /// <summary>Rendre le focus à la fenêtre de départ une fois la macro terminée.</summary>
    public bool RestoreInitialWindow
    {
        get => _restoreInitialWindow;
        set => Set(ref _restoreInitialWindow, value);
    }

    /// <summary>Remettre le curseur là où il était avant la macro.</summary>
    public bool RestoreCursorPosition
    {
        get => _restoreCursorPosition;
        set => Set(ref _restoreCursorPosition, value);
    }

    private ObservableCollection<MacroStep> _steps = [];

    public ObservableCollection<MacroStep> Steps
    {
        get => _steps;
        set
        {
            _steps.CollectionChanged -= OnStepsChanged;
            _steps = value ?? [];
            _steps.CollectionChanged += OnStepsChanged;
            Raise(nameof(Summary));
        }
    }

    public Macro() => _steps.CollectionChanged += OnStepsChanged;

    private void OnStepsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => Raise(nameof(Summary));

    [JsonIgnore]
    public string Summary => $"{Steps.Count} étape(s) — {Hotkey?.ToString() ?? "aucun raccourci"}";
}
