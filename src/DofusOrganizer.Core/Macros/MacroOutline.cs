using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>Une étape telle qu'elle se présente dans la liste de l'éditeur, avec son retrait.</summary>
/// <param name="Depth">0 à la racine de la macro, 1 à l'intérieur d'une boucle.</param>
public readonly record struct OutlinedStep(MacroStep Step, int Depth);

/// <summary>
/// Met la macro à plat pour l'éditeur : une ligne par étape, sous-étapes comprises.
///
/// Ce calcul vit ici parce qu'il décide de ce que l'utilisateur voit. Une étape oubliée à
/// l'aplatissement disparaît de la liste sans que rien ne le signale — elle continue de
/// s'exécuter, invisible et impossible à corriger. C'est exactement le genre de silence que ce
/// projet range dans Core, avec des tests.
///
/// L'imbrication ne va jamais au-delà d'un niveau : l'éditeur refuse une boucle dans une boucle.
/// </summary>
public static class MacroOutline
{
    public static IReadOnlyList<OutlinedStep> Flatten(IReadOnlyList<MacroStep> steps)
    {
        var rows = new List<OutlinedStep>(steps.Count);

        foreach (var step in steps)
        {
            rows.Add(new OutlinedStep(step, 0));
            if (step is not ForEachCharacterStep loop) continue;

            foreach (var inner in loop.Steps) rows.Add(new OutlinedStep(inner, 1));
        }

        return rows;
    }

    /// <summary>
    /// La liste où vit une étape : celle de la macro, ou celle d'une boucle. Null si l'étape
    /// n'appartient pas à cette macro.
    /// </summary>
    public static IList<MacroStep>? ContainerOf(IList<MacroStep> steps, MacroStep step)
    {
        if (steps.Contains(step)) return steps;

        foreach (var loop in steps.OfType<ForEachCharacterStep>())
        {
            if (loop.Steps.Contains(step)) return loop.Steps;
        }

        return null;
    }

    /// <summary>
    /// Déplace l'étape de la ligne <paramref name="from"/> à la place de celle de la ligne
    /// <paramref name="to"/>, et dit si le déplacement a eu lieu.
    ///
    /// On ne réordonne qu'à l'intérieur d'un même conteneur. Sortir une étape de sa boucle
    /// changerait <b>quand</b> elle s'exécute — une fois au lieu d'une fois par personnage — et
    /// cela ne doit pas tenir à la précision d'un lâcher de souris. C'est déjà la règle des
    /// boutons Monter et Descendre, qui s'arrêtent aux bornes de la boucle.
    ///
    /// Déposer une boucle sur ses propres sous-étapes est refusé pour la même raison : la
    /// destination est à l'intérieur d'elle-même.
    /// </summary>
    public static bool Reorder(IList<MacroStep> steps, int from, int to)
    {
        var rows = Flatten((IReadOnlyList<MacroStep>)steps);
        if (from < 0 || from >= rows.Count || to < 0 || to >= rows.Count || from == to) return false;

        var moved = rows[from].Step;
        var target = rows[to].Step;

        var container = ContainerOf(steps, moved);
        if (container is null || !ReferenceEquals(container, ContainerOf(steps, target))) return false;

        int source = container.IndexOf(moved);
        int destination = container.IndexOf(target);
        if (source < 0 || destination < 0 || source == destination) return false;

        container.RemoveAt(source);
        container.Insert(destination, moved);
        return true;
    }
}
