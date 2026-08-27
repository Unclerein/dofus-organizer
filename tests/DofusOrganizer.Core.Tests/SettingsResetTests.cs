using DofusOrganizer.Core.Config;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Les raccourcis globaux partagent le même objet que les temporisations. Ce qui est vérifié
/// ici n'est donc pas tant ce que chaque rétablissement remet en place que ce qu'il laisse
/// tranquille : une touche effacée au passage ne se verrait qu'en jeu, bien plus tard.
/// </summary>
public class SettingsResetTests
{
    /// <summary>La touche H — celle du havre-sac, l'usage type d'une touche lente.</summary>
    private const int TouchH = 0x48;

    private static AppSettings Bricolees() => new()
    {
        FocusSettleDelayMs = 999,
        ActionDelayMs = 888,
        TeamReplayDelayMs = 4321,
        MultiClickIntervalMs = 777,
        ScrollDelayMs = 666,
        TypingDelayMs = 555,
        TitlePattern = "n'importe quoi",
        WindowClassFilter = "UneClasseQuelconque",
    };

    [Fact]
    public void Les_temporisations_reviennent_a_leurs_valeurs_d_origine()
    {
        var settings = Bricolees();
        var neuf = new AppSettings();

        SettingsReset.RestoreDelays(settings);

        Assert.Equal(neuf.FocusSettleDelayMs, settings.FocusSettleDelayMs);
        Assert.Equal(neuf.ActionDelayMs, settings.ActionDelayMs);
        Assert.Equal(neuf.TeamReplayDelayMs, settings.TeamReplayDelayMs);
        Assert.Equal(neuf.MultiClickIntervalMs, settings.MultiClickIntervalMs);
        Assert.Equal(neuf.ScrollDelayMs, settings.ScrollDelayMs);
        Assert.Equal(neuf.TypingDelayMs, settings.TypingDelayMs);
    }

    [Fact]
    public void La_detection_revient_a_ses_valeurs_d_origine()
    {
        var settings = Bricolees();

        SettingsReset.RestoreDetection(settings);

        Assert.Equal(AppSettings.DefaultTitlePattern, settings.TitlePattern);
        Assert.Equal("", settings.WindowClassFilter);
    }

    [Fact]
    public void Chaque_bouton_laisse_l_autre_moitie_tranquille()
    {
        // C'est tout l'intérêt d'avoir deux boutons : réparer un motif de titre ne doit pas
        // faire perdre des temporisations réglées client par client, et l'inverse non plus.
        var delais = Bricolees();
        SettingsReset.RestoreDelays(delais);
        Assert.Equal("n'importe quoi", delais.TitlePattern);
        Assert.Equal("UneClasseQuelconque", delais.WindowClassFilter);

        var detection = Bricolees();
        SettingsReset.RestoreDetection(detection);
        Assert.Equal(999, detection.FocusSettleDelayMs);
        Assert.Equal(4321, detection.TeamReplayDelayMs);
        Assert.Equal(555, detection.TypingDelayMs);
    }

    [Fact]
    public void Les_raccourcis_globaux_sont_intacts()
    {
        // Ce que ni l'un ni l'autre ne doit emporter : ils vivent dans le même objet.
        var settings = Bricolees();
        settings.NextCharacterHotkey = new Hotkey(VirtualKeys.F1);
        settings.PreviousCharacterHotkey = new Hotkey(VirtualKeys.F2);
        settings.PanicHotkey = new Hotkey(VirtualKeys.F3);
        settings.ToggleRecordingHotkey = new Hotkey(VirtualKeys.F4);
        settings.RepeatOnTeamHotkey = new Hotkey(VirtualKeys.F5);

        SettingsReset.RestoreDelays(settings);
        SettingsReset.RestoreDetection(settings);

        Assert.Equal(new Hotkey(VirtualKeys.F1), settings.NextCharacterHotkey);
        Assert.Equal(new Hotkey(VirtualKeys.F2), settings.PreviousCharacterHotkey);
        Assert.Equal(new Hotkey(VirtualKeys.F3), settings.PanicHotkey);
        Assert.Equal(new Hotkey(VirtualKeys.F4), settings.ToggleRecordingHotkey);
        Assert.Equal(new Hotkey(VirtualKeys.F5), settings.RepeatOnTeamHotkey);
    }

    [Fact]
    public void Les_touches_lentes_survivent_au_retablissement_des_temporisations()
    {
        // C'est une temporisation, mais attachée à une touche choisie à la main : l'effacer
        // casserait un aller au havre-sac sans que rien ne le signale.
        var settings = Bricolees();
        settings.SlowKeys.Add(new SlowKey { Key = new Hotkey(TouchH), ExtraDelayMs = 900 });

        SettingsReset.RestoreDelays(settings);

        var slow = Assert.Single(settings.SlowKeys);
        Assert.Equal(new Hotkey(TouchH), slow.Key);
        Assert.Equal(900, slow.ExtraDelayMs);
    }

    [Fact]
    public void Les_cases_du_comportement_sont_conservees()
    {
        // Décocher les codes de balayage est le remède quand le jeu ignore les frappes :
        // le rétablir dans le dos de l'utilisateur rendrait l'organizer muet.
        var settings = Bricolees();
        settings.UseScanCodes = false;
        settings.SwallowBoundKeys = false;
        settings.HotkeysOnlyWhenGameFocused = false;
        settings.RecordDelays = true;
        settings.RecordingFeedbackSound = false;

        SettingsReset.RestoreDelays(settings);
        SettingsReset.RestoreDetection(settings);

        Assert.False(settings.UseScanCodes);
        Assert.False(settings.SwallowBoundKeys);
        Assert.False(settings.HotkeysOnlyWhenGameFocused);
        Assert.True(settings.RecordDelays);
        Assert.False(settings.RecordingFeedbackSound);
    }

    [Fact]
    public void Le_reste_du_profil_n_est_pas_concerne()
    {
        // La signature ne prend que les réglages ; ce test l'ancre, pour qu'un futur
        // élargissement à Profile ne passe pas inaperçu.
        var profile = new Profile();
        profile.Characters.Add(new CharacterSlot { Key = "Iop", Hotkey = new Hotkey(VirtualKeys.F6) });
        profile.Macros.Add(new Macro { Name = "Soin", Hotkey = new Hotkey(VirtualKeys.F7) });
        profile.Settings.ActionDelayMs = 999;

        SettingsReset.RestoreDelays(profile.Settings);
        SettingsReset.RestoreDetection(profile.Settings);

        Assert.Equal(new Hotkey(VirtualKeys.F6), Assert.Single(profile.Characters).Hotkey);
        Assert.Equal(new Hotkey(VirtualKeys.F7), Assert.Single(profile.Macros).Hotkey);
        Assert.NotEqual(999, profile.Settings.ActionDelayMs);
    }
}
