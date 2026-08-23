using System.Text.Json.Serialization;

namespace DofusOrganizer.Core.Models;

/// <summary>
/// Un emplacement de personnage dans la liste ordonnée de l'organizer.
/// Il survit à la fermeture du client : c'est <see cref="Key"/> (le nom lu dans le
/// titre de la fenêtre) qui permet de retrouver l'ordre et le raccourci au relancement.
/// </summary>
public sealed class CharacterSlot : NotifyBase
{
    private string _displayName = "";
    private Hotkey? _hotkey;
    private bool _enabled = true;

    /// <summary>Identifiant stable, dérivé du titre de la fenêtre au premier repérage.</summary>
    public string Key { get; set; } = "";

    /// <summary>Nom montré dans l'interface, renommable si le titre de la fenêtre est peu lisible.</summary>
    public string DisplayName
    {
        get => string.IsNullOrWhiteSpace(_displayName) ? Key : _displayName;
        set => Set(ref _displayName, value ?? "");
    }

    public Hotkey? Hotkey
    {
        get => _hotkey;
        set => Set(ref _hotkey, value);
    }

    /// <summary>Décoché, le personnage est ignoré par la touche « suivant » et par les boucles de macro.</summary>
    public bool Enabled
    {
        get => _enabled;
        set => Set(ref _enabled, value);
    }

    [JsonIgnore]
    public bool HasCustomName => !string.IsNullOrWhiteSpace(_displayName);
}
