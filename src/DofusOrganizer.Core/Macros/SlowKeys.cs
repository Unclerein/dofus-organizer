using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>
/// Décide de l'attente supplémentaire à accorder après une touche.
///
/// Règle pure, comme <see cref="TeamReplay"/> et <see cref="ClickMerging"/> : une attente qui ne
/// s'applique pas ne se voit pas, la macro se contentant de rater un clic de temps en temps.
/// </summary>
public static class SlowKeys
{
    /// <summary>
    /// Attente supplémentaire pour cette étape, ou zéro si sa touche n'est pas listée.
    ///
    /// La comparaison porte sur la touche et ses modificateurs — c'est ce que fait
    /// <see cref="Hotkey.Matches"/>. Une entrée sans touche est ignorée : elle correspondrait
    /// sinon à n'importe quoi, et ralentirait toutes les frappes de toutes les macros.
    /// </summary>
    public static int ExtraDelayFor(KeyStep step, IReadOnlyList<SlowKey>? slowKeys)
    {
        if (slowKeys is null) return 0;

        foreach (var slow in slowKeys)
        {
            if (slow.Key?.Matches(step.VirtualKey, step.Modifiers) == true) return slow.ExtraDelayMs;
        }

        return 0;
    }
}
