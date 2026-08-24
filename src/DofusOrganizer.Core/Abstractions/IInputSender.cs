using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Abstractions;

/// <summary>Injection d'entrées clavier/souris au niveau du système.</summary>
public interface IInputSender
{
    void MoveMouse(AbsolutePoint point);

    /// <summary>
    /// Déplace le curseur sur le point et y clique une fois.
    ///
    /// Un seul clic, sans paramètre de répétition : enchaîner plusieurs clics demande un écart
    /// de temps entre eux, et cet écart appartient au moteur de macro, qui dispose de l'horloge
    /// et des réglages. Les émettre d'un bloc ici produirait des clics simultanés, que le jeu
    /// ne distingue pas.
    /// </summary>
    void Click(AbsolutePoint point, MouseButton button);

    void SendKey(int virtualKey, KeyModifiers modifiers, KeyAction action, bool useScanCodes);

    /// <summary>Molette au point donné. Crans positifs vers le haut, négatifs vers le bas.</summary>
    void Scroll(AbsolutePoint point, int notches);

    /// <summary>
    /// Enfonce ou relâche un bouton à un point donné, sans faire l'autre moitié. Nécessaire
    /// au glisser-déposer, où le bouton doit rester tenu pendant les déplacements.
    /// </summary>
    void PressButton(AbsolutePoint point, MouseButton button, bool down);

    ScreenPoint GetCursorPosition();

    void SetCursorPosition(ScreenPoint point);
}
