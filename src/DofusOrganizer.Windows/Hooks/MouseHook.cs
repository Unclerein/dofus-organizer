using System.Runtime.InteropServices;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Windows.Native;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows.Hooks;

public sealed record MouseEvent(
    ScreenPoint Point,
    MouseButton? Button,
    int? ExtraButton,
    bool IsDown,
    bool IsInjected,
    int WheelNotches = 0);

public sealed class MouseHook() : LowLevelHook(WH_MOUSE_LL)
{
    /// <summary>Appelé pour chaque clic. Renvoyer vrai absorbe l'événement.</summary>
    public Func<MouseEvent, bool>? MouseEventReceived { get; set; }

    protected override bool Handle(nint wParam, nint lParam)
    {
        int message = (int)wParam;
        var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        bool injected = (data.flags & LLKHF_INJECTED) != 0 || data.dwExtraInfo == SendInputSender.InjectionTag;

        MouseButton? button = null;
        int? extra = null;
        bool isDown;

        switch (message)
        {
            case WM_LBUTTONDOWN: button = MouseButton.Left; isDown = true; break;
            case WM_LBUTTONUP: button = MouseButton.Left; isDown = false; break;
            case WM_RBUTTONDOWN: button = MouseButton.Right; isDown = true; break;
            case WM_RBUTTONUP: button = MouseButton.Right; isDown = false; break;
            case WM_MBUTTONDOWN: button = MouseButton.Middle; isDown = true; break;
            case WM_MBUTTONUP: button = MouseButton.Middle; isDown = false; break;
            case WM_MOUSEWHEEL:
            {
                // Le nombre de crans est un entier signé logé dans le mot haut de mouseData.
                int delta = (short)(data.mouseData >> 16);
                int notches = delta / WHEEL_DELTA;
                if (notches == 0) return false;

                var wheelHandler = MouseEventReceived;
                return wheelHandler is not null && wheelHandler(new MouseEvent(
                    new ScreenPoint(data.pt.X, data.pt.Y), null, null, false, injected, notches));
            }

            case WM_XBUTTONDOWN:
            case WM_XBUTTONUP:
                // Le numéro du bouton latéral (1 ou 2) est logé dans le mot haut de mouseData.
                extra = (int)(data.mouseData >> 16) == 1 ? VirtualKeys.MouseButton4 : VirtualKeys.MouseButton5;
                isDown = message == WM_XBUTTONDOWN;
                break;
            default:
                return false;
        }

        var handler = MouseEventReceived;
        if (handler is null) return false;

        return handler(new MouseEvent(new ScreenPoint(data.pt.X, data.pt.Y), button, extra, isDown, injected));
    }
}
