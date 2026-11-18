using System.Reflection;

namespace N2.LoggingService;

/// <summary>Reads name and version of the entry assembly and truncates text.</summary>
internal static class AssemblyInfoUtility
{
    private const string UnknownName = "unknown";

    internal static string GetAppName() =>
        Assembly.GetEntryAssembly()?.GetName().Name ?? UnknownName;

    internal static string GetVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? string.Empty;

    internal static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
