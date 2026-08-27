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
    /// Part à prendre, sachant ce qui reste en stock et combien de personnages restent à servir,
    /// celui du tour compris.
    ///
    /// Diviser par ce qui reste plutôt que par l'effectif de départ paraît un détail et n'en est
    /// pas un. Un quart figé de dix donne 2, 2, 2, 2 et abandonne deux items au coffre ; la
    /// division par ce qui reste donne 2, puis 8/3 = 2, puis 6/2 = 3, puis 3/1 = 3 — dix
    /// distribués, rien d'oublié. Et comme chaque tour relit le stock réel, un personnage sauté
    /// ou une part mal tapée se rattrapent d'eux-mêmes au tour suivant, au lieu de propager
    /// l'erreur.
    ///
    /// La division entière ne donne jamais plus que le stock, ce qui est le bon sens du refus :
    /// mieux vaut laisser un item au coffre que réclamer ce qui n'y est pas.
    /// </summary>
    public static int Share(int stock, int remainingCharacters)
    {
        if (stock <= 0 || remainingCharacters <= 0) return 0;
        return stock / remainingCharacters;
    }
}
