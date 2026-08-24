using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>Un appui déjà enregistré, retenu le temps de savoir si le suivant le complète.</summary>
public readonly record struct RecordedClick(MouseButton Button, ScreenPoint Point, long TimestampMs);

/// <summary>
/// Seuils du système régissant double-clic et glisser. Ils sont lus chez Windows plutôt que
/// figés ici : l'utilisateur peut les avoir réglés, et une valeur en dur produirait une
/// capture qui ne correspond pas à sa façon de cliquer.
/// </summary>
public readonly record struct InputThresholds(int DoubleClickTimeMs, int DoubleClickSlopX, int DoubleClickSlopY, int DragSlopX, int DragSlopY)
{
    /// <summary>Valeurs par défaut de Windows, pour les tests et en cas d'échec de lecture.</summary>
    public static InputThresholds Default => new(500, 4, 4, 4, 4);
}

/// <summary>
/// Décide si deux appuis successifs forment un double-clic, et si un appui suivi d'un
/// relâchement forme un glisser.
///
/// Cette règle vit ici, à l'écart de la capture, parce qu'elle se trompe en silence : un
/// double-clic non reconnu devient deux étapes que le rejeu sépare du délai configuré —
/// 600 ms pour un rejeu sur l'équipe — et le jeu ne voit alors que deux clics isolés.
/// </summary>
public static class ClickMerging
{
    /// <summary>Nombre d'appuis au-delà duquel on n'agrège plus.</summary>
    public const int MaximumClicks = 3;

    /// <summary>Vrai si le nouvel appui prolonge le précédent en double (ou triple) clic.</summary>
    public static bool ContinuesClick(RecordedClick? previous, MouseButton button, ScreenPoint point, long timestampMs, int alreadyCounted, InputThresholds thresholds)
    {
        if (previous is not { } last) return false;
        if (last.Button != button) return false;
        if (alreadyCounted >= MaximumClicks) return false;

        long elapsed = timestampMs - last.TimestampMs;
        if (elapsed < 0 || elapsed > thresholds.DoubleClickTimeMs) return false;

        return Math.Abs(point.X - last.Point.X) <= thresholds.DoubleClickSlopX
            && Math.Abs(point.Y - last.Point.Y) <= thresholds.DoubleClickSlopY;
    }

    /// <summary>
    /// Vrai si le curseur s'est assez éloigné entre l'appui et le relâchement pour qu'il
    /// s'agisse d'un glisser plutôt que d'un clic.
    /// </summary>
    public static bool IsDrag(ScreenPoint pressed, ScreenPoint released, InputThresholds thresholds)
        => Math.Abs(released.X - pressed.X) > thresholds.DragSlopX
        || Math.Abs(released.Y - pressed.Y) > thresholds.DragSlopY;
}
