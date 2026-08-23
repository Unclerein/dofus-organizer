using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using DofusOrganizer.App.Services;
using DofusOrganizer.App.ViewModels;

namespace DofusOrganizer.App;

public partial class MainWindow : Window
{
    private readonly OrganizerService _service;
    private bool _released;

    /// <param name="profilePath">
    /// Chemin du profil à utiliser. Null pour l'emplacement habituel dans le dossier de
    /// l'utilisateur ; le contrôle de démarrage passe un fichier temporaire pour ne pas
    /// toucher à la configuration réelle.
    /// </param>
    public MainWindow(string? profilePath = null)
    {
        InitializeComponent();

        // Les liaisons WPF convertissent les nombres en « en-US » par défaut, quel que
        // soit le système. Les champs de position de clic afficheraient donc un point
        // décimal et refuseraient la virgule tapée sur un clavier français.
        Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        _service = new OrganizerService(Dispatcher, profilePath);
        DataContext = new MainViewModel(_service);

        // Les hooks bas niveau ne peuvent être posés qu'une fois la fenêtre créée,
        // depuis le fil qui fait tourner la boucle de messages.
        Loaded += (_, _) => Start();
    }

    private void Start()
    {
        try
        {
            _service.Start();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, "Dofus Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Arrête la détection et retire les hooks. Sans cette désinstallation ils
    /// resteraient posés jusqu'à ce que Windows finisse par les retirer, et les touches
    /// assignées continueraient d'être avalées après la fermeture.
    /// </summary>
    internal void ReleaseResources(bool save = false)
    {
        if (_released) return;
        _released = true;

        if (save) _service.Save();
        _service.Dispose();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ReleaseResources(save: true);
        base.OnClosing(e);
    }
}
