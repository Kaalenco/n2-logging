using System.Diagnostics.CodeAnalysis;
using Azure;
using Azure.Core;

namespace N2.LoggingService.UnitTests;

/// <summary>Minimal <see cref="Response"/> with a status and an optional x-ms-error-code header.</summary>
internal sealed class FakeResponse : Response
{
    private const string ErrorCodeHeader = "x-ms-error-code";

    private readonly string? errorCode;

    public FakeResponse(int status, string? errorCode = null)
    {
        Status = status;
        this.errorCode = errorCode;
    }

    public override int Status { get; }

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override string ClientRequestId { get; set; } = string.Empty;

    public override void Dispose()
    {
    }

    protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value)
    {
        value = string.Equals(name, ErrorCodeHeader, StringComparison.OrdinalIgnoreCase) ? errorCode : null;
        return value is not null;
    }

    protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
    {
        var found = TryGetHeader(name, out var value);
        values = found ? [value!] : null;
        return found;
    }

    protected override bool ContainsHeader(string name) => TryGetHeader(name, out _);

    protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
        errorCode is null ? [] : [new HttpHeader(ErrorCodeHeader, errorCode)];
}
