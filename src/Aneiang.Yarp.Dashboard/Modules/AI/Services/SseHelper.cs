using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Server-Sent Events (SSE) helper methods for AI streaming responses.
/// Eliminates duplicate SSE setup and write logic across controller endpoints.
/// </summary>
public static class SseHelper
{
    /// <summary>Configure HTTP response headers for SSE streaming.</summary>
    public static void SetupResponse(HttpResponse response, string? sessionId = null)
    {
        response.ContentType = "text/event-stream";
        response.Headers["Cache-Control"] = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        if (sessionId != null)
            response.Headers["X-Session-Id"] = sessionId;
    }

    /// <summary>Write a content chunk as SSE data event.</summary>
    public static async Task WriteContentAsync(HttpResponse response, string chunk, JsonSerializerOptions opts, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(new { content = chunk }, opts);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>Write an error as SSE data event.</summary>
    public static async Task WriteErrorAsync(HttpResponse response, string message, JsonSerializerOptions opts, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(new { content = message }, opts);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>Write a pending_action event for user confirmation.</summary>
    public static async Task WritePendingActionAsync(HttpResponse response, AIPendingAction action, JsonSerializerOptions opts, CancellationToken ct)
    {
        var data = JsonSerializer.Serialize(new
        {
            type = "pending_action",
            pendingAction = new
            {
                callId = action.CallId,
                toolName = action.ToolName,
                arguments = action.Arguments,
                description = action.Description
            }
        }, opts);
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>Write the SSE termination event.</summary>
    public static async Task WriteDoneAsync(HttpResponse response, CancellationToken ct)
    {
        await response.WriteAsync("data: [DONE]\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>Stream AI response chunks and accumulate full text.</summary>
    public static async Task<string> StreamAndAccumulateAsync(
        HttpResponse response,
        IAsyncEnumerable<string> chunks,
        JsonSerializerOptions opts,
        CancellationToken ct)
    {
        var fullText = new System.Text.StringBuilder();
        await foreach (var chunk in chunks)
        {
            fullText.Append(chunk);
            await WriteContentAsync(response, chunk, opts, ct);
        }
        return fullText.ToString();
    }
}
