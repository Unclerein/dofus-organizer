namespace DofusOrganizer.Core.Abstractions;

/// <summary>
/// Le presse-papiers du système, seul moyen pour une macro de lire un nombre affiché par le jeu.
///
/// Injectable pour deux raisons. La première est habituelle : Core ne dépend pas de Windows, et
/// les tests ont besoin d'un faux. La seconde tient à ce que le presse-papiers est un bien
/// commun — il appartient à l'utilisateur, pas à l'application. Ce qu'il contenait est
/// sauvegardé avant une macro et remis après, d'où <see cref="SetText"/>, qui ne sert qu'à ça.
/// </summary>
public interface IClipboard
{
    /// <summary>
    /// Vide le presse-papiers.
    ///
    /// C'est la précaution qui empêche une macro de lire une valeur périmée : si la copie
    /// échoue — la boîte de saisie ne s'est pas ouverte, la frappe n'est pas arrivée — le
    /// presse-papiers garderait sinon son contenu précédent, que la macro prendrait pour la
    /// réponse du jeu.
    /// </summary>
    void Clear();

    /// <summary>Contenu textuel, ou null si le presse-papiers est vide ou illisible.</summary>
    string? GetText();

    void SetText(string text);
}
