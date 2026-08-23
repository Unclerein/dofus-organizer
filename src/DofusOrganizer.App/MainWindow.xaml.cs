using System.ComponentModel;
using System.Windows;
using DofusOrganizer.App.Services;
using DofusOrganizer.App.ViewModels;

namespace DofusOrganizer.App;

public partial class MainWindow : Window
{
    private readonly OrganizerService _service;

    public MainWindow()
    {
        InitializeComponent();

        _service = new OrganizerService(Dispatcher);
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

    protected override void OnClosing(CancelEventArgs e)
    {
        // Sans désinstallation explicite, les hooks resteraient posés jusqu'à ce que
        // Windows finisse par les retirer, et les touches assignées resteraient avalées.
        _service.Save();
        _service.Dispose();
        base.OnClosing(e);
    }
}
