namespace DofusOrganizer.Core.Models;

/// <summary>Une fenêtre de client Dofus repérée sur le bureau, telle que remontée par la couche Win32.</summary>
public sealed record GameWindow(nint Handle, int ProcessId, string Title, string ProcessName, string ClassName)
{
    /// <summary>Nom du personnage extrait du titre, ou le titre brut si le motif n'a rien reconnu.</summary>
    public string CharacterName { get; init; } = "";
}
