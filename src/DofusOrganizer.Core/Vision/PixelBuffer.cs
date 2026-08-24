namespace DofusOrganizer.Core.Vision;

/// <summary>
/// Une image en mémoire, pixels au format BGRA, lignes contiguës.
///
/// Volontairement autonome plutôt que fondée sur System.Drawing : ce type vit dans Core,
/// qui doit rester compilable et testable hors de Windows. La couche Win32 remplit ce
/// tampon depuis l'écran, les tests le remplissent à la main.
/// </summary>
public sealed class PixelBuffer
{
    public const int BytesPerPixel = 4;

    public PixelBuffer(int width, int height, byte[]? pixels = null)
    {
        if (width < 0 || height < 0) throw new ArgumentOutOfRangeException(nameof(width));

        Width = width;
        Height = height;
        Pixels = pixels ?? new byte[width * height * BytesPerPixel];

        if (Pixels.Length < width * height * BytesPerPixel)
        {
            throw new ArgumentException("Tampon trop petit pour les dimensions annoncées.", nameof(pixels));
        }
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Pixels en BGRA, index = (y * Width + x) * 4.</summary>
    public byte[] Pixels { get; }

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int OffsetOf(int x, int y) => (y * Width + x) * BytesPerPixel;

    public void SetPixel(int x, int y, byte r, byte g, byte b)
    {
        int offset = OffsetOf(x, y);
        Pixels[offset] = b;
        Pixels[offset + 1] = g;
        Pixels[offset + 2] = r;
        Pixels[offset + 3] = 255;
    }

    /// <summary>Extrait une sous-image. Les parties hors limites sont laissées à zéro.</summary>
    public PixelBuffer Crop(int x, int y, int width, int height)
    {
        var result = new PixelBuffer(width, height);

        for (int row = 0; row < height; row++)
        {
            int sourceY = y + row;
            if (sourceY < 0 || sourceY >= Height) continue;

            for (int column = 0; column < width; column++)
            {
                int sourceX = x + column;
                if (sourceX < 0 || sourceX >= Width) continue;

                Array.Copy(Pixels, OffsetOf(sourceX, sourceY),
                    result.Pixels, result.OffsetOf(column, row), BytesPerPixel);
            }
        }

        return result;
    }

    /// <summary>
    /// Réduit l'image d'un facteur entier en moyennant chaque bloc. Sert à la passe
    /// grossière de la recherche de motif : comparer des images réduites de 4 divise
    /// le travail par 256 avant d'affiner au pixel près.
    /// </summary>
    public PixelBuffer Downsample(int factor)
    {
        if (factor <= 1) return this;

        int width = Width / factor;
        int height = Height / factor;
        if (width <= 0 || height <= 0) return new PixelBuffer(0, 0);

        var result = new PixelBuffer(width, height);
        int samples = factor * factor;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int blue = 0, green = 0, red = 0;

                for (int dy = 0; dy < factor; dy++)
                {
                    int offset = OffsetOf(x * factor, y * factor + dy);
                    for (int dx = 0; dx < factor; dx++)
                    {
                        blue += Pixels[offset];
                        green += Pixels[offset + 1];
                        red += Pixels[offset + 2];
                        offset += BytesPerPixel;
                    }
                }

                result.SetPixel(x, y, (byte)(red / samples), (byte)(green / samples), (byte)(blue / samples));
            }
        }

        return result;
    }
}
