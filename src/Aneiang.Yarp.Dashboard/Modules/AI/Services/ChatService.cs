using System.Text.Json;
using Aneiang.Yarp.Dashboard.Infrastructure;
using Aneiang.Yarp.Dashboard.Modules.AI.Tools;
using Aneiang.Yarp.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aneiang.Yarp.Dashboard.Modules.AI.Services;

/// <summary>
/// Orchestrates AI chat conversations: builds system prompt with gateway context,
/// manages conversation history, delegates to the AI provider, and handles tool calling.
/// </summary>
public class ChatService
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IAIProvider _provider;
    private readonly GatewayContextProvider _contextProvider;
    private readonly IAIConversationRepository _conversationRepo;
    private readonly GatewayToolRegistry _toolRegistry;
    private readonly GatewayToolExecutor _toolExecutor;
    private readonly AIOptions _options;
    private readonly string _locale;
    private readonly ILogger<ChatService> _logger;

    /// <summary>Maximum number of tool call round-trips per user message.</summary>
    private const int MaxToolRounds = 5;

    public ChatService(
        IAIProvider provider,
        GatewayContextProvider contextProvider,
        IAIConversationRepository conversationRepo,
        GatewayToolRegistry toolRegistry,
        GatewayToolExecutor toolExecutor,
        IOptions<AIOptions> options,
        IOptions<DashboardOptions> dashboardOptions,
        ILogger<ChatService> logger)
    {
        _provider = provider;
        _contextProvider = contextProvider;
        _conversationRepo = conversationRepo;
        _toolRegistry = toolRegistry;
        _toolExecutor = toolExecutor;
        _options = options.Value;
        _locale = dashboardOptions.Value.Locale ?? "zh-CN";
        _logger = logger;
    }

    private bool IsChineseLocale(string? locale) =>
        (locale ?? _locale).StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Build the full system prompt including live gateway context and tool usage instructions.
    /// </summary>
    public async Task<string> BuildSystemPromptAsync(string? locale = null, CancellationToken ct = default)
    {
        var context = await _contextProvider.BuildContextAsync(ct);
        var isChinese = IsChineseLocale(locale);
        var langDirective = isChinese
            ? "★★★ 语言规则（最高优先级）★★★\n你必须全程用中文回复，包括表头、状态描述、操作总结、表格内容等所有部分。绝对不要回复英文。"
            : "★★★ LANGUAGE RULE (HIGHEST PRIORITY) ★★★\nYou MUST respond entirely in English for all messages, including table headers, status descriptions, and summaries.";

        return $"""
            You are the AI assistant for Aneiang.Yarp Gateway Dashboard.
            You help administrators manage routes, clusters, WAF rules, rate limiting, circuit breakers, and troubleshoot issues.
            You have access to tools that can directly query and modify gateway configuration.

            {langDirective}

            TOOL USAGE RULES:
            1. For ANY query about gateway state (routes, clusters, logs, health, plugins, WAF, circuit breakers):
               ALWAYS call the appropriate read tool immediately. NEVER describe data from memory or guess.
            2. For ANY write operation the user requests (create, update, delete, rename routes/clusters, create/reset circuit breakers, toggle plugins, update WAF, clear logs, create snapshots, rollback config, manage policies):
               Call the write tool IMMEDIATELY. Do NOT just describe what you plan to do — actually call the tool.
               The system will automatically ask the user to confirm before executing. You do not need to ask permission.
            3. When calling a write tool, provide a one-sentence summary of what you are doing, then call the tool right away.
            4. After a tool returns results, analyze the data and present it clearly using markdown tables when appropriate.

            General guidelines:
            - Always respond concisely. Use markdown for formatting. Use tables for structured data.
            - For troubleshooting, use diagnostic tools first, then suggest step-by-step actions.
            - If a tool fails, explain the error and suggest alternatives.
            - When creating routes, use sensible defaults with YARP path wildcard format.
            - When creating policies for a specific cluster/route, ALWAYS pass cluster_ids or route_ids in the create call to create and apply in one step. Do NOT create a policy template first and then apply it separately unless the user asks for a reusable template.
            - Use create_cluster_policy (with cluster_ids) instead of create_circuit_breaker when the user wants to manage circuit breakers via the policy system (which shows in the Policy Management list).

            {context}
            """;
    }

    /// <summary>
    /// Process a chat message: save user input, load history, call AI provider, save response.
    /// Returns the session ID.
    /// </summary>
    public async Task<string> ProcessMessageAsync(
        string? sessionId,
        string userContent,
        CancellationToken ct = default)
    {
        sessionId ??= Guid.NewGuid().ToString("N")[..12];

        await _conversationRepo.SaveMessageAsync(new AIConversationEntry
        {
            SessionId = sessionId,
            Role = "user",
            Content = userContent
        }, ct);

        return sessionId;
    }

    /// <summary>
    /// Save an assistant response to the conversation history.
    /// </summary>
    public async Task SaveAssistantResponseAsync(
        string sessionId,
        string content,
        CancellationToken ct = default)
    {
        await _conversationRepo.SaveMessageAsync(new AIConversationEntry
        {
            SessionId = sessionId,
            Role = "assistant",
            Content = content
        }, ct);
    }

    /// <summary>
    /// Build the AI chat request with conversation history and tool definitions.
    /// </summary>
    public async Task<AIChatRequest> BuildChatRequestAsync(
        string sessionId,
        string? locale = null,
        CancellationToken ct = default)
    {
        var history = await _conversationRepo.GetSessionMessagesAsync(
            sessionId, _options.MaxConversationHistory, ct);

        var request = new AIChatRequest
        {
            SystemPrompt = await BuildSystemPromptAsync(locale, ct),
            Messages = history.Select(h => new AIChatMessage
            {
                Role = h.Role,
                Content = h.Content,
                ToolCallId = h.ToolCallId
            }).ToList(),
            Tools = _toolRegistry.GetToolDefinitions()
        };

        return request;
    }

    /// <summary>
    /// Process a chat message with tool calling support.
    /// Read-only tools are auto-executed in a loop; write tools return a pending action.
    /// Returns either the final text response or a pending action for user confirmation.
    /// </summary>
    public async Task<ChatWithToolsResult> ProcessWithToolsAsync(
        string sessionId,
        AIChatRequest request,
        string? locale = null,
        CancellationToken ct = default)
    {
        var messages = new List<AIChatMessage>(request.Messages);

        for (int round = 0; round < MaxToolRounds; round++)
        {
            var currentRequest = new AIChatRequest
            {
                SystemPrompt = request.SystemPrompt,
                Messages = messages,
                Tools = request.Tools,
                Model = request.Model,
                Temperature = 0.2, // Lower temperature for tool accuracy
                MaxTokens = request.MaxTokens
            };

            var response = await _provider.ChatAsync(currentRequest, ct);

            if (response == null || !response.Success)
            {
                return new ChatWithToolsResult
                {
                    Type = ChatResultType.Error,
                    Text = response?.Error ?? "AI request failed."
                };
            }

            // No tool calls — return messages so controller can stream the final response
            if (!response.HasToolCalls)
            {
                // Return accumulated messages for streaming; controller handles save
                return new ChatWithToolsResult
                {
                    Type = ChatResultType.Text,
                    AccumulatedMessages = messages,
                    Text = "" // Will be filled by streaming
                };
            }

            // Process tool calls
            var toolCalls = CollectToolCalls(response);

            // Check if ANY tool call is a write tool (return pending action immediately)
            foreach (var toolCall in toolCalls)
            {
                if (!_toolRegistry.IsReadOnlyTool(toolCall.ToolName))
                {
                    var argsPreview = TryParseArguments(toolCall.Arguments);
                    return new ChatWithToolsResult
                    {
                        Type = ChatResultType.PendingAction,
                        Text = response.Content ?? "",
                        PendingAction = new AIPendingAction
                        {
                            CallId = toolCall.Id,
                            ToolName = toolCall.ToolName,
                            Arguments = toolCall.Arguments,
                            Description = ToolActionDescriber.Describe(toolCall.ToolName, argsPreview, IsChineseLocale(locale))
                        },
                        PendingRequest = currentRequest
                    };
                }
            }

            // All tool calls are read-only: add ONE assistant message with all tool_calls,
            // then execute each and add individual tool result messages.
            messages.Add(new AIChatMessage
            {
                Role = "assistant",
                Content = response.Content ?? "",
                ToolCalls = toolCalls
            });

            foreach (var toolCall in toolCalls)
            {
                var result = await _toolExecutor.ExecuteToolAsync(toolCall.ToolName, toolCall.Arguments, ct);
                messages.Add(new AIChatMessage
                {
                    Role = "tool",
                    Content = SerializeToolResult(result),
                    ToolCallId = toolCall.Id
                });
            }
        }

        return new ChatWithToolsResult
        {
            Type = ChatResultType.Error,
            Text = IsChineseLocale(locale) ? "工具调用轮次已达上限，请简化您的请求。" : "Maximum tool call rounds exceeded. Please simplify your request."
        };
    }

    /// <summary>
    /// Execute a confirmed write tool and continue the AI conversation with the result.
    /// Used after user confirms a pending action.
    /// </summary>
    public async Task<ChatWithToolsResult> ExecuteConfirmedActionAsync(
        string sessionId,
        AIPendingAction action,
        AIChatRequest originalRequest,
        CancellationToken ct = default)
    {
        // Execute the tool
        var result = await _toolExecutor.ExecuteToolAsync(action.ToolName, action.Arguments, ct);

        // Add assistant message with tool call and tool result to continue conversation
        var messages = new List<AIChatMessage>(originalRequest.Messages)
        {
            new()
            {
                Role = "assistant",
                Content = "",
                ToolCalls = new List<AIToolCall>
                {
                    new() { Id = action.CallId, ToolName = action.ToolName, Arguments = action.Arguments }
                }
            },
            new()
            {
                Role = "tool",
                Content = SerializeToolResult(result),
                ToolCallId = action.CallId
            }
        };

        // Continue the conversation loop to let AI summarize the result
        var followUpRequest = new AIChatRequest
        {
            SystemPrompt = originalRequest.SystemPrompt,
            Messages = messages,
            Tools = originalRequest.Tools,
            Model = originalRequest.Model,
            Temperature = 0.3,
            MaxTokens = originalRequest.MaxTokens
        };

        var response = await _provider.ChatAsync(followUpRequest, ct);
        var text = response?.Content ?? (result.Success
            ? $"Action completed: {action.ToolName} executed successfully."
            : $"Action failed: {result.ErrorMessage}");

        await SaveAssistantResponseAsync(sessionId, text, ct);

        return new ChatWithToolsResult
        {
            Type = ChatResultType.Text,
            Text = text,
            ToolResult = result
        };
    }

    /// <summary>
    /// Stream a chat response (for non-tool scenarios).
    /// </summary>
    public async IAsyncEnumerable<string> StreamChatAsync(
        string sessionId,
        AIChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var chunk in _provider.ChatStreamAsync(request, ct))
        {
            yield return chunk;
        }
    }

    // ===================== Helpers =====================

    private static List<AIToolCall> CollectToolCalls(AIChatResponse response)
    {
        var calls = new List<AIToolCall>();

        // New format: tool_calls array
        if (response.ToolCalls?.Count > 0)
        {
            calls.AddRange(response.ToolCalls);
        }
        // Legacy format: single function_call
        else if (!string.IsNullOrEmpty(response.FunctionName))
        {
            calls.Add(new AIToolCall
            {
                Id = "call_" + Guid.NewGuid().ToString("N")[..8],
                ToolName = response.FunctionName!,
                Arguments = response.FunctionArguments ?? "{}"
            });
        }

        return calls;
    }

    private static string SerializeToolResult(AIToolResult result)
    {
        if (!result.Success)
            return JsonSerializer.Serialize(new { error = result.ErrorMessage }, _jsonOpts);

        if (result.Data == null)
            return "{\"success\": true}";

        return JsonSerializer.Serialize(result.Data, _jsonOpts);
    }

    private static Dictionary<string, JsonElement> TryParseArguments(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var dict = new Dictionary<string, JsonElement>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.Clone();
            }
            return dict;
        }
        catch
        {
            return new Dictionary<string, JsonElement>();
        }
    }

}
