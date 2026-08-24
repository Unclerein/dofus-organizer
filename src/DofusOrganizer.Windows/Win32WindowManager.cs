using System.Text;
using DofusOrganizer.Core.Abstractions;
using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using DofusOrganizer.Core.Vision;
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

    /// <summary>
    /// Délai maximal accordé à une fenêtre pour devenir réellement utilisable après
    /// activation. Un client en plein écran exclusif se minimise quand il perd le focus :
    /// le restaurer impose à Windows un changement de mode d'affichage, qui prend
    /// plusieurs centaines de millisecondes. Attendre l'état réel plutôt qu'une durée
    /// fixe évite aussi bien les clics envoyés trop tôt que l'attente inutile en fenêtré.
    /// </summary>
    private const int ReadyTimeoutMs = 2000;
    private const int ReadyPollIntervalMs = 20;

    /// <summary>
    /// Identité de l'organizer, pour qu'il s'écarte de sa propre liste de personnages.
    /// Calculée une fois : elle ne change pas au cours de la vie du processus.
    /// </summary>
    private static readonly SelfIdentity Self = new(
        Environment.ProcessId,
        Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "");

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

            if (!GameWindowFilter.IsGameProcess(processName, (int)pid, Self, settings)) return true;

            string className = GetClassNameOf(hWnd);
            if (!GameWindowFilter.MatchesWindowClass(className, settings)) return true;

            results.Add(new GameWindow(hWnd, (int)pid, title, processName, className)
            {
                CharacterName = CharacterNameParser.Parse(title, settings.TitlePattern),
            });
            return true;
        }, 0);

        return results;
    }

    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();

    /// <summary>
    /// Fenêtre de premier niveau située sous un point de l'écran. C'est elle qui reçoit
    /// le clic, et pas nécessairement celle au premier plan : un hook bas niveau se
    /// déclenche avant que le focus ne change, donc cliquer sur un client pour l'activer
    /// se produit alors qu'un autre est encore au premier plan.
    /// </summary>
    public nint WindowUnder(ScreenPoint point)
    {
        nint hit = WindowFromPoint(new POINT { X = point.X, Y = point.Y });
        if (hit == 0) return 0;

        // WindowFromPoint rend le contrôle enfant le plus profond ; on remonte à la
        // fenêtre principale du client pour que les coordonnées soient relatives à elle.
        nint root = GetAncestor(hit, GA_ROOT);
        return root != 0 ? root : hit;
    }

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
        if (NativeMethods.GetForegroundWindow() == handle) return WaitUntilReady(handle);

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
                if (NativeMethods.GetForegroundWindow() == handle) return WaitUntilReady(handle);
                Thread.Sleep(ActivationRetryDelayMs);
            }

            SwitchToThisWindow(handle, true);
            Thread.Sleep(ActivationRetryDelayMs);
            return NativeMethods.GetForegroundWindow() == handle && WaitUntilReady(handle);
        }
        finally
        {
            if (attachedTarget) AttachThreadInput(ourThread, targetThread, false);
            if (attachedForeground) AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    /// <summary>
    /// Attend qu'une fenêtre tout juste activée soit vraiment prête à recevoir un clic :
    /// au premier plan, non minimisée, et dotée d'une zone client de taille non nulle.
    /// Une fenêtre en cours de restauration rapporte une zone client vide, et un clic
    /// envoyé à cet instant serait calculé sur des dimensions fausses.
    /// </summary>
    private bool WaitUntilReady(nint handle)
    {
        var deadline = Environment.TickCount64 + ReadyTimeoutMs;

        while (true)
        {
            bool foreground = NativeMethods.GetForegroundWindow() == handle;
            bool visible = !IsIconic(handle) && GetClientRect(handle, out RECT rect) && rect.Width > 0 && rect.Height > 0;

            if (foreground && visible) return true;
            if (Environment.TickCount64 >= deadline) return foreground;

            Thread.Sleep(ReadyPollIntervalMs);
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

    public PixelBuffer? CaptureScreen(ScreenRect area) => ScreenCapture.Capture(area);

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
