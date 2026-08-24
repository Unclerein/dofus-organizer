using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// La diffusion se réduit à une macro construite au vol, mais c'est justement ce qui peut se
/// tromper en silence : une boucle mal cadrée saute le meneur ou l'oublie, une touche mal
/// recopiée part sans ses modificateurs, un retour de focus manquant laisse l'utilisateur sur
/// le dernier personnage visité. Ces tests exécutent la macro obtenue plutôt que d'en
/// inspecter la forme, pour constater la séquence réellement produite.
/// </summary>
public class KeyBroadcastTests
{
    private static (FakeWindowManager Windows, CharacterRoster Roster, Profile Profile) BuildTeam(int count)
    {
        var windows = new FakeWindowManager();
        for (int i = 0; i < count; i++)
        {
            windows.AddWindow(i + 1, $"Perso{i + 1}", new ClientBounds(new ScreenPoint(i * 900, 0), 800, 600));
        }

        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = 0;

        var roster = new CharacterRoster();
        roster.Sync(windows.Windows, profile.Characters);
        return (windows, roster, profile);
    }

    private static async Task<List<RecordedAction>> RunAsync(
        BroadcastKey broadcast, FakeWindowManager windows, CharacterRoster roster, Profile profile)
    {
        var macro = KeyBroadcast.BuildMacro(broadcast);
        Assert.NotNull(macro);

        var actions = windows.Actions;
        var runner = new MacroRunner(windows, new FakeInputSender(actions), new FakeClock(actions));

        var result = await runner.RunAsync(macro!, roster, profile.Settings, CancellationToken.None);

        Assert.Equal(MacroOutcome.Completed, result.Outcome);
        return actions;
    }

    [Fact]
    public void Sans_touche_a_envoyer_aucune_macro_n_est_construite()
    {
        // Un raccourci peut avoir été assigné sans que la touche diffusée l'ait été.
        // Parcourir l'équipe pour n'y rien faire serait pire que de renoncer.
        Assert.Null(KeyBroadcast.BuildMacro(new BroadcastKey { Trigger = new Hotkey(VirtualKeys.F9) }));
        Assert.False(new BroadcastKey().IsUsable);
    }

    [Fact]
    public async Task Chaque_personnage_recoit_la_touche_dans_l_ordre_de_la_liste()
    {
        var (windows, roster, profile) = BuildTeam(3);
        windows.Foreground = 1;

        var broadcast = new BroadcastKey
        {
            Name = "S'asseoir",
            Trigger = new Hotkey(VirtualKeys.F9, KeyModifiers.Control),
            Sent = new Hotkey(VirtualKeys.F1),
        };

        var actions = await RunAsync(broadcast, windows, roster, profile);

        // Un focus puis une frappe, personnage par personnage, sans rien d'autre entre les deux.
        var relevant = actions.Where(a => a is RecordedAction.Focus or RecordedAction.Key).ToList();
        Assert.Collection(relevant,
            a => Assert.Equal(new RecordedAction.Focus(1, true), a),
            a => Assert.Equal(new RecordedAction.Key(VirtualKeys.F1, KeyModifiers.None, KeyAction.Press), a),
            a => Assert.Equal(new RecordedAction.Focus(2, true), a),
            a => Assert.Equal(new RecordedAction.Key(VirtualKeys.F1, KeyModifiers.None, KeyAction.Press), a),
            a => Assert.Equal(new RecordedAction.Focus(3, true), a),
            a => Assert.Equal(new RecordedAction.Key(VirtualKeys.F1, KeyModifiers.None, KeyAction.Press), a),
            // Retour sur le personnage d'où la diffusion est partie.
            a => Assert.Equal(new RecordedAction.Focus(1, true), a));
    }

