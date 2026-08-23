using System.Collections.ObjectModel;
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
        Steps.CollectionChanged += (_, _) => Raise(nameof(StepCount));
    }

    public Macro Macro { get; }

    public ObservableCollection<MacroStep> Steps => Macro.Steps;

    public int StepCount => Steps.Count;

    public MacroStep? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (!Set(ref _selectedStep, value)) return;
            Raise(nameof(HasSelection));
            Raise(nameof(SelectedIndex));
        }
    }

    public bool HasSelection => _selectedStep is not null;

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
        SelectedStep = Steps.FirstOrDefault();
    }

    /// <summary>La première boucle « pour chaque personnage » de la macro, s'il y en a une.</summary>
    public ForEachCharacterStep? FindLoop() => Steps.OfType<ForEachCharacterStep>().FirstOrDefault();
}
