using System.Runtime.InteropServices;
using DofusOrganizer.Core.Abstractions;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows;

/// <summary>
/// Le presse-papiers du système, par les appels bruts.
///
/// Deux raisons de ne pas passer par celui de WPF. Il exige un fil en appartement cloisonné
/// (STA), alors que les macros tournent délibérément hors du fil d'interface. Et il lève une
/// exception quand le presse-papiers est occupé, là où le comportement voulu ici est de
/// réessayer : le presse-papiers est un verrou global que n'importe quelle application peut
/// tenir une fraction de seconde — un gestionnaire d'historique, un navigateur — et un unique
/// appel échoue au hasard.
/// </summary>
public sealed class WindowsClipboard : IClipboard
{
    /// <summary>
    /// Tentatives d'ouverture avant de renoncer. Le verrou est rarement tenu longtemps ;
    /// l'échec durable veut dire qu'une autre application se comporte mal, et il vaut mieux
    /// rendre la main que de bloquer une macro.
    /// </summary>
    private const int Attempts = 10;

    private const int RetryDelayMs = 20;

    public void Clear() => WithClipboard(() =>
    {
        EmptyClipboard();
        return true;
    });

    public string? GetText()
    {
        string? text = null;

        WithClipboard(() =>
        {
            nint handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0) return true;   // pas de texte : ce n'est pas un échec, c'est une réponse

            nint memory = GlobalLock(handle);
            if (memory == 0) return false;

            try
            {
                // La taille borne la lecture : un bloc sans terminateur ferait courir
                // PtrToStringUni au-delà de ce qui lui appartient.
                int characters = (int)(GlobalSize(handle) / sizeof(char));
                text = Marshal.PtrToStringUni(memory, characters)?.TrimEnd('\0');
            }
            finally
            {
                GlobalUnlock(handle);
            }

            return true;
        });

        return text;
    }

    public void SetText(string text) => WithClipboard(() =>
    {
        nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
        nint block = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (block == 0) return false;

        nint memory = GlobalLock(block);
        if (memory == 0)
        {
            GlobalFree(block);
            return false;
        }

        try
        {
            Marshal.Copy(text.ToCharArray(), 0, memory, text.Length);
            Marshal.WriteInt16(memory, text.Length * sizeof(char), 0);
        }
        finally
        {
            GlobalUnlock(block);
        }

        if (!EmptyClipboard() || SetClipboardData(CF_UNICODETEXT, block) == 0)
        {
            // Le bloc n'a pas été adopté : il est encore à nous, donc à nous de le rendre.
            GlobalFree(block);
            return false;
        }

        // Passé ce point le système possède le bloc et le libérera lui-même. Le libérer ici
        // rendrait le presse-papiers illisible, ou pire.
        return true;
    });

    /// <summary>
    /// Ouvre le presse-papiers, exécute l'action, et le referme quoi qu'il arrive.
    ///
    /// Ne pas refermer le laisserait verrouillé pour tout le système jusqu'à la fin du
    /// processus : plus aucune application ne pourrait copier ni coller. D'où le
    /// <c>finally</c>, y compris sur le chemin d'erreur.
    /// </summary>
    private static bool WithClipboard(Func<bool> action)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (!OpenClipboard(0))
            {
                Thread.Sleep(RetryDelayMs);
                continue;
            }

            try
            {
                return action();
            }
            finally
            {
                CloseClipboard();
            }
        }

        return false;
    }
}
