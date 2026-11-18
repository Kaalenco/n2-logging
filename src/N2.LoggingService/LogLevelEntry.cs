namespace N2.LoggingService;

/// <summary>One row of the levels table: a category and its level text.</summary>
public sealed record LogLevelEntry(string Category, string Level);
