# N2.LoggingService

Process logging to Azure Table Storage, with log levels you can change at runtime.
Version 3.x targets net10.0 and net11.0 and implements `N2.Core.ILogService`
(N2.Core.Abstractions 2.x).

## Installation

```bash
dotnet add package N2.LoggingService
```

## Registration

```csharp
using N2.LoggingService;

var builder = WebApplication.CreateBuilder(args);
var settings = TableLoggingSettings.Read(builder.Configuration);
var credential = new DefaultAzureCredential();   // any TokenCredential

// 1. Runtime levels: before the host is built.
builder.Configuration.AddN2LogLevelTable(settings, credential);

// 2. The sink: ILogService (scoped) plus a hosted writer.
builder.Services.AddSingleton<TokenCredential>(credential);
builder.Services.AddN2TableLogging(builder.Configuration);
```

The host must also register an `N2.Core.Identity.IUserContext`. Without a configured
`TableEndpoint`, `AddN2LogLevelTable` does nothing and `AddN2TableLogging` registers a
`NullLogService`, so local development needs no Azure.

## Settings

Section `N2:TableLogging` (for example environment variable `N2__TableLogging__TableEndpoint`):

| Key | Default | Meaning |
| --- | --- | --- |
| `TableEndpoint` | none | `https://<account>.table.core.windows.net`; unset disables logging |
| `LogTableName` | `n2logs` | table for log records |
| `LevelsTableName` | `n2loglevels` | table for level rows |
| `AppName` | entry assembly name | partition key of records and level rows |
| `QueueCapacity` | 1000 | records waiting to be written; more are dropped |
| `BatchSize` | 100 | records per write (maximum 100) |
| `LevelsPollSeconds` | 60 | interval for reading the level rows |
| `ShutdownFlushSeconds` | 10 | maximum time to flush on shutdown |
| `RetentionDays` | 30 | log rows older than this are purged; 0 or negative disables the purge |
| `RetentionRunHours` | 24 | interval between purge runs (minimum 1) |

The tables must exist. Give the identity Storage Table Data Contributor on the logs table
(it covers the delete rights the purge needs) and Storage Table Data Reader on the levels
table (the app only reads the levels table). Authentication is by `TokenCredential`; no
connection string.

Privacy: never put names, e-mail addresses, tokens or exception text containing user data in
`message`.

## Retention (since 2.1.0)

When table logging is enabled and `RetentionDays` is above zero, `AddN2TableLogging` also
registers a hosted `LogRetentionService`. It purges the rows of its own app name partition
only (each application purges its own rows), five minutes after start and then every
`RetentionRunHours`. Rows are deleted in transactions of at most 100, paging through the
result; only the keys are read. A failed run is logged and counted in `TableLogStatistics`
(`Purged`, `PurgeFailed`, `LastPurgeError`), retried at the next run, and never stops the
host. Set `RetentionDays` to 0 when the host purges the table some other way.

## Levels

Add rows to the levels table:

| PartitionKey | RowKey | Level |
| --- | --- | --- |
| app name | `Default` | `Information` |
| app name | `Microsoft.AspNetCore` | `Warning` |

`RowKey` is a log category (type name prefix) or `Default`. `Level` is a
`Microsoft.Extensions.Logging.LogLevel` name (`Trace` ... `Critical`, `None`). The rows
appear as `Logging:LogLevel:<category>` configuration keys and are re-read every poll
interval. When a level changed, Microsoft.Extensions.Logging re-applies its filters (for all
providers), and the table sink follows the same level. A failed read keeps the last values.
`LogEvent<T>(message, category)` is not filtered by level.

## What is stored

message, category, type, app name, assembly version, trace id (`Activity.Current`) and user
id (Guid; `Guid.Empty` when not authenticated). No headers, IP address, user name or e-mail.
A full queue or failed write never blocks or faults the caller; see `TableLogStatistics`.
