using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DofusOrganizer.Core.Models;

/// <summary>
/// Base INotifyPropertyChanged partagée par les modèles.
/// Les modèles sont liés directement aux contrôles WPF : les avoir observables ici
/// évite toute une couche de wrappers dans l'application, sans gêner la sérialisation
/// (System.Text.Json ignore les événements).
/// </summary>
public abstract class NotifyBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    protected void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
