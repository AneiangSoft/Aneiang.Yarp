namespace Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;

/// <summary>
/// Base exception type for all Dashboard domain exceptions.
/// Carries an HTTP-like status code consumed by <see cref="Filters.GlobalExceptionFilter"/>.
/// </summary>
public abstract class DashboardException : Exception
{
    /// <summary>HTTP status code to return to the client.</summary>
    public int StatusCode { get; }

    protected DashboardException(string message, int statusCode = 400)
        : base(message)
    {
        StatusCode = statusCode;
    }

    protected DashboardException(string message, Exception innerException, int statusCode = 400)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}

/// <summary>Resource was not found (HTTP 404).</summary>
public class NotFoundException : DashboardException
{
    public NotFoundException(string message) : base(message, 404) { }
    public NotFoundException(string resource, string id) : base($"{resource} '{id}' not found.", 404) { }
}

/// <summary>Request validation failed (HTTP 422).</summary>
public class ValidationException : DashboardException
{
    public IReadOnlyList<string> Errors { get; }

    public ValidationException(string message) : base(message, 422)
    {
        Errors = new[] { message };
    }

    public ValidationException(IReadOnlyList<string> errors)
        : base("Validation failed.", 422)
    {
        Errors = errors;
    }
}

/// <summary>Resource already exists or state conflict (HTTP 409).</summary>
public class ConflictException : DashboardException
{
    public ConflictException(string message) : base(message, 409) { }
}

/// <summary>Caller is not authorized (HTTP 401).</summary>
public class UnauthorizedException : DashboardException
{
    public UnauthorizedException(string message = "Unauthorized") : base(message, 401) { }
}

/// <summary>Caller is authenticated but lacks permission (HTTP 403).</summary>
public class ForbiddenException : DashboardException
{
    public ForbiddenException(string message = "Forbidden") : base(message, 403) { }
}

/// <summary>Internal server error (HTTP 500).</summary>
public class ServerException : DashboardException
{
    public ServerException(string message) : base(message, 500) { }
    public ServerException(string message, Exception innerException) : base(message, innerException, 500) { }
}
