# Logging services

Library to simplify process logging and to enable dynamically changing the log level
without restarting an application.

## The basic idea

This logging is intended for process logging, not for debugging or tracing.
Use `Microsoft.Extensions.Logging` for that. [Logging in C# and .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging)

With the simple `N2.Core.ILogService` interface, logging is easily added to any C# class or
method. The service adds context: type, app name, assembly version, trace id and the user id
(Guid). It stores no headers, IP addresses, user names or e-mail addresses.

Version 3.x (net10.0 and net11.0) contains:

- `TableLogService`: `ILogService` that writes to Azure Table Storage through a bounded
  background queue, using a `TokenCredential` (no connection string).
- `LogLevelTableConfigurationSource`: reads a levels table every 60 seconds and exposes the
  levels as `Logging:LogLevel:*` configuration keys, so `ILogger` filtering (and the table
  sink) change without a restart.
- `LogRetentionService` (2.1.0): hosted service that purges log rows older than
  `RetentionDays` (default 30) from the application's own partition. 0 disables it.

## Usage

```csharp
var settings = TableLoggingSettings.Read(builder.Configuration);
builder.Configuration.AddN2LogLevelTable(settings, credential);   // levels from the table
builder.Services.AddN2TableLogging(builder.Configuration);        // ILogService sink
```

Settings live in the `N2:TableLogging` section. Details, settings keys and the levels table
format: [src/N2.LoggingService/readme.md](src/N2.LoggingService/readme.md). Design:
[docs/adr](docs/adr).

## Build and test

```bash
dotnet build src/N2.LoggingService.sln
dotnet test src/N2.LoggingService.sln
```

The unit tests need no Azure. `src/nuget.config` uses the private feed `vault-packages` for
`N2.*` packages (sign in with the Azure Artifacts credential provider) and nuget.org for the rest.

## Publishing

- Every push to trunk that changes the package is built, tested and pushed to the private
  feed `vault-packages` by the Azure Pipelines pipeline `pipelines/n2-logging-ci-publish.yml`
  (existing versions are skipped; bump `Version` in the csproj for a new package).
- nuget.org releases follow monthly from vault-packages through a central promotion
  pipeline. Nothing is pushed to nuget.org from this repository.
- The GitHub workflow only builds and tests.
