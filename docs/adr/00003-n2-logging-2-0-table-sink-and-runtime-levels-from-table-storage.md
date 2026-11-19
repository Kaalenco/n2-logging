# 00003. N2.LoggingService 2.0: table sink and runtime levels from Table Storage

2026-11-18

## Status

__Proposed__

## Context

ADR 00002 asked for generic, flexible process logging whose level can change while the
application runs. The 1.x package does not deliver that well enough:

- It depends on N2.Core 1.x and N2.Core.Identity.Abstractions 1.x and targets net8.0. The
  contracts moved to N2.Core.Abstractions 2.x (net10.0 / net11.0).
- The log level is read once from settings, so changing it needs a restart or redeploy.
- The table access uses a connection string; hosts on Azure disallow shared keys and use a
  managed identity.
- Records contain request headers, client IP address, connection id and user name. That is
  more personal data than process logging needs.
- Writing is a fire-and-forget Task per burst; a failure faults the task and is easy to miss.
- The console implementation duplicates what ILogger already provides.

## Decision

N2.LoggingService 2.0.0 is a redesign, targeting `net10.0;net11.0` and depending only on
N2.Core.Abstractions 2.x, Azure.Data.Tables, Azure.Core and Microsoft.Extensions.*.

Sink:

- `TableLogService` implements `N2.Core.ILogService` unchanged (its void methods are an
  accepted exception: external contract). It is registered scoped because `IUserContext` is
  usually scoped.
- Access to Table Storage is through `TokenCredential` and a table endpoint URI. No
  connection string.
- Records go into a bounded `Channel` (`LogRecordBuffer`). `TableLogWriterService`
  (a hosted service) writes them in batches with one `TableClient`. A full queue drops
  records; a failed write loses the batch. Both are counted in `TableLogStatistics` and
  never fault or block the caller. The queue is flushed on shutdown, bounded by a timeout.
- Record fields: message, category (level or event category), type, app name, assembly
  version, trace id (`Activity.Current`) and user id (Guid from `IUserContext.PublicId`,
  `Guid.Empty` when not authenticated). No headers, no IP address, no user name or e-mail.
- `LogEvent<T>(message, category)` is an explicit event and is not filtered by level.
- The table reader and writer sit behind `ILogLevelTableReader` and `ILogTableWriter`, so
  unit tests need no Azure.
- `ConsoleLoggingService` is removed; console output comes from ILogger.

Runtime levels:

- `LogLevelTableConfigurationSource` / `LogLevelTableConfigurationProvider` read a levels
  table (PartitionKey = app name, RowKey = category or `Default`, property `Level`) every
  60 seconds (configurable) and expose `Logging:LogLevel:<category>` configuration keys.
  The provider raises the reload token only when the levels changed, so
  Microsoft.Extensions.Logging re-applies its filters for every provider. A failed read keeps
  the last known values. It never touches a database.
- `LogLevelResolver` reads the same keys (longest prefix, then `Default`, then Information)
  so the same level governs the table sink.

Registration: `AddN2LogLevelTable` on the `IConfigurationBuilder` and `AddN2TableLogging` on
the `IServiceCollection`. Settings come from the `N2:TableLogging` section
(`TableLoggingSettings`). Without an endpoint both do nothing / register `NullLogService`.

Publishing: vault-packages through an Azure Pipelines pipeline (trunk pushes, skip
duplicates); nuget.org through the central monthly promotion pipeline. The GitHub workflow
only builds and tests.

## Consequences

- Breaking change: major version 2.0.0; consumers on N2.Core 1.x stay on 1.x.
- Hosts must register a `TokenCredential` and an `IUserContext`, add the levels source
  before building the host and wire `Logging` configuration into ILogger (the default host
  builders do this).
- The managed identity needs Storage Table Data Contributor on the log table and Storage
  Table Data Reader on the levels table (the app only reads it). Tables are created by the infrastructure, not by the package.
- Level changes take effect within one poll interval; no restart or redeploy.
- Records can be lost under sustained table outage or overload; this is visible in
  `TableLogStatistics`, and acceptable for process logging.
- Consumers that inject `ILogService` into singletons must resolve it per scope.

## Amendment 2.1.0: retention in the package

2.0.0 never deleted log rows, which costs storage and conflicts with GDPR storage
limitation (the records carry a user id and a trace id). Decision (2026-11-19): the package
purges its own rows.

- `TableLoggingSettings.RetentionDays` (default 30; 0 or negative disables) and
  `RetentionRunHours` (default 24, minimum 1).
- `LogRetentionService` (hosted) is registered by `AddN2TableLogging` only when an endpoint is
  configured and `RetentionDays` > 0. First run after 5 minutes (off the startup path), then
  every interval, using `TimeProvider`.
- Row keys are `reverse ticks (D19) + "-" + guid`, so rows older than the cutoff are the keys
  `>= D19(MaxTicks - cutoffTicks + 1)`. A row stamped exactly at the cutoff is kept. Only the
  app's own partition is queried, selecting PartitionKey and RowKey only; deletes are
  transactions of at most 100, per result page.
- The purge query is bounded by `RowKey lt ':'` (':' sorts directly after '9'), so non-numeric
  row keys (level-style rows such as "Default") are never purged.
- Table access sits behind `ILogRetentionStore` (Azure implementation `AzureLogRetentionStore`),
  so unit tests need no Azure.
- A failed run is logged and counted in `TableLogStatistics` (`Purged`, `PurgeFailed`,
  `LastPurgeError`); it never stops the host. Shutdown cancels the run cleanly.
- Partitions of app names that are no longer deployed are never purged; retention only runs for the app's own partition.
- No new permission: Storage Table Data Contributor on the log table already allows delete.

Consequences: hosts that upgrade to 2.1.0 start purging after 30 days of data unless they set
`RetentionDays` to 0. Rows of other app names in the same table are not touched.
