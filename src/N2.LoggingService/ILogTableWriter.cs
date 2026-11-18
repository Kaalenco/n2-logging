namespace N2.LoggingService;

/// <summary>Writes log records to the log table. Abstraction over Azure Table Storage.</summary>
public interface ILogTableWriter
{
    /// <summary>Writes the records. Throws when the table service cannot be reached.</summary>
    /// <returns>The number of records written.</returns>
    Task<int> Write(IReadOnlyList<LogRecord> records, CancellationToken cancellationToken);
}
