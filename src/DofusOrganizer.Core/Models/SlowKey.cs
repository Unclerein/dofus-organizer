using System.Text.Json.Serialization;

namespace DofusOrganizer.Core.Models;

/// <summary>
/// Une touche dont l'effet met du temps à venir, et l'attente à lui accorder en plus.
///
/// Certaines touches n'agissent pas sur-le-champ : celle du havre-sac ouvre un panneau qui met
/// parfois un moment à apparaître, et le clic suivant partirait dans le vide. Monter le délai
/// général couvrirait le cas mais ralentirait toute la séquence, sur chaque personnage.
///
/// L'attente est attachée à la touche et non à une étape parce que c'est une propriété du jeu et
/// non d'une capture : la séquence de « Refaire sur l'équipe » est reconstruite à chaque
/// téléportation, et une étape d'attente ajoutée à la main serait effacée au voyage suivant.
/// </summary>
public sealed class SlowKey : NotifyBase
{
    private Hotkey? _key;
    private int _extraDelayMs = 1000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// La touche concernée, modificateurs compris : « H » et « Ctrl + H » ne font pas la même
    /// chose en jeu, et n'ouvrent donc pas forcément le même panneau.
    /// </summary>
    public Hotkey? Key
    {
        get => _key;
        set { if (Set(ref _key, value)) Raise(nameof(Summary)); }
    }

    /// <summary>Attente ajoutée à celle qui suit déjà l'étape, et non à sa place.</summary>
    public int ExtraDelayMs
    {
        get => _extraDelayMs;
        set { if (Set(ref _extraDelayMs, Math.Clamp(value, 0, 30000))) Raise(nameof(Summary)); }
    }

    [JsonIgnore]
    public bool IsUsable => Key is { IsEmpty: false };

    [JsonIgnore]
    public string Summary => IsUsable
        ? $"{Key} : + {ExtraDelayMs} ms"
        : $"(aucune touche) : + {ExtraDelayMs} ms";
}
