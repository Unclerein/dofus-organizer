using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DofusOrganizer.App.ViewModels;

namespace DofusOrganizer.App;

public partial class App : Application
{
    /// <summary>Argument qui déclenche le contrôle de démarrage utilisé par l'intégration continue.</summary>
    private const string SelfTestArgument = "--selftest";

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DofusOrganizer",
        "crash.log");

    /// <summary>
    /// Vrai tant que la fenêtre principale n'existe pas. Une erreur survenue avant
    /// n'est pas rattrapable : continuer laisserait un processus vivant sans interface.
    /// </summary>
    private bool _startingUp = true;

    /// <summary>
    /// Vrai pendant le contrôle de démarrage. Une boîte de dialogue y serait fatale : personne
    /// ne la ferme sur un agent d'intégration, et le processus attend jusqu'au délai du
    /// workflow — deux minutes de silence, puis « l'application ne rend pas la main », sans que
    /// rien n'indique la cause. Les erreurs partent sur la sortie d'erreur à la place.
    /// </summary>
    private static bool _selfTesting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherException;

        // Le fil de travail de HotkeyDispatcher ne passe pas par le répartiteur WPF :
        // sans cet abonnement, une exception qui s'en échapperait serait invisible.
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        if (e.Args.Any(a => string.Equals(a, SelfTestArgument, StringComparison.OrdinalIgnoreCase)))
        {
            _selfTesting = true;
            int code = RunSelfTest();

            // Les flux redirigés sont tamponnés : un processus tué avant la purge n'écrit rien
            // du tout, ce qui laisse un échec sans la moindre explication dans le journal.
            Console.Out.Flush();
            Console.Error.Flush();

            Shutdown(code);
            return;
        }

        _startingUp = false;

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Construit la fenêtre principale sans l'afficher, pour vérifier que l'application
    /// démarre réellement. Compiler ne prouve rien : c'est au chargement du XAML et à
    /// l'initialisation de WPF que se jouent les erreurs de configuration du runtime.
    /// Aucun hook n'est posé, donc le contrôle tourne aussi sur un agent d'intégration.
    /// </summary>
    private static int RunSelfTest()
    {
        string profile = Path.Combine(Path.GetTempPath(), $"dofus-organizer-selftest-{Guid.NewGuid():N}.json");

        MainWindow? window = null;

        try
        {
            window = new MainWindow(profile);
            ExerciseEditor((MainViewModel)window.DataContext);
            ExerciseLayout(window);

            Console.WriteLine("Contrôle de démarrage : fenêtre construite, éditeur parcouru et mise en page calculée sans erreur.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Contrôle de démarrage en échec :");
            Console.Error.WriteLine(Describe(ex));
            return 1;
        }
        finally
        {
            // Dans un finally, et non après le parcours : une vérification qui échoue laissait
            // sinon la fenêtre et son service en vie.
            try { window?.ReleaseResources(); } catch (Exception ex) { Console.Error.WriteLine(Describe(ex)); }
            try { File.Delete(profile); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Parcourt les opérations d'édition les plus courantes. Volontairement limité aux
    /// commandes qui n'ouvrent aucune boîte de dialogue : une confirmation resterait
    /// sans réponse sur un agent d'intégration et bloquerait la vérification.
    /// </summary>
    private static void ExerciseEditor(MainViewModel model)
    {
        model.AddMacroCommand.Execute(null);

        foreach (var kind in Choices.StepKinds)
        {
            model.NewStepKind = kind.Value;
            model.AddStepCommand.Execute(null);
        }

        // Le prédicat des boutons « Pointer » : une étape de clic doit être reconnue comme
        // étape de position. Ne prouve pas la notification — appeler CanExecute directement la
        // contourne — mais verrouille au moins la condition.
        model.NewStepKind = StepKind.Clic;
        model.AddStepCommand.Execute(null);
        if (!model.PointStepCommand.CanExecute(null))
        {
            throw new InvalidOperationException(
                "« Pointer » reste inaccessible alors qu'une étape de clic est sélectionnée.");
        }

        model.MoveStepUpCommand.Execute(null);
        model.MoveStepDownCommand.Execute(null);
        model.RemoveStepCommand.Execute(null);

        ExerciseLoopEditing(model);

        // Ajout puis suppression d'une touche lente : le panneau se lie à la sélection, et une
        // liste vidée est précisément le cas où une liaison mal écrite se voit.
        model.AddSlowKeyCommand.Execute(null);
        model.DeleteSlowKeyCommand.Execute(null);

        // Les rétablissements touchent à un objet lié à une douzaine de champs : c'est le
        // genre de commande dont une liaison mal écrite ne se voit qu'au clic.
        // La répartition du coffre : la désignation ne peut pas être simulée sans souris, mais
        // le reste du chemin — construire, effacer — se lie à une liste et à des commandes.
        model.ClearChestPointsCommand.Execute(null);
        model.BuildChestMacroCommand.Execute(null);

        model.RestoreDefaultDelaysCommand.Execute(null);
        model.RestoreDefaultDetectionCommand.Execute(null);

        model.RefreshCommand.Execute(null);
        model.SaveCommand.Execute(null);
    }

    /// <summary>
    /// Applique les gabarits et calcule une mise en page complète, onglet par onglet.
    ///
    /// Construire la fenêtre ne fait que charger le XAML : une ressource mal typée dans un
    /// ControlTemplate, ou un gabarit auquel il manque une pièce, ne se révèle qu'au moment
    /// où le gabarit est appliqué — c'est-à-dire à la première mesure. Et le contenu d'un
    /// onglet non sélectionné n'est jamais réalisé tant qu'on ne l'a pas montré, d'où le
    /// passage par chacun.
    /// </summary>
    /// <summary>
    /// Éditer à l'intérieur d'une boucle : ajouter, sélectionner, déplacer.
    ///
    /// Ce chemin était mort. Les sous-étapes étaient dessinées par une liste imbriquée en
    /// lecture seule, donc jamais sélectionnées — et tout ce que produit la répartition du
    /// coffre vit dans une boucle. Le modèle savait pourtant les traiter depuis toujours ;
    /// personne ne pouvait le lui demander.
    /// </summary>
    private static void ExerciseLoopEditing(MainViewModel model)
    {
        var editor = model.SelectedMacro;
        if (editor?.FindLoop() is not { } loop) return;

        editor.SelectedStep = loop;

        model.NewStepKind = StepKind.Attente;
        model.AddStepCommand.Execute(null);

        model.NewStepKind = StepKind.Touche;
        model.AddStepCommand.Execute(null);

        if (!editor.Rows.Any(row => row.Depth > 0))
        {
            throw new InvalidOperationException(
                "Les sous-étapes d'une boucle n'apparaissent pas dans la liste de l'éditeur.");
        }

        if (editor.SelectedStep is null || !loop.Steps.Contains(editor.SelectedStep))
        {
            throw new InvalidOperationException(
                "Une étape ajoutée dans une boucle n'y est pas sélectionnée.");
        }

        // Déplacer à l'intérieur de la boucle, puis par le même chemin que le glisser-déposer.
        model.MoveStepUpCommand.Execute(null);
        editor.Reorder(editor.Rows.Count - 1, editor.Rows.Count - 2);
    }

    private static void ExerciseLayout(MainWindow window)
    {
        if (window.Content is not FrameworkElement content) return;

        var tabs = FindTabControl(content);
        int tabCount = tabs?.Items.Count ?? 0;
        var overflowing = new List<string>();

        for (int i = 0; i < Math.Max(tabCount, 1); i++)
        {
            if (tabs is not null && i < tabCount) tabs.SelectedIndex = i;

            content.Measure(new Size(window.Width, window.Height));
            content.Arrange(new Rect(0, 0, window.Width, window.Height));
            content.UpdateLayout();

            if (tabs is not null && i < tabCount) overflowing.AddRange(Overflow(tabs.Items[i]));
        }

        if (tabs is not null && tabCount > 0) tabs.SelectedIndex = 0;

        // Tous les onglets sont mesurés avant de conclure : s'arrêter au premier fautif
        // demanderait autant d'allers-retours d'intégration continue que d'onglets.
        if (overflowing.Count > 0) throw new InvalidOperationException(string.Join(" ", overflowing));
    }

    /// <summary>Marge tolérée entre ce qu'un onglet demande et ce qu'il obtient, en pixels.</summary>
    private const double OverflowToleranceDip = 8;

    /// <summary>
    /// Signale un onglet qui réclame plus de hauteur qu'il n'en reçoit.
    ///
    /// Le contenu qui déborde est coupé sans un mot : ni exception, ni trace, ni ascenseur.
    /// C'est ce qui a rendu le bouton « Construire la macro » introuvable — présent, lié,
    /// fonctionnel, et cent trente pixels sous le bord de la fenêtre. Un défaut invisible à la
    /// compilation comme aux tests, et que seul l'œil attrape.
    ///
    /// Un onglet enveloppé dans un ScrollViewer est hors de cause : déborder est justement ce
    /// qu'il sait faire.
    /// </summary>
    private static IEnumerable<string> Overflow(object tab)
    {
        if (tab is not TabItem { Content: FrameworkElement panel } item) yield break;

        double wanted = panel.DesiredSize.Height;
        double given = panel.ActualHeight;

        // La mesure de chaque onglet part au journal : quand la vérification tombe, savoir sur
        // lequel et de combien évite un aller-retour d'intégration continue.
        Console.WriteLine($"  onglet « {item.Header} » : {wanted:0} px voulus, {given:0} px reçus"
            + (panel is ScrollViewer ? ", avec ascenseur" : ""));

        if (panel is ScrollViewer || given <= 0 || wanted <= given + OverflowToleranceDip) yield break;

        yield return $"L'onglet « {item.Header} » demande {wanted:0} px de haut pour {given:0} px "
            + "disponibles : le bas est coupé sans moyen d'y accéder. Enveloppez-le dans un "
            + "ScrollViewer, ou allégez son contenu.";
    }

    /// <summary>
    /// Parcourt l'arbre logique — le visuel n'existe pas encore avant la première mesure.
    /// </summary>
    private static TabControl? FindTabControl(DependencyObject root)
    {
        if (root is TabControl found) return found;

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject node && FindTabControl(node) is { } match) return match;
        }

        return null;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Report(e.Exception);

        // Une fois l'interface debout, une erreur isolée ne doit pas fermer l'application ;
        // avant, il n'y a rien à sauver.
        if (_startingUp) return;
        e.Handled = true;
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception) Report(exception);
    }

    private static void Report(Exception exception)
    {
        string detail = Describe(exception);

        if (_selfTesting)
        {
            Console.Error.WriteLine("Erreur remontée pendant le contrôle de démarrage :");
            Console.Error.WriteLine(detail);
            Console.Error.Flush();
            return;
        }

        string? logPath = TryWriteLog(detail);

        string message = new StringBuilder()
            .AppendLine("Une erreur est survenue :")
            .AppendLine()
            .AppendLine(Summarize(exception))
            .AppendLine()
            .AppendLine(logPath is null
                ? "Le détail n'a pas pu être écrit sur le disque."
                : $"Le détail complet a été enregistré dans :\n{logPath}")
            .ToString();

        MessageBox.Show(message, "Dofus Organizer", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    /// <summary>
    /// Résumé court : le type et le message de chaque niveau de la chaîne.
    /// N'afficher que l'exception extérieure ne dit rien — une construction par réflexion
    /// qui échoue rapporte toujours « Exception has been thrown by the target of an
    /// invocation », et la cause réelle se trouve un ou deux niveaux plus bas.
    /// </summary>
    private static string Summarize(Exception exception)
    {
        var lines = new StringBuilder();
        for (var current = exception; current is not null; current = current.InnerException)
        {
            lines.AppendLine($"{current.GetType().Name} : {current.Message}");
        }
        return lines.ToString().TrimEnd();
    }

    /// <summary>Chaîne complète, piles d'appels comprises, destinée au journal.</summary>
    private static string Describe(Exception exception)
    {
        var text = new StringBuilder();
        int level = 0;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (level > 0) text.AppendLine().AppendLine($"--- Cause interne {level} ---");
            text.AppendLine($"{current.GetType().FullName} : {current.Message}");
            if (!string.IsNullOrWhiteSpace(current.StackTrace)) text.AppendLine(current.StackTrace);
            level++;
        }

        return text.ToString();
    }

    private static string? TryWriteLog(string detail)
    {
        try
        {
            string? directory = Path.GetDirectoryName(CrashLogPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var entry = new StringBuilder()
                .AppendLine(new string('=', 70))
                .AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} — Dofus Organizer")
                .AppendLine(new string('=', 70))
                .Append(detail)
                .AppendLine()
                .ToString();

            File.AppendAllText(CrashLogPath, entry);
            return CrashLogPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
