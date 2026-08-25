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

        string json;
        try
        {
            json = File.ReadAllText(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CreateDefault();
        }

        try
        {
            return JsonSerializer.Deserialize<Profile>(json, JsonOptions) ?? CreateDefault();
        }
        catch (JsonException)
        {
            // Peut-être un profil écrit par une version qui connaissait des étapes que
            // celle-ci ignore : le lecteur lève sur un discriminant inconnu. Les écarter et
            // réessayer sauve le reste — sans cela, l'utilisateur perdrait au premier
            // lancement ses personnages, ses raccourcis et toutes ses macros.
            //
            // Cette seconde lecture n'a lieu qu'après un échec, jamais au démarrage ordinaire :
            // analyser tout le profil une deuxième fois à chaque lancement coûterait bien plus
            // que la lecture qu'elle protège.
            if (TryLoadPruned(json) is { } recovered) return recovered;

            // Un profil corrompu est mis de côté au lieu d'être écrasé : l'utilisateur
            // peut encore y récupérer ses macros à la main.
            TryBackupCorruptFile();
            return CreateDefault();
        }
    }

    /// <summary>
    /// Relit le profil après avoir écarté ses étapes d'un type inconnu, ou null s'il n'y avait
    /// rien à écarter — <see cref="ProfileMigration.DropUnknownSteps"/> rend alors la chaîne
    /// reçue elle-même — ou si le résultat ne se lit toujours pas.
    /// </summary>
    private static Profile? TryLoadPruned(string json)
    {
        string pruned = ProfileMigration.DropUnknownSteps(json);
        if (ReferenceEquals(pruned, json)) return null;

        try
        {
            return JsonSerializer.Deserialize<Profile>(pruned, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
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
