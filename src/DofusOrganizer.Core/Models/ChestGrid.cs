using System.Text.Json.Serialization;
using DofusOrganizer.Core.Geometry;

namespace DofusOrganizer.Core.Models;

/// <summary>
/// Le calibrage de la grille du coffre, tel qu'il est rangé dans le profil.
///
/// Des nombres simples plutôt que la <see cref="CellGrid"/> elle-même : une structure à
/// constructeur paramétré se sérialise, mais rien dans ce projet ne l'a jamais vérifié, et un
/// profil qui ne se relit pas est la panne la plus coûteuse qui soit — elle emporte macros et
/// raccourcis. Les propriétés nommées, elles, se relisent sans surprise.
///
/// Le calibrage ne dépend que de la disposition du panneau à l'écran : il survit donc aux
/// redémarrages et ne se refait qu'après un changement de banque ou de taille de fenêtre.
/// </summary>
public sealed class ChestGrid : NotifyBase
{
    private double _topLeftX;
    private double _topLeftY;
    private double _bottomRightX;
    private double _bottomRightY;
    private int _rows = 9;
    private int _columns = 5;

    /// <summary>Position horizontale du centre de la case en haut à gauche, en fraction de la fenêtre.</summary>
    public double TopLeftX { get => _topLeftX; set => Set(ref _topLeftX, value); }

    public double TopLeftY { get => _topLeftY; set => Set(ref _topLeftY, value); }

    public double BottomRightX { get => _bottomRightX; set => Set(ref _bottomRightX, value); }

    public double BottomRightY { get => _bottomRightY; set => Set(ref _bottomRightY, value); }

    /// <summary>Lignes de la grille du coffre. Neuf sur un coffre de guilde ordinaire.</summary>
    public int Rows { get => _rows; set => Set(ref _rows, Math.Clamp(value, 1, 50)); }

    /// <summary>Colonnes de la grille. Cinq sur un coffre de guilde ordinaire.</summary>
    public int Columns { get => _columns; set => Set(ref _columns, Math.Clamp(value, 1, 50)); }

    [JsonIgnore]
    public CellGrid Grid => new(
        new NormalizedPoint(TopLeftX, TopLeftY),
        new NormalizedPoint(BottomRightX, BottomRightY),
        Rows,
        Columns);

    /// <summary>Vrai une fois les deux coins relevés, donc quand la grille sait où sont ses cases.</summary>
    [JsonIgnore]
    public bool IsCalibrated => Grid.IsUsable;

    public void Calibrate(NormalizedPoint topLeft, NormalizedPoint bottomRight)
    {
        TopLeftX = topLeft.Fx;
        TopLeftY = topLeft.Fy;
        BottomRightX = bottomRight.Fx;
        BottomRightY = bottomRight.Fy;
        Raise(nameof(Grid));
        Raise(nameof(IsCalibrated));
    }

    public void Forget()
    {
        TopLeftX = TopLeftY = BottomRightX = BottomRightY = 0;
        Raise(nameof(Grid));
        Raise(nameof(IsCalibrated));
    }
}
