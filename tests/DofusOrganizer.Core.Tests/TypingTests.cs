using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Chercher un zaap se fait en tapant son nom : cinq lettres, cinq étapes de touche. Espacées du
/// délai des actions — une demi-seconde chacune lors d'un rejeu sur l'équipe — écrire « Bonta »
/// prenait deux secondes et demie par personnage, pour une saisie que le jeu traite instantanément.
///
/// La dernière touche, elle, garde le délai ordinaire : c'est après elle que le jeu a quelque
/// chose à faire.
/// </summary>
public class TypingTests
{
    private const int Saisie = 25;
    private const int Action = 500;

    private static Profile Reglages()
    {
        var profile = new Profile();
        profile.Settings.FocusSettleDelayMs = 0;
        profile.Settings.ActionDelayMs = Action;
        profile.Settings.TypingDelayMs = Saisie;
        return profile;
    }

    private static KeyStep Touche(int virtualKey) => new() { VirtualKey = virtualKey };

    [Fact]
    public async Task Des_touches_enchainees_ne_subissent_pas_le_delai_des_actions()
    {
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.ActionDelayMs = Action;
        profile.Settings.TypingDelayMs = Saisie;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(Touche(VirtualKeys.F1), Touche(VirtualKeys.F2), Touche(VirtualKeys.F3)),
            windows, roster, profile);

        // Deux écarts de saisie entre les trois touches, puis le délai ordinaire à la fin.
        Assert.Equal([Saisie, Saisie, Action], actions.OfType<RecordedAction.Delay>().Select(d => d.Milliseconds));
    }

    [Fact]
    public async Task Le_rejeu_sur_l_equipe_ne_ralentit_pas_la_saisie()
    {
        // Le cas qui motive tout : 600 ms par lettre, sur chaque personnage.
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.TypingDelayMs = Saisie;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(Touche(VirtualKeys.F1), Touche(VirtualKeys.F2)),
            windows, roster, profile, actionDelayOverride: 600);

        Assert.Equal([Saisie, 600], actions.OfType<RecordedAction.Delay>().Select(d => d.Milliseconds));
    }

    [Fact]
    public async Task Une_touche_isolee_garde_le_delai_ordinaire()
    {
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.ActionDelayMs = Action;
        profile.Settings.TypingDelayMs = Saisie;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(Touche(VirtualKeys.F1)), windows, roster, profile);

        Assert.Equal(Action, Assert.Single(actions.OfType<RecordedAction.Delay>()).Milliseconds);
    }

    [Fact]
    public async Task Une_touche_suivie_d_un_clic_garde_le_delai_ordinaire()
    {
        // Ce n'est plus de la saisie : le jeu a quelque chose à faire avant le clic.
        var (windows, roster, profile) = MacroHarness.BuildSolo();
        profile.Settings.ActionDelayMs = Action;
        profile.Settings.TypingDelayMs = Saisie;

        var actions = await MacroHarness.RunAsync(
            MacroHarness.MacroOf(Touche(VirtualKeys.F1), new MouseClickStep { Fx = 0.5, Fy = 0.5 }),
            windows, roster, profile);

        Assert.Equal([Action, Action], actions.OfType<RecordedAction.Delay>().Select(d => d.Milliseconds));
    }

    [Fact]
    public void Une_touche_lente_garde_son_supplement_meme_au_milieu_d_une_saisie()
    {
        // L'ouverture d'un panneau ne devient pas instantanée parce qu'on tape vite ensuite.
        var profile = Reglages();
        profile.Settings.SlowKeys.Add(new SlowKey { Key = new Hotkey(VirtualKeys.F7), ExtraDelayMs = 1500 });

        int pause = StepPacing.After(Touche(VirtualKeys.F7), Touche(VirtualKeys.F1), profile.Settings, Action);
        Assert.Equal(Saisie + 1500, pause);
    }

    [Fact]
    public void La_table_dit_pour_chaque_nature_d_etape_quelle_pause_s_applique()
    {
        var profile = Reglages();
        profile.Settings.ScrollDelayMs = 40;
        var clic = new MouseClickStep();

        Assert.Equal(Saisie, StepPacing.After(Touche(VirtualKeys.F1), Touche(VirtualKeys.F2), profile.Settings, Action));
        Assert.Equal(Action, StepPacing.After(Touche(VirtualKeys.F1), clic, profile.Settings, Action));
        Assert.Equal(Action, StepPacing.After(Touche(VirtualKeys.F1), null, profile.Settings, Action));
        Assert.Equal(40, StepPacing.After(new ScrollStep(), Touche(VirtualKeys.F1), profile.Settings, Action));
        Assert.Equal(Action, StepPacing.After(clic, Touche(VirtualKeys.F1), profile.Settings, Action));
        Assert.Equal(Action, StepPacing.After(new MouseDragStep(), null, profile.Settings, Action));
    }
}
