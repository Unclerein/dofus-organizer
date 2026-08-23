using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Config;

/// <summary>Lecture et écriture du profil sur disque.</summary>
public sealed class ProfileStore(string path)
{
    public string Path { get; } = path;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Les noms de personnages contiennent des accents : sans cet encodeur ils
        // ressortent en é dans un fichier qu'on veut pouvoir relire à la main.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DofusOrganizer",
        "profile.json");

    /// <summary>Charge le profil, ou en renvoie un neuf si le fichier est absent ou illisible.</summary>
    public Profile Load()
    {
        if (!File.Exists(Path)) return CreateDefault();

        try
        {
            string json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? CreateDefault();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Un profil corrompu est mis de côté au lieu d'être écrasé : l'utilisateur
            // peut encore y récupérer ses macros à la main.
            TryBackupCorruptFile();
            return CreateDefault();
        }
    }

    /// <summary>
    /// Écrit le profil de façon atomique : un plantage pendant la sauvegarde laisse
    /// l'ancien fichier intact plutôt qu'un JSON tronqué.
    /// </summary>
    public void Save(Profile profile)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        string temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(profile, JsonOptions));

        if (File.Exists(Path)) File.Replace(temp, Path, null);
        else File.Move(temp, Path);
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            string backup = Path + ".corrupt";
            File.Copy(Path, backup, overwrite: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Profile CreateDefault()
    {
        var profile = new Profile();
        profile.Settings.NextCharacterHotkey = new Hotkey(VirtualKeys.Tab, KeyModifiers.Control);
        profile.Macros.Add(CreateHealTemplate());
        return profile;
    }

    /// <summary>
    /// Macro d'exemple montrant la structure attendue pour soigner l'équipe.
    /// Les positions sont volontairement au centre : elles doivent être ré-enregistrées
    /// ou ajustées avec les vraies coordonnées de la barre de sorts.
    /// </summary>
    private static Macro CreateHealTemplate() => new()
    {
        Name = "Soin de l'équipe (à régler)",
        Steps =
        {
            new ForEachCharacterStep
            {
                Steps =
                {
                    new MouseClickStep { Fx = 0.42, Fy = 0.93 },   // icône du sort de soin
                    new DelayStep { Milliseconds = 150 },
                    new MouseClickStep { Fx = 0.50, Fy = 0.50 },   // cible du sort
                    new DelayStep { Milliseconds = 250 },
                },
            },
        },
    };
}
