using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Le titre d'un client traverse trois états — « Dofus », puis « Dofus 3.6.10.11 - Release »,
/// puis « Nom - Classe - Version - Release ». Comme l'extraction retombait sur le titre brut,
/// chacun devenait un personnage à part entière : quatre clients en produisaient douze, dont
/// huit affichés « Client fermé » à tout jamais.
/// </summary>
public class CharacterNamingTests
{
    private const string Motif = AppSettings.DefaultTitlePattern;

    [Theory]
    [InlineData("Saignalisation - Pandawa - 3.6.10.11 - Release", "Saignalisation")]
    [InlineData("Saignatore - Roublard - 3.6.10.11 - Release", "Saignatore")]
    [InlineData("Saignatour - Enutrof - 3.6.10.11 - Release", "Saignatour")]
    [InlineData("Saignatorus - Zobal - 3.6.10.11 - Release", "Saignatorus")]
    public void Un_titre_de_personnage_connecte_donne_son_nom_seul(string titre, string attendu)
        => Assert.Equal(attendu, CharacterNameParser.Parse(titre, Motif));

    [Theory]
    [InlineData("Dofus")]
    [InlineData("Dofus 3.6.10.11 - Release")]
    public void Un_titre_d_avant_connexion_ne_nomme_personne(string titre)
        => Assert.Null(CharacterNameParser.Parse(titre, Motif));

    [Fact]
    public void Un_nom_avec_un_tiret_survit()
    {
        // Le tiret d'un nom n'est pas entouré d'espaces, contrairement aux séparateurs.
        Assert.Equal("Jean-Luc", CharacterNameParser.Parse("Jean-Luc - Iop - 3.6.10.11 - Release", Motif));
    }

    [Fact]
    public void Un_motif_vide_accepte_tout_titre_tel_quel()
    {
        // La porte de sortie pour une version dont le titre aurait une forme imprévue.
        Assert.Equal("Dofus 4.0 - Beta", CharacterNameParser.Parse("Dofus 4.0 - Beta", ""));
    }

    [Fact]
    public void Un_motif_invalide_n_invente_aucun_personnage()
    {
        // Ne rien reconnaître vaut mieux que reconnaître n'importe quoi : la barre d'état
        // signale les clients restés sans nom.
        Assert.Null(CharacterNameParser.Parse("Saignalisation - Pandawa - 3.6.10.11 - Release", "((("));
    }

    private static GameWindow Fenetre(nint handle, string titre) =>
        new(handle, 1000 + (int)handle, titre, "Dofus", "UnityWndClass")
        {
            CharacterName = CharacterNameParser.Parse(titre, Motif),
        };

    [Fact]
    public void Une_fenetre_qui_change_de_titre_ne_produit_qu_un_emplacement()
    {
        // Le test qui verrouille la panne : la même fenêtre, vue sous ses trois titres
        // successifs, ne doit laisser aucun emplacement orphelin derrière elle.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync([Fenetre(1, "Dofus")], slots);
        Assert.Empty(slots);
        Assert.Equal(1, roster.PendingWindows);

        roster.Sync([Fenetre(1, "Dofus 3.6.10.11 - Release")], slots);
        Assert.Empty(slots);
        Assert.Equal(1, roster.PendingWindows);

        roster.Sync([Fenetre(1, "Saignalisation - Pandawa - 3.6.10.11 - Release")], slots);
        Assert.Equal("Saignalisation", Assert.Single(slots).Key);
        Assert.Equal(0, roster.PendingWindows);
    }

    [Fact]
    public void Quatre_clients_donnent_quatre_lignes_et_non_douze()
    {
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();
        string[] noms = ["Saignalisation - Pandawa", "Saignatore - Roublard", "Saignatour - Enutrof", "Saignatorus - Zobal"];

        // Les trois états traversés par les quatre clients, dans l'ordre.
        roster.Sync([.. Enumerable.Range(1, 4).Select(i => Fenetre(i, "Dofus"))], slots);
        roster.Sync([.. Enumerable.Range(1, 4).Select(i => Fenetre(i, "Dofus 3.6.10.11 - Release"))], slots);
        roster.Sync([.. Enumerable.Range(1, 4).Select(i => Fenetre(i, $"{noms[i - 1]} - 3.6.10.11 - Release"))], slots);

        Assert.Equal(4, slots.Count);
        Assert.All(roster.Entries, e => Assert.True(e.IsPresent));
    }

    [Fact]
    public void Un_personnage_qui_se_connecte_retrouve_son_raccourci()
    {
        // Ce qui ne doit surtout pas casser : l'emplacement persisté est retrouvé par son nom,
        // avec son raccourci et sa position.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>
        {
            new() { Key = "Saignalisation", Hotkey = new Hotkey(VirtualKeys.F1) },
            new() { Key = "Saignatore", Hotkey = new Hotkey(VirtualKeys.F2) },
        };

        roster.Sync([Fenetre(1, "Dofus")], slots);
        Assert.Equal(2, slots.Count);                                    // rien de persisté pour un client anonyme
        Assert.All(roster.Entries.Where(e => !e.IsPending), e => Assert.False(e.IsPresent));

        roster.Sync([Fenetre(1, "Saignatore - Roublard - 3.6.10.11 - Release")], slots);

        Assert.Equal(2, slots.Count);
        var entry = Assert.Single(roster.Entries, e => e.IsPresent);
        Assert.Equal("Saignatore", entry.Slot.Key);
        Assert.Equal(new Hotkey(VirtualKeys.F2), entry.Slot.Hotkey);
        Assert.Equal(1, roster.Entries.ToList().IndexOf(entry));
        Assert.DoesNotContain(roster.Entries, e => e.IsPending);         // la ligne éphémère s'est effacée
    }

