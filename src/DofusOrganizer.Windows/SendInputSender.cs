using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Windows.Native;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows;

/// <summary>
/// Injection d'entrées via SendInput. C'est la seule voie utilisable avec un client Unity :
/// ces moteurs lisent l'entrée brute et ignorent les messages postés à une fenêtre
/// d'arrière-plan, d'où l'obligation de donner le focus avant de cliquer.
/// </summary>
public sealed class SendInputSender : IInputSender
{
    /// <summary>
    /// Marqueur apposé à nos propres injections. Les hooks s'en servent pour ne pas
    /// réagir aux entrées qu'on vient d'émettre, ce qui déclencherait une macro en boucle.
    /// </summary>
    public static readonly nint InjectionTag = 0x0D0F5;

    private static readonly int InputSize = System.Runtime.InteropServices.Marshal.SizeOf<INPUT>();

    public void MoveMouse(AbsolutePoint point) => Send([MouseInput(point, MOUSEEVENTF_MOVE)]);

    public void Click(AbsolutePoint point, MouseButton button, int clicks)
    {
        (uint down, uint up) = button switch
        {
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        var inputs = new List<INPUT> { MouseInput(point, MOUSEEVENTF_MOVE) };
        for (int i = 0; i < Math.Max(1, clicks); i++)
        {
            // Le déplacement est répété avec chaque appui : certains clients recalculent
            // la case survolée au moment du clic et non au déplacement précédent.
            inputs.Add(MouseInput(point, down));
            inputs.Add(MouseInput(point, up));
        }

        Send([.. inputs]);
    }

    public void SendKey(int virtualKey, KeyModifiers modifiers, KeyAction action, bool useScanCodes)
    {
        var inputs = new List<INPUT>();

        bool press = action is KeyAction.Press or KeyAction.Down;
        bool release = action is KeyAction.Press or KeyAction.Up;

        if (press) AddModifiers(inputs, modifiers, down: true, useScanCodes);
        if (press) inputs.Add(KeyInput(virtualKey, down: true, useScanCodes));
        if (release) inputs.Add(KeyInput(virtualKey, down: false, useScanCodes));
        // Les modificateurs sont relâchés dans l'ordre inverse, comme le ferait une vraie main.
        if (release) AddModifiers(inputs, modifiers, down: false, useScanCodes);

        if (inputs.Count > 0) Send([.. inputs]);
    }

    public void PressButton(AbsolutePoint point, MouseButton button, bool down)
    {
        (uint press, uint release) = button switch
        {
            MouseButton.Right => (MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP),
            MouseButton.Middle => (MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP),
            _ => (MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP),
        };

        Send([MouseInput(point, down ? press : release)]);
    }

    public void Scroll(AbsolutePoint point, int notches)
    {
        if (notches == 0) return;

        // Le curseur est amené sur le point d'abord : la molette agit sur ce qui est
        // survolé, pas sur ce qui a le focus.
        var inputs = new List<INPUT> { MouseInput(point, MOUSEEVENTF_MOVE) };

        // Un cran vaut WHEEL_DELTA. Les envoyer un par un plutôt qu'en un seul bloc :
        // les listes défilent souvent d'une ligne par cran, et un multiple géant peut
        // être plafonné ou ignoré.
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            inputs.Add(WheelInput(point, notches > 0 ? WHEEL_DELTA : -WHEEL_DELTA));
        }

        Send([.. inputs]);
    }

    private static INPUT WheelInput(AbsolutePoint point, int amount) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = point.X,
                dy = point.Y,
                mouseData = unchecked((uint)amount),
                dwFlags = MOUSEEVENTF_WHEEL | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                dwExtraInfo = InjectionTag,
            },
        },
    };

    public ScreenPoint GetCursorPosition()
        => GetCursorPos(out POINT point) ? new ScreenPoint(point.X, point.Y) : new ScreenPoint(0, 0);

    public void SetCursorPosition(ScreenPoint point) => SetCursorPos(point.X, point.Y);

    private static void AddModifiers(List<INPUT> inputs, KeyModifiers modifiers, bool down, bool useScanCodes)
    {
        var keys = new List<int>(4);
        if (modifiers.HasFlag(KeyModifiers.Control)) keys.Add(VirtualKeys.Control);
        if (modifiers.HasFlag(KeyModifiers.Alt)) keys.Add(VirtualKeys.Menu);
        if (modifiers.HasFlag(KeyModifiers.Shift)) keys.Add(VirtualKeys.Shift);
        if (modifiers.HasFlag(KeyModifiers.Windows)) keys.Add(VirtualKeys.LWin);

        if (!down) keys.Reverse();
        foreach (int key in keys) inputs.Add(KeyInput(key, down, useScanCodes));
    }

    private static INPUT MouseInput(AbsolutePoint point, uint flags) => new()
    {
        type = INPUT_MOUSE,
        U = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = point.X,
                dy = point.Y,
                dwFlags = flags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                dwExtraInfo = InjectionTag,
            },
        },
    };

    private static INPUT KeyInput(int virtualKey, bool down, bool useScanCodes)
    {
        ushort scan = (ushort)MapVirtualKey((uint)virtualKey, MAPVK_VK_TO_VSC);
        uint flags = down ? 0 : KEYEVENTF_KEYUP;

        // Les touches du pavé directionnel et quelques autres portent un préfixe 0xE0 :
        // sans ce drapeau, le jeu reçoit la touche du pavé numérique à la place.
        if (IsExtendedKey(virtualKey)) flags |= KEYEVENTF_EXTENDEDKEY;

        // Le code de balayage seul est ce que lisent les moteurs de jeu en entrée brute ;
        // le code virtuel seul suffit aux applications classiques.
        if (useScanCodes && scan != 0) flags |= KEYEVENTF_SCANCODE;

        return new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)((flags & KEYEVENTF_SCANCODE) != 0 ? 0 : virtualKey),
                    wScan = scan,
                    dwFlags = flags,
                    dwExtraInfo = InjectionTag,
                },
            },
        };
    }

    private static bool IsExtendedKey(int virtualKey) => virtualKey is
        VirtualKeys.Left or VirtualKeys.Up or VirtualKeys.Right or VirtualKeys.Down or
        VirtualKeys.Home or VirtualKeys.End or VirtualKeys.Prior or VirtualKeys.Next or
        VirtualKeys.Insert or VirtualKeys.Delete or VirtualKeys.NumLock or
        VirtualKeys.Divide or VirtualKeys.RControl or VirtualKeys.RMenu;

    private static void Send(INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        SendInput((uint)inputs.Length, inputs, InputSize);
    }
}
