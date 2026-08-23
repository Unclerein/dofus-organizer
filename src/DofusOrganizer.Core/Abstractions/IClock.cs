namespace DofusOrganizer.Core.Abstractions;

/// <summary>Attente injectable, pour que les tests de macro s'exécutent instantanément.</summary>
public interface IClock
{
    Task DelayAsync(int milliseconds, CancellationToken cancellationToken);
}

public sealed class SystemClock : IClock
{
    public Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
        => milliseconds <= 0 ? Task.CompletedTask : Task.Delay(milliseconds, cancellationToken);
}
