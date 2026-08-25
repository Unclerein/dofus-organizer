namespace DofusOrganizer.Core.Organizer;

/// <summary>
/// Décide si la surveillance du clavier a été décrochée par Windows et doit être reposée.
///
/// Le système retire un hook bas niveau dont le rappel tarde trop, et il le fait **sans rien
/// signaler** : pas d'erreur, pas de code de retour. Le programme continue de croire ses
/// raccourcis en place pendant que plus aucun n'arrive, et seul un redémarrage les ramène.
///
/// Aucune API ne répond « ton hook est-il vivant ». Mais on peut le prouver par l'absurde :
/// le système sait quand il a reçu une entrée pour la dernière fois. S'il en a vu une plus
/// récemment que notre dernier rappel, c'est que nous l'avons manquée — donc que le hook est
/// mort. C'est une constatation, pas une supposition.
/// </summary>
public static class HookWatchdog
{
    /// <summary>
    /// Écart au-delà duquel une entrée non vue vaut condamnation.
    ///
    /// Il absorbe le décalage normal entre le moment où le système date une entrée et celui où
    /// notre rappel s'exécute. Deux secondes ramènent la panne d'« irrécupérable sans
    /// redémarrage » à « le temps de s'en apercevoir ».
    /// </summary>
    public const int SilenceMarginMs = 2000;

    /// <summary>
    /// Vrai si le système a vu une entrée que nous n'avons pas vue, au-delà de la marge.
    /// </summary>
    /// <param name="lastCallbackMs">Dernier rappel reçu, tous hooks confondus.</param>
    /// <param name="lastSystemInputMs">Dernière entrée vue par le système.</param>
    public static bool ShouldReinstall(long lastCallbackMs, long lastSystemInputMs, int marginMs = SilenceMarginMs)
        => lastSystemInputMs - lastCallbackMs > marginMs;
}
