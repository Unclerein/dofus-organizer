using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Config;

/// <summary>
/// Rattrape un profil écrit par une version qui connaissait des étapes que celle-ci ignore.
///
/// Une propriété disparue est ignorée sans bruit par le lecteur JSON, mais un <em>type</em>
/// d'étape disparu le fait échouer — et <see cref="ProfileStore.Load"/> répond à un échec en
/// repartant d'un profil neuf. Retirer un type d'étape effacerait donc, chez qui en avait une,
/// ses personnages, ses raccourcis et toutes ses macros d'un coup.
///
/// Les étapes devenues inconnues sont donc écartées : on perd l'étape, pas le profil.
/// </summary>
public static class ProfileMigration
{
    /// <summary>
    /// Discriminants encore reconnus, et le nom de la propriété qui les porte, lus tous deux
    /// sur les attributs de <see cref="MacroStep"/>.
    ///
    /// Lus et non recopiés : une liste tenue à la main finirait par diverger, et une étape bien
    /// vivante mais oubliée ici serait alors supprimée des profils au chargement — un dégât pire
    /// que celui qu'on cherche à éviter. Le nom de la propriété suit la même règle : recopié, il
    /// suffirait de le renommer sur le modèle pour que plus aucune étape ne soit reconnue et que
    /// toutes disparaissent.
    /// </summary>
    private static readonly HashSet<string> KnownStepTypes = typeof(MacroStep)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .Select(attribute => attribute.TypeDiscriminator?.ToString())
        .OfType<string>()
        .ToHashSet(StringComparer.Ordinal);

    private static readonly string TypeProperty =
        typeof(MacroStep).GetCustomAttribute<JsonPolymorphicAttribute>()?.TypeDiscriminatorPropertyName
        ?? "$type";

    /// <summary>
    /// Renvoie le profil débarrassé de ses étapes d'un type inconnu.
    ///
    /// Rend <b>la chaîne reçue elle-même</b>, à l'identité près, quand il n'y avait rien à
    /// retirer ou que le texte n'est pas du JSON exploitable. L'appelant peut donc savoir par un
    /// simple <see cref="object.ReferenceEquals"/> qu'une seconde tentative de lecture serait
    /// inutile.
    /// </summary>
    public static string DropUnknownSteps(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return json;
        }

        return root is not null && Prune(root) ? root.ToJsonString() : json;
    }

    /// <summary>
    /// Parcourt l'arbre et retire, de toute collection, les objets portant un discriminant
    /// d'étape inconnu.
    ///
    /// Le tri porte sur la forme et non sur le nom de la collection : un jour où un type
    /// d'étape porterait ses sous-étapes sous un autre nom que « Steps », une sélection par nom
    /// cesserait de les couvrir en silence — et la conséquence serait exactement celle que cette
    /// classe existe pour éviter. Un objet dépourvu du discriminant n'est jamais touché, ce qui
    /// laisse intacts les personnages, les touches lentes et les macros elles-mêmes.
    /// </summary>
    private static bool Prune(JsonNode node)
    {
        bool changed = false;

        switch (node)
        {
            case JsonObject obj:
                foreach (var (_, value) in obj)
                {
                    if (value is not null) changed |= Prune(value);
                }
                break;

            case JsonArray array:
                // À l'envers : retirer un élément décale ceux qui suivent.
                for (int i = array.Count - 1; i >= 0; i--)
                {
                    if (array[i] is not JsonNode item) continue;

                    if (IsUnknownStep(item))
                    {
                        array.RemoveAt(i);
                        changed = true;
                        continue;
                    }

                    changed |= Prune(item);
                }
                break;
        }

        return changed;
    }

    private static bool IsUnknownStep(JsonNode node)
        => node is JsonObject step
           && step[TypeProperty]?.GetValueKind() == JsonValueKind.String
           && !KnownStepTypes.Contains(step[TypeProperty]!.GetValue<string>());
}
