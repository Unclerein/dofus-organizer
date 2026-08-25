using DofusOrganizer.Core.Organizer;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Windows retire un hook bas niveau dont le rappel tarde trop, et le fait sans rien signaler :
/// tous les raccourcis cessent de répondre, le programme continue de les croire en place, et
/// seul un redémarrage les ramène.
///
/// Aucune API ne dit si un hook est vivant. La preuve se fait par l'absurde : si le système a
/// reçu une entrée plus récemment que notre dernier rappel, c'est que nous l'avons manquée.
/// </summary>
public class HookWatchdogTests
{
    private const int Marge = HookWatchdog.SilenceMarginMs;

    [Fact]
    public void Une_entree_vue_par_le_systeme_et_pas_par_nous_condamne_le_hook()
    {
        // Le cas réel : l'utilisateur tape, le système l'enregistre, nos rappels se sont tus.
        Assert.True(HookWatchdog.ShouldReinstall(
            lastCallbackMs: 10_000, lastSystemInputMs: 10_000 + Marge + 1));
    }

    [Fact]
    public void Sous_la_marge_on_laisse_sa_chance_au_rappel()
    {
        // Le système date l'entrée avant que notre rappel ne s'exécute : un écart court est
        // normal et ne prouve rien.
        Assert.False(HookWatchdog.ShouldReinstall(
            lastCallbackMs: 10_000, lastSystemInputMs: 10_000 + Marge));
    }

    [Fact]
    public void Une_machine_au_repos_ne_declenche_rien()
    {
        // Personne ne touche à rien : les deux horloges restent figées, et une réinstallation
        // en boucle serait du bruit pur.
        Assert.False(HookWatchdog.ShouldReinstall(lastCallbackMs: 500_000, lastSystemInputMs: 500_000));
        Assert.False(HookWatchdog.ShouldReinstall(lastCallbackMs: 500_000, lastSystemInputMs: 480_000));
    }

    [Fact]
    public void Un_hook_qui_repond_ne_se_fait_pas_reposer()
    {
        // Cas courant : nos rappels sont aussi frais que les entrées du système.
        for (long t = 0; t < 10_000; t += 250)
        {
            Assert.False(HookWatchdog.ShouldReinstall(lastCallbackMs: t, lastSystemInputMs: t));
        }
    }

    [Fact]
    public void Un_systeme_muet_ne_declenche_rien()
    {
        // Le repli quand GetLastInputInfo refuse de répondre : zéro, donc un écart négatif.
        Assert.False(HookWatchdog.ShouldReinstall(lastCallbackMs: 900_000, lastSystemInputMs: 0));
    }

    [Fact]
    public void La_marge_est_reglable_pour_le_test_comme_pour_l_appel()
    {
        Assert.True(HookWatchdog.ShouldReinstall(1_000, 1_150, marginMs: 100));
        Assert.False(HookWatchdog.ShouldReinstall(1_000, 1_150, marginMs: 200));
    }
}
