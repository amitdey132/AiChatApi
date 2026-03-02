using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AiChatApi.Models;
using AiChatApi.Tools;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Services
{
    public class AgentOrchestrator
    {
        private readonly IOpenAiService _llm;
        private readonly IEnumerable<ITool> _tools;
        private readonly ILogger<AgentOrchestrator> _logger;
        private readonly IConversationMemory _memory;

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public AgentOrchestrator(IOpenAiService llm, IEnumerable<ITool> tools, IConversationMemory memory, ILogger<AgentOrchestrator> logger)
        {
            _llm = llm ?? throw new ArgumentNullException(nameof(llm));
            _tools = tools ?? Enumerable.Empty<ITool>();
            _memory = memory ?? throw new ArgumentNullException(nameof(memory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AgentResponse> ProcessAsync(AgentRequest request, CancellationToken cancellationToken = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.SessionId)) throw new ArgumentException("SessionId is required", nameof(request.SessionId));
            if (string.IsNullOrWhiteSpace(request.Message)) throw new ArgumentException("Message is required", nameof(request.Message));

            _logger.LogInformation("Agent processing request. SessionId={SessionId}", request.SessionId);

            // 1) Load conversation memory
            var memory = await _memory.GetMessagesAsync(request.SessionId).ConfigureAwait(false);

            // 2) Build prompt: system prompt + memory + user input
            var systemPrompt = "You are an AI assistant that can call tools. When a tool is required, respond with strict JSON only.";
            var promptBuilder = new System.Text.StringBuilder();
            promptBuilder.AppendLine(systemPrompt);
            promptBuilder.AppendLine();

            if (memory != null && memory.Count > 0)
            {
                promptBuilder.AppendLine("Conversation history:");
                foreach (var m in memory)
                {
                    promptBuilder.AppendLine(m);
                }
                promptBuilder.AppendLine();
            }

            // Force structured output instruction
            promptBuilder.AppendLine("When you respond, return JSON only with the following schema:");
            promptBuilder.AppendLine("{\n  \"type\": \"answer\" | \"tool_call\",\n  \"tool\": null | \"tool_name\",\n  \"input\": \"string\",\n  \"response\": \"string\"\n}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("If the user asks to get the weather, respond with type=\"tool_call\" and tool=\"weather\" and set input to the city name. Otherwise, set type=\"answer\" and fill response with the text answer.");
            promptBuilder.AppendLine();

            promptBuilder.AppendLine("User:");
            promptBuilder.AppendLine(request.Message);

            var prompt = promptBuilder.ToString();

            // 3) Query LLM
            var llmReply = await _llm.GetChatReplyAsync(prompt, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("LLM replied. SessionId={SessionId}, ReplyLength={Len}", request.SessionId, llmReply?.Length ?? 0);

            // Save user message to memory
            await _memory.AddMessageAsync(request.SessionId, $"User: {request.Message}").ConfigureAwait(false);

            // 4) Parse LLM response as JSON
            var parsed = TryParseAgentJson(llmReply, out var docRoot);
            if (!parsed)
            {
                // No structured JSON -> return plain text
                await _memory.AddMessageAsync(request.SessionId, $"Assistant: {llmReply}").ConfigureAwait(false);
                return new AgentResponse { FinalAnswer = llmReply };
            }

            try
            {
                var type = docRoot.GetProperty("type").GetString();
                if (string.Equals(type, "tool_call", StringComparison.OrdinalIgnoreCase))
                {
                    var toolName = docRoot.TryGetProperty("tool", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var input = docRoot.TryGetProperty("input", out var inp) ? inp.GetString() ?? string.Empty : string.Empty;

                    _logger.LogInformation("Tool call detected. Tool={Tool}, Input={Input}", toolName, input);

                    var tool = _tools.FirstOrDefault(x => string.Equals(x.Name, toolName, StringComparison.OrdinalIgnoreCase));
                    if (tool == null)
                    {
                        _logger.LogWarning("No tool found with name={Tool}", toolName);
                        var err = $"Tool '{toolName}' not found.";
                        await _memory.AddMessageAsync(request.SessionId, $"Assistant: {err}").ConfigureAwait(false);
                        return new AgentResponse { FinalAnswer = err };
                    }

                    // Execute tool (limit to one per request by design)
                    string toolResult;
                    try
                    {
                        toolResult = await tool.ExecuteAsync(input).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tool execution failed for {Tool}", toolName);
                        var err = "Tool execution failed.";
                        await _memory.AddMessageAsync(request.SessionId, $"Assistant: {err}").ConfigureAwait(false);
                        return new AgentResponse { FinalAnswer = err };
                    }

                    _logger.LogInformation("Tool {Tool} executed. ResultLength={Len}", toolName, toolResult?.Length ?? 0);

                    // Send tool result back to LLM for a final answer
                    var followupPrompt = $"Tool result for '{toolName}': {toolResult}\nPlease produce a final answer in the same JSON schema, with type=\"answer\" and response field containing the assistant message.";
                    var finalReply = await _llm.GetChatReplyAsync(followupPrompt, cancellationToken).ConfigureAwait(false);

                    // Save assistant tool result and final reply
                    await _memory.AddMessageAsync(request.SessionId, $"Assistant (tool:{toolName}): {toolResult}").ConfigureAwait(false);
                    await _memory.AddMessageAsync(request.SessionId, $"Assistant: {finalReply}").ConfigureAwait(false);

                    return new AgentResponse { FinalAnswer = finalReply };
                }
                else
                {
                    // type == answer
                    var responseText = docRoot.TryGetProperty("response", out var r) ? r.GetString() ?? string.Empty : string.Empty;
                    await _memory.AddMessageAsync(request.SessionId, $"Assistant: {responseText}").ConfigureAwait(false);
                    return new AgentResponse { FinalAnswer = responseText };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to interpret LLM JSON response");
                // fallback
                await _memory.AddMessageAsync(request.SessionId, $"Assistant: {llmReply}").ConfigureAwait(false);
                return new AgentResponse { FinalAnswer = llmReply };
            }
        }

        private static bool TryParseAgentJson(string text, out JsonElement root)
        {
            root = default;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var trimmed = text.Trim();
            // strip markdown code fences if needed
            if (trimmed.StartsWith("```") && trimmed.EndsWith("```"))
            {
                var idx = trimmed.IndexOf('\n');
                if (idx >= 0)
                {
                    trimmed = trimmed.Substring(idx + 1).Trim();
                    if (trimmed.EndsWith("```")) trimmed = trimmed.Substring(0, trimmed.Length - 3).Trim();
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                root = doc.RootElement.Clone();
                // simple validation: must contain type
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out _))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }

            return false;
        }
    }
}
