using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(DragReorder), new PropertyMetadata(null, OnCommandChanged));

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ItemsControl items) return;

        items.PreviewMouseLeftButtonDown -= OnPress;
        items.PreviewMouseMove -= OnMove;
        items.Drop -= OnDrop;

        if (e.NewValue is null) return;

        items.AllowDrop = true;
        items.PreviewMouseLeftButtonDown += OnPress;
        items.PreviewMouseMove += OnMove;
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
        DragDrop.DoDragDrop(items, new DataObject(Format, from), DragDropEffects.Move);
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not ItemsControl items || !e.Data.GetDataPresent(Format)) return;

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
