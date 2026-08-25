using System.Text.RegularExpressions;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Organizer;

/// <summary>Un emplacement de la liste, associé à la fenêtre qui l'occupe actuellement (s'il y en a une).</summary>
public sealed class RosterEntry(CharacterSlot slot)
{
    public CharacterSlot Slot { get; } = slot;
    public GameWindow? Window { get; internal set; }
    public bool IsPresent => Window is not null;

    /// <summary>Présent, coché, donc éligible au cycle et aux boucles de macro.</summary>
    public bool IsActive => IsPresent && Slot.Enabled;
}

/// <summary>
/// La liste ordonnée de personnages : elle réconcilie à chaque rafraîchissement les
/// fenêtres réellement ouvertes avec les emplacements persistés, et décide qui vient
/// après qui pour la touche « suivant ».
/// </summary>
public sealed class CharacterRoster
{
    private readonly List<RosterEntry> _entries = [];

    public IReadOnlyList<RosterEntry> Entries => _entries;

    /// <summary>Emplacements présents et cochés, dans l'ordre de la liste.</summary>
    public IReadOnlyList<RosterEntry> ActiveEntries => _entries.Where(e => e.IsActive).ToList();

    /// <summary>
    /// Nombre de clients détectés qui ne nomment encore aucun personnage — en cours de
    /// chargement ou restés à l'écran de connexion.
    ///
    /// Compté plutôt qu'affiché : ces fenêtres n'ont pas leur place dans la liste, mais une
    /// liste vide sans explication serait indéchiffrable, en particulier si le motif
    /// d'extraction ne convient pas à la version installée.
    /// </summary>
    public int PendingWindows { get; private set; }

    /// <summary>
    /// Aligne la liste sur les fenêtres ouvertes : les emplacements connus retrouvent
    /// leur fenêtre, les personnages jamais vus sont ajoutés à la fin, et ceux dont le
    /// client est fermé restent en place (grisés) pour ne pas perdre leur raccourci.
    ///
    /// Une fenêtre qui ne nomme aucun personnage est comptée et ignorée. Elle en nommera un
    /// dans quelques secondes, et retrouvera alors l'emplacement persisté à ce nom — avec son
    /// raccourci et sa position. Lui en créer un dès maintenant, sous son titre de passage,
    /// laisserait derrière elle un emplacement orphelin à chaque changement de titre.
    /// </summary>
    public void Sync(IReadOnlyList<GameWindow> windows, List<CharacterSlot> slots)
    {
        foreach (var entry in _entries) entry.Window = null;

        // On repart des emplacements persistés pour que l'ordre choisi par l'utilisateur fasse foi.
        SyncSlots(slots);

        PendingWindows = 0;
        var unmatched = new List<(GameWindow Window, string Name)>();

        foreach (var window in windows)
        {
            if (window.CharacterName is not { Length: > 0 } name || string.IsNullOrWhiteSpace(name))
            {
                PendingWindows++;
                continue;
            }

            var entry = _entries.FirstOrDefault(e => e.Window is null && KeyMatches(e.Slot.Key, name));
            if (entry is null) unmatched.Add((window, name));
            else entry.Window = window;
        }

        foreach (var (window, name) in unmatched)
        {
            var slot = new CharacterSlot { Key = name };
            slots.Add(slot);
            _entries.Add(new RosterEntry(slot) { Window = window });
        }
    }

    /// <summary>
    /// Retire tous les emplacements dont le client est fermé, et renvoie leur nombre.
    ///
    /// Déclenché par l'utilisateur et jamais automatiquement : un emplacement survit à la
    /// fermeture du client exprès, c'est ce qui garde un raccourci attaché à un personnage
    /// d'une session à l'autre, et l'ordre que suit la touche « personnage suivant ».
    /// </summary>
    public int ForgetAbsent(List<CharacterSlot> slots)
    {
        var absent = _entries.Where(e => !e.IsPresent).Select(e => e.Slot).ToList();

        foreach (var slot in absent) slots.Remove(slot);
        _entries.RemoveAll(e => absent.Contains(e.Slot));

        return absent.Count;
    }