    [Fact]
    public async Task Les_modificateurs_de_la_touche_diffusee_sont_conserves()
    {
        var (windows, roster, profile) = BuildTeam(2);
        windows.Foreground = 1;

        // Le déclencheur et la touche envoyée sont distincts, et c'est bien la seconde
        // qui doit partir dans le jeu — avec ses modificateurs, pas ceux du déclencheur.
        var broadcast = new BroadcastKey
        {
            Trigger = new Hotkey(VirtualKeys.F9, KeyModifiers.Alt),
            Sent = new Hotkey(VirtualKeys.Space, KeyModifiers.Shift),
        };

        var actions = await RunAsync(broadcast, windows, roster, profile);

        var keys = actions.OfType<RecordedAction.Key>().ToList();
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key =>
        {
            Assert.Equal(VirtualKeys.Space, key.VirtualKey);
            Assert.Equal(KeyModifiers.Shift, key.Modifiers);
        });
    }

    [Fact]
    public async Task Le_personnage_actif_peut_etre_ecarte()
    {
        var (windows, roster, profile) = BuildTeam(3);
        windows.Foreground = 2;

        var broadcast = new BroadcastKey
        {
            Sent = new Hotkey(VirtualKeys.F1),
            IncludeCurrent = false,
        };

        var actions = await RunAsync(broadcast, windows, roster, profile);

        // Le meneur ne reçoit rien : deux frappes seulement, et aucune sur sa fenêtre.
        Assert.Equal(2, actions.OfType<RecordedAction.Key>().Count());

        var focused = actions.OfType<RecordedAction.Focus>().Select(f => f.Handle).ToList();
        Assert.Equal([(nint)1, (nint)3, (nint)2], focused);
    }

    [Fact]
    public async Task Un_personnage_decoche_ou_ferme_est_ignore()
    {
        var (windows, roster, profile) = BuildTeam(3);
        windows.Foreground = 1;

        // Décocher un personnage doit l'exclure de la diffusion comme de toute autre boucle :
        // c'est la promesse de la case « Actif » de la liste.
        roster.Entries[1].Slot.Enabled = false;

        var actions = await RunAsync(new BroadcastKey { Sent = new Hotkey(VirtualKeys.F1) }, windows, roster, profile);

        Assert.Equal(2, actions.OfType<RecordedAction.Key>().Count());
        Assert.DoesNotContain(actions.OfType<RecordedAction.Focus>(), f => f.Handle == 2);
    }

    [Fact]
    public async Task Le_curseur_n_est_pas_deplace()
    {
        var (windows, roster, profile) = BuildTeam(2);
        windows.Foreground = 1;

        var actions = await RunAsync(new BroadcastKey { Sent = new Hotkey(VirtualKeys.F1) }, windows, roster, profile);

        // Une diffusion n'utilise pas la souris : la reposer serait un mouvement gratuit,
        // et déplacerait le curseur de quelqu'un qui ne l'a pas demandé.
        Assert.DoesNotContain(actions, a => a is RecordedAction.Cursor or RecordedAction.Move);
    }

    [Fact]
    public void Le_raccourci_declencheur_est_resolu_et_compte_dans_les_conflits()
    {
        var profile = new Profile();
        var broadcast = new BroadcastKey { Trigger = new Hotkey(VirtualKeys.F9), Sent = new Hotkey(VirtualKeys.F1) };
        profile.Broadcasts.Add(broadcast);

        var action = HotkeyBindings.Build(profile).Resolve(VirtualKeys.F9, KeyModifiers.None);
        Assert.NotNull(action);
        Assert.Equal(HotkeyActionKind.Broadcast, action!.Kind);
        Assert.Same(broadcast, action.Broadcast);

        // Un doublon avec un raccourci de personnage doit être signalé comme les autres,
        // sinon la diffusion volerait silencieusement une touche déjà assignée.
        profile.Characters.Add(new CharacterSlot { Key = "Perso1", Hotkey = new Hotkey(VirtualKeys.F9) });
        Assert.Contains(HotkeyBindings.FindConflicts(profile), key => key.VirtualKey == VirtualKeys.F9);
    }
}
