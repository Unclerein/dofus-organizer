using DofusOrganizer.Core.Macros;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Ces deux calculs décident combien d'items changent de mains. Se tromper d'un facteur dix ne
/// provoque aucune erreur : la macro tape le nombre, le jeu obéit, et rien ne le signale.
/// </summary>
public class QuantitySplitTests
{
    [Theory]
    [InlineData("250", 250)]
    [InlineData("1", 1)]
    [InlineData("1 234", 1234)]          // espace ordinaire
    [InlineData("1 234", 1234)]     // espace insécable, celle des interfaces francophones
    [InlineData("1 234", 1234)]     // espace fine insécable
    [InlineData("1'234", 1234)]
    [InlineData("  42  ", 42)]
    public void Une_quantite_est_lue_quels_que_soient_ses_separateurs(string copie, int attendu)
    {
        Assert.True(QuantitySplit.TryParse(copie, out int quantite));
        Assert.Equal(attendu, quantite);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("-12")]
    [InlineData("1.5")]      // ni une quantité mal écrite, ni un nombre à arrondir : on ne lit pas ce qu'on croit
    [InlineData("1,5")]
    [InlineData("12 items")]
    [InlineData("99999999999")]   // au-delà d'un entier : refusé plutôt que tronqué
    public void Ce_qui_n_est_pas_une_quantite_est_refuse(string? copie)
    {
        // Refuser, c'est arrêter la macro. Deviner, c'est taper un nombre venu d'ailleurs.
        Assert.False(QuantitySplit.TryParse(copie, out int quantite));
        Assert.Equal(0, quantite);
    }

    [Fact]
    public void Un_stock_qui_tombe_juste_se_partage_a_parts_egales()
    {
        // Le cas nominal : 100 items sur quatre personnages, chacun repart avec 25.
        Assert.Equal(25, QuantitySplit.Share(100, 4));
        Assert.Equal(25, QuantitySplit.Share(75, 3));
        Assert.Equal(25, QuantitySplit.Share(50, 2));
        Assert.Equal(25, QuantitySplit.Share(25, 1));
    }

    [Fact]
    public void Un_stock_qui_ne_tombe_pas_juste_est_distribue_en_entier()
    {
        // Tout l'intérêt de diviser par ce qui reste : un quart figé de dix donnerait
        // 2, 2, 2, 2 et laisserait deux items au coffre.
        int stock = 10;
        var parts = new List<int>();

        for (int restants = 4; restants > 0; restants--)
        {
            int part = QuantitySplit.Share(stock, restants);
            parts.Add(part);
            stock -= part;
        }

        Assert.Equal([2, 2, 3, 3], parts);
        Assert.Equal(0, stock);
    }

    [Fact]
    public void Un_stock_plus_petit_que_l_equipe_donne_une_part_nulle()
    {
        // Trois items pour quatre personnages : les premiers n'ont rien, les derniers se
        // partagent le reste. C'est à l'appelant de sauter l'item plutôt que de taper zéro.
        Assert.Equal(0, QuantitySplit.Share(3, 4));
        Assert.Equal(1, QuantitySplit.Share(3, 3));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(-5, 4)]
    [InlineData(100, 0)]
    [InlineData(100, -1)]
    public void Les_cas_absurdes_donnent_zero_plutot_que_de_lever(int stock, int restants)
        => Assert.Equal(0, QuantitySplit.Share(stock, restants));
}
