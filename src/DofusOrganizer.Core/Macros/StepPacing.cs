using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>
/// Combien de temps se taire après une étape.
///
/// Deux natures de pause se cachaient derrière un même nombre, et les confondre a coûté trois
/// corrections successives — le double-clic qui n'en était plus un, la molette au ralenti, et
/// maintenant la saisie d'un nom de zaap lettre par lettre.
///
/// <list type="bullet">
///   <item><description>
///     Le <b>temps que met le jeu à réagir</b> : ouvrir un panneau, charger une liste. Il dépend
///     du client, croît avec sa lenteur, et c'est lui que le rejeu sur l'équipe rallonge.
///   </description></item>
///   <item><description>
///     La <b>cadence du geste</b> : l'écart entre deux clics d'un double-clic, entre deux crans
///     de molette, entre deux touches d'un mot qu'on tape. Elle ne dépend que de ce que le
///     système et le jeu savent distinguer. La rallonger ne rend pas le geste plus sûr, elle le
///     défait — deux clics espacés d'une demi-seconde ne sont plus un double-clic.
///   </description></item>
/// </list>
///
/// D'où cette table : elle dit, pour chaque nature d'étape, laquelle des deux s'applique. Ajouter
/// un cas coûte une ligne, et non une branche de plus dans le moteur avec sa justification
/// recopiée à côté.
/// </summary>
public static class StepPacing
{
    /// <summary>
    /// Pause à observer après <paramref name="step"/>, connaissant l'étape qui la suit.
    /// </summary>
    /// <param name="next">L'étape suivante, ou null si c'est la dernière de la séquence.</param>
    /// <param name="actionDelayMs">
    /// Délai entre actions en vigueur, éventuellement rallongé pour un rejeu sur l'équipe.
    /// </param>
    public static int After(MacroStep step, MacroStep? next, AppSettings settings, int actionDelayMs) => step switch
    {
        // Une liste défile à la vitesse où on la fait tourner : rien à ouvrir, rien à charger.
        ScrollStep => settings.ScrollDelayMs,

        // Des touches qui s'enchaînent, c'est de la saisie — un nom de zaap dans un champ de
        // recherche. Le jeu n'a rien à faire entre deux lettres, et les espacer du délai des
        // actions rendrait le moindre mot interminable, sur chaque personnage.
        KeyStep key when next is KeyStep
            => settings.TypingDelayMs + SlowKeys.ExtraDelayFor(key, settings.SlowKeys),

        // La dernière touche d'une saisie, elle, laisse au jeu le temps d'en tenir compte. Le
        // supplément d'une touche lente s'ajoute au délai courant au lieu de le remplacer :
        // l'ouverture d'un panneau vient par-dessus ce que la séquence demandait déjà.
        KeyStep key => actionDelayMs + SlowKeys.ExtraDelayFor(key, settings.SlowKeys),

        // Une répartition se termine sur une validation, après quoi le jeu déplace des items et
        // referme une boîte. Attraper le suivant pendant ce temps fait partir le glisser dans
        // une fenêtre qui n'est plus dans l'état attendu. Le supplément s'ajoute au délai
        // courant plutôt que de le remplacer, comme pour une touche lente : il vient par-dessus
        // ce que la séquence demandait déjà.
        DistributeQuantityStep distribute => actionDelayMs + distribute.TransferDelayMs,

        _ => actionDelayMs,
    };
}
