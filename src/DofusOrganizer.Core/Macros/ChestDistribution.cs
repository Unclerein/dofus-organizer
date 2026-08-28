using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>
/// Transforme quelques points désignés à la souris en macro de répartition : chaque personnage
/// passe au coffre et y prend sa part de chaque item.
///
/// Cette construction vit ici plutôt que dans l'interface pour la même raison que
/// <see cref="TeamReplay"/> : l'ordre des étapes porte une décision qui ne se voit pas à
/// l'écran, et qui coûte des items quand elle est fausse.
/// </summary>
public static class ChestDistribution
{
    public const string MacroName = "Répartir le coffre";

    /// <summary>
    /// Temps laissé au coffre pour s'ouvrir. Généreux, et volontairement posé comme une étape
    /// d'attente ordinaire : c'est le premier réglage à monter si les glissers partent dans le
    /// vide, et il doit donc rester visible et modifiable dans l'éditeur.
    /// </summary>
    public const int ChestOpenDelayMs = 800;

    /// <param name="chest">Le coffre, tel qu'on le clique à la banque. Sa place change d'une banque à l'autre.</param>
    /// <param name="drop">La case d'arrivée, dans l'onglet d'inventaire du personnage.</param>
    /// <param name="items">Les items à répartir, dans l'ordre où ils ont été désignés.</param>
    /// <param name="divisor">
    /// En combien de parts découper chaque pile. Le même nombre pour tous les personnages, posé
    /// sur chaque étape : la construction les règle ensemble, et rien n'empêche ensuite d'en
    /// changer une seule dans l'éditeur.
    /// </param>
    public static Macro BuildMacro(
        NormalizedPoint chest, NormalizedPoint drop, IReadOnlyList<NormalizedPoint> items,
        int divisor = DistributeQuantityStep.DefaultDivisor)
    {
        var loop = new ForEachCharacterStep { SkipCurrentWindow = false };

        loop.Steps.Add(new MouseClickStep { Fx = chest.Fx, Fy = chest.Fy });
        loop.Steps.Add(new DelayStep { Milliseconds = ChestOpenDelayMs });

        // Du dernier désigné vers le premier, et ce n'est pas un détail de présentation : quand
        // une pile se vide, les items qui la suivaient remontent d'une case, et tous les points
        // désignés après elle tombent à côté. En commençant par la fin, ce qui disparaît est
        // toujours derrière ce qu'il reste à traiter.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            loop.Steps.Add(new MouseDragStep
            {
                Fx = items[i].Fx,
                Fy = items[i].Fy,
                ToFx = drop.Fx,
                ToFy = drop.Fy,
            });
            loop.Steps.Add(new DistributeQuantityStep { Divisor = divisor });
        }

        loop.Steps.Add(new KeyStep { VirtualKey = VirtualKeys.Escape });

        return new Macro
        {
            Name = MacroName,
            RestoreInitialWindow = true,
            RestoreCursorPosition = true,
            Steps = { loop },
        };
    }

    /// <summary>Vrai s'il y a de quoi construire quelque chose : au moins un item désigné.</summary>
    public static bool HasItems(IReadOnlyList<NormalizedPoint> items) => items.Count > 0;
}
