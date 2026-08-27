using System.Text.RegularExpressions;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Organizer;

/// <summary>Un emplacement de la liste, associé à la fenêtre qui l'occupe actuellement (s'il y en a une).</summary>
public sealed class RosterEntry(CharacterSlot slot, bool pending = false)
{
    public CharacterSlot Slot { get; } = slot;
    public GameWindow? Window { get; internal set; }
    public bool IsPresent => Window is not null;

    /// <summary>
    /// Vrai pour un client détecté qui ne nomme encore aucun personnage — en cours de
    /// chargement, ou resté à l'écran de sélection.
    ///
    /// Il occupe une ligne le temps de sa connexion, pour qu'on puisse basculer dessus,
    /// mais rien de lui n'est conservé : ni raccourci, ni place dans l'ordre. Son titre
    /// change deux fois avant de nommer un personnage, et persister quoi que ce soit sous
    /// un titre de passage est précisément ce qui fabriquait des doublons.
    /// </summary>
    public bool IsPending { get; } = pending;

    /// <summary>
    /// Présent, coché, et nommé : éligible aux boucles de macro.
    ///
    /// Un client resté à l'écran de sélection en est exclu — lui rejouer une séquence de
    /// sorts n'aurait aucun sens.
    /// </summary>
    public bool IsActive => IsPresent && !IsPending && Slot.Enabled;

    /// <summary>
    /// Présent et coché : éligible à la touche « personnage suivant ».
    ///
    /// Plus large que <see cref="IsActive"/> à dessein — basculer d'un client à l'autre
    /// pendant qu'on les connecte est justement le moment où l'on en a le plus besoin.
    /// </summary>
    public bool IsSelectable => IsPresent && Slot.Enabled;
}

/// <summary>
/// La liste ordonnée de personnages : elle réconcilie à chaque rafraîchissement les
/// fenêtres réellement ouvertes avec les emplacements persistés, et décide qui vient
/// après qui pour la touche « suivant ».
/// </summary>
public sealed class CharacterRoster
{
    /// <summary>Emplacements persistés, dans l'ordre choisi par l'utilisateur.</summary>
    private readonly List<RosterEntry> _slots = [];

    /// <summary>Clients encore anonymes, en fin de liste, reconstruits à chaque rafraîchissement.</summary>
    private readonly List<RosterEntry> _pending = [];

    private RosterEntry[] _entries = [];

    public IReadOnlyList<RosterEntry> Entries => _entries;

    /// <summary>Emplacements présents, cochés et nommés, dans l'ordre de la liste.</summary>
    public IReadOnlyList<RosterEntry> ActiveEntries => _slots.Where(e => e.IsActive).ToList();

    /// <summary>
    /// Nombre de clients détectés qui ne nomment encore aucun personnage — en cours de
    /// chargement ou restés à l'écran de sélection.
    ///
    /// Ils figurent dans la liste, mais ce compte reste utile à la barre d'état : voir
    /// « 4 clients à l'écran de sélection » et aucun personnage nommé pointe le motif
    /// d'extraction du titre, seul suspect quand la version installée n'a pas la forme
    /// attendue.
    /// </summary>
    public int PendingWindows => _pending.Count;

