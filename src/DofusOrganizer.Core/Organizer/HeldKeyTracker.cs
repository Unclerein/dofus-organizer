using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Organizer;

/// <summary>
/// Suit les touches actuellement tenues, pour ne déclencher un raccourci qu'au premier appui et
/// non à chaque répétition automatique du clavier.
///
/// La liste des touches tenues est un cache que l'on vérifie, et non une vérité. La version
/// précédente ne la vidait qu'au relâchement : un seul relâchement manqué — le rappel du hook
/// n'ayant pas été servi à temps parce que le fil d'interface était bloqué par l'activation d'une
/// fenêtre — y laissait la touche pour de bon, et ce raccourci ne se déclenchait plus jamais
/// jusqu'au redémarrage, pendant que tous les autres continuaient de marcher.
///
/// Le système sait, lui, quelles touches sont physiquement enfoncées. Une touche que l'on croit
/// tenue mais que le clavier dit relâchée est une entrée périmée : on la purge et l'appui repart
/// normalement. La répétition automatique reste filtrée, puisque la touche y est bel et bien
/// enfoncée.
/// </summary>
public sealed class HeldKeyTracker
{
    private readonly HashSet<int> _held = [];

    /// <summary>
    /// Touches dont l'appui a été absorbé. Leur relâchement doit l'être aussi : un jeu qui
    /// reçoit un relâchement sans l'appui correspondant peut considérer la touche comme coincée.
    /// </summary>
    private readonly HashSet<int> _swallowed = [];

    /// <summary>
    /// Signale un appui. Renvoie vrai s'il s'agit d'un véritable premier appui, à distribuer ;
    /// faux pour une répétition automatique, à ignorer.
    /// </summary>
    public bool BeginPress(int virtualKey) => _held.Add(virtualKey);

    /// <summary>Note qu'un appui distribué a été absorbé, pour absorber également son relâchement.</summary>
    public bool MarkSwallowed(int virtualKey, bool swallowed)
    {
        if (swallowed) _swallowed.Add(virtualKey);
        return swallowed;
    }

    /// <summary>Vrai si l'appui en cours de cette touche avait été absorbé.</summary>
    public bool IsSwallowed(int virtualKey) => _swallowed.Contains(virtualKey);

    /// <summary>Signale un relâchement. Renvoie vrai s'il doit être absorbé.</summary>
    public bool EndPress(int virtualKey)
    {
        _held.Remove(virtualKey);
        return _swallowed.Remove(virtualKey);
    }

    /// <summary>Oublie tout, par exemple quand les hooks sont réinstallés.</summary>
    public void Clear()
    {
        _held.Clear();
        _swallowed.Clear();
    }

    /// <summary>Nombre de touches considérées comme tenues, pour les tests et le diagnostic.</summary>
    public int HeldCount => _held.Count;

    /// <summary>
    /// Retire les touches que l'on croit tenues alors que le clavier les dit relâchées, et
    /// renvoie leur nombre.
    ///
    /// À appeler périodiquement, et non au moment d'un appui : à cet instant précis la touche
    /// est justement enfoncée, qu'elle le soit depuis une répétition automatique ou qu'elle
    /// vienne d'être frappée à nouveau. Rien ne les distingue là, alors qu'entre deux frappes le
    /// clavier tranche sans ambiguïté. C'est ce balayage régulier qui répare un relâchement
    /// manqué, en une seconde au lieu de jamais.
    ///
    /// Les deux ensembles sont purgés de pair : ne vider que le premier laisserait une touche
    /// marquée comme absorbée sans être tenue, et le relâchement de l'appui suivant — que nous
    /// aurions pourtant laissé passer au jeu — serait absorbé à tort.
    /// </summary>
    public int DropKeysNoLongerHeld(Func<int, bool> isPhysicallyDown)
    {
        if (_held.Count == 0) return 0;

        var stale = _held.Where(key => !isPhysicallyDown(key)).ToList();
        foreach (int key in stale)
        {
            _held.Remove(key);
            _swallowed.Remove(key);
        }

        return stale.Count;
    }
}

/// <summary>
/// Traduit les codes que l'organizer se donne pour les boutons de souris supplémentaires vers
/// les codes virtuels que le système comprend.
///
/// Sans cette traduction, interroger l'état physique d'un bouton de souris porterait sur un code
/// inexistant, toujours rapporté comme relâché : la purge le retirerait à chaque appui et le
/// filtrage de la répétition serait désarmé pour ces boutons.
/// </summary>
public static class PhysicalKeyCodes
{
    private const int VkMiddleButton = 0x04;
    private const int VkExtraButton1 = 0x05;
    private const int VkExtraButton2 = 0x06;

    public static int ForSystem(int virtualKey) => virtualKey switch
    {
        VirtualKeys.MouseMiddle => VkMiddleButton,
        VirtualKeys.MouseButton4 => VkExtraButton1,
        VirtualKeys.MouseButton5 => VkExtraButton2,
        _ => virtualKey,
    };
}
