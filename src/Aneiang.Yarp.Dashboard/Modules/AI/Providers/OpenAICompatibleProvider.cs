using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Providers;

/// <summary>
/// Universal OpenAI-compatible LLM provider.
/// Works with OpenAI, DeepSeek, Qwen, and any provider that implements
/// the OpenAI chat completions API protocol.
/// </summary>
public class OpenAICompatibleProvider : IAIProvider, IDisposable
{
    private readonly AIOptions _options;
    private readonly ILogger<OpenAICompatibleProvider> _logger;
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OpenAICompatibleProvider(
        IOptions<AIOptions> options,
        ILogger<OpenAICompatibleProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public bool IsAvailable =>
        !string.IsNullOrWhiteSpace(_options.ApiKey) &&
        !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public string ProviderName => _options.Provider;

    public async Task<AIChatResponse> ChatAsync(AIChatRequest request, CancellationToken ct = default)
    {
        if (!IsAvailable)
            return AIChatResponse.Fail("AI provider not configured. Please set API key in Settings.");

        try
        {
            var payload = BuildPayload(request, stream: false);
            var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";

            var jsonBody = JsonSerializer.Serialize(payload, _jsonOpts);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            using var response = await _http.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI API error {Status}: {Body}", (int)response.StatusCode, body);
                return AIChatResponse.Fail($"API error {(int)response.StatusCode}: {Truncate(body, 200)}");
            }

            return ParseChatResponse(body);
        }
        catch (TaskCanceledException)
        {
            return AIChatResponse.Fail("Request timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI chat request failed");
            return AIChatResponse.Fail($"Request failed: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        AIChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (!IsAvailable)
        {
            yield return "[AI provider not configured]";
            yield break;
        }

        HttpResponseMessage? response = null;
        try
        {
            var payload = BuildPayload(request, stream: true);
            var url = $"{_options.BaseUrl.TrimEnd('/')}/chat/completions";

            var jsonBody = JsonSerializer.Serialize(payload, _jsonOpts);
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct);

                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!line.StartsWith("data: ")) continue;

                var data = line[6..];
                if (data == "[DONE]") yield break;

                string? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    var delta = doc.RootElement
                        .GetProperty("choices")[0]
                        .GetProperty("delta");

                    if (delta.TryGetProperty("content", out var content))
                        chunk = content.GetString();
                }
                catch
                {
                    // Malformed chunk — skip
                }

                if (chunk != null)
                    yield return chunk;
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    private object BuildPayload(AIChatRequest request, bool stream)
    {
        var messages = new List<object>();

        // System prompt
        var systemPrompt = request.SystemPrompt ?? BuildDefaultSystemPrompt();
        messages.Add(new { role = "system", content = systemPrompt });

        // Conversation messages
        foreach (var msg in request.Messages)
        {
            // Handle tool response messages (role = "tool")
            if (msg.Role == "tool" && msg.ToolCallId != null)
            {
                messages.Add(new
                {
                    role = "tool",
                    tool_call_id = msg.ToolCallId,
                    content = msg.Content
                });
                continue;
            }

            // Handle assistant messages with tool calls (new format: ToolCalls list)
            if (msg.Role == "assistant" && msg.ToolCalls?.Count > 0)
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = string.IsNullOrEmpty(msg.Content) ? (object?)null : msg.Content,
                    tool_calls = msg.ToolCalls.Select(tc => new
                    {
                        id = tc.Id,
                        type = "function",
                        function = new
                        {
                            name = tc.ToolName,
                            arguments = tc.Arguments
                        }
                    }).ToList()
                });
                continue;
            }

            // Handle assistant messages with single tool call (legacy: ToolCallId only)
            if (msg.Role == "assistant" && msg.ToolCallId != null)
            {
                messages.Add(new
                {
                    role = "assistant",
                    content = string.IsNullOrEmpty(msg.Content) ? (object?)null : msg.Content,
                    tool_calls = new[]
                    {
                        new
                        {
                            id = msg.ToolCallId,
                            type = "function",
                            function = new
                            {
                                name = msg.FunctionName ?? "",
                                arguments = msg.FunctionArguments ?? "{}"
                            }
                        }
                    }
                });
                continue;
            }

            // Standard messages
            messages.Add(new { role = msg.Role, content = msg.Content });

            // Legacy function call/result if present
            if (msg.FunctionName != null)
            {
                messages.Add(new
                {
                    role = "function",
                    name = msg.FunctionName,
                    content = msg.FunctionResult ?? ""
                });
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = request.Model ?? _options.ChatModel,
            ["messages"] = messages,
            ["max_tokens"] = request.MaxTokens ?? _options.MaxTokens,
            ["temperature"] = request.Temperature ?? _options.Temperature,
            ["stream"] = stream
        };

        // Add tools if available
        if (request.Tools?.Count > 0)
        {
            payload["tools"] = request.Tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = t.Parameters
                }
            }).ToList();
            payload["tool_choice"] = "auto";
        }

        return payload;
    }

    private static AIChatResponse ParseChatResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        var content = message.TryGetProperty("content", out var contentProp) && contentProp.ValueKind != JsonValueKind.Null
            ? contentProp.GetString() ?? ""
            : "";

        int promptTokens = 0, completionTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) promptTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct)) completionTokens = ct.GetInt32();
        }

        var response = new AIChatResponse
        {
            Content = content,
            Success = true,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens
        };

        // Parse tool_calls (new OpenAI format)
        if (message.TryGetProperty("tool_calls", out var toolCallsArr) && toolCallsArr.ValueKind == JsonValueKind.Array)
        {
            response.ToolCalls = new List<AIToolCall>();
            foreach (var tc in toolCallsArr.EnumerateArray())
            {
                response.ToolCalls.Add(new AIToolCall
                {
                    Id = tc.GetProperty("id").GetString() ?? "",
                    ToolName = tc.GetProperty("function").GetProperty("name").GetString() ?? "",
                    Arguments = tc.GetProperty("function").GetProperty("arguments").GetString() ?? "{}"
                });
            }
        }
        // Legacy function_call format
        else if (message.TryGetProperty("function_call", out var fc))
        {
            response.FunctionName = fc.GetProperty("name").GetString();
            response.FunctionArguments = fc.GetProperty("arguments").GetString();
        }

        return response;
    }

    private static string BuildDefaultSystemPrompt()
    {
        return """
            You are the AI assistant for Aneiang.Yarp Gateway Dashboard. 
            You help administrators manage routes, clusters, WAF rules, rate limiting, and troubleshoot issues.
            Always respond concisely. Use markdown for formatting. 
            When presenting data (routes, stats, etc.), use tables.
            If the user asks about gateway status, refer to the context provided.
            """;
    }

    private static string Truncate(string s, int maxLen) =>
        s.Length <= maxLen ? s : s[..maxLen] + "...";

    public void Dispose() => _http.Dispose();
}