    private void SyncSlots(List<CharacterSlot> slots)
    {
        _entries.RemoveAll(e => !slots.Contains(e.Slot));
        for (int i = 0; i < slots.Count; i++)
        {
            var existing = _entries.FirstOrDefault(e => ReferenceEquals(e.Slot, slots[i]));
            if (existing is null)
            {
                _entries.Insert(Math.Min(i, _entries.Count), new RosterEntry(slots[i]));
            }
            else
            {
                int current = _entries.IndexOf(existing);
                if (current != i && i < _entries.Count)
                {
                    _entries.RemoveAt(current);
                    _entries.Insert(i, existing);
                }
            }
        }
    }

    private static bool KeyMatches(string slotKey, string windowKey)
        => string.Equals(slotKey, windowKey, StringComparison.OrdinalIgnoreCase);

    public RosterEntry? BySlotIndex(int index)
        => index >= 0 && index < _entries.Count ? _entries[index] : null;

    public RosterEntry? ByHandle(nint handle)
        => handle == 0 ? null : _entries.FirstOrDefault(e => e.Window?.Handle == handle);

    public RosterEntry? First() => ActiveEntries.FirstOrDefault();

    public RosterEntry? Next(nint currentHandle) => Step(currentHandle, +1);

    public RosterEntry? Previous(nint currentHandle) => Step(currentHandle, -1);

    /// <summary>
    /// Avance dans la liste des personnages actifs en repartant de la fenêtre au premier plan.
    /// Si le focus n'est sur aucun personnage connu, on repart du premier — c'est le
    /// comportement attendu quand on revient d'un navigateur ou de Discord.
    /// </summary>
    private RosterEntry? Step(nint currentHandle, int direction)
    {
        var active = ActiveEntries;
        if (active.Count == 0) return null;

        int index = -1;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].Window?.Handle == currentHandle) { index = i; break; }
        }
        if (index < 0) return active[0];

        int next = ((index + direction) % active.Count + active.Count) % active.Count;
        return active[next];
    }

    public void Move(CharacterSlot slot, int delta, List<CharacterSlot> slots)
    {
        int from = slots.IndexOf(slot);
        if (from < 0) return;
        int to = Math.Clamp(from + delta, 0, slots.Count - 1);
        if (to == from) return;
        slots.RemoveAt(from);
        slots.Insert(to, slot);
    }
}

/// <summary>Extraction du nom de personnage depuis le titre de la fenêtre.</summary>
public static class CharacterNameParser
{
    /// <summary>
    /// Applique le motif configuré et renvoie le groupe « name », ou null si le titre ne nomme
    /// aucun personnage.
    ///
    /// Ne pas répondre est le point important. Le titre d'un client traverse trois états —
    /// « Dofus », puis « Dofus 3.6.10.11 - Release », puis « Nom - Classe - Version - Release » —
    /// et un repli sur le titre brut ferait des deux premiers des personnages à part entière,
    /// chacun avec son emplacement, sa ligne et son raccourci à assigner. Quatre clients en
    /// produisaient douze.
    ///
    /// Un motif vide vaut « accepter tout titre tel quel » : c'est la porte de sortie pour une
    /// version dont le titre aurait une forme imprévue.
    /// </summary>
    public static string? Parse(string title, string pattern)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        if (string.IsNullOrWhiteSpace(pattern)) return title.Trim();

        try
        {
            var match = Regex.Match(title, pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (match.Success)
            {
                var group = match.Groups["name"];
                string value = group.Success ? group.Value : match.Value;
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
        }
        catch (ArgumentException)
        {
            // Motif invalide saisi dans les réglages. Ne rien reconnaître vaut mieux que
            // reconnaître n'importe quoi : la barre d'état signale les clients sans nom.
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return null;
    }
}
