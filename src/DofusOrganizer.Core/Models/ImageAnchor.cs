using System.Text.Json.Serialization;
using DofusOrganizer.Core.Vision;

namespace DofusOrganizer.Core.Models;

/// <summary>
/// Un fragment d'image capturé autour d'un point au moment de l'enregistrement, avec de quoi
/// le retrouver ailleurs.
///
/// C'est ce qui rend une étape indépendante de la position : la cible peut avoir glissé —
/// liste défilée, panneau ouvert plus bas, fenêtre d'une autre taille — le fragment, lui,
/// se ressemble. La position enregistrée ne sert plus que de point de départ à la recherche.
/// </summary>
public sealed class ImageAnchor : NotifyBase
{
    private int _searchRadius = 200;
    private double _minimumScore = TemplateMatcher.DefaultMinimumScore;

    public int Width { get; set; }

    public int Height { get; set; }

    /// <summary>
    /// Pixels BGRA encodés en base64. Le profil reste un fichier JSON lisible, et un
    /// fragment d'interface de 64×64 y pèse quelques kilo-octets — acceptable pour une
    /// poignée d'étapes, et cela évite d'éparpiller des fichiers image à côté.
    /// </summary>
    public string Pixels { get; set; } = "";

    /// <summary>
    /// Décalage du point cliqué par rapport au coin haut-gauche du fragment. Le clic ne vise
    /// pas le coin de l'image retrouvée mais l'endroit exact qui avait été cliqué dedans.
    /// </summary>
    public int OffsetX { get; set; }

    public int OffsetY { get; set; }

    /// <summary>
    /// Rayon de recherche autour de la position enregistrée. Chercher dans toute la fenêtre
    /// serait plus lent et exposerait à retrouver un élément semblable ailleurs à l'écran.
    /// </summary>
    public int SearchRadius
    {
        get => _searchRadius;
        set => Set(ref _searchRadius, Math.Clamp(value, 16, 4000));
    }

    /// <summary>Ressemblance minimale exigée. En deçà, l'étape retombe sur les coordonnées.</summary>
    public double MinimumScore
    {
        get => _minimumScore;
        set => Set(ref _minimumScore, Math.Clamp(value, 0.5, 1.0));
    }

    [JsonIgnore]
    public bool IsEmpty => Width <= 0 || Height <= 0 || string.IsNullOrEmpty(Pixels);

    /// <summary>Reconstruit l'image à partir de sa forme sérialisée, ou null si elle est inutilisable.</summary>
    public PixelBuffer? ToPixelBuffer()
    {
        if (IsEmpty) return null;

        try
        {
            byte[] pixels = Convert.FromBase64String(Pixels);
            if (pixels.Length < Width * Height * PixelBuffer.BytesPerPixel) return null;
            return new PixelBuffer(Width, Height, pixels);
        }
        catch (FormatException)
        {
            // Profil édité à la main et abîmé : l'étape retombera sur ses coordonnées.
            return null;
        }
    }

    public static ImageAnchor FromPixelBuffer(PixelBuffer image, int offsetX, int offsetY) => new()
    {
        Width = image.Width,
        Height = image.Height,
        Pixels = Convert.ToBase64String(image.Pixels),
        OffsetX = offsetX,
        OffsetY = offsetY,
    };
}
