using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace DofusOrganizer.App.Views;

/// <summary>
/// Le trait qui dit où la ligne déplacée va se poser, tracé sur un bord de la ligne survolée.
///
/// Un <see cref="Adorner"/> et non un élément ajouté à l'arbre : la couche d'ornement se dessine
/// par-dessus sans participer à la mise en page, donc rien ne bouge sous le curseur au moment où
/// l'utilisateur vise.
/// </summary>
internal sealed class InsertionLine : Adorner
{
    private const double Thickness = 2;

    /// <summary>Longueur des pattes verticales aux extrémités, qui rendent le trait lisible sur un fond chargé.</summary>
    private const double CapHeight = 3;

    private readonly Pen _pen;
    private readonly bool _below;

    internal InsertionLine(UIElement row, bool below) : base(row)
    {
        _below = below;
        IsHitTestVisible = false;

        // La couleur vient du thème : un trait en dur jurerait sur le parchemin.
        var brush = Application.Current?.TryFindResource("Wood") as Brush ?? Brushes.Black;
        _pen = new Pen(brush, Thickness);
        _pen.Freeze();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;
        double y = _below ? size.Height - Thickness / 2 : Thickness / 2;

        drawingContext.DrawLine(_pen, new Point(0, y), new Point(size.Width, y));
        drawingContext.DrawLine(_pen, new Point(Thickness / 2, y - CapHeight), new Point(Thickness / 2, y + CapHeight));
        drawingContext.DrawLine(_pen, new Point(size.Width - Thickness / 2, y - CapHeight),
                                       new Point(size.Width - Thickness / 2, y + CapHeight));
    }
}
