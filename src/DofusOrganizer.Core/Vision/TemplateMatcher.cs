namespace DofusOrganizer.Core.Vision;

/// <summary>Position d'un motif retrouvé, et sa ressemblance sur 0 à 1.</summary>
public readonly record struct ImageMatch(int X, int Y, double Score);

/// <summary>
/// Recherche d'un motif dans une image.
///
/// C'est ce qui remplace « clique à 42 % / 91 % » par « clique sur ce qui ressemble à ça » :
/// une position n'est valable que si la cible est au même endroit chez tous les personnages,
/// alors que l'apparence d'une ligne de liste ou d'un bouton, elle, ne change pas.
/// </summary>
public static class TemplateMatcher
{
    /// <summary>
    /// Facteur de réduction de la passe grossière. Chercher un motif de 64×64 dans une zone
    /// de 400×400 demanderait ~650 millions de comparaisons en force brute ; en réduisant
    /// d'abord d'un facteur 4, la passe grossière en demande 256 fois moins et l'affinage
    /// ne porte que sur quelques dizaines de positions.
    /// </summary>
    private const int PyramidFactor = 4;

    /// <summary>
    /// Taille minimale du motif réduit pour que la passe grossière ait un sens. En deçà,
    /// la réduction écrase le motif au point qu'il ressemble à n'importe quoi, et la
    /// recherche exhaustive est de toute façon immédiate à cette taille.
    /// </summary>
    private const int MinimumCoarseSize = 4;

    /// <summary>
    /// Nombre de pistes conservées par la passe grossière. Une seule ne suffit pas : les
    /// éléments d'interface se ressemblent — les lignes d'une liste, par exemple — et une
    /// version réduite peut désigner la mauvaise. L'affinage tranche entre plusieurs pistes
    /// au lieu de rester prisonnier du premier choix.
    /// </summary>
    private const int CoarseCandidates = 8;

    /// <summary>Rayon d'affinage autour d'une piste, en pixels de l'image d'origine.</summary>
    private const int RefineMargin = PyramidFactor + PyramidFactor / 2;

    /// <summary>Ressemblance minimale en deçà de laquelle on considère que le motif est absent.</summary>
    public const double DefaultMinimumScore = 0.9;

    /// <summary>
    /// Cherche <paramref name="needle"/> dans <paramref name="haystack"/>.
    /// Renvoie null si le motif est absent, trop grand, ou si la ressemblance reste sous
    /// <paramref name="minimumScore"/> — un rejeu doit pouvoir constater l'échec plutôt que
    /// de cliquer sur la moins mauvaise approximation.
    /// </summary>
    public static ImageMatch? Find(PixelBuffer haystack, PixelBuffer needle, double minimumScore = DefaultMinimumScore)
    {
        if (haystack.IsEmpty || needle.IsEmpty) return null;
        if (needle.Width > haystack.Width || needle.Height > haystack.Height) return null;

        var found = Locate(haystack, needle);
        if (found is null) return null;

        long worst = (long)needle.Width * needle.Height * 3 * 255;
        double score = worst == 0 ? 0 : 1.0 - (double)found.Value.Difference / worst;

        return score >= minimumScore ? new ImageMatch(found.Value.X, found.Value.Y, score) : null;
    }

    private static Candidate? Locate(PixelBuffer haystack, PixelBuffer needle)
    {
        var coarseHaystack = haystack.Downsample(PyramidFactor);
        var coarseNeedle = needle.Downsample(PyramidFactor);

        bool pyramidUsable = !coarseNeedle.IsEmpty && !coarseHaystack.IsEmpty
            && coarseNeedle.Width >= MinimumCoarseSize && coarseNeedle.Height >= MinimumCoarseSize
            && coarseNeedle.Width <= coarseHaystack.Width && coarseNeedle.Height <= coarseHaystack.Height;

        if (!pyramidUsable)
        {
            return SearchBest(haystack, needle,
                0, 0, haystack.Width - needle.Width, haystack.Height - needle.Height, long.MaxValue);
        }

        var candidates = SearchCandidates(coarseHaystack, coarseNeedle, CoarseCandidates);
        if (candidates.Count == 0) return null;

        Candidate? best = null;
        long abandonAbove = long.MaxValue;

        foreach (var candidate in candidates)
        {
            int centerX = candidate.X * PyramidFactor;
            int centerY = candidate.Y * PyramidFactor;

            // Le meilleur résultat trouvé jusqu'ici sert de seuil d'abandon pour les pistes
            // suivantes : sans ce report, chaque affinage repartirait de zéro et le coût
            // serait multiplié par le nombre de pistes.
            var refined = SearchBest(haystack, needle,
                Math.Max(0, centerX - RefineMargin),
                Math.Max(0, centerY - RefineMargin),
                Math.Min(haystack.Width - needle.Width, centerX + RefineMargin),
                Math.Min(haystack.Height - needle.Height, centerY + RefineMargin),
                abandonAbove);

            if (refined is null) continue;

            best = refined;
            abandonAbove = refined.Value.Difference;
            if (abandonAbove == 0) break;
        }

        return best;
    }

