using DofusOrganizer.Core.Models;

namespace DofusOrganizer.Core.Macros;

/// <summary>
/// Transforme une séquence capturée sur le personnage meneur en macro rejouable sur les autres.
///
/// Cette logique vit ici plutôt que dans l'application parce qu'elle s'est déjà trompée en
/// silence : l'enregistreur place en tête une étape « aller sur le personnage N » — utile pour
/// une macro ordinaire, désastreuse une fois enfermée dans une boucle par personnage, où elle
/// ramenait chaque tour sur le meneur.
/// </summary>
public static class TeamReplay
{
    public const string MacroName = "Refaire sur l'équipe (dernière capture)";

    /// <summary>
    /// Enveloppe les étapes dans une boucle qui saute le personnage au premier plan — le
    /// meneur, qui vient de faire l'action lui-même.
    /// </summary>
    public static Macro BuildMacro(IReadOnlyList<MacroStep> captured)
    {
        var loop = new ForEachCharacterStep { SkipCurrentWindow = true };
        foreach (var step in Sanitize(captured)) loop.Steps.Add(step);

        return new Macro
        {
            Name = MacroName,
            RestoreInitialWindow = true,
            RestoreCursorPosition = true,
            Steps = { loop },
        };
    }

    /// <summary>
    /// Écarte ce qui n'a pas de sens à l'intérieur d'une boucle par personnage : tout
    /// changement de fenêtre, puisque c'est la boucle qui décide de qui agit, et toute
    /// boucle imbriquée.
    /// </summary>
    public static IEnumerable<MacroStep> Sanitize(IReadOnlyList<MacroStep> captured)
        => captured.Where(step => step is not FocusStep and not ForEachCharacterStep);

    /// <summary>Vrai si la séquence contient au moins une action à rejouer.</summary>
    public static bool HasReplayableSteps(IReadOnlyList<MacroStep> captured) => Sanitize(captured).Any();
}
