namespace Aneiang.Yarp.Dashboard.Infrastructure.Common;

/// <summary>
/// Standard API response wrapper for all Dashboard API endpoints.
/// Ensures consistent response shape: <c>{ code, success, message, data }</c>.
/// Use <see cref="ApiResponse"/> static factory methods to create instances.
/// </summary>
public class ApiResponse<T>
{
    /// <summary>HTTP-like status code (200 = success).</summary>
    public int Code { get; init; } = 200;

    /// <summary>True when <see cref="Code"/> is 200.</summary>
    public bool Success => Code == 200;

    /// <summary>Optional human-readable message.</summary>
    public string? Message { get; init; }

    /// <summary>Response payload.</summary>
    public T? Data { get; init; }
}

/// <summary>
/// Static factory methods for creating <see cref="ApiResponse{T}"/> instances.
/// </summary>
public static class ApiResponse
{
    /// <summary>Create a success response with data.</summary>
    public static ApiResponse<T> Ok<T>(T data) => new() { Data = data };

    /// <summary>Create a success response with data and message.</summary>
    public static ApiResponse<T> Ok<T>(T data, string message) => new() { Data = data, Message = message };

    /// <summary>Create a success response without data.</summary>
    public static ApiResponse<object?> Ok() => new() { Data = null };

    /// <summary>Create a success response with only a message.</summary>
    public static ApiResponse<object?> Ok(string message) => new() { Message = message, Data = null };

    /// <summary>Create a failure response with no data.</summary>
    public static ApiResponse<object?> Fail(string message, int code = 400) => new() { Code = code, Message = message, Data = null };

    /// <summary>Create a failure response for a specific data type.</summary>
    public static ApiResponse<T> Fail<T>(string message, int code = 400) => new() { Code = code, Message = message, Data = default };
}
