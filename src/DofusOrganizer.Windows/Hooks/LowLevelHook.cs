using DofusOrganizer.Windows.Native;
using static DofusOrganizer.Windows.Native.NativeMethods;

namespace DofusOrganizer.Windows.Hooks;

/// <summary>
/// Base des hooks WH_*_LL. Deux règles à ne pas enfreindre :
/// le délégué de rappel doit rester référencé par un champ (le ramasse-miettes le
/// collecterait sinon, et le hook mourrait au bout de quelques minutes), et le hook
/// doit être installé depuis un fil disposant d'une boucle de messages — en pratique
/// le fil d'interface de l'application.
/// </summary>
public abstract class LowLevelHook : IDisposable
{
    private readonly HookProc _callback;
    private readonly int _hookId;
    private nint _handle;

    protected LowLevelHook(int hookId)
    {
        _hookId = hookId;
        _callback = OnHook;
    }

    public bool IsInstalled => _handle != 0;

    public void Install()
    {
        if (_handle != 0) return;
        _handle = SetWindowsHookEx(_hookId, _callback, GetModuleHandle(null), 0);
        if (_handle == 0)
        {
            throw new InvalidOperationException(
                "Installation du hook clavier/souris impossible. " +
                "Vérifiez qu'aucun autre logiciel ne la bloque.");
        }
    }

    public void Uninstall()
    {
        if (_handle == 0) return;
        UnhookWindowsHookEx(_handle);
        _handle = 0;
    }

    /// <summary>Renvoie vrai pour absorber l'événement et l'empêcher d'atteindre le jeu.</summary>
    protected abstract bool Handle(nint wParam, nint lParam);

    private nint OnHook(int nCode, nint wParam, nint lParam)
    {
        if (nCode == HC_ACTION)
        {
            try
            {
                if (Handle(wParam, lParam)) return 1;
            }
            catch
            {
                // Une exception qui traverserait le rappel ferait tomber le hook :
                // mieux vaut laisser passer l'événement que perdre tous les raccourcis.
            }
        }
        return CallNextHookEx(0, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        Uninstall();
        GC.SuppressFinalize(this);
    }
}
