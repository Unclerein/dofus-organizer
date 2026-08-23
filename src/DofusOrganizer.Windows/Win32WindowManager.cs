using System.Text;
using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Windows.Native;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows;

public sealed class Win32WindowManager : IWindowManager
{
    /// <summary>
    /// Nombre de tentatives d'activation. Windows refuse parfois le premier plan à un
    /// processus qui n'a pas l'entrée utilisateur ; une nouvelle tentative après quelques
    /// millisecondes suffit en général.
    /// </summary>
    private const int ActivationAttempts = 3;
    private const int ActivationRetryDelayMs = 30;

    public IReadOnlyList<GameWindow> EnumerateGameWindows(AppSettings settings)
    {
        var results = new List<GameWindow>();
        var processNames = new Dictionary<uint, string>();

        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            string title = GetText(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0) return true;

            if (!processNames.TryGetValue(pid, out string? processName))
            {
                processName = ProcessName(pid);
                processNames[pid] = processName;
            }

            if (!MatchesProcess(processName, settings.ProcessNames)) return true;

            string className = GetClassNameOf(hWnd);
            if (!string.IsNullOrWhiteSpace(settings.WindowClassFilter)
                && !className.Contains(settings.WindowClassFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            results.Add(new GameWindow(hWnd, (int)pid, title, processName, className)
            {
                CharacterName = CharacterNameParser.Parse(title, settings.TitlePattern),
            });
            return true;
        }, 0);

        return results;
    }

    private static bool MatchesProcess(string processName, List<string> allowed)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        foreach (string candidate in allowed)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (processName.Contains(candidate.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    public bool IsWindow(nint handle) => handle != 0 && NativeMethods.IsWindow(handle);

    /// <summary>
    /// Met une fenêtre au premier plan. SetForegroundWindow seul échoue dès que le
    /// processus appelant n'est pas celui qui a l'entrée utilisateur : on s'attache
    /// d'abord au fil de la fenêtre active pour que Windows nous considère légitimes,
    /// puis on retombe sur SwitchToThisWindow si le compte n'y est toujours pas.
    /// </summary>
    public bool Activate(nint handle)
    {
        if (!IsWindow(handle)) return false;
        if (NativeMethods.GetForegroundWindow() == handle) return true;

        if (IsIconic(handle)) ShowWindow(handle, SW_RESTORE);

        uint ourThread = GetCurrentThreadId();
        uint targetThread = GetWindowThreadProcessId(handle, out _);
        uint foregroundThread = GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out _);

        bool attachedForeground = foregroundThread != 0 && foregroundThread != ourThread
            && AttachThreadInput(ourThread, foregroundThread, true);
        bool attachedTarget = targetThread != 0 && targetThread != ourThread && targetThread != foregroundThread
            && AttachThreadInput(ourThread, targetThread, true);

        try
        {
            for (int attempt = 0; attempt < ActivationAttempts; attempt++)
            {
                BringWindowToTop(handle);
                SetForegroundWindow(handle);
                if (NativeMethods.GetForegroundWindow() == handle) return true;
                Thread.Sleep(ActivationRetryDelayMs);
            }

            SwitchToThisWindow(handle, true);
            Thread.Sleep(ActivationRetryDelayMs);
            return NativeMethods.GetForegroundWindow() == handle;
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(ourThread, targetThread, false);
            if (attachedForeground) AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    public bool TryGetClientBounds(nint handle, out ClientBounds bounds)
    {
        bounds = default;
        if (!IsWindow(handle)) return false;
        if (!GetClientRect(handle, out RECT rect)) return false;

        var origin = new POINT { X = rect.Left, Y = rect.Top };
        if (!ClientToScreen(handle, ref origin)) return false;

        bounds = new ClientBounds(new ScreenPoint(origin.X, origin.Y), rect.Width, rect.Height);
        return !bounds.IsEmpty;
    }

    public VirtualScreen GetVirtualScreen() => new(
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN));

    private static string GetText(nint hWnd)
    {
        int length = GetWindowTextLength(hWnd);
        if (length <= 0) return "";
        var buffer = new StringBuilder(length + 1);
        int written = GetWindowText(hWnd, buffer, buffer.Capacity);
        return written > 0 ? buffer.ToString() : "";
    }

    private static string GetClassNameOf(nint hWnd)
    {
        var buffer = new StringBuilder(256);
        int written = GetClassName(hWnd, buffer, buffer.Capacity);
        return written > 0 ? buffer.ToString() : "";
    }

    /// <summary>
    /// Nom d'exécutable d'un processus. On passe par QueryFullProcessImageName plutôt que
    /// par Process.GetProcessById : l'appel est fait pour chaque fenêtre du bureau à
    /// chaque rafraîchissement, et il ne doit rien allouer de coûteux.
    /// </summary>
    private static string ProcessName(uint processId)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0) return "";

        try
        {
            int size = 1024;
            var buffer = new StringBuilder(size);
            if (!QueryFullProcessImageName(process, 0, buffer, ref size)) return "";
            return Path.GetFileNameWithoutExtension(buffer.ToString());
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>
    /// Vrai si le processus refuse de s'ouvrir alors que sa fenêtre est visible, ce qui
    /// signale presque toujours un client lancé en administrateur. Dans ce cas, ni les
    /// hooks ni SendInput ne l'atteindront tant que l'organizer n'est pas élevé lui aussi.
    /// </summary>
    public static bool LooksElevated(int processId)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (process == 0) return true;
        CloseHandle(process);
        return false;
    }
}
