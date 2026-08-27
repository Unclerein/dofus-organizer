using DofusOrganizer.Core.Models;

namespace DofusOrganizer.App.ViewModels;

/// <summary>Une entrée de liste déroulante : la valeur réelle, et son libellé lisible.</summary>
public sealed record Choice<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Contenu des listes déroulantes de l'éditeur. Les énumérations ne sont pas liées
/// directement : leurs noms de membres s'afficheraient tels quels à l'écran.
/// </summary>
public static class Choices
{
    public static IReadOnlyList<Choice<MouseButton>> MouseButtons { get; } =
    [
        new(MouseButton.Left, "Clic gauche"),
        new(MouseButton.Right, "Clic droit"),
        new(MouseButton.Middle, "Clic milieu"),
    ];

    public static IReadOnlyList<Choice<KeyAction>> KeyActions { get; } =
    [
        new(KeyAction.Press, "Appui complet"),
        new(KeyAction.Down, "Enfoncer"),
        new(KeyAction.Up, "Relâcher"),
    ];

    public static IReadOnlyList<Choice<FocusTarget>> FocusTargets { get; } =
    [
        new(FocusTarget.Slot, "Un personnage précis"),
        new(FocusTarget.Next, "Le suivant"),
        new(FocusTarget.Previous, "Le précédent"),
        new(FocusTarget.First, "Le premier"),
        new(FocusTarget.Initial, "La fenêtre de départ"),
    ];

    public static IReadOnlyList<Choice<StepKind>> StepKinds { get; } =
    [
        new(StepKind.Clic, "Clic"),
        new(StepKind.Glisser, "Glisser-déposer"),
        new(StepKind.Deplacement, "Déplacer la souris"),
        new(StepKind.Touche, "Touche"),
        new(StepKind.Attente, "Attente"),
        new(StepKind.Molette, "Molette"),
        new(StepKind.Focus, "Changer de fenêtre"),
        new(StepKind.PourChaquePersonnage, "Pour chaque personnage"),
        new(StepKind.Repartition, "Répartir la quantité"),
    ];

    public static IReadOnlyList<Choice<ScrollDirection>> ScrollDirections { get; } =
    [
        new(ScrollDirection.Down, "Vers le bas"),
        new(ScrollDirection.Up, "Vers le haut"),
    ];

    public static IReadOnlyList<Choice<int>> Keys { get; } =
        VirtualKeys.CommonKeys().Select(vk => new Choice<int>(vk, VirtualKeys.GetName(vk))).ToList();
}
