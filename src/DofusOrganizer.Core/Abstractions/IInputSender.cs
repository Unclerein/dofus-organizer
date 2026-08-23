using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Abstractions;

/// <summary>Injection d'entrées clavier/souris au niveau du système.</summary>
public interface IInputSender
{
    void MoveMouse(AbsolutePoint point);

    /// <summary>Déplace le curseur sur le point puis clique le nombre de fois demandé.</summary>
    void Click(AbsolutePoint point, MouseButton button, int clicks);

    void SendKey(int virtualKey, KeyModifiers modifiers, KeyAction action, bool useScanCodes);

    ScreenPoint GetCursorPosition();

    void SetCursorPosition(ScreenPoint point);
}
