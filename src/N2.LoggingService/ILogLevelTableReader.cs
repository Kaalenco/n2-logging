namespace N2.LoggingService;

/// <summary>Reads the level rows of one application. Abstraction over Azure Table Storage.</summary>
public interface ILogLevelTableReader
{
    /// <summary>Reads all level rows for the application. Throws when the table cannot be read.</summary>
    Task<IReadOnlyList<LogLevelEntry>> Read(string appName, CancellationToken cancellationToken);
}
