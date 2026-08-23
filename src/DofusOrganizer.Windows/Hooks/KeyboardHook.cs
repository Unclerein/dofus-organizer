using System.Runtime.InteropServices;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Windows.Native;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows.Hooks;

public sealed record KeyEvent(int VirtualKey, KeyModifiers Modifiers, bool IsDown, bool IsInjected);

public sealed class KeyboardHook() : LowLevelHook(WH_KEYBOARD_LL)
{
    /// <summary>Appelé pour chaque frappe. Renvoyer vrai absorbe la touche.</summary>
    public Func<KeyEvent, bool>? KeyEventReceived { get; set; }

    protected override bool Handle(nint wParam, nint lParam)
    {
        int message = (int)wParam;
        bool isDown = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool isUp = message is WM_KEYUP or WM_SYSKEYUP;
        if (!isDown && !isUp) return false;

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        bool injected = (data.flags & LLKHF_INJECTED) != 0 || data.dwExtraInfo == SendInputSender.InjectionTag;

        var handler = KeyEventReceived;
        if (handler is null) return false;

        return handler(new KeyEvent((int)data.vkCode, ModifierState.Current(), isDown, injected));
    }
}

/// <summary>
/// État courant des modificateurs. Le hook bas niveau ne rapporte que la touche frappée :
/// pour savoir si Ctrl ou Alt est maintenu, il faut interroger le clavier.
/// </summary>
public static class ModifierState
{
    public static KeyModifiers Current()
    {
        var modifiers = KeyModifiers.None;
        if (IsDown(VirtualKeys.Control)) modifiers |= KeyModifiers.Control;
        if (IsDown(VirtualKeys.Menu)) modifiers |= KeyModifiers.Alt;
        if (IsDown(VirtualKeys.Shift)) modifiers |= KeyModifiers.Shift;
        if (IsDown(VirtualKeys.LWin) || IsDown(VirtualKeys.RWin)) modifiers |= KeyModifiers.Windows;
        return modifiers;
    }

    private static bool IsDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
}
