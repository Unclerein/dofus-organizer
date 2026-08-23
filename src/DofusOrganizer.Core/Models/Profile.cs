namespace DofusOrganizer.Core.Models;

/// <summary>Tout ce qui est persisté entre deux lancements de l'organizer.</summary>
public sealed class Profile
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<CharacterSlot> Characters { get; set; } = [];
    public List<Macro> Macros { get; set; } = [];
}
