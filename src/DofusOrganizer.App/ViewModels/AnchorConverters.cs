using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.App.ViewModels;

/// <summary>
/// Affiche l'image d'ancrage dans l'éditeur. On doit pouvoir vérifier d'un coup d'œil ce qui
/// a été capturé : une capture ratée — panneau à moitié ouvert, curseur au mauvais endroit —
/// ne se voit pas autrement qu'en la regardant.
/// </summary>
public sealed class AnchorPreviewConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ImageAnchor { IsEmpty: false } anchor) return null;

        var buffer = anchor.ToPixelBuffer();
        if (buffer is null) return null;

        var bitmap = BitmapSource.Create(
            buffer.Width, buffer.Height, 96, 96, PixelFormats.Bgra32, null,
            buffer.Pixels, buffer.Width * Core.Vision.PixelBuffer.BytesPerPixel);

        bitmap.Freeze();
        return bitmap;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Décrit l'ancrage en toutes lettres, à côté de son aperçu.</summary>
public sealed class AnchorSummaryConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ImageAnchor { IsEmpty: false } anchor
            ? $"Image de {anchor.Width}×{anchor.Height} px, recherchée dans un rayon de "
              + $"{anchor.SearchRadius} px, ressemblance exigée {anchor.MinimumScore:P0}."
            : "Aucune image : le clic visera sa position enregistrée, qui peut ne pas "
              + "correspondre chez un autre personnage.";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
