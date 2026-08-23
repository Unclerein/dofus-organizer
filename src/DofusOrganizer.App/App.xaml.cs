using System.Windows;
using System.Windows.Threading;

namespace DofusOrganizer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Une exception non gérée sur le fil d'interface fermerait l'application en
        // silence et laisserait les hooks clavier posés : mieux vaut la montrer.
        DispatcherUnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Une erreur inattendue est survenue :\n\n{e.Exception.Message}",
            "Dofus Organizer", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
