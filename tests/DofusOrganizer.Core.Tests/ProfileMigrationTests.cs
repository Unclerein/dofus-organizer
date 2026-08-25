using DofusOrganizer.Core.Config;
using DofusOrganizer.Core.Models;
using Xunit;

namespace DofusOrganizer.Core.Tests;

/// <summary>
/// Une propriété disparue est ignorée sans bruit par le lecteur JSON, mais un type d'étape
/// disparu le fait échouer — et ProfileStore répond à un échec en repartant d'un profil neuf.
/// Retirer l'attente sur image aurait donc effacé, chez qui en avait une, ses personnages, ses
/// raccourcis et toutes ses macros d'un coup, sans un mot.
/// </summary>
public class ProfileMigrationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("dofus-organizer-migration").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string PathFor(string name) => Path.Combine(_directory, name);

    /// <summary>Un profil tel qu'écrit par une version qui connaissait encore l'attente sur image.</summary>
    private const string AncienProfil = """
    {
      "Version": 1,
      "Settings": { "AnchorClicksToImages": true, "AnchorPatchWidth": 160 },
      "Characters": [ { "Key": "Iop", "Hotkey": { "VirtualKey": 112, "Modifiers": "None" } } ],
      "Macros": [
        {
          "Name": "Zaap",
          "Steps": [
            { "type": "click", "Fx": 0.3, "Fy": 0.7, "Anchor": { "Width": 4, "Pixels": "AAAA" } },
            { "type": "waitimage", "Fx": 0.4, "Fy": 0.2, "TimeoutMs": 2500 },
            { "type": "scroll", "Fx": 0.6, "Fy": 0.5, "Notches": 7 }
          ]
        }
      ]
    }
    """;

    [Fact]
    public void Un_profil_contenant_une_etape_disparue_garde_tout_le_reste()
    {
        string path = PathFor("ancien.json");
        File.WriteAllText(path, AncienProfil);

        var profile = new ProfileStore(path).Load();

        // Le test qui compte : les personnages et leurs raccourcis survivent.
        var slot = Assert.Single(profile.Characters);
        Assert.Equal("Iop", slot.Key);
        Assert.Equal(new Hotkey(VirtualKeys.F1), slot.Hotkey);

        // Et la macro garde ses étapes connues, seule l'attente sur image ayant disparu.
        var macro = Assert.Single(profile.Macros);
        Assert.Equal("Zaap", macro.Name);
        Assert.Collection(macro.Steps,
            step => Assert.Equal(0.3, Assert.IsType<MouseClickStep>(step).Fx, 6),
            step => Assert.Equal(7, Assert.IsType<ScrollStep>(step).Notches));
    }

    [Fact]
    public void Une_etape_disparue_au_fond_d_une_boucle_est_ecartee_aussi()
    {
        // C'est là que se trouvent les étapes d'un rejeu sur l'équipe : les oublier laisserait
        // le lecteur lever, et le profil serait perdu malgré la migration.
        string json = """
        {
          "Macros": [ { "Name": "Équipe", "Steps": [
            { "type": "foreach", "SkipCurrentWindow": true, "Steps": [
              { "type": "waitimage", "Fx": 0.1, "Fy": 0.1 },
              { "type": "key", "VirtualKey": 112 }
            ] }
          ] } ]
        }
        """;

        string path = PathFor("boucle.json");
        File.WriteAllText(path, json);

        var macro = Assert.Single(new ProfileStore(path).Load().Macros);
        var loop = Assert.IsType<ForEachCharacterStep>(Assert.Single(macro.Steps));

        Assert.True(loop.SkipCurrentWindow);
        Assert.IsType<KeyStep>(Assert.Single(loop.Steps));
    }

    [Fact]
    public void Les_types_reconnus_sont_lus_sur_le_modele_et_non_recopies()
    {
        // Une liste tenue à la main finirait par diverger, et une étape bien vivante mais
        // oubliée serait alors supprimée des profils au chargement.
        string json = """
        { "Macros": [ { "Steps": [
          { "type": "drag", "Fx": 0.1, "Fy": 0.1, "ToFx": 0.9, "ToFy": 0.9 },
          { "type": "delay", "Milliseconds": 250 }
        ] } ] }
        """;

        Assert.Contains("\"drag\"", ProfileMigration.DropUnknownSteps(json));
        Assert.Contains("\"delay\"", ProfileMigration.DropUnknownSteps(json));
    }

    [Fact]
    public void Un_profil_sain_rend_la_chaine_recue_elle_meme()
    {
        // Contrat dont ProfileStore se sert : il compare les références pour savoir qu'une
        // seconde lecture serait inutile. Sans cette garantie il relirait le profil pour rien.
        const string sain = """
        { "Macros": [ { "Steps": [ { "type": "delay", "Milliseconds": 250 } ] } ] }
        """;

        Assert.Same(sain, ProfileMigration.DropUnknownSteps(sain));
    }

    [Fact]
    public void Un_texte_illisible_ressort_tel_quel()
    {
        // La migration n'a pas à juger d'un fichier abîmé : le lecteur le met déjà de côté
        // au lieu de l'écraser, et c'est ce comportement qu'il faut lui laisser.
        const string abime = "ceci n'est pas du JSON {{{";
        Assert.Same(abime, ProfileMigration.DropUnknownSteps(abime));
    }
}
