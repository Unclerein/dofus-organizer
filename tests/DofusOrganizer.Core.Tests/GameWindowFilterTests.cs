using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class GameWindowFilterTests
{
    private static readonly SelfIdentity Organizer = new(ProcessId: 4242, ProcessName: "DofusOrganizer");
    private static readonly AppSettings Settings = new();

    [Fact]
    public void Un_client_dofus_est_retenu()
        => Assert.True(GameWindowFilter.IsGameProcess("Dofus", 1000, Organizer, Settings));

    [Fact]
    public void L_organizer_ne_se_detecte_pas_lui_meme()
    {
        // « DofusOrganizer » contient « Dofus » : sans exclusion explicite, l'organizer
        // entrait dans sa propre liste de personnages, et ses fenêtres devenaient des
        // cibles valides pour l'enregistreur de macros.
        Assert.False(GameWindowFilter.IsGameProcess("DofusOrganizer", Organizer.ProcessId, Organizer, Settings));
    }

    [Fact]
    public void L_exclusion_par_identifiant_survit_a_un_renommage_de_l_executable()
    {
        var renamed = new SelfIdentity(ProcessId: 4242, ProcessName: "MonOrganizerPerso");
        Assert.False(GameWindowFilter.IsGameProcess("MonOrganizerPerso", 4242, renamed, Settings));
    }

    [Fact]
    public void Une_seconde_instance_de_l_organizer_est_ecartee_par_son_nom()
        => Assert.False(GameWindowFilter.IsGameProcess("DofusOrganizer", 9999, Organizer, Settings));

    [Fact]
    public void La_detection_reste_permissive_sur_le_nom_de_l_executable()
    {
        // Le nom exact varie selon les versions du jeu : rater le client serait pire
        // que d'en détecter un de trop, que l'utilisateur peut retirer de la liste.
        Assert.True(GameWindowFilter.IsGameProcess("Dofus-3", 1000, Organizer, Settings));
        Assert.True(GameWindowFilter.IsGameProcess("dofus", 1000, Organizer, Settings));
    }

    [Fact]
    public void Un_processus_etranger_est_ignore()
    {
        Assert.False(GameWindowFilter.IsGameProcess("chrome", 1000, Organizer, Settings));
        Assert.False(GameWindowFilter.IsGameProcess("", 1000, Organizer, Settings));
    }

    [Fact]
    public void Le_filtre_de_classe_vide_laisse_tout_passer()
    {
        Assert.True(GameWindowFilter.MatchesWindowClass("UnityWndClass", new AppSettings()));
        Assert.True(GameWindowFilter.MatchesWindowClass("", new AppSettings()));
    }

    [Fact]
    public void Le_filtre_de_classe_renseigne_restreint_la_detection()
    {
        var settings = new AppSettings { WindowClassFilter = "UnityWndClass" };

        Assert.True(GameWindowFilter.MatchesWindowClass("UnityWndClass", settings));
        Assert.False(GameWindowFilter.MatchesWindowClass("Chrome_WidgetWin_1", settings));
    }
}
