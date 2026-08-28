namespace DofusOrganizer.Core.Geometry;

/// <summary>Une case de la grille, repérée par sa ligne et sa colonne, toutes deux à partir de zéro.</summary>
public readonly record struct Cell(int Row, int Column);

/// <summary>
/// Une grille régulière de cases, décrite par les centres de ses deux cases extrêmes.
///
/// Sert à désigner les items d'un coffre sans les cliquer un par un : deux points et les
/// dimensions de la grille la déterminent exactement, sans rien supposer — ni que les cases
/// soient carrées, ni que les espacements soient égaux dans les deux sens. C'est de la
/// géométrie, là où lire l'écran devinerait, et une case mal devinée ferait déplacer autre
/// chose sans que rien ne le signale.
///
/// Les positions sont en fraction de la zone client, comme partout ailleurs dans les macros :
/// la grille reste juste si la fenêtre est redimensionnée après le calibrage.
/// </summary>
/// <param name="TopLeft">Centre de la case en haut à gauche.</param>
/// <param name="BottomRight">Centre de la case en bas à droite.</param>
public readonly record struct CellGrid(NormalizedPoint TopLeft, NormalizedPoint BottomRight, int Rows, int Columns)
{
    /// <summary>
    /// Tolérance autour de la grille, en pas de case : un clic manqué d'un demi-pas appartient
    /// encore à la case la plus proche, au-delà il est hors grille.
    /// </summary>
    private const double Slack = 0.5;

    /// <summary>Vrai si la grille a de quoi être calculée : au moins une case, et deux points distincts.</summary>
    public bool IsUsable => Rows > 0 && Columns > 0
        && (Rows == 1 || Math.Abs(BottomRight.Fy - TopLeft.Fy) > double.Epsilon)
        && (Columns == 1 || Math.Abs(BottomRight.Fx - TopLeft.Fx) > double.Epsilon);

    /// <summary>Pas horizontal entre deux centres de cases voisines. Zéro si la grille n'a qu'une colonne.</summary>
    public double StepX => Columns > 1 ? (BottomRight.Fx - TopLeft.Fx) / (Columns - 1) : 0;

    /// <summary>Pas vertical entre deux centres de cases voisines. Zéro si la grille n'a qu'une ligne.</summary>
    public double StepY => Rows > 1 ? (BottomRight.Fy - TopLeft.Fy) / (Rows - 1) : 0;

    public NormalizedPoint CenterOf(Cell cell)
        => new(TopLeft.Fx + cell.Column * StepX, TopLeft.Fy + cell.Row * StepY);

    /// <summary>
    /// La case qui contient un point, ou null s'il tombe trop loin de la grille.
    ///
    /// Ramener un clic au centre de sa case n'est pas cosmétique : un clic près d'un bord
    /// dérivait jusqu'ici tel quel dans la macro, et le glisser partait de travers sur les
    /// personnages dont la fenêtre n'est pas exactement de la même taille.
    /// </summary>
    public Cell? Locate(NormalizedPoint point)
    {
        if (!IsUsable) return null;

        double column = Columns > 1 ? (point.Fx - TopLeft.Fx) / StepX : 0;
        double row = Rows > 1 ? (point.Fy - TopLeft.Fy) / StepY : 0;

        if (column < -Slack || column > Columns - 1 + Slack) return null;
        if (row < -Slack || row > Rows - 1 + Slack) return null;

        return new Cell((int)Math.Round(row), (int)Math.Round(column));
    }

    /// <summary>Le centre de la case d'un point, ou le point tel quel s'il est hors grille.</summary>
    public NormalizedPoint Snap(NormalizedPoint point) => Locate(point) is { } cell ? CenterOf(cell) : point;

    /// <summary>
    /// Toutes les cases entre deux points, dans l'ordre de lecture : jusqu'au bout de la ligne,
    /// puis la suivante depuis son début.
    ///
    /// L'ordre des deux clics ne compte pas — désigner de la fin vers le début rend la même
    /// plage. Personne ne devrait avoir à se souvenir dans quel sens cliquer.
    ///
    /// Rend une liste vide si l'un des points tombe hors de la grille, plutôt que d'inventer un
    /// bout de plage : mieux vaut ne rien désigner que désigner à côté.
    /// </summary>
    public IReadOnlyList<NormalizedPoint> Range(NormalizedPoint from, NormalizedPoint to)
    {
        if (Locate(from) is not { } first || Locate(to) is not { } last) return [];

        int start = first.Row * Columns + first.Column;
        int end = last.Row * Columns + last.Column;
        if (start > end) (start, end) = (end, start);

        var points = new List<NormalizedPoint>(end - start + 1);
        for (int index = start; index <= end; index++)
        {
            points.Add(CenterOf(new Cell(index / Columns, index % Columns)));
        }

        return points;
    }
}