    /// <summary>
    /// Aligne la liste sur les fenêtres ouvertes : les emplacements connus retrouvent
    /// leur fenêtre, les personnages jamais vus sont ajoutés à la fin, et ceux dont le
    /// client est fermé restent en place (grisés) pour ne pas perdre leur raccourci.
    ///
    /// Une fenêtre qui ne nomme encore aucun personnage occupe une ligne éphémère en fin de
    /// liste : on peut basculer dessus, mais rien d'elle n'est écrit dans le profil. Le titre
    /// d'un client change deux fois avant de nommer un personnage — « Dofus », puis
    /// « Dofus 3.6.10.11 - Release » — et persister un emplacement sous un titre de passage
    /// est exactement ce qui laissait un orphelin derrière chaque changement. La ligne
    /// éphémère, elle, s'efface d'elle-même : à la connexion, la fenêtre se nomme et
    /// retrouve l'emplacement persisté à ce nom, avec son raccourci et sa position.
    /// </summary>
    public void Sync(IReadOnlyList<GameWindow> windows, List<CharacterSlot> slots)
    {
        // Une ligne éphémère se retrouve par sa fenêtre et non par un nom — elle n'en a pas.
        // C'est aussi ce qui lui garde le même objet d'un rafraîchissement à l'autre, donc sa
        // sélection dans l'interface, alors qu'elle est reconstruite chaque seconde.
        var byHandle = _pending.Where(e => e.Window is not null).ToDictionary(e => e.Window!.Handle);

        foreach (var entry in _slots) entry.Window = null;
        foreach (var entry in _pending) entry.Window = null;

        // On repart des emplacements persistés pour que l'ordre choisi par l'utilisateur fasse foi.
        SyncSlots(slots);

        var anonymous = new List<GameWindow>();
        var unmatched = new List<(GameWindow Window, string Name)>();

        foreach (var window in windows)
        {
            if (window.CharacterName is not { Length: > 0 } name || string.IsNullOrWhiteSpace(name))
            {
                anonymous.Add(window);
                continue;
            }

            var entry = _slots.FirstOrDefault(e => e.Window is null && KeyMatches(e.Slot.Key, name));
            if (entry is null) unmatched.Add((window, name));
            else entry.Window = window;
        }

        foreach (var (window, name) in unmatched)
        {
            var slot = new CharacterSlot { Key = name };
            slots.Add(slot);
            _slots.Add(new RosterEntry(slot) { Window = window });
        }

        // Les clients encore anonymes ferment la liste, ordonnés par identifiant de fenêtre.
        // L'ordre d'énumération de Windows suit le premier plan : s'y fier ferait sauter le
        // cycle sous les doigts, puisque basculer sur une fenêtre la remonterait dans la liste.
        _pending.Clear();
        foreach (var window in anonymous.OrderBy(w => w.Handle))
        {
            if (byHandle.TryGetValue(window.Handle, out var existing))
            {
                // Même fenêtre, titre peut-être différent : « Dofus » devenu « Dofus … - Release ».
                existing.Slot.Key = window.Title;
                existing.Window = window;
                _pending.Add(existing);
            }
            else
            {
                _pending.Add(new RosterEntry(new CharacterSlot { Key = window.Title }, pending: true) { Window = window });
            }
        }

        _entries = [.. _slots, .. _pending];
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
        // Les lignes éphémères ne sont pas concernées : elles ont toujours une fenêtre, et
        // rien à oublier puisque rien n'est persisté.
        var absent = _slots.Where(e => !e.IsPresent).Select(e => e.Slot).ToList();

        foreach (var slot in absent) slots.Remove(slot);
        _slots.RemoveAll(e => absent.Contains(e.Slot));
        _entries = [.. _slots, .. _pending];

        return absent.Count;
    }

    private void SyncSlots(List<CharacterSlot> slots)
    {
        _slots.RemoveAll(e => !slots.Contains(e.Slot));
        for (int i = 0; i < slots.Count; i++)
        {
            var existing = _slots.FirstOrDefault(e => ReferenceEquals(e.Slot, slots[i]));
            if (existing is null)
            {
                _slots.Insert(Math.Min(i, _slots.Count), new RosterEntry(slots[i]));
            }
            else
            {
                int current = _slots.IndexOf(existing);
                if (current != i && i < _slots.Count)
                {
                    _slots.RemoveAt(current);
                    _slots.Insert(i, existing);
                }
            }
        }
    }

    private static bool KeyMatches(string slotKey, string windowKey)
        => string.Equals(slotKey, windowKey, StringComparison.OrdinalIgnoreCase);

    public RosterEntry? BySlotIndex(int index)
        => index >= 0 && index < _entries.Length ? _entries[index] : null;

    public RosterEntry? ByHandle(nint handle)
        => handle == 0 ? null : _entries.FirstOrDefault(e => e.Window?.Handle == handle);

    public RosterEntry? First() => ActiveEntries.FirstOrDefault();

    /// <param name="includePending">
    /// Inclure les clients qui ne nomment encore aucun personnage. Vrai pour la touche
    /// « personnage suivant », qui sert justement à faire le tour des clients pendant qu'on
    /// les connecte ; faux pour une étape de macro, qui vise un personnage.
    /// </param>
    public RosterEntry? Next(nint currentHandle, bool includePending = false)
        => Step(currentHandle, +1, includePending);

    /// <inheritdoc cref="Next"/>
    public RosterEntry? Previous(nint currentHandle, bool includePending = false)
        => Step(currentHandle, -1, includePending);

    /// <summary>
    /// Avance dans la liste en repartant de la fenêtre au premier plan.
    /// Si le focus n'est sur aucune fenêtre connue, on repart de la première — c'est le
    /// comportement attendu quand on revient d'un navigateur ou de Discord.
    /// </summary>
    private RosterEntry? Step(nint currentHandle, int direction, bool includePending)
    {
        var cycle = includePending
            ? _entries.Where(e => e.IsSelectable).ToList()
            : ActiveEntries;
        if (cycle.Count == 0) return null;

        int index = -1;
        for (int i = 0; i < cycle.Count; i++)
        {
            if (cycle[i].Window?.Handle == currentHandle) { index = i; break; }
        }
        if (index < 0) return cycle[0];

        int next = ((index + direction) % cycle.Count + cycle.Count) % cycle.Count;
        return cycle[next];
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
