using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Abstractions;

/// <summary>
/// Tout ce que le moteur a besoin de savoir des fenêtres. L'implémentation Win32 vit
/// dans DofusOrganizer.Windows ; les tests en fournissent une version en mémoire.
/// </summary>
public interface IWindowManager
{
    /// <summary>Fenêtres de jeu actuellement ouvertes, filtrées selon les réglages.</summary>
    IReadOnlyList<GameWindow> EnumerateGameWindows(AppSettings settings);

    nint GetForegroundWindow();

    /// <summary>
    /// Fenêtre de premier niveau sous un point de l'écran, ou 0. Ce n'est pas toujours
    /// celle au premier plan : un hook de souris se déclenche avant le changement de focus.
    /// </summary>
    nint WindowUnder(ScreenPoint point);

    /// <summary>Met la fenêtre au premier plan. Renvoie faux si Windows a refusé le changement.</summary>
    bool Activate(nint handle);

    bool IsWindow(nint handle);

    /// <summary>Zone client de la fenêtre, position à l'écran comprise.</summary>
    bool TryGetClientBounds(nint handle, out ClientBounds bounds);

    /// <summary>Rectangle englobant tous les écrans.</summary>
    VirtualScreen GetVirtualScreen();
}
