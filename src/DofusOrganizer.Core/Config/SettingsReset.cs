using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Config;

/// <summary>
/// Remet à leurs valeurs d'origine les réglages qu'on peut casser sans s'en rendre compte :
/// les temporisations et la détection des clients.
///
/// Cette opération vit ici, et non dans l'interface, parce qu'elle est exactement du genre à
/// se tromper en silence. Les raccourcis globaux — personnage suivant, arrêt d'urgence,
/// bascule d'enregistrement, rejeu sur l'équipe — sont rangés dans le même objet que les
/// temporisations : un « tout remettre à zéro » écrit d'un trait les emporterait, et l'auteur
/// s'en apercevrait le lendemain en jeu, pas au moment du clic. La liste de ce qui est
/// rétabli est donc explicite, et des tests disent ce qui doit survivre.
/// </summary>
public static class SettingsReset
{
    /// <summary>
    /// Rétablit les six temporisations et les deux champs de détection.
    ///
    /// Ne touche ni aux raccourcis, ni aux touches lentes — qui portent une touche choisie à
    /// la main, et dont la perte casserait une macro sans rien dire — ni aux cases à cocher
    /// du comportement, ni bien sûr aux macros et aux personnages, qui ne sont pas ici.
    ///
    /// Les valeurs viennent d'un objet neuf plutôt que de constantes recopiées : il ne peut
    /// donc pas exister de « défaut » qui diffère de celui d'une installation vierge.
    /// </summary>
    public static void RestoreDelaysAndDetection(AppSettings settings)
    {
        var defaults = new AppSettings();

        settings.FocusSettleDelayMs = defaults.FocusSettleDelayMs;
        settings.ActionDelayMs = defaults.ActionDelayMs;
        settings.TeamReplayDelayMs = defaults.TeamReplayDelayMs;
        settings.MultiClickIntervalMs = defaults.MultiClickIntervalMs;
        settings.ScrollDelayMs = defaults.ScrollDelayMs;
        settings.TypingDelayMs = defaults.TypingDelayMs;

        settings.TitlePattern = defaults.TitlePattern;
        settings.WindowClassFilter = defaults.WindowClassFilter;
    }
}
