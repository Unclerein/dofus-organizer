using System.Text.Json.Serialization;

namespace DofusOrganizer.Core.Models;

/// <summary>
/// Une touche envoyée à tous les personnages, l'un après l'autre.
///
/// Répond aux gestes identiques sur chaque client — s'asseoir, boire une potion, ouvrir la
/// carte, se déconnecter — qui n'ont pas besoin d'être enregistrés : il n'y a ni position à
/// retrouver ni image à reconnaître, juste une frappe à répéter.
/// </summary>
public sealed class BroadcastKey : NotifyBase
{
    private string _name = "Nouvelle diffusion";
    private Hotkey? _trigger;
    private Hotkey? _sent;
    private bool _includeCurrent = true;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Nom affiché dans la liste, pour s'y retrouver quand il y en a plusieurs.</summary>
    public string Name
    {
        get => _name;
        set { if (Set(ref _name, value ?? "")) Raise(nameof(Summary)); }
    }

    /// <summary>Raccourci qui déclenche la diffusion.</summary>
    public Hotkey? Trigger
    {
        get => _trigger;
        set { if (Set(ref _trigger, value)) Raise(nameof(Summary)); }
    }

    /// <summary>
    /// Touche réellement envoyée aux personnages. Distincte du déclencheur parce qu'elles ne
    /// coïncident pas toujours : diffuser une touche dont le jeu se sert déjà suppose de la
    /// déclencher autrement, sans quoi elle serait absorbée et perdue en usage ordinaire.
    /// </summary>
    public Hotkey? Sent
    {
        get => _sent;
        set { if (Set(ref _sent, value)) Raise(nameof(Summary)); }
    }

    /// <summary>
    /// Envoyer aussi la touche au personnage au premier plan.
    ///
    /// Vrai par défaut, et ce n'est pas un détail : l'absorption des touches assignées étant
    /// active elle aussi par défaut, le déclencheur n'atteint jamais le client que l'on joue.
    /// Sans cette case, le meneur serait le seul à ne rien faire. Elle se décoche pour qui a
    /// désactivé l'absorption, et qui recevrait sinon deux frappes.
    /// </summary>
    public bool IncludeCurrent
    {
        get => _includeCurrent;
        set { if (Set(ref _includeCurrent, value)) Raise(nameof(Summary)); }
    }

    /// <summary>Vrai quand une touche à envoyer a été choisie : sans elle il n'y a rien à diffuser.</summary>
    [JsonIgnore]
    public bool IsUsable => Sent is { IsEmpty: false };

    [JsonIgnore]
    public string Summary
    {
        get
        {
            string trigger = Trigger is { IsEmpty: false } key ? key.ToString() : "(aucun raccourci)";
            string sent = Sent is { IsEmpty: false } value ? value.ToString() : "(aucune touche)";
            string scope = IncludeCurrent ? "" : " — sauf le personnage actif";
            return $"{Name} : {trigger} → {sent}{scope}";
        }
    }
}
