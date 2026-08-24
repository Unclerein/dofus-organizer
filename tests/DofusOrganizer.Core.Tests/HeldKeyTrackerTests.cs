using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Une seule touche mourait, définitivement, pendant que toutes les autres continuaient de
/// marcher : son relâchement avait été manqué, elle restait donc considérée comme tenue, et
/// chaque appui suivant était pris pour une répétition automatique. Seul un redémarrage la
/// ramenait.
///
/// Le relâchement se perdait quand le rappel du hook n'était pas servi à temps — le fil
/// d'interface étant bloqué par l'activation d'un client lent à passer au premier plan, ce qui
/// arrive typiquement au sortir d'un combat.
/// </summary>
public class HeldKeyTrackerTests
{
    private const int Suivant = VirtualKeys.F2;
    private const int Autre = VirtualKeys.F3;

    /// <summary>Clavier simulé : on y enfonce et relâche des touches comme le ferait une main.</summary>
    private sealed class Keyboard
    {
        private readonly HashSet<int> _down = [];

        public void Press(int key) => _down.Add(key);
        public void Release(int key) => _down.Remove(key);
        public bool IsDown(int key) => _down.Contains(key);
    }

    [Fact]
    public void Un_relachement_manque_ne_condamne_pas_la_touche()
    {
        // Le test qui verrouille la panne.
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        Assert.True(tracker.BeginPress(Suivant));

        // La main relâche, mais le rappel du hook n'a jamais été servi : EndPress n'est pas
        // appelé. C'est exactement ce qui se produisait pendant qu'Activate dormait sur le fil
        // d'interface.
        keyboard.Release(Suivant);

        // Le balayage régulier constate que le clavier ne la tient plus.
        Assert.Equal(1, tracker.DropKeysNoLongerHeld(keyboard.IsDown));

        // Avant le correctif, cet appui — et tous les suivants — étaient pris pour une
        // répétition automatique et jamais distribués.
        keyboard.Press(Suivant);
        Assert.True(tracker.BeginPress(Suivant));
    }

    [Fact]
    public void Le_balayage_epargne_une_touche_reellement_tenue()
    {
        // Le balayage ne doit pas désarmer le filtrage de la répétition : tant que la touche est
        // enfoncée, elle reste tenue.
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        Assert.True(tracker.BeginPress(Suivant));

        Assert.Equal(0, tracker.DropKeysNoLongerHeld(keyboard.IsDown));
        Assert.False(tracker.BeginPress(Suivant));
    }

    [Fact]
    public void Le_balayage_ne_fait_rien_quand_rien_n_est_tenu()
    {
        var tracker = new HeldKeyTracker();
        Assert.Equal(0, tracker.DropKeysNoLongerHeld(_ => throw new InvalidOperationException(
            "L'état du clavier ne devrait pas être interrogé quand aucune touche n'est tenue.")));
    }

    [Fact]
    public void La_repetition_automatique_reste_filtree()
    {
        // La raison d'être de tout ce suivi : touche tenue, le clavier répète, et une macro
        // partirait en boucle si chaque répétition était distribuée.
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);

        Assert.True(tracker.BeginPress(Suivant));
        Assert.False(tracker.BeginPress(Suivant));
        Assert.False(tracker.BeginPress(Suivant));
    }

    [Fact]
    public void Un_appui_suivi_de_son_relachement_laisse_l_ensemble_vide()
    {
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        Assert.True(tracker.BeginPress(Suivant));

        keyboard.Release(Suivant);
        tracker.EndPress(Suivant);

        Assert.Equal(0, tracker.HeldCount);

        keyboard.Press(Suivant);
        Assert.True(tracker.BeginPress(Suivant));
    }

    [Fact]
    public void Une_entree_perimee_ne_bloque_pas_une_autre_touche()
    {
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        tracker.BeginPress(Suivant);
        keyboard.Release(Suivant);

        // L'autre touche n'a jamais été tenue : elle doit partir, périmée ou non à côté.
        keyboard.Press(Autre);
        Assert.True(tracker.BeginPress(Autre));

        // Et le balayage ne retire que la périmée.
        Assert.Equal(1, tracker.DropKeysNoLongerHeld(keyboard.IsDown));
        Assert.Equal(1, tracker.HeldCount);
    }

    [Fact]
    public void Le_relachement_d_une_touche_absorbee_est_absorbe()
    {
        // Sans cela le jeu recevrait un relâchement sans son appui, et pourrait considérer la
        // touche comme coincée.
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        tracker.BeginPress(Suivant);
        Assert.True(tracker.MarkSwallowed(Suivant, swallowed: true));
        Assert.True(tracker.IsSwallowed(Suivant));

        Assert.True(tracker.EndPress(Suivant));
        Assert.False(tracker.IsSwallowed(Suivant));
    }

    [Fact]
    public void Une_purge_n_absorbe_pas_le_relachement_de_l_appui_qui_suit()
    {
        // Le piège des deux ensembles : purger les touches tenues sans purger les absorbées
        // laisserait la touche marquée comme absorbée. L'appui suivant, lui, a été laissé passer
        // au jeu — absorber son relâchement lui ferait croire à une touche coincée.
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        tracker.BeginPress(Suivant);
        tracker.MarkSwallowed(Suivant, swallowed: true);

        // Relâchement manqué, puis nouvel appui que rien n'absorbe cette fois.
        keyboard.Release(Suivant);
        tracker.DropKeysNoLongerHeld(keyboard.IsDown);
        keyboard.Press(Suivant);
        Assert.True(tracker.BeginPress(Suivant));
        tracker.MarkSwallowed(Suivant, swallowed: false);

        Assert.False(tracker.IsSwallowed(Suivant));
        Assert.False(tracker.EndPress(Suivant));
    }

    [Fact]
    public void Les_boutons_de_souris_sont_traduits_vers_les_codes_du_systeme()
    {
        // Sans traduction, l'état physique porterait sur un code inexistant, toujours rapporté
        // relâché : la purge retirerait le bouton à chaque appui et la répétition ne serait plus
        // filtrée pour lui.
        Assert.Equal(0x04, PhysicalKeyCodes.ForSystem(VirtualKeys.MouseMiddle));
        Assert.Equal(0x05, PhysicalKeyCodes.ForSystem(VirtualKeys.MouseButton4));
        Assert.Equal(0x06, PhysicalKeyCodes.ForSystem(VirtualKeys.MouseButton5));

        // Une touche ordinaire n'est pas touchée.
        Assert.Equal(VirtualKeys.F2, PhysicalKeyCodes.ForSystem(VirtualKeys.F2));
    }

    [Fact]
    public void Clear_oublie_tout()
    {
        var tracker = new HeldKeyTracker();
        var keyboard = new Keyboard();

        keyboard.Press(Suivant);
        tracker.BeginPress(Suivant);
        tracker.MarkSwallowed(Suivant, swallowed: true);

        tracker.Clear();

        Assert.Equal(0, tracker.HeldCount);
        Assert.False(tracker.IsSwallowed(Suivant));
    }
}