    [Fact]
    public void Un_client_a_l_ecran_de_selection_occupe_une_ligne_ephemere()
    {
        // On doit pouvoir basculer dessus pendant qu'on connecte les personnages, sans que
        // le titre de passage laisse quoi que ce soit dans le profil.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync([Fenetre(1, "Dofus 3.6.10.11 - Release")], slots);

        var entry = Assert.Single(roster.Entries);
        Assert.True(entry.IsPending);
        Assert.True(entry.IsPresent);
        Assert.True(entry.IsSelectable);
        Assert.Equal("Dofus 3.6.10.11 - Release", entry.Slot.Key);
        Assert.Empty(slots);
    }

    [Fact]
    public void Une_ligne_ephemere_ne_devient_jamais_un_doublon()
    {
        // Le test qui verrouille la demande : la même fenêtre traverse ses trois titres, et à
        // aucun moment la liste n'affiche deux lignes pour un seul client.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        foreach (var titre in new[] { "Dofus", "Dofus 3.6.10.11 - Release", "Dofus 3.6.10.11 - Release" })
        {
            roster.Sync([Fenetre(1, titre)], slots);
            Assert.Single(roster.Entries);
        }

        roster.Sync([Fenetre(1, "Saignalisation - Pandawa - 3.6.10.11 - Release")], slots);

        var entry = Assert.Single(roster.Entries);
        Assert.False(entry.IsPending);
        Assert.Equal("Saignalisation", entry.Slot.Key);
        Assert.Equal("Saignalisation", Assert.Single(slots).Key);
    }

    [Fact]
    public void Une_ligne_ephemere_garde_le_meme_objet_d_un_rafraichissement_a_l_autre()
    {
        // La détection tourne chaque seconde. Recréer l'objet à chaque passage ferait
        // perdre la sélection dans le tableau et rendrait la ligne inutilisable.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync([Fenetre(1, "Dofus")], slots);
        var premier = Assert.Single(roster.Entries);

        roster.Sync([Fenetre(1, "Dofus 3.6.10.11 - Release")], slots);
        var second = Assert.Single(roster.Entries);

        Assert.Same(premier, second);
        Assert.Equal("Dofus 3.6.10.11 - Release", second.Slot.Key);   // le titre, lui, suit
    }

    [Fact]
    public void Un_client_a_l_ecran_de_selection_reste_hors_des_boucles_de_macro()
    {
        // Lui rejouer une séquence de sorts n'aurait aucun sens : il n'y a pas de personnage.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync(
            [Fenetre(1, "Saignalisation - Pandawa - 3.6.10.11 - Release"), Fenetre(2, "Dofus 3.6.10.11 - Release")],
            slots);

        Assert.Equal(2, roster.Entries.Count);
        Assert.Equal("Saignalisation", Assert.Single(roster.ActiveEntries).Slot.Key);
    }

    [Fact]
    public void La_touche_suivant_bascule_sur_les_clients_a_l_ecran_de_selection()
    {
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync(
            [Fenetre(1, "Saignalisation - Pandawa - 3.6.10.11 - Release"), Fenetre(2, "Dofus 3.6.10.11 - Release")],
            slots);

        // Le raccourci global fait le tour de tous les clients…
        Assert.Equal(2, roster.Next(1, includePending: true)!.Window!.Handle);
        Assert.Equal(1, roster.Next(2, includePending: true)!.Window!.Handle);

        // …là où une étape de macro ne vise que les personnages, et reste donc sur place.
        Assert.Equal(1, roster.Next(1)!.Window!.Handle);
    }

    [Fact]
    public void L_ordre_des_lignes_ephemeres_ne_suit_pas_le_premier_plan()
    {
        // Windows énumère les fenêtres par ordre de premier plan : s'y fier ferait sauter le
        // cycle sous les doigts, puisque basculer sur une fenêtre la remonterait dans la liste.
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync([Fenetre(1, "Dofus"), Fenetre(2, "Dofus"), Fenetre(3, "Dofus")], slots);
        Assert.Equal([1, 2, 3], roster.Entries.Select(e => e.Window!.Handle));

        roster.Sync([Fenetre(3, "Dofus"), Fenetre(1, "Dofus"), Fenetre(2, "Dofus")], slots);
        Assert.Equal([1, 2, 3], roster.Entries.Select(e => e.Window!.Handle));
    }

    [Fact]
    public void L_oubli_groupe_ignore_les_lignes_ephemeres()
    {
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot> { new() { Key = "Parti" } };

        roster.Sync([Fenetre(1, "Dofus 3.6.10.11 - Release")], slots);
        Assert.Equal(2, roster.Entries.Count);

        Assert.Equal(1, roster.ForgetAbsent(slots));

        Assert.Empty(slots);
        Assert.True(Assert.Single(roster.Entries).IsPending);
    }

    [Fact]
    public void L_oubli_groupe_ne_retire_que_les_absents()
    {
        var roster = new CharacterRoster();
        var slots = new List<CharacterSlot>();

        roster.Sync([Fenetre(1, "Saignalisation - Pandawa - 3.6.10.11 - Release")], slots);

        // Des entrées héritées de l'ancienne détection, qui ne correspondront plus jamais.
        slots.Add(new CharacterSlot { Key = "Dofus" });
        slots.Add(new CharacterSlot { Key = "Dofus 3.6.10.11 - Release" });
        roster.Sync([Fenetre(1, "Saignalisation - Pandawa - 3.6.10.11 - Release")], slots);
        Assert.Equal(3, slots.Count);

        Assert.Equal(2, roster.ForgetAbsent(slots));

        Assert.Equal("Saignalisation", Assert.Single(slots).Key);
        Assert.True(Assert.Single(roster.Entries).IsPresent);
    }
}
