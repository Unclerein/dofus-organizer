using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;

namespace DofusOrganizer.App.ViewModels;

/// <summary>Une ligne de l'onglet Personnages : l'emplacement persisté et l'état de sa fenêtre.</summary>
public sealed class CharacterRowViewModel(RosterEntry entry, int position) : ObservableObject
{
    public CharacterSlot Slot { get; } = entry.Slot;

    public int Position { get; } = position;

    public string PositionLabel => Position.ToString();

    public string DisplayName
    {
        get => Slot.DisplayName;
        set { Slot.DisplayName = value; Raise(); }
    }

    public bool Enabled
    {
        get => Slot.Enabled;
        set { Slot.Enabled = value; Raise(); }
    }

    public string HotkeyLabel => Slot.Hotkey?.ToString() ?? "(aucun)";

    /// <summary>Titre brut de la fenêtre, affiché pour aider à régler le motif d'extraction des noms.</summary>
    public string WindowTitle => entry.Window?.Title ?? "—";

    public bool IsPresent => entry.IsPresent;

    public string StatusLabel => entry.IsPresent ? "Connecté" : "Client fermé";

    public void RefreshHotkey() => Raise(nameof(HotkeyLabel));
}
