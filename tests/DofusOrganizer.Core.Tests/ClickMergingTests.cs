using DofusOrganizer.Core.Geometry;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Un double-clic non reconnu devient deux étapes séparées, que le rejeu espace du délai
/// configuré — 600 ms sur l'équipe — bien au-delà du seuil de Windows. Le jeu ne voit alors
/// que deux clics isolés, et la téléportation ne part pas.
/// </summary>
public class ClickMergingTests
{
    private static readonly InputThresholds Thresholds = InputThresholds.Default;

    private static RecordedClick At(int x, int y, long time, MouseButton button = MouseButton.Left)
        => new(button, new ScreenPoint(x, y), time);

    [Fact]
    public void Deux_appuis_rapproches_forment_un_double_clic()
        => Assert.True(ClickMerging.ContinuesClick(
            At(400, 300, 1000), MouseButton.Left, new ScreenPoint(401, 300), 1120, 1, Thresholds));

    [Fact]
    public void Deux_appuis_trop_espaces_dans_le_temps_restent_distincts()
        => Assert.False(ClickMerging.ContinuesClick(
            At(400, 300, 1000), MouseButton.Left, new ScreenPoint(400, 300), 1600, 1, Thresholds));

    [Fact]
    public void Deux_appuis_trop_eloignes_restent_distincts()
        => Assert.False(ClickMerging.ContinuesClick(
            At(400, 300, 1000), MouseButton.Left, new ScreenPoint(430, 300), 1100, 1, Thresholds));

    [Fact]
    public void Deux_boutons_differents_ne_fusionnent_pas()
        => Assert.False(ClickMerging.ContinuesClick(
            At(400, 300, 1000), MouseButton.Right, new ScreenPoint(400, 300), 1100, 1, Thresholds));

    [Fact]
    public void Le_plafond_d_appuis_est_respecte()
    {
        Assert.True(ClickMerging.ContinuesClick(At(400, 300, 1000), MouseButton.Left, new ScreenPoint(400, 300), 1100, 2, Thresholds));
        Assert.False(ClickMerging.ContinuesClick(At(400, 300, 1000), MouseButton.Left, new ScreenPoint(400, 300), 1100, 3, Thresholds));
    }

    [Fact]
    public void Sans_appui_precedent_il_n_y_a_rien_a_prolonger()
        => Assert.False(ClickMerging.ContinuesClick(null, MouseButton.Left, new ScreenPoint(1, 1), 10, 0, Thresholds));

    [Fact]
    public void Les_seuils_du_systeme_sont_respectes_quand_ils_different_des_valeurs_usuelles()
    {
        // Un utilisateur ayant élargi la tolérance doit voir ses appuis fusionner de la
        // même façon : c'est pour cela que les seuils sont lus et non figés.
        var large = new InputThresholds(DoubleClickTimeMs: 900, DoubleClickSlopX: 20, DoubleClickSlopY: 20, DragSlopX: 4, DragSlopY: 4);

        Assert.True(ClickMerging.ContinuesClick(At(400, 300, 1000), MouseButton.Left, new ScreenPoint(415, 310), 1800, 1, large));
        Assert.False(ClickMerging.ContinuesClick(At(400, 300, 1000), MouseButton.Left, new ScreenPoint(415, 310), 1800, 1, Thresholds));
    }

    [Fact]
    public void Un_relachement_eloigne_de_l_appui_est_un_glisser()
    {
        Assert.True(ClickMerging.IsDrag(new ScreenPoint(100, 100), new ScreenPoint(300, 240), Thresholds));
        Assert.True(ClickMerging.IsDrag(new ScreenPoint(100, 100), new ScreenPoint(100, 140), Thresholds));
    }

    [Fact]
    public void Un_relachement_sur_place_reste_un_clic()
    {
        // La main tremble : quelques pixels ne doivent pas transformer un clic en glisser.
        Assert.False(ClickMerging.IsDrag(new ScreenPoint(100, 100), new ScreenPoint(100, 100), Thresholds));
        Assert.False(ClickMerging.IsDrag(new ScreenPoint(100, 100), new ScreenPoint(103, 102), Thresholds));
    }
}
