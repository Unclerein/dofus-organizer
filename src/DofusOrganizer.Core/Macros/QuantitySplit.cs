using System.Globalization;

namespace DofusOrganizer.Core.Macros;

/// <summary>
/// Lecture d'une quantité affichée par le jeu, et sa répartition sur les personnages.
///
/// Ces deux calculs vivent ici parce qu'ils décident combien d'items changent de mains. Se
/// tromper d'un facteur dix ne provoque aucune erreur : la macro tape le nombre, le jeu obéit,
/// et rien ne le signale. Ils sont donc écrits comme des fonctions pures, et vérifiés sur les
/// formes que le nombre peut prendre à l'écran.
/// </summary>
public static class QuantitySplit
{
    /// <summary>
    /// Lit la quantité copiée depuis le champ du jeu.
    ///
    /// Les séparateurs de milliers sont écartés, y compris l'espace insécable et l'espace fine
    /// insécable, que les interfaces francophones emploient couramment et qu'un simple
    /// <c>int.Parse</c> refuse. Tout le reste est refusé plutôt qu'interprété : un texte qui
    /// n'est pas une quantité veut dire que la copie a échoué, et deviner à ce moment-là
    /// coûterait des items.
    /// </summary>
    public static bool TryParse(string? text, out int quantity)
    {
        quantity = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        Span<char> digits = stackalloc char[text.Length];
        int length = 0;

        foreach (char c in text)
        {
            if (char.IsDigit(c)) digits[length++] = c;
            else if (!IsSeparator(c)) return false;
        }

        return length > 0
            && int.TryParse(digits[..length], NumberStyles.None, CultureInfo.InvariantCulture, out quantity);
    }

    /// <summary>
    /// Espaces et apostrophes admis entre les chiffres. Le point et la virgule sont exclus à
    /// dessein : « 1.5 » n'est pas une quantité mal écrite, c'est le signe qu'on ne lit pas ce
    /// qu'on croit.
    /// </summary>
    private static bool IsSeparator(char c) => c is ' ' or ' ' or ' ' or ' ' or '\'' or '\t';

    /// <summary>
    /// Part à prendre dans un stock, pour un nombre de parts donné.
    ///
    /// Division entière, et le reste demeure au coffre : dix items en quatre parts font deux
    /// chacun et deux oubliés. C'est assumé — un ou deux items qui restent valent mieux qu'un
    /// diviseur qui changerait d'un personnage à l'autre sans être écrit nulle part.
    ///
    /// Et la division entière ne réclame jamais plus qu'il n'y a, ce qui est le bon sens du
    /// refus : mieux vaut laisser un item que demander ce qui n'existe pas.
    /// </summary>
    public static int Share(int stock, int parts)
    {
        if (stock <= 0 || parts <= 0) return 0;
        return stock / parts;
    }
}
