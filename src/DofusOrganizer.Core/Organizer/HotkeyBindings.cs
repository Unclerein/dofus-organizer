using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Organizer;

public enum HotkeyActionKind { FocusSlot, FocusNext, FocusPrevious, RunMacro, Panic }

public sealed record HotkeyAction(HotkeyActionKind Kind, CharacterSlot? Slot = null, Macro? Macro = null);

/// <summary>
/// Table de correspondance touche -> action, reconstruite à chaque modification du profil.
/// Le hook clavier la consulte à chaque frappe : la recherche doit rester immédiate,
/// sans quoi Windows finit par désactiver le hook.
/// </summary>
public sealed class HotkeyBindings
{
    private readonly List<(Hotkey Key, HotkeyAction Action)> _bindings = [];

    public static HotkeyBindings Build(Profile profile)
    {
        var table = new HotkeyBindings();

        // La touche d'arrêt d'urgence passe en premier : elle doit rester joignable
        // même si l'utilisateur l'a assignée par ailleurs à autre chose.
        Add(table, profile.Settings.PanicHotkey, new HotkeyAction(HotkeyActionKind.Panic));
        Add(table, profile.Settings.NextCharacterHotkey, new HotkeyAction(HotkeyActionKind.FocusNext));
        Add(table, profile.Settings.PreviousCharacterHotkey, new HotkeyAction(HotkeyActionKind.FocusPrevious));

        foreach (var slot in profile.Characters)
            Add(table, slot.Hotkey, new HotkeyAction(HotkeyActionKind.FocusSlot, Slot: slot));

        foreach (var macro in profile.Macros)
            Add(table, macro.Hotkey, new HotkeyAction(HotkeyActionKind.RunMacro, Macro: macro));

        return table;
    }

    private static void Add(HotkeyBindings table, Hotkey? hotkey, HotkeyAction action)
    {
        if (hotkey is null || hotkey.IsEmpty) return;
        // Premier assigné, premier servi : un doublon est signalé dans l'interface
        // plutôt que d'écraser silencieusement le raccourci existant.
        if (table._bindings.Any(b => b.Key.VirtualKey == hotkey.VirtualKey && b.Key.Modifiers == hotkey.Modifiers)) return;
        table._bindings.Add((hotkey, action));
    }

    public HotkeyAction? Resolve(int virtualKey, KeyModifiers modifiers)
    {
        foreach (var (key, action) in _bindings)
        {
            if (key.Matches(virtualKey, modifiers)) return action;
        }
        return null;
    }

    /// <summary>Raccourcis assignés plusieurs fois, à signaler à l'utilisateur.</summary>
    public static IReadOnlyList<Hotkey> FindConflicts(Profile profile)
    {
        var seen = new Dictionary<(int, KeyModifiers), int>();
        foreach (var hotkey in AllHotkeys(profile))
        {
            var id = (hotkey.VirtualKey, hotkey.Modifiers);
            seen[id] = seen.GetValueOrDefault(id) + 1;
        }
        return seen.Where(kv => kv.Value > 1).Select(kv => new Hotkey(kv.Key.Item1, kv.Key.Item2)).ToList();
    }

    private static IEnumerable<Hotkey> AllHotkeys(Profile profile)
    {
        foreach (var hotkey in new[] { profile.Settings.PanicHotkey, profile.Settings.NextCharacterHotkey, profile.Settings.PreviousCharacterHotkey })
            if (hotkey is { IsEmpty: false }) yield return hotkey;
        foreach (var slot in profile.Characters)
            if (slot.Hotkey is { IsEmpty: false }) yield return slot.Hotkey;
        foreach (var macro in profile.Macros)
            if (macro.Hotkey is { IsEmpty: false }) yield return macro.Hotkey;
    }
}
