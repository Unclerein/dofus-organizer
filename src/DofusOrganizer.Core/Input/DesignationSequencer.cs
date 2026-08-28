namespace DofusOrganizer.Core.Input;

/// <summary>Sort réservé à un clic pendant une désignation.</summary>
public enum DesignationVerdict
{
    /// <summary>Laisser passer : le jeu doit le recevoir.</summary>
    LetThrough,

    /// <summary>Avaler, sans plus.</summary>
    Swallow,

    /// <summary>Avaler et retenir le point désigné.</summary>
    Take,

    /// <summary>
    /// Avaler, et défaire ce qui va avec la désignation : le séquenceur s'est déjà refermé, il
    /// reste à retirer le hook.
    /// </summary>
    SwallowAndClose,
}

/// <summary>
/// Enchaînement des appuis et des relâchements pendant une désignation à la souris.
///
/// Séparé du hook parce que c'est là que se joue une subtilité d'ordre que rien ne rendait
/// visible : le demandeur d'une capture est réveillé dès l'appui, et peut donc en ouvrir une
/// autre <em>avant</em> que le bouton ne soit relâché. Le calibrage de la grille du coffre fait
/// exactement cela — deux points l'un après l'autre. Refermer aveuglément la désignation au
/// relâchement emportait cette seconde capture, et le clic suivant n'était vu de personne.
///
/// Une machine à états, donc, plutôt que trois booléens dispersés dans une méthode de hook :
/// ici l'ordre se met à l'épreuve dans un test, sans souris ni Windows.
/// </summary>
public sealed class DesignationSequencer
{
    /// <summary>Vrai quand l'appui en cours a été avalé, et que son relâchement doit l'être aussi.</summary>
    private bool _swallowedPress;

    public bool IsDesignating { get; private set; }

    /// <summary>Vrai pour une désignation qui se referme d'elle-même au premier point.</summary>
    public bool IsSingleShot { get; private set; }

    /// <summary>
    /// Ouvre une désignation, ou se greffe sur celle qui court déjà.
    ///
    /// Le mode d'une désignation déjà ouverte n'est pas touché : une capture à l'unité posée
    /// pendant qu'une désignation libre est en cours ne doit pas la refermer sous elle.
    /// </summary>
    public void Open(bool singleShot)
    {
        if (IsDesignating) return;

        _swallowedPress = false;
        IsDesignating = true;
        IsSingleShot = singleShot;
    }

    public void Close()
    {
        _swallowedPress = false;
        IsDesignating = false;
        IsSingleShot = false;
    }

    /// <param name="onTarget">
    /// Vrai quand le curseur est sur une fenêtre suivie dont on sait ramener les coordonnées.
    /// Ailleurs — sur l'organizer lui-même, notamment — le clic doit suivre sa route, sans quoi
    /// le bouton « Terminer » serait inatteignable.
    /// </param>
    public DesignationVerdict OnPress(bool onTarget)
    {
        if (!IsDesignating || !onTarget) return DesignationVerdict.LetThrough;

        _swallowedPress = true;
        return DesignationVerdict.Take;
    }

    /// <param name="captureAwaiting">
    /// Vrai lorsqu'une capture attend encore un point. Elle a pu être posée entre l'appui et le
    /// relâchement, l'appui ayant réveillé son demandeur : c'est le cas au calibrage, et c'est
    /// ce qui interdit de refermer ici.
    /// </param>
    public DesignationVerdict OnRelease(bool captureAwaiting)
    {
        if (!IsDesignating) return DesignationVerdict.LetThrough;

        // Le relâchement suit le sort de son appui. Avaler l'un sans l'autre laisserait le jeu
        // croire à un bouton resté enfoncé.
        bool swallow = _swallowedPress;
        _swallowedPress = false;
        if (!swallow) return DesignationVerdict.LetThrough;

        if (!IsSingleShot || captureAwaiting) return DesignationVerdict.Swallow;

        Close();
        return DesignationVerdict.SwallowAndClose;
    }
}
