namespace Aneiang.Yarp.Models;

/// <summary>Result of a route add/remove operation.</summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Message">Human-readable result message.</param>
public readonly record struct RouteOperationResult(bool Success, string Message);
