namespace DofusOrganizer.Core.Abstractions;

/// <summary>Journal affiché dans la barre d'état de l'application.</summary>
public interface ILogSink
{
    void Log(string message);
}

public sealed class NullLogSink : ILogSink
{
    public static readonly NullLogSink Instance = new();
    public void Log(string message) { }
}
