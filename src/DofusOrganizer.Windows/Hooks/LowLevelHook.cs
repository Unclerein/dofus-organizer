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

    /// <summary>
    /// Instant du dernier rappel reçu. Lu et écrit depuis le fil d'interface, qui pose les
    /// hooks et les sert : pas de synchronisation à prévoir.
    /// </summary>
    private long _lastCallbackMs;

    protected LowLevelHook(int hookId)
    {
        _hookId = hookId;
        _callback = OnHook;
    }

    /// <summary>
    /// Vrai si <em>nous</em> croyons le hook posé. Windows peut l'avoir décroché sans rien dire
    /// — c'est ce qu'il fait quand un rappel tarde trop — et cette propriété répondra encore
    /// vrai. Elle ne prouve donc rien : voir <see cref="LastCallbackMs"/>.
    /// </summary>
    public bool IsInstalled => _handle != 0;

    /// <summary>
    /// Instant du dernier rappel, seule preuve que le hook est vivant. Comparé à la dernière
    /// entrée vue par le système, il dit si nous en avons manqué.
    /// </summary>
    public long LastCallbackMs => _lastCallbackMs;

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

        // Le compteur repart de l'installation, sinon un hook tout juste posé passerait pour
        // muet et serait réinstallé aussitôt.
        _lastCallbackMs = Environment.TickCount64;
    }

    /// <summary>
    /// Repose le hook. Renvoie faux si le système refuse la nouvelle pose — auquel cas il n'y a
    /// plus de hook du tout, et mieux vaut le dire que le laisser croire rétabli.
    /// </summary>
    public bool Reinstall()
    {
        Uninstall();

        try
        {
            Install();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
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
        // Avant tout traitement, et quel que soit le code : être appelé suffit à prouver qu'on
        // est vivant, c'est tout ce que la surveillance demande.
        _lastCallbackMs = Environment.TickCount64;

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
