using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>Un défilement déjà enregistré, retenu le temps de savoir si le suivant le prolonge.</summary>
public readonly record struct RecordedScroll(ScrollDirection Direction, ScreenPoint Point, long TimestampMs);

/// <summary>
/// Décide si un cran de molette prolonge le défilement précédent.
///
/// Une main qui fait tourner la molette produit un événement par cran : sans regroupement,
/// parcourir une liste devient dix ou vingt étapes que le rejeu sépare chacune de son délai.
/// Le geste, instantané pour qui l'a fait, se rejoue au ralenti.
///
/// Regroupés, les crans repartent dans une seule injection, donc sans aucun écart entre eux —
/// c'est exactement le geste d'origine.
/// </summary>
public static class ScrollMerging
{
    /// <summary>
    /// Écart maximal entre deux crans d'un même geste. Large par rapport à la cadence d'une
    /// molette, qui émet plusieurs crans par dixième de seconde, mais assez court pour que deux
    /// défilements volontairement séparés le restent.
    /// </summary>
    public const int GestureGapMs = 300;

    /// <summary>
    /// Tolérance de position. La main qui tient la souris bouge un peu en faisant tourner la
    /// molette ; exiger le pixel près couperait le geste en morceaux.
    /// </summary>
    public const int PositionSlop = 24;

    /// <summary>Au-delà, l'étape ne peut plus grandir : <see cref="ScrollStep.Notches"/> y est borné.</summary>
    public const int MaximumNotches = 50;

    /// <summary>
    /// Vrai si ce cran prolonge le défilement précédent et doit s'ajouter à son étape.
    /// </summary>
    /// <param name="alreadyCounted">Crans déjà portés par l'étape en cours.</param>
    /// <param name="adding">Crans que le nouvel événement apporte.</param>
    public static bool ContinuesScroll(
        RecordedScroll? previous, ScrollDirection direction, ScreenPoint point, long timestampMs,
        int alreadyCounted, int adding)
    {
        if (previous is not { } last) return false;

        // Un changement de sens est une intention nouvelle, pas la suite du même geste.
        if (last.Direction != direction) return false;

        // Déborder le plafond ferait perdre les crans en trop, l'étape ne pouvant plus grandir.
        if (alreadyCounted + adding > MaximumNotches) return false;

        long elapsed = timestampMs - last.TimestampMs;
        if (elapsed < 0 || elapsed > GestureGapMs) return false;

        return Math.Abs(point.X - last.Point.X) <= PositionSlop
            && Math.Abs(point.Y - last.Point.Y) <= PositionSlop;
    }
}
