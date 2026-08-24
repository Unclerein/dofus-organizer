using DofusOrganizer.Core.Vision;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class TemplateMatcherTests
{
    /// <summary>
    /// Fond déterministe mais non uniforme : un fond plat rendrait la recherche triviale
    /// et masquerait les erreurs de positionnement.
    /// </summary>
    private static PixelBuffer Background(int width, int height)
    {
        var image = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                image.SetPixel(x, y, (byte)((x * 7 + y * 3) % 256), (byte)((x * 3 + y * 11) % 256), (byte)((x + y) % 256));
            }
        }
        return image;
    }

    /// <summary>Un carré reconnaissable, dessiné dans l'image et renvoyé comme motif.</summary>
    private static PixelBuffer Stamp(PixelBuffer target, int x, int y, int size)
    {
        var pattern = new PixelBuffer(size, size);
        for (int dy = 0; dy < size; dy++)
        {
            for (int dx = 0; dx < size; dx++)
            {
                byte red = (byte)(dx * 9 % 256);
                byte green = (byte)(dy * 13 % 256);
                byte blue = (byte)((dx * dy) % 256);
                pattern.SetPixel(dx, dy, red, green, blue);
                target.SetPixel(x + dx, y + dy, red, green, blue);
            }
        }
        return pattern;
    }

    [Fact]
    public void Le_motif_est_retrouve_a_sa_position_exacte()
    {
        var screen = Background(320, 240);
        var pattern = Stamp(screen, 137, 91, 24);

        var match = TemplateMatcher.Find(screen, pattern);

        Assert.NotNull(match);
        Assert.Equal(137, match!.Value.X);
        Assert.Equal(91, match.Value.Y);
        Assert.Equal(1.0, match.Value.Score, 3);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(295, 215)]
    [InlineData(160, 0)]
    public void Le_motif_est_retrouve_ou_qu_il_soit_y_compris_dans_les_coins(int x, int y)
    {
        var screen = Background(320, 240);
        var pattern = Stamp(screen, x, y, 24);

        var match = TemplateMatcher.Find(screen, pattern);

        Assert.NotNull(match);
        Assert.Equal(x, match!.Value.X);
        Assert.Equal(y, match.Value.Y);
    }

    [Fact]
    public void Un_motif_legerement_bruite_reste_reconnu()
    {
        // Le rendu d'un client n'est jamais au pixel près identique d'une image à l'autre :
        // antialiasing, compression, curseur qui passe. La reconnaissance doit tolérer ça.
        var screen = Background(320, 240);
        var pattern = Stamp(screen, 100, 100, 32);

        for (int y = 0; y < pattern.Height; y += 5)
        {
            for (int x = 0; x < pattern.Width; x += 5)
            {
                int offset = screen.OffsetOf(100 + x, 100 + y);
                screen.Pixels[offset] = (byte)Math.Min(255, screen.Pixels[offset] + 12);
            }
        }

        var match = TemplateMatcher.Find(screen, pattern, minimumScore: 0.9);

        Assert.NotNull(match);
        Assert.Equal(100, match!.Value.X);
        Assert.Equal(100, match.Value.Y);
    }

    [Fact]
    public void Un_motif_absent_n_est_pas_invente()
    {
        // Le point qui compte pour le rejeu : sans ce refus, la macro cliquerait sur la
        // moins mauvaise approximation, c'est-à-dire n'importe où.
        var screen = Background(320, 240);

        var foreign = new PixelBuffer(20, 20);
        for (int y = 0; y < 20; y++)
        {
            for (int x = 0; x < 20; x++) foreign.SetPixel(x, y, 255, 0, 255);
        }

        Assert.Null(TemplateMatcher.Find(screen, foreign));
    }

    [Fact]
    public void Un_motif_plus_grand_que_l_image_ne_provoque_pas_d_erreur()
    {
        var screen = Background(40, 40);
        var pattern = Background(80, 80);

        Assert.Null(TemplateMatcher.Find(screen, pattern));
    }

    [Fact]
    public void Une_image_vide_ne_provoque_pas_d_erreur()
    {
        var empty = new PixelBuffer(0, 0);

        Assert.Null(TemplateMatcher.Find(empty, Background(4, 4)));
        Assert.Null(TemplateMatcher.Find(Background(40, 40), empty));
    }

    [Fact]
    public void Un_motif_plus_petit_que_le_facteur_de_reduction_reste_trouvable()
    {
        // Un motif de 5 px disparaît à la réduction d'un facteur 4 : la recherche doit
        // basculer sur un parcours exhaustif au lieu de rendre un résultat faux.
        var screen = Background(120, 90);
        var pattern = Stamp(screen, 61, 44, 5);

        var match = TemplateMatcher.Find(screen, pattern);

        Assert.NotNull(match);
        Assert.Equal(61, match!.Value.X);
        Assert.Equal(44, match.Value.Y);
    }

    [Fact]
    public void La_bonne_ligne_est_choisie_parmi_des_lignes_quasi_identiques()
    {
        // Le cas réel qui a dicté la conception : une liste de zaaps, dont toutes les
        // lignes se ressemblent. Une passe grossière ne retenant qu'une seule piste
        // désignerait facilement une ligne voisine, et l'affinage ne pourrait plus la
        // corriger. Ici huit lignes ne diffèrent que par une bande de quelques pixels.
        var list = new PixelBuffer(200, 240);
        for (int y = 0; y < list.Height; y++)
        {
            for (int x = 0; x < list.Width; x++) list.SetPixel(x, y, 30, 30, 34);
        }

        const int rowHeight = 30;
        for (int row = 0; row < 8; row++)
        {
            int top = row * rowHeight;

            // Fond de ligne commun à toutes : c'est ce qui les rend confusables une fois réduites.
            for (int y = top + 6; y < top + 20; y++)
            {
                for (int x = 10; x < 190; x++) list.SetPixel(x, y, 200, 200, 205);
            }

            // Puis un libellé propre à la ligne, comme le seraient des noms de zaaps :
            // même graisse et même hauteur, mais une répartition de glyphes différente.
            var glyphs = new Random(row * 977 + 13);
            for (int glyph = 0; glyph < 24; glyph++)
            {
                int x = 14 + glyphs.Next(0, 160);
                int y = top + 8 + glyphs.Next(0, 10);
                for (int dy = 0; dy < 4; dy++)
                {
                    for (int dx = 0; dx < 3; dx++) list.SetPixel(x + dx, y + dy, 25, 30, 40);
                }
            }
        }

        const int targetRow = 5;
        var wanted = list.Crop(10, targetRow * rowHeight + 4, 180, 22);

        var match = TemplateMatcher.Find(list, wanted);

        Assert.NotNull(match);
        Assert.Equal(10, match!.Value.X);
        Assert.Equal(targetRow * rowHeight + 4, match.Value.Y);
    }

    [Fact]
    public void La_recherche_reste_rapide_sur_une_zone_realiste()
    {
        // Ordre de grandeur du rejeu réel : un fragment d'interface de 64×64 cherché dans
        // une zone de 400×400 autour de la position enregistrée. La passe en pyramide doit
        // rendre cela imperceptible ; en force brute ce serait des centaines de millions
        // de comparaisons.
        var screen = Background(400, 400);
        var pattern = Stamp(screen, 250, 300, 64);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var match = TemplateMatcher.Find(screen, pattern);
        stopwatch.Stop();

        Assert.NotNull(match);
        Assert.Equal(250, match!.Value.X);
        Assert.Equal(300, match.Value.Y);
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Recherche trop lente pour un rejeu : {stopwatch.ElapsedMilliseconds} ms.");
    }

    [Fact]
    public void La_reduction_moyenne_correctement_les_blocs()
    {
        var image = new PixelBuffer(4, 4);
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++) image.SetPixel(x, y, 40, 80, 120);
        }

        var reduced = image.Downsample(4);

        Assert.Equal(1, reduced.Width);
        Assert.Equal(1, reduced.Height);
        Assert.Equal(120, reduced.Pixels[0]);  // bleu
        Assert.Equal(80, reduced.Pixels[1]);   // vert
        Assert.Equal(40, reduced.Pixels[2]);   // rouge
    }
}
