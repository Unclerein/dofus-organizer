using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DofusOrganizer.App.Views;

/// <summary>
/// Réordonner une liste en faisant glisser ses lignes, en plus des boutons Monter et Descendre.
///
/// Posé sur n'importe quel <see cref="ItemsControl"/> — la liste d'étapes comme le tableau des
/// personnages — parce que la seule chose dont il a besoin est de savoir traduire un point de
/// l'écran en position dans la liste, ce que le générateur de conteneurs fait pour les deux.
///
/// Le déplacement lui-même n'est pas décidé ici : le comportement se contente de dire « de
/// cette ligne vers celle-là » à une commande, qui reste libre de refuser.
/// </summary>
public static class DragReorder
{
    /// <summary>Ce que le comportement demande : déplacer la ligne <paramref name="From"/> à la place de <paramref name="To"/>.</summary>
    public readonly record struct Move(int From, int To);

    /// <summary>Format d'échange privé : rien de ce qui est glissé ici n'a de sens ailleurs.</summary>
    private const string Format = "DofusOrganizer.Reorder";

    private static Point _origin;
    private static int _from = -1;

    /// <summary>Le trait d'insertion affiché, et la couche qui le porte.</summary>
    private static InsertionLine? _line;
    private static AdornerLayer? _layer;

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DragReorder), new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items) return;

        items.PreviewMouseLeftButtonDown -= OnPress;
        items.PreviewMouseMove -= OnMove;
        items.DragOver -= OnDragOver;
        items.DragLeave -= OnDragLeave;
        items.Drop -= OnDrop;

        if (e.NewValue is null) return;

        items.AllowDrop = true;
        items.PreviewMouseLeftButtonDown += OnPress;
        items.PreviewMouseMove += OnMove;
        items.DragOver += OnDragOver;
        items.DragLeave += OnDragLeave;
        items.Drop += OnDrop;
    }

    private static void OnPress(object sender, MouseButtonEventArgs e)
    {
        _from = -1;
        if (sender is not ItemsControl items) return;

        // Un appui destiné à un champ de saisie ou à une case à cocher n'est pas un début de
        // glisser : sélectionner du texte dans le nom d'un personnage, ou décocher « Actif »,
        // demande de bouger la souris bouton enfoncé, exactement comme un glisser.
        if (IsInteractive(e.OriginalSource as DependencyObject)) return;

        _origin = e.GetPosition(items);
        _from = IndexAt(items, _origin);
    }

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (_from < 0 || e.LeftButton != MouseButtonState.Pressed) return;
        if (sender is not ItemsControl items) return;

        // Le seuil du système, et non une valeur choisie : c'est lui qui sépare un clic d'un
        // glisser partout ailleurs sous Windows.
        var moved = e.GetPosition(items) - _origin;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        int from = _from;
        _from = -1;

        // Appel bloquant : il ne rend la main qu'une fois le glisser terminé, abandon par Échap
        // compris. C'est donc le seul endroit sûr pour effacer le trait — une annulation ou un
        // lâcher hors du contrôle ne passent par aucun autre gestionnaire.
        try
        {
            DragDrop.DoDragDrop(items, new DataObject(Format, from), DragDropEffects.Move);
        }
        finally
        {
            HideLine();
        }
    }

    /// <summary>
    /// Déplace le trait sous le curseur, ou l'efface si le dépôt serait refusé.
    ///
    /// De quel côté : le réordonnancement retire puis réinsère à l'index de la cible, ce qui
    /// fait passer l'élément <b>après</b> elle quand on descend et <b>avant</b> quand on monte.
    /// Le trait ne fait que dire cette vérité-là.
    ///
    /// La légalité est demandée à la commande elle-même, qui sait déjà refuser un dépôt
    /// franchissant la frontière d'une boucle. L'interroger pendant le survol évite que
    /// l'utilisateur ne l'apprenne qu'en lâchant, devant une liste qui ne bouge pas.
    /// </summary>
    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl items || !e.Data.GetDataPresent(Format)) return;

        AutoScroll(items, e.GetPosition(items));
        HideLine();
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        int target = IndexAt(items, e.GetPosition(items));
        if (target < 0 || e.Data.GetData(Format) is not int source) return;

        var move = new Move(source, target);
        if (GetCommand(items) is not { } command || !command.CanExecute(move)) return;

        e.Effects = DragDropEffects.Move;

        if (items.ItemContainerGenerator.ContainerFromIndex(target) is not UIElement row) return;

        _layer = AdornerLayer.GetAdornerLayer(row);
        if (_layer is null) return;

        _line = new InsertionLine(row, below: target > source);
        _layer.Add(_line);
    }

    private static void OnDragLeave(object sender, DragEventArgs e) => HideLine();

    /// <summary>Bande, en pixels, où l'approche du bord déclenche le défilement.</summary>
    private const double ScrollMargin = 28;

    /// <summary>
    /// Fait défiler la liste quand le curseur approche d'un bord, pendant un glisser.
    ///
    /// La molette ne peut pas servir ici : pendant un glisser, Windows capte l'entrée et ne
    /// route pas ses événements vers la cible. Le défilement de bord est le mécanisme qu'emploie
    /// tout gestionnaire de fichiers pour la même raison — et sans lui, une liste plus haute que
    /// sa fenêtre interdit de déplacer une ligne au-delà de ce qui est visible.
    /// </summary>
    private static void AutoScroll(ItemsControl items, Point point)
    {
        if (FindScrollViewer(items) is not { } scroller) return;

        if (point.Y < ScrollMargin) scroller.LineUp();
        else if (point.Y > items.ActualHeight - ScrollMargin) scroller.LineDown();
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer found) return found;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } match) return match;
        }

        return null;
    }

    private static void HideLine()
    {
        if (_line is not null) _layer?.Remove(_line);
        _line = null;
        _layer = null;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl items || !e.Data.GetDataPresent(Format)) return;

        HideLine();

        int to = IndexAt(items, e.GetPosition(items));
        if (to < 0 || e.Data.GetData(Format) is not int from) return;

        var move = new Move(from, to);
        if (GetCommand(items) is { } command && command.CanExecute(move)) command.Execute(move);

        e.Handled = true;
    }

    /// <summary>Position dans la liste de la ligne située sous un point, ou -1.</summary>
    private static int IndexAt(ItemsControl items, Point point)
    {
        if (items.InputHitTest(point) is not DependencyObject hit) return -1;

        for (var node = hit; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            int index = items.ItemContainerGenerator.IndexFromContainer(node);
            if (index >= 0) return index;
        }

        return -1;
    }

    private static bool IsInteractive(DependencyObject? source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBoxBase or ToggleButton or ButtonBase or ComboBox) return true;
        }

        return false;
    }
}
