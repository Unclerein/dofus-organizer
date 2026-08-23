using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Organizer;

/// <summary>
/// Décide si une fenêtre appartient à un client de jeu à suivre.
///
/// Cette décision vit ici, à l'écart des appels Win32, parce qu'elle s'est déjà trompée :
/// la comparaison par sous-chaîne fait correspondre « Dofus » au nom de l'organizer
/// lui-même, qui se retrouvait alors dans sa propre liste de personnages — et ses fenêtres
/// devenaient des cibles valides pour l'enregistreur de macros.
/// </summary>
public static class GameWindowFilter
{
    /// <param name="processName">Nom de l'exécutable, sans extension.</param>
    /// <param name="processId">Identifiant du processus propriétaire de la fenêtre.</param>
    /// <param name="self">Identifiant et nom du processus de l'organizer.</param>
    public static bool IsGameProcess(string processName, int processId, SelfIdentity self, AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        // Notre propre processus, d'abord : c'est le critère qui ne peut pas se tromper,
        // même si l'exécutable est renommé.
        if (processId == self.ProcessId) return false;

        // Puis toute autre instance de l'organizer, qui serait sinon détectée comme un client.
        if (!string.IsNullOrEmpty(self.ProcessName)
            && processName.Equals(self.ProcessName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // La comparaison reste volontairement permissive : le nom exact de l'exécutable de
        // Dofus varie d'une version à l'autre, et rater le jeu serait pire que d'en détecter
        // un peu trop — l'utilisateur peut retirer une entrée superflue de la liste.
        foreach (string candidate in settings.ProcessNames)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (processName.Contains(candidate.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Vrai si la classe de fenêtre correspond au filtre configuré, ou s'il est vide.</summary>
    public static bool MatchesWindowClass(string className, AppSettings settings)
        => string.IsNullOrWhiteSpace(settings.WindowClassFilter)
            || className.Contains(settings.WindowClassFilter.Trim(), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Identité du processus de l'organizer, pour qu'il puisse s'exclure lui-même.</summary>
public readonly record struct SelfIdentity(int ProcessId, string ProcessName);
