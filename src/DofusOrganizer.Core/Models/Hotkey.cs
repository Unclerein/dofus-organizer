using System.Text;
using System.Text.Json.Serialization;

namespace DofusOrganizer.Core.Models;

[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>
/// Une combinaison de touches, stockée par code virtuel Windows pour rester
/// indépendante de la disposition clavier (le A d'un AZERTY et d'un QWERTY
/// n'ont pas le même code virtuel, mais chacun garde le sien entre deux sessions).
/// </summary>
public sealed record Hotkey(int VirtualKey, KeyModifiers Modifiers = KeyModifiers.None)
{
    [JsonIgnore]
    public bool IsEmpty => VirtualKey == 0;

    public bool Matches(int virtualKey, KeyModifiers modifiers)
        => !IsEmpty && VirtualKey == virtualKey && Modifiers == modifiers;

    public override string ToString()
    {
        if (IsEmpty) return "(aucun)";
        var sb = new StringBuilder();
        if (Modifiers.HasFlag(KeyModifiers.Control)) sb.Append("Ctrl + ");
        if (Modifiers.HasFlag(KeyModifiers.Alt)) sb.Append("Alt + ");
        if (Modifiers.HasFlag(KeyModifiers.Shift)) sb.Append("Maj + ");
        if (Modifiers.HasFlag(KeyModifiers.Windows)) sb.Append("Win + ");
        sb.Append(VirtualKeys.GetName(VirtualKey));
        return sb.ToString();
    }
}
