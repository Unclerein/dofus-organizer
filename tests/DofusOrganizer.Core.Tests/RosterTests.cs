using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class RosterTests
{
    private static FakeWindowManager TeamOf(params string[] names)
    {
        var windows = new FakeWindowManager();
        for (int i = 0; i < names.Length; i++)
            windows.AddWindow(i + 1, names[i], new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        return windows;
    }

    [Fact]
    public void La_touche_suivant_parcourt_les_personnages_en_boucle()
    {
        var windows = TeamOf("Iop", "Eni", "Cra");
        var profile = new Profile();
        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);

        Assert.Equal("Eni", roster.Next(1)!.Slot.DisplayName);
        Assert.Equal("Cra", roster.Next(2)!.Slot.DisplayName);
        Assert.Equal("Iop", roster.Next(3)!.Slot.DisplayName);   // retour au début
        Assert.Equal("Cra", roster.Previous(1)!.Slot.DisplayName);
    }

    [Fact]
    public void Depuis_une_fenetre_hors_jeu_la_touche_suivant_repart_du_premier()
    {
        // Cas courant : on revient d'un navigateur, aucune fenêtre Dofus n'est le point de départ.
        var windows = TeamOf("Iop", "Eni");
        var profile = new Profile();
        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);

        Assert.Equal("Iop", roster.Next(9999)!.Slot.DisplayName);
    }

    [Fact]
    public void Les_personnages_decoches_sont_exclus_du_cycle()
    {
        var windows = TeamOf("Iop", "Eni", "Cra");
        var profile = new Profile();
        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        profile.Characters[1].Enabled = false;

        Assert.Equal("Cra", roster.Next(1)!.Slot.DisplayName);
    }

    [Fact]
    public void Un_client_ferme_garde_sa_place_et_son_raccourci()
    {
        // C'est ce qui permet de relancer un personnage sans devoir réassigner sa touche.
        var windows = TeamOf("Iop", "Eni", "Cra");
        var profile = new Profile();
        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        profile.Characters[1].Hotkey = new Hotkey(VirtualKeys.F2);

        windows.Windows.RemoveAt(1);
        roster.Sync(windows.Windows, profile.Characters);

        Assert.Equal(3, profile.Characters.Count);
        Assert.False(roster.Entries[1].IsPresent);
        Assert.Equal(new Hotkey(VirtualKeys.F2), profile.Characters[1].Hotkey);

        windows.AddWindow(2, "Eni", new ClientBounds(new ScreenPoint(0, 0), 800, 600));
        roster.Sync(windows.Windows, profile.Characters);

        Assert.True(roster.Entries[1].IsPresent);
        Assert.Equal(3, profile.Characters.Count);
    }

    [Fact]
    public void L_ordre_choisi_par_l_utilisateur_est_conserve()
    {
        var windows = TeamOf("Iop", "Eni", "Cra");
        var profile = new Profile();
        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);

        var eni = profile.Characters[1];
        roster.Move(eni, -1, profile.Characters);
        roster.Sync(windows.Windows, profile.Characters);

        Assert.Equal(["Eni", "Iop", "Cra"], roster.Entries.Select(e => e.Slot.DisplayName));
        Assert.Equal("Iop", roster.Next(2)!.Slot.DisplayName);
    }
}
