using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Config;

/// <summary>
/// Met un profil écrit par une version précédente en état d'être relu.
///
/// Une propriété disparue est ignorée sans bruit par le lecteur JSON, mais un <em>type</em>
/// d'étape disparu le fait échouer — et <see cref="ProfileStore.Load"/> répond à un échec en
/// repartant d'un profil neuf. Retirer un type d'étape effacerait donc, chez qui en avait une,
/// ses personnages, ses raccourcis et toutes ses macros d'un coup.
///
/// Les étapes devenues inconnues sont donc écartées avant lecture : on perd l'étape, pas le
/// profil.
/// </summary>
public static class ProfileMigration
{
    /// <summary>
    /// Discriminants encore reconnus, lus sur les attributs de <see cref="MacroStep"/>.
    ///
    /// Lus et non recopiés : une liste tenue à la main finirait par diverger, et une étape
    /// bien vivante mais oubliée ici serait alors supprimée des profils au chargement — un
    /// dégât pire que celui qu'on cherche à éviter.
    /// </summary>
    private static readonly HashSet<string> KnownStepTypes = typeof(MacroStep)
        .GetCustomAttributes<JsonDerivedTypeAttribute>()
        .Select(attribute => attribute.TypeDiscriminator?.ToString())
        .Where(discriminator => !string.IsNullOrEmpty(discriminator))
        .Select(discriminator => discriminator!)
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>Nom de la propriété portant le discriminant, tel que déclaré sur MacroStep.</summary>
    private const string TypeProperty = "type";

    /// <summary>Nom des collections d'étapes, dans le profil comme dans une boucle.</summary>
    private const string StepsProperty = "Steps";

    /// <summary>
    /// Renvoie le profil débarrassé de ses étapes d'un type inconnu, ou le texte d'origine
    /// s'il n'est pas du JSON exploitable — ce cas est déjà traité par le lecteur, qui met le
    /// fichier de côté au lieu de l'écraser.
    /// </summary>
    public static string DropUnknownSteps(string json)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }

        if (root is null) return json;

        return Prune(root) ? root.ToJsonString() : json;
    }

    /// <summary>Parcourt l'arbre et vide les collections d'étapes de leurs types inconnus.</summary>
    private static bool Prune(JsonNode node)
    {
        bool changed = false;

        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj.ToList())
                {
                    if (value is null) continue;
                    if (name == StepsProperty && value is JsonArray steps) changed |= PruneSteps(steps);
                    else changed |= Prune(value);
                }
                break;

            case JsonArray array:
                foreach (var item in array.ToList())
                {
                    if (item is not null) changed |= Prune(item);
                }
                break;
        }

        return changed;
    }

    private static bool PruneSteps(JsonArray steps)
    {
        bool changed = false;

        for (int i = steps.Count - 1; i >= 0; i--)
        {
            if (steps[i] is not JsonObject step) continue;

            // Une boucle contient ses propres étapes : la descente précède le retrait, sinon
            // une boucle écartée emporterait des sous-étapes qu'on n'aurait jamais examinées.
            changed |= Prune(step);

            string? type = step[TypeProperty]?.GetValue<string>();
            if (type is null || KnownStepTypes.Contains(type)) continue;

            steps.RemoveAt(i);
            changed = true;
        }

        return changed;
    }
}
