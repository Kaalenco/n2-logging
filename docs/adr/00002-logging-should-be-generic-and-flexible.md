# 00002. Logging should be generic and flexible

2024-08-06

## Status

__New__

## Context

Although Microsoft has excellent libraries and guidelines for logging, it is complex to set up and often you end up
with logging to a microsoft specific format. A second issue is that, if you want to dynamically modify the logging
level while running the code, it often involves restarting the application of adding a specific implementation.

## Decision

This project contains several logging implementations, each with a simple interface.

## Consequences

- You should use the N2.Core.ILogService in your project
- Use dependency injection to comfigure which logging service to use
- Expand this project if you need more functionality