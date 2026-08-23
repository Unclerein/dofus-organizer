using DofusOrganizer.Core.Config;
using DofusOrganizer.Core.Models;
using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class ProfileStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dofus-organizer-tests").FullName;

    private string PathFor(string name) => Path.Combine(_directory, name);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Un_profil_complet_survit_a_un_aller_retour_sur_disque()
    {
        var store = new ProfileStore(PathFor("profile.json"));
        var profile = new Profile
        {
            Characters =
            {
                new CharacterSlot { Key = "Iop", DisplayName = "Mon Iop", Hotkey = new Hotkey(VirtualKeys.F1) },
                new CharacterSlot { Key = "Éni", Enabled = false },
            },
            Macros =
            {
                new Macro
                {
                    Name = "Soin",
                    Hotkey = new Hotkey(VirtualKeys.F5, KeyModifiers.Control | KeyModifiers.Shift),
                    Steps =
                    {
                        new ForEachCharacterStep
                        {
                            SkipCurrentWindow = true,
                            Steps =
                            {
                                new MouseClickStep { Fx = 0.25, Fy = 0.9, Button = MouseButton.Right, Clicks = 2 },
                                new KeyStep { VirtualKey = VirtualKeys.F3, Action = KeyAction.Down },
                                new DelayStep { Milliseconds = 250 },
                            },
                        },
                        new FocusStep { Target = FocusTarget.Slot, SlotIndex = 2 },
                        new MouseMoveStep { Fx = 0.1, Fy = 0.2 },
                    },
                },
            },
        };

        store.Save(profile);
        var loaded = store.Load();

        Assert.Equal("Mon Iop", loaded.Characters[0].DisplayName);
        Assert.Equal(new Hotkey(VirtualKeys.F1), loaded.Characters[0].Hotkey);
        Assert.False(loaded.Characters[1].Enabled);

        var macro = Assert.Single(loaded.Macros);
        Assert.Equal(new Hotkey(VirtualKeys.F5, KeyModifiers.Control | KeyModifiers.Shift), macro.Hotkey);

        // Le point sensible : les sous-types d'étapes doivent revenir intacts, boucle comprise.
        var loop = Assert.IsType<ForEachCharacterStep>(macro.Steps[0]);
        Assert.True(loop.SkipCurrentWindow);
        var click = Assert.IsType<MouseClickStep>(loop.Steps[0]);
        Assert.Equal(MouseButton.Right, click.Button);
        Assert.Equal(2, click.Clicks);
        Assert.Equal(0.25, click.Fx, 6);
        Assert.Equal(KeyAction.Down, Assert.IsType<KeyStep>(loop.Steps[1]).Action);
        Assert.Equal(250, Assert.IsType<DelayStep>(loop.Steps[2]).Milliseconds);
        Assert.Equal(2, Assert.IsType<FocusStep>(macro.Steps[1]).SlotIndex);
        Assert.IsType<MouseMoveStep>(macro.Steps[2]);
    }

    [Fact]
    public void Les_accents_restent_lisibles_dans_le_fichier()
    {
        var path = PathFor("accents.json");
        var store = new ProfileStore(path);
        store.Save(new Profile { Characters = { new CharacterSlot { Key = "Éniripsa-Zébu" } } });

        Assert.Contains("Éniripsa-Zébu", File.ReadAllText(path));
    }

    [Fact]
    public void Un_profil_absent_donne_une_configuration_par_defaut_utilisable()
    {
        var profile = new ProfileStore(PathFor("absent.json")).Load();

        Assert.NotNull(profile.Settings.NextCharacterHotkey);
        Assert.NotNull(profile.Settings.PanicHotkey);
        Assert.NotEmpty(profile.Macros);
    }

    [Fact]
    public void Un_profil_corrompu_est_mis_de_cote_au_lieu_d_etre_perdu()
    {
        var path = PathFor("corrupt.json");
        File.WriteAllText(path, "{ ceci n'est pas du JSON");

        var profile = new ProfileStore(path).Load();

        Assert.NotNull(profile);
        Assert.True(File.Exists(path + ".corrupt"));
    }

    [Fact]
    public void Une_sauvegarde_ne_laisse_pas_de_fichier_temporaire()
    {
        var path = PathFor("atomic.json");
        var store = new ProfileStore(path);

        store.Save(new Profile());
        store.Save(new Profile { Characters = { new CharacterSlot { Key = "Iop" } } });

        Assert.False(File.Exists(path + ".tmp"));
        Assert.Single(store.Load().Characters);
    }
}

public class HotkeyBindingsTests
{
    [Fact]
    public void Chaque_raccourci_est_resolu_vers_son_action()
    {
        var profile = new Profile();
        profile.Settings.NextCharacterHotkey = new Hotkey(VirtualKeys.Tab, KeyModifiers.Control);
        profile.Settings.PanicHotkey = new Hotkey(VirtualKeys.Pause);
        var slot = new CharacterSlot { Key = "Iop", Hotkey = new Hotkey(VirtualKeys.F1) };
        var macro = new Macro { Name = "Soin", Hotkey = new Hotkey(VirtualKeys.F5) };
        profile.Characters.Add(slot);
        profile.Macros.Add(macro);

        var table = HotkeyBindings.Build(profile);

        Assert.Equal(HotkeyActionKind.FocusNext, table.Resolve(VirtualKeys.Tab, KeyModifiers.Control)!.Kind);
        Assert.Equal(HotkeyActionKind.Panic, table.Resolve(VirtualKeys.Pause, KeyModifiers.None)!.Kind);
        Assert.Same(slot, table.Resolve(VirtualKeys.F1, KeyModifiers.None)!.Slot);
        Assert.Same(macro, table.Resolve(VirtualKeys.F5, KeyModifiers.None)!.Macro);
    }

    [Fact]
    public void Une_touche_sans_le_bon_modificateur_n_est_pas_declenchee()
    {
        // Ctrl+Tab est assigné : un Tab seul doit continuer d'atteindre le jeu.
        var profile = new Profile();
        profile.Settings.PanicHotkey = null;
        profile.Settings.NextCharacterHotkey = new Hotkey(VirtualKeys.Tab, KeyModifiers.Control);

        var table = HotkeyBindings.Build(profile);

        Assert.Null(table.Resolve(VirtualKeys.Tab, KeyModifiers.None));
        Assert.Null(table.Resolve(VirtualKeys.Tab, KeyModifiers.Alt));
    }

    [Fact]
    public void Les_raccourcis_assignes_deux_fois_sont_signales()
    {
        var profile = new Profile();
        profile.Settings.PanicHotkey = null;
        profile.Settings.NextCharacterHotkey = new Hotkey(VirtualKeys.F1);
        profile.Characters.Add(new CharacterSlot { Key = "Iop", Hotkey = new Hotkey(VirtualKeys.F1) });

        var conflicts = HotkeyBindings.FindConflicts(profile);

        Assert.Equal(new Hotkey(VirtualKeys.F1), Assert.Single(conflicts));
    }
}
