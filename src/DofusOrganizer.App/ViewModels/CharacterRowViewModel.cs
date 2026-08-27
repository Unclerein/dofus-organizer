using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;

namespace DofusOrganizer.App.ViewModels;

/// <summary>Une ligne de l'onglet Personnages : l'emplacement persisté et l'état de sa fenêtre.</summary>
public sealed class CharacterRowViewModel(RosterEntry entry, int position) : ObservableObject
{
    public CharacterSlot Slot { get; } = entry.Slot;

    public int Position { get; } = position;

    public string PositionLabel => Position.ToString();

    /// <summary>
    /// Vrai pour un client qui ne nomme encore aucun personnage : il tient une ligne le
    /// temps de sa connexion, sur laquelle on peut basculer, mais rien ne s'y attache —
    /// elle s'effacera d'elle-même quand le personnage entrera en jeu.
    /// </summary>
    public bool IsPending => entry.IsPending;

    /// <summary>Faux tant que la ligne ne désigne pas un personnage : il n'y a rien à quoi lier une touche.</summary>
    public bool CanBind => !entry.IsPending;

    public string DisplayName
    {
        get => Slot.DisplayName;
        set
        {
            if (IsPending) return;   // renommer une ligne qui va disparaître n'aurait aucun effet
            Slot.DisplayName = value;
            Raise();
        }
    }

    public bool Enabled
    {
        get => Slot.Enabled;
        set { Slot.Enabled = value; Raise(); }
    }

    public string HotkeyLabel => IsPending ? "—" : Slot.Hotkey?.ToString() ?? "(aucun)";

    /// <summary>Titre brut de la fenêtre, affiché pour aider à régler le motif d'extraction des noms.</summary>
    public string WindowTitle => entry.Window?.Title ?? "—";

    /// <summary>
    /// Classe Win32 de la fenêtre, montrée en infobulle sur son titre.
    ///
    /// C'est le seul moyen de savoir quoi mettre dans « Classe de fenêtre » des Réglages, et
    /// de vérifier si une ligne indésirable est bien une fenêtre du jeu ou une autre fenêtre
    /// du même processus — un lanceur, une mise à jour — qu'un filtre écarterait.
    /// </summary>
    public string WindowClass => entry.Window?.ClassName ?? "—";

    public bool IsPresent => entry.IsPresent;

    public string StatusLabel => entry switch
    {
        { IsPending: true } => "Écran de connexion",
        { IsPresent: true } => "Connecté",
        _ => "Client fermé",
    };

    public void RefreshHotkey() => Raise(nameof(HotkeyLabel));
}
