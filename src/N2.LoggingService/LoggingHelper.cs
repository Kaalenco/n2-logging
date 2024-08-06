using Microsoft.AspNetCore.Http;
using System.Reflection;
using System.Text;

namespace N2.LoggingService;

internal static class LoggingHelper
{
    internal static string GetAssemblyVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        if (assembly == null)
        {
            return string.Empty;
        }
        var assemblyName = assembly.GetName();
        if (assemblyName == null)
        {
            return string.Empty;
        }
        return assemblyName.FullName;
    }

    internal static string GetHeaders(IHeaderDictionary? headers)
    {
        if (headers == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        foreach (var header in headers)
        {
            // Skip Authorization header
            if (header.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendJoin(", ", $"{header.Key}|{header.Value}").AppendLine();
        }
        return sb.ToString();
    }
}