    /// <summary>
    /// Meilleures positions de la passe grossière, triées par ressemblance décroissante et
    /// contraintes à être **distinctes**.
    ///
    /// Sans cette contrainte, les meilleures positions sont presque toujours le même endroit
    /// à un pixel près : les pistes conservées ne couvriraient qu'une seule zone, et la bonne
    /// cible, si elle arrive juste derrière, ne serait jamais réexaminée. C'est exactement le
    /// cas d'une liste dont toutes les lignes se ressemblent une fois réduites.
    /// </summary>
    private static List<Candidate> SearchCandidates(PixelBuffer haystack, PixelBuffer needle, int limit)
    {
        var best = new List<Candidate>(limit + 1);

        int separationX = Math.Max(1, needle.Width / 2);
        int separationY = Math.Max(1, needle.Height / 2);

        for (int y = 0; y <= haystack.Height - needle.Height; y++)
        {
            for (int x = 0; x <= haystack.Width - needle.Width; x++)
            {
                // Pas d'abandon anticipé ici : une position écartée trop tôt ne pourrait pas
                // être classée, et la liste doit rester ordonnée. La passe grossière porte sur
                // des images réduites d'un facteur 4, soit 256 fois moins de travail — le coût
                // reste négligeable.
                long difference = Difference(haystack, needle, x, y, long.MaxValue);

                int neighbour = best.FindIndex(candidate =>
                    Math.Abs(candidate.X - x) < separationX && Math.Abs(candidate.Y - y) < separationY);

                if (neighbour >= 0)
                {
                    // Même zone qu'une piste déjà retenue : on n'en garde que la meilleure.
                    if (difference >= best[neighbour].Difference) continue;
                    best.RemoveAt(neighbour);
                }
                else if (best.Count == limit && difference >= best[^1].Difference)
                {
                    continue;
                }

                int index = best.FindIndex(candidate => difference < candidate.Difference);
                if (index < 0) index = best.Count;
                best.Insert(index, new Candidate(x, y, difference));

                if (best.Count > limit) best.RemoveAt(best.Count - 1);
            }
        }

        return best;
    }

    /// <summary>
    /// Parcourt les positions de la plage donnée et retient la meilleure, en abandonnant
    /// toute position qui dépasse <paramref name="abandonAbove"/>.
    /// </summary>
    private static Candidate? SearchBest(PixelBuffer haystack, PixelBuffer needle, int minX, int minY, int maxX, int maxY, long abandonAbove)
    {
        if (maxX < minX || maxY < minY) return null;

        long best = abandonAbove;
        int bestX = -1, bestY = -1;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                long difference = Difference(haystack, needle, x, y, best);
                if (difference >= best) continue;

                best = difference;
                bestX = x;
                bestY = y;
                if (best == 0) break;
            }
            if (best == 0) break;
        }

        return bestX < 0 ? null : new Candidate(bestX, bestY, best);
    }

    private static long Difference(PixelBuffer haystack, PixelBuffer needle, int originX, int originY, long abandonAbove)
    {
        long total = 0;

        for (int y = 0; y < needle.Height; y++)
        {
            int needleOffset = needle.OffsetOf(0, y);
            int haystackOffset = haystack.OffsetOf(originX, originY + y);

            for (int x = 0; x < needle.Width; x++)
            {
                total += Math.Abs(haystack.Pixels[haystackOffset] - needle.Pixels[needleOffset]);
                total += Math.Abs(haystack.Pixels[haystackOffset + 1] - needle.Pixels[needleOffset + 1]);
                total += Math.Abs(haystack.Pixels[haystackOffset + 2] - needle.Pixels[needleOffset + 2]);

                needleOffset += PixelBuffer.BytesPerPixel;
                haystackOffset += PixelBuffer.BytesPerPixel;
            }

            // Inutile de finir de comparer une position déjà moins bonne que la meilleure connue.
            if (total >= abandonAbove) return total;
        }

        return total;
    }

    private readonly record struct Candidate(int X, int Y, long Difference);
}
