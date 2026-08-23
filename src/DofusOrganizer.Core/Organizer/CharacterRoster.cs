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
    /// Aligne la liste sur les fenêtres ouvertes : les emplacements connus retrouvent
    /// leur fenêtre, les personnages jamais vus sont ajoutés à la fin, et ceux dont le
    /// client est fermé restent en place (grisés) pour ne pas perdre leur raccourci.
    /// </summary>
    public void Sync(IReadOnlyList<GameWindow> windows, List<CharacterSlot> slots)
    {
        foreach (var entry in _entries) entry.Window = null;

        // On repart des emplacements persistés pour que l'ordre choisi par l'utilisateur fasse foi.
        SyncSlots(slots);

        var unmatched = new List<GameWindow>();
        foreach (var window in windows)
        {
            string key = KeyFor(window);
            var entry = _entries.FirstOrDefault(e => e.Window is null && KeyMatches(e.Slot.Key, key));
            if (entry is null) unmatched.Add(window);
            else entry.Window = window;
        }

        foreach (var window in unmatched)
        {
            // Un client resté à l'écran de connexion n'a pas encore de nom de personnage :
            // il apparaît sous son titre brut, et l'emplacement définitif se créera une
            // fois connecté. Le bouton « Oublier » sert à retirer celui devenu inutile.
            var slot = new CharacterSlot { Key = KeyFor(window) };
            slots.Add(slot);
            _entries.Add(new RosterEntry(slot) { Window = window });
        }
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

    private static string KeyFor(GameWindow window)
        => string.IsNullOrWhiteSpace(window.CharacterName) ? window.Title : window.CharacterName;

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
    /// Applique le motif configuré et renvoie le groupe « name ». Le titre brut sert de
    /// repli : un motif qui ne colle pas ne doit jamais faire disparaître un personnage
    /// de la liste, seulement le nommer moins joliment.
    /// </summary>
    public static string Parse(string title, string pattern)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
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
            // Motif invalide saisi dans les réglages : on retombe sur le titre brut.
        }
        catch (RegexMatchTimeoutException)
        {
        }

        return title.Trim();
    }
}
