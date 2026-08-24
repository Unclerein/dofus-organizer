using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>
/// Transforme une diffusion en macro : une boucle sur les personnages, une frappe à
/// l'intérieur.
///
/// Passer par le moteur de macro plutôt que par une boucle écrite à part n'est pas un
/// détour. Le retour sur la fenêtre de départ, l'arrêt d'urgence, le verrou qui empêche deux
/// séquences de s'entrelacer et le journal personnage par personnage existent déjà là et
/// s'appliquent tels quels ; les réécrire ailleurs reviendrait à entretenir deux
/// comportements au lieu d'un.
/// </summary>
public static class KeyBroadcast
{
    /// <summary>
    /// Construit la macro, ou null s'il n'y a aucune touche à envoyer. Un raccourci peut
    /// avoir été assigné sans que la touche diffusée l'ait été : la diffusion n'a alors rien
    /// à faire, et mieux vaut le dire que parcourir l'équipe pour rien.
    /// </summary>
    public static Macro? BuildMacro(BroadcastKey broadcast)
    {
        if (broadcast.Sent is not { IsEmpty: false } sent) return null;

        var loop = new ForEachCharacterStep
        {
            // Le déclencheur est absorbé avant d'atteindre le client au premier plan :
            // sans l'inclure, le meneur serait le seul personnage à ne rien faire.
            SkipCurrentWindow = !broadcast.IncludeCurrent,
            Steps =
            {
                new KeyStep
                {
                    VirtualKey = sent.VirtualKey,
                    Modifiers = sent.Modifiers,
                    Action = KeyAction.Press,
                },
            },
        };

        return new Macro
        {
            Name = NameFor(broadcast),
            RestoreInitialWindow = true,
            // Aucune souris n'entre en jeu : reposer le curseur n'aurait rien à défaire.
            RestoreCursorPosition = false,
            Steps = { loop },
        };
    }

    /// <summary>Nom de la macro construite, tel qu'il apparaît dans le journal en cas d'échec.</summary>
    public static string NameFor(BroadcastKey broadcast)
        => $"Diffusion « {broadcast.Name} »";
}
