using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Aneiang.Yarp.Services;

/// <summary>Builds safe error messages for HTTP responses without leaking secrets in production.</summary>
public static partial class SafeErrorMessages
{
    public static string Create(HttpContext httpContext, string operation, Exception exception)
    {
        var env = httpContext.RequestServices.GetService<IHostEnvironment>();
        if (env?.IsDevelopment() == true)
            return $"{operation}: {Redact(exception.Message)}";

        return $"{operation}. TraceId: {httpContext.TraceIdentifier}";
    }

    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var text = value;
        text = SecretAssignmentRegex().Replace(text, "$1=***REDACTED***");
        text = ConnectionStringPasswordRegex().Replace(text, "$1=***REDACTED***");
        return text;
    }

    [GeneratedRegex("(?i)(password|pwd|token|secret|api[-_]?key|access[_-]?token|refresh[_-]?token)\\s*=\\s*[^;\\s,}]+")]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex("(?i)(password|pwd)\\s*=\\s*[^;]+")]
    private static partial Regex ConnectionStringPasswordRegex();
}
