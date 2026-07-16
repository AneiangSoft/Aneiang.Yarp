using Aneiang.Yarp.Dashboard.Infrastructure.Common;
using Aneiang.Yarp.Dashboard.Infrastructure.Exceptions;
using Aneiang.Yarp.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Aneiang.Yarp.Dashboard.Infrastructure.Filters;

/// <summary>
/// Global exception filter that converts <see cref="DashboardException"/> subclasses
/// into structured <see cref="ApiResponse"/> JSON responses, and logs unexpected
/// exceptions as HTTP 500 with safe error messages (via <see cref="SafeErrorMessages"/>).
/// Eliminates per-controller try-catch boilerplate.
/// </summary>
public class GlobalExceptionFilter : IExceptionFilter
{
    private readonly ILogger<GlobalExceptionFilter> _logger;

    public GlobalExceptionFilter(ILogger<GlobalExceptionFilter> logger)
    {
        _logger = logger;
    }

    public void OnException(ExceptionContext context)
    {
        // Dashboard domain exceptions -> structured response with correct status code
        if (context.Exception is DashboardException dex)
        {
            var apiResponse = BuildResponse(dex);
            var result = new ObjectResult(apiResponse)
            {
                StatusCode = dex.StatusCode
            };
            context.Result = result;
            context.ExceptionHandled = true;

            if (dex.StatusCode >= 500)
                _logger.LogError(dex, "Unhandled domain exception: {Message}", dex.Message);
            else
                _logger.LogDebug(dex, "Domain exception: {Message}", dex.Message);

            return;
        }

        // Unexpected exceptions -> 500 with safe message (no stack trace leak)
        var operation = ExtractOperationName(context.ActionDescriptor.DisplayName);
        _logger.LogError(context.Exception, "Unhandled exception in {Action}", context.ActionDescriptor.DisplayName);

        var safeMessage = SafeErrorMessages.Create(context.HttpContext, operation, context.Exception);
        var fallback = ApiResponse.Fail(safeMessage, 500);

        context.Result = new ObjectResult(fallback) { StatusCode = 500 };
        context.ExceptionHandled = true;
    }

    private static object BuildResponse(DashboardException ex)
    {
        // ValidationException carries multiple error messages
        if (ex is ValidationException vex)
        {
            return new
            {
                code = vex.StatusCode,
                success = false,
                message = vex.Message,
                errors = vex.Errors
            };
        }

        return ApiResponse.Fail(ex.Message, ex.StatusCode);
    }

    /// <summary>
    /// Extract a human-readable operation name from the action descriptor display name.
    /// e.g. "Aneiang.Yarp.Dashboard.Modules.GatewayConfig.Controllers.RouteConfigController.SaveRoute (Aneiang.Yarp.Dashboard)"
    /// -> "RouteConfigController.SaveRoute"
    /// </summary>
    private static string ExtractOperationName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "Operation failed";

        // Take the part before " (" if present
        var name = displayName.Contains('(') ? displayName[..displayName.IndexOf('(')].Trim() : displayName.Trim();

        // Take the last two segments (Controller.Action)
        var parts = name.Split('.');
        if (parts.Length >= 2)
            return $"{parts[^2]}.{parts[^1]} failed";

        return "Operation failed";
    }
}
