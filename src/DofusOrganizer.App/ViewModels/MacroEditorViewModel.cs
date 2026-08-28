using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DofusOrganizer.Core.Macros;
using DofusOrganizer.Core.Models;

namespace DofusOrganizer.App.ViewModels;

/// <summary>
/// Édition d'une macro. La liste d'étapes du modèle est observable, donc liée
/// directement : recopier les étapes dans une collection parallèle serait une
/// occasion de plus de les désynchroniser de ce qui part à la sauvegarde.
/// </summary>
public sealed class MacroEditorViewModel : ObservableObject
{
    private MacroStep? _selectedStep;

    public MacroEditorViewModel(Macro macro)
    {
        Macro = macro;
        Steps.CollectionChanged += (_, _) => { Raise(nameof(StepCount)); RebuildRows(); };
        RebuildRows();
    }

    public Macro Macro { get; }

    public ObservableCollection<MacroStep> Steps => Macro.Steps;

    /// <summary>
    /// La macro à plat, une ligne par étape, sous-étapes comprises.
    ///
    /// L'éditeur ne montrait auparavant que les étapes de premier niveau : le contenu d'une
    /// boucle était dessiné par une liste imbriquée en lecture seule, donc impossible à
    /// sélectionner. Comme tout ce que produit la répartition du coffre vit dans une boucle,
    /// plus rien n'y était ni déplaçable, ni supprimable, ni insérable ailleurs qu'à la fin —
    /// alors que le modèle savait le faire depuis toujours.
    /// </summary>
    public ObservableCollection<OutlinedStep> Rows { get; } = [];

    public int StepCount => Steps.Count;

    public MacroStep? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (!Set(ref _selectedStep, value)) return;
            Raise(nameof(HasSelection));
            Raise(nameof(SelectedIndex));
            Raise(nameof(SelectedRow));
        }
    }

    /// <summary>
    /// La ligne de l'étape sélectionnée, pour la liste de l'interface.
    ///
    /// La sélection reste portée par l'étape et non par la ligne : les lignes sont reconstruites
    /// à chaque changement, et une sélection accrochée à l'une d'elles se perdrait au premier
    /// ajout.
    /// </summary>
    public OutlinedStep? SelectedRow
    {
        get
        {
            foreach (var row in Rows)
            {
                if (ReferenceEquals(row.Step, _selectedStep)) return row;
            }
            return null;
        }
        set => SelectedStep = value?.Step;
    }

    /// <summary>
    /// Reconstruit les lignes, et se réabonne aux boucles.
    ///
    /// Le réabonnement compte autant que la reconstruction : une étape ajoutée dans une boucle
    /// ne touche pas la liste de la macro, donc seule la collection de la boucle le signale.
    /// </summary>
    private void RebuildRows()
    {
        foreach (var loop in _watched) loop.Steps.CollectionChanged -= OnLoopChanged;
        _watched.Clear();

        foreach (var loop in Steps.OfType<ForEachCharacterStep>())
        {
            loop.Steps.CollectionChanged += OnLoopChanged;
            _watched.Add(loop);
        }

        Rows.Clear();
        foreach (var row in MacroOutline.Flatten(Steps)) Rows.Add(row);

        Raise(nameof(SelectedRow));
        Raise(nameof(SelectedIndex));
    }

    private void OnLoopChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildRows();

    private readonly List<ForEachCharacterStep> _watched = [];

    /// <summary>Déplace l'étape d'une ligne vers une autre, sans jamais franchir une boucle.</summary>
    public bool Reorder(int from, int to)
    {
        if (!MacroOutline.Reorder(Steps, from, to)) return false;

        RebuildRows();
        return true;
    }

    public bool HasSelection => _selectedStep is not null;

    /// <summary>Position de l'étape sélectionnée dans sa propre liste, ou -1.</summary>
    public int SelectedIndex => _selectedStep is null ? -1 : Steps.IndexOf(_selectedStep);

    /// <summary>
    /// Insère l'étape après celle qui est sélectionnée. Si la sélection est une boucle,
    /// l'étape est ajoutée *dans* la boucle : c'est ce qu'on veut en construisant un
    /// enchaînement à répéter sur chaque personnage.
    /// </summary>
    public void Add(MacroStep step)
    {
        // Une boucle sélectionnée accueille l'étape ; imbriquer une boucle dans une
        // autre n'aurait pas de sens, celle-là part toujours à la racine.
        if (_selectedStep is ForEachCharacterStep loop && step is not ForEachCharacterStep)
        {
            loop.Steps.Add(step);
            SelectedStep = step;
            return;
        }

        int index = SelectedIndex;
        if (index >= 0)
        {
            Steps.Insert(index + 1, step);
        }
        else if (_selectedStep is not null && TryInsertNextToSelection(step))
        {
            // L'étape sélectionnée vivait à l'intérieur d'une boucle : la nouvelle
            // se range à sa suite, et non à la racine de la macro.
        }
        else
        {
            Steps.Add(step);
        }

        SelectedStep = step;
    }

    private bool TryInsertNextToSelection(MacroStep step)
    {
        foreach (var loop in Steps.OfType<ForEachCharacterStep>())
        {
            int index = loop.Steps.IndexOf(_selectedStep!);
            if (index < 0) continue;
            loop.Steps.Insert(index + 1, step);
            return true;
        }
        return false;
    }

    public void RemoveSelected()
    {
        if (_selectedStep is null) return;
        int index = Steps.IndexOf(_selectedStep);

        // L'étape sélectionnée peut vivre à l'intérieur d'une boucle et non à la racine.
        if (index < 0)
        {
            foreach (var loop in Steps.OfType<ForEachCharacterStep>())
            {
                if (loop.Steps.Remove(_selectedStep)) { SelectedStep = loop; return; }
            }
            return;
        }

        Steps.Remove(_selectedStep);
        SelectedStep = Steps.Count == 0 ? null : Steps[Math.Min(index, Steps.Count - 1)];
    }

    public void MoveSelected(int delta)
    {
        if (_selectedStep is null) return;

        int from = Steps.IndexOf(_selectedStep);
        if (from < 0)
        {
            MoveInsideLoop(delta);
            return;
        }

        int to = from + delta;
        if (to < 0 || to >= Steps.Count) return;
        Steps.Move(from, to);
        Raise(nameof(SelectedIndex));
    }

    private void MoveInsideLoop(int delta)
    {
        foreach (var loop in Steps.OfType<ForEachCharacterStep>())
        {
            int from = loop.Steps.IndexOf(_selectedStep!);
            if (from < 0) continue;
            int to = from + delta;
            if (to < 0 || to >= loop.Steps.Count) return;
            loop.Steps.Move(from, to);
            return;
        }
    }

    /// <summary>Remplace toutes les étapes, à l'issue d'un enregistrement.</summary>
    public void Replace(IEnumerable<MacroStep> steps)
    {
        Steps.Clear();
        foreach (var step in steps) Steps.Add(step);
        RebuildRows();
        SelectedStep = Steps.FirstOrDefault();
    }

    /// <summary>La première boucle « pour chaque personnage » de la macro, s'il y en a une.</summary>
    public ForEachCharacterStep? FindLoop() => Steps.OfType<ForEachCharacterStep>().FirstOrDefault();
}
