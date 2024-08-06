# Logging services

Library to simplify logging and to enable dynamically changing the loglevel
without restarting an application.

## The basic idea

This logging is intended for process logging, not for debugging or tracing.
Use the `Microsoft.Extensions.Logging` for that. [Logging in C# and .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging)

With a simple logging interface, logging is easily added to any c# class or method.
Services use the available information to add context to the log entry, such
as user information, assembly information and tags.

Depending on the implementation, logs can be cleaned up or initialize other processes.