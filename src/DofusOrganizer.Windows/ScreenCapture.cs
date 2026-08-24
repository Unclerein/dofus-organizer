using System.Runtime.InteropServices;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Vision;
using DofusOrganizer.Windows.Native;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows;

/// <summary>
/// Lecture d'une zone de l'écran.
///
/// La capture passe par le contexte d'affichage de l'écran et non par celui de la fenêtre
/// visée : un client Unity rend par le GPU, et lire son contexte de fenêtre renverrait une
/// image noire. Cela suppose que la fenêtre soit visible au premier plan — ce qui est le cas
/// au moment du rejeu, le moteur de macro l'activant avant d'agir.
/// </summary>
public static class ScreenCapture
{
    /// <summary>Capture une zone de l'écran, ou null si l'une des étapes GDI échoue.</summary>
    public static PixelBuffer? Capture(ScreenRect area)
    {
        if (area.IsEmpty) return null;

        nint screen = GetDC(0);
        if (screen == 0) return null;

        nint memory = 0;
        nint bitmap = 0;
        nint previous = 0;

        try
        {
            memory = CreateCompatibleDC(screen);
            if (memory == 0) return null;

            bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
            if (bitmap == 0) return null;

            previous = SelectObject(memory, bitmap);

            if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SRCCOPY)) return null;

            var buffer = new PixelBuffer(area.Width, area.Height);
            var info = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                    biWidth = area.Width,
                    // Hauteur négative : sans cela GDI rend l'image de bas en haut et
                    // toutes les positions trouvées seraient inversées verticalement.
                    biHeight = -area.Height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                },
            };

            int copied = GetDIBits(memory, bitmap, 0, (uint)area.Height, buffer.Pixels, ref info, DIB_RGB_COLORS);
            return copied == 0 ? null : buffer;
        }
        finally
        {
            // Les objets GDI ne sont pas ramassés automatiquement : les oublier fuiterait
            // à chaque capture, et une macro en déclenche plusieurs par personnage.
            if (previous != 0) SelectObject(memory, previous);
            if (bitmap != 0) DeleteObject(bitmap);
            if (memory != 0) DeleteDC(memory);
            ReleaseDC(0, screen);
        }
    }
}
