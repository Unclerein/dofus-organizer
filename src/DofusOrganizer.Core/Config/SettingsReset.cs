using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Config;

/// <summary>
/// Remet à leurs valeurs d'origine les réglages qu'on peut casser sans s'en rendre compte :
/// les temporisations et la détection des clients.
///
/// Ces opérations vivent ici, et non dans l'interface, parce qu'elles sont exactement du genre
/// à se tromper en silence. Les raccourcis globaux — personnage suivant, arrêt d'urgence,
/// bascule d'enregistrement, rejeu sur l'équipe — sont rangés dans le même objet que les
/// temporisations : un « tout remettre à zéro » écrit d'un trait les emporterait, et l'auteur
/// s'en apercevrait le lendemain en jeu, pas au moment du clic. La liste de ce qui est
/// rétabli est donc explicite, et des tests disent ce qui doit survivre.
///
/// Les valeurs viennent d'un objet neuf plutôt que de constantes recopiées : il ne peut donc
/// pas exister de « défaut » qui diffère de celui d'une installation vierge.
/// </summary>
public static class SettingsReset
{
    /// <summary>
    /// Rétablit les six temporisations générales.
    ///
    /// Pas les touches lentes : ce sont bien des temporisations, mais chacune est attachée à
    /// une touche choisie à la main — celle du havre-sac, typiquement. Les effacer casserait
    /// un aller au havre-sac sans que rien ne le signale, alors que les six valeurs ci-dessous
    /// sont anonymes et se retrouvent d'un coup d'œil.
    /// </summary>
    public static void RestoreDelays(AppSettings settings)
    {
        var defaults = new AppSettings();

        settings.FocusSettleDelayMs = defaults.FocusSettleDelayMs;
        settings.ActionDelayMs = defaults.ActionDelayMs;
        settings.TeamReplayDelayMs = defaults.TeamReplayDelayMs;
        settings.MultiClickIntervalMs = defaults.MultiClickIntervalMs;
        settings.ScrollDelayMs = defaults.ScrollDelayMs;
        settings.TypingDelayMs = defaults.TypingDelayMs;
    }

    /// <summary>
    /// Rétablit le motif du titre et la classe de fenêtre.
    ///
    /// C'est la porte de sortie du cas sans retour : un motif saisi à la main qui ne reconnaît
    /// plus rien, alors que l'expression régulière d'origine n'est écrite nulle part dans
    /// l'application et qu'effacer le profil emporterait macros et raccourcis.
    /// </summary>
    public static void RestoreDetection(AppSettings settings)
    {
        var defaults = new AppSettings();

        settings.TitlePattern = defaults.TitlePattern;
        settings.WindowClassFilter = defaults.WindowClassFilter;
    }
}
