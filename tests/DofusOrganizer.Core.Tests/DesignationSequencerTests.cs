using DofusOrganizer.Core.Input;
using Xunit;

namespace DofusOrganizer.Core.Tests;

public class DesignationSequencerTests
{
    [Fact]
    public void Fermee_laisse_tout_passer()
    {
        var sequencer = new DesignationSequencer();

        Assert.Equal(DesignationVerdict.LetThrough, sequencer.OnPress(onTarget: true));
        Assert.Equal(DesignationVerdict.LetThrough, sequencer.OnRelease(captureAwaiting: false));
    }

    [Fact]
    public void Clic_hors_dune_fenetre_suivie_passe_appui_et_relachement()
    {
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: false);

        Assert.Equal(DesignationVerdict.LetThrough, sequencer.OnPress(onTarget: false));
        Assert.Equal(DesignationVerdict.LetThrough, sequencer.OnRelease(captureAwaiting: false));
    }

    [Fact]
    public void Designation_libre_retient_le_point_et_reste_ouverte()
    {
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: false);

        Assert.Equal(DesignationVerdict.Take, sequencer.OnPress(onTarget: true));
        Assert.Equal(DesignationVerdict.Swallow, sequencer.OnRelease(captureAwaiting: false));
        Assert.True(sequencer.IsDesignating);
    }

    [Fact]
    public void Capture_a_lunite_se_referme_au_relachement()
    {
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: true);

        Assert.Equal(DesignationVerdict.Take, sequencer.OnPress(onTarget: true));
        Assert.Equal(DesignationVerdict.SwallowAndClose, sequencer.OnRelease(captureAwaiting: false));
        Assert.False(sequencer.IsDesignating);
    }

    /// <summary>
    /// Le cas qui manquait : le calibrage pose sa seconde capture dès que la première est
    /// servie, donc avant le relâchement du premier clic. Refermer là emportait cette seconde
    /// capture, et le clic « en bas à droite » n'était vu de personne.
    /// </summary>
    [Fact]
    public void Une_capture_posee_avant_le_relachement_garde_la_designation_ouverte()
    {
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: true);

        Assert.Equal(DesignationVerdict.Take, sequencer.OnPress(onTarget: true));

        // Le demandeur, réveillé par l'appui, en redemande un second point.
        sequencer.Open(singleShot: true);

        Assert.Equal(DesignationVerdict.Swallow, sequencer.OnRelease(captureAwaiting: true));
        Assert.True(sequencer.IsDesignating);

        // Le second clic est bien vu, et referme cette fois.
        Assert.Equal(DesignationVerdict.Take, sequencer.OnPress(onTarget: true));
        Assert.Equal(DesignationVerdict.SwallowAndClose, sequencer.OnRelease(captureAwaiting: false));
        Assert.False(sequencer.IsDesignating);
    }

    [Fact]
    public void Une_capture_greffee_ne_referme_pas_une_designation_libre()
    {
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: false);
        sequencer.Open(singleShot: true);

        Assert.False(sequencer.IsSingleShot);

        Assert.Equal(DesignationVerdict.Take, sequencer.OnPress(onTarget: true));
        Assert.Equal(DesignationVerdict.Swallow, sequencer.OnRelease(captureAwaiting: false));
        Assert.True(sequencer.IsDesignating);
    }

    [Fact]
    public void Un_relachement_orphelin_passe()
    {
        // Le bouton était enfoncé avant que la désignation ne commence : le jeu attend son
        // relâchement, avaler celui-ci le laisserait croire à un bouton resté appuyé.
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: true);

        Assert.Equal(DesignationVerdict.LetThrough, sequencer.OnRelease(captureAwaiting: false));
        Assert.True(sequencer.IsDesignating);
    }

    [Fact]
    public void Refermer_puis_rouvrir_repart_dun_etat_propre()
    {
        var sequencer = new DesignationSequencer();
        sequencer.Open(singleShot: true);
        sequencer.OnPress(onTarget: true);
        sequencer.Close();

        sequencer.Open(singleShot: false);

        // L'appui avalé de la session précédente ne doit pas faire avaler ce relâchement-ci.
        Assert.Equal(DesignationVerdict.LetThrough, sequencer.OnRelease(captureAwaiting: false));
    }
}
