using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiChatApi.Models;
using Polly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiChatApi.Services
{
    public class HuggingFaceService : IOpenAiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly HuggingFaceSettings _settings;
        private readonly ILogger<HuggingFaceService> _logger;
        private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

        private readonly ToolExecutionService _toolExecution;

        public HuggingFaceService(IHttpClientFactory httpClientFactory, IOptions<HuggingFaceSettings> settings, ILogger<HuggingFaceService> logger, ToolExecutionService toolExecution)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _toolExecution = toolExecution ?? throw new ArgumentNullException(nameof(toolExecution));
        }

        public async Task<string> GetChatReplyAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(userMessage))
                throw new ArgumentException("User message must be provided.", nameof(userMessage));

            var client = _httpClientFactory.CreateClient("HuggingFace");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            // Determine endpoint shape: router/chat-completions (OpenAI compatible) vs models/{model} (inference API)
            var baseUrlLower = _settings.BaseUrl?.ToLowerInvariant() ?? string.Empty;
            HttpContent content;
            string requestUri;

            if (baseUrlLower.Contains("chat/completions") || baseUrlLower.Contains("/v1/chat"))
            {
                // OpenAI-compatible chat completions endpoint (router)
                requestUri = _settings.BaseUrl; // use full base as endpoint
                // Router expects OpenAI-compatible chat parameters (use max_tokens)
                var payload = new
                {
                    model = _settings.Model,
                    messages = new[]
                    {
                        new { role = "system", content = _settings.SystemPrompt },
                        new { role = "user", content = userMessage }
                    },
                    temperature = _settings.Temperature,
                    max_tokens = _settings.MaxNewTokens
                };
                var json = JsonSerializer.Serialize(payload);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else
            {
                // Standard inference API: POST /models/{model} with inputs
                // Ensure model id is URL-encoded (may contain / or :)
                requestUri = $"models/{Uri.EscapeDataString(_settings.Model)}";
                var prompt = $"System: {_settings.SystemPrompt}\nUser: {userMessage}\nAssistant:";
                var requestPayload = new
                {
                    inputs = prompt,
                    parameters = new
                    {
                        temperature = _settings.Temperature,
                        max_new_tokens = _settings.MaxNewTokens
                    }
                };

                var json = JsonSerializer.Serialize(requestPayload);
                content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            _logger.LogInformation("Starting Hugging Face request (model={Model})", _settings.Model);
            var sw = Stopwatch.StartNew();

            HttpResponseMessage response;
            string responseBody;

            // Prepare retry policy: handle HttpRequestException, TaskCanceledException, and 5xx responses
            var retryCount = _settings.RetryCount > 0 ? _settings.RetryCount : 3;
            var policy = Policy<HttpResponseMessage>
                .Handle<HttpRequestException>()
                .Or<TaskCanceledException>()
                .OrResult(r => (int)r.StatusCode >= 500)
                .WaitAndRetryAsync(retryCount, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)), (outcome, timespan, retryAttempt, context) =>
                {
                    _logger.LogWarning("Retrying Hugging Face request - attempt {Attempt}. Reason: {Reason}", retryAttempt, outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString());
                });

            try
            {
                // Execute against the resolved requestUri. If requestUri is absolute (starts with http) use it as-is.
                if (Uri.IsWellFormedUriString(requestUri, UriKind.Absolute))
                {
                    response = await policy.ExecuteAsync(async (ct) => await client.PostAsync(requestUri, content, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    response = await policy.ExecuteAsync(async (ct) => await client.PostAsync(requestUri, content, ct).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
                }
                responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning(ex, "Hugging Face request cancelled or timed out");
                throw; // handled by middleware
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP request to Hugging Face failed");
                throw; // handled by middleware
            }

            sw.Stop();
            _logger.LogInformation("Hugging Face request completed in {ElapsedMs}ms with status {StatusCode}", sw.ElapsedMilliseconds, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Hugging Face returned non-success status {StatusCode}", (int)response.StatusCode);

                // Map specific codes to friendly errors
                // Handle rate limiting: try to read Retry-After
                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? null;
                    _logger.LogWarning("Hugging Face rate limit (429). RetryAfterSeconds={RetryAfter}", retryAfter);
                    // attempt to include response body in debug logs
                    _logger.LogDebug("Hugging Face 429 response body: {Body}", responseBody);
                    throw new ApiException("Rate limit exceeded.", 429);
                }

                // log response body for diagnostics. Include as Debug and a short Warning note.
                _logger.LogDebug("Hugging Face non-success response. Status={Status}, Body={Body}", (int)response.StatusCode, responseBody);
                // Surface a short message at Warning level so it's visible in standard logs (avoid leaking secrets)
                var snippet = responseBody;
                if (!string.IsNullOrEmpty(snippet) && snippet.Length > 200) snippet = snippet.Substring(0, 200) + "...";
                _logger.LogWarning("Hugging Face returned {Status} - response snippet: {Snippet}", (int)response.StatusCode, snippet);

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => throw new ApiException("Unauthorized call to Hugging Face.", 401),
                    var s when ((int)s >= 500) => throw new ApiException("Upstream service error.", 502),
                    _ => throw new ApiException("Upstream service returned an error.", 502)
                };
            }

            try
            {
                // Parse common Hugging Face response shapes including:
                // - OpenAI-style chat completions: { choices: [ { message: { content: "..." } } ] }
                // - Inference API: [ { generated_text: "..." } ] or { generated_text: "..." }
                using var doc = JsonDocument.Parse(responseBody);
                var root = doc.RootElement;

                // 1) OpenAI-style chat completions / router responses
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];

                    // look for message.content
                    if (first.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object && message.TryGetProperty("content", out var contentElement))
                    {
                        var text = contentElement.GetString()?.Trim() ?? string.Empty;
                        // try tool handling
                        var toolResult = await TryHandleToolJsonAsync(text, cancellationToken).ConfigureAwait(false);
                        if (toolResult != null) return toolResult;
                        return text;
                    }

                    // look for text
                    if (first.TryGetProperty("text", out var textEl))
                    {
                        var text = textEl.GetString()?.Trim() ?? string.Empty;
                        var toolResult = await TryHandleToolJsonAsync(text, cancellationToken).ConfigureAwait(false);
                        if (toolResult != null) return toolResult;
                        return text;
                    }

                    // look for delta.content (streaming)
                    if (first.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.Object && delta.TryGetProperty("content", out var deltaContent))
                    {
                        var text = deltaContent.GetString()?.Trim() ?? string.Empty;
                        var toolResult = await TryHandleToolJsonAsync(text, cancellationToken).ConfigureAwait(false);
                        if (toolResult != null) return toolResult;
                        return text;
                    }
                }

                // 2) Inference API array shape [{ generated_text: "..." }, ...]
                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var first = root[0];
                    if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("generated_text", out var gen))
                    {
                        var text = gen.GetString()?.Trim() ?? string.Empty;
                        var toolResult = await TryHandleToolJsonAsync(text, cancellationToken).ConfigureAwait(false);
                        if (toolResult != null) return toolResult;
                        return text;
                    }
                }

                // 3) Top-level generated_text
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("generated_text", out var gen2))
                {
                    var text = gen2.GetString()?.Trim() ?? string.Empty;
                    var toolResult = await TryHandleToolJsonAsync(text, cancellationToken).ConfigureAwait(false);
                    if (toolResult != null) return toolResult;
                    return text;
                }

                // 4) Plain string
                if (root.ValueKind == JsonValueKind.String)
                {
                    var text = root.GetString()?.Trim() ?? string.Empty;
                    var toolResult = await TryHandleToolJsonAsync(text, cancellationToken).ConfigureAwait(false);
                    if (toolResult != null) return toolResult;
                    return text;
                }

                // nothing found - log full body at debug for diagnosis
                _logger.LogDebug("Unable to extract assistant reply from Hugging Face response. ResponseBody={Body}", responseBody);
                return string.Empty;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse Hugging Face response. ResponseLength={Length}", responseBody?.Length ?? 0);
                throw new ApiException("Failed to parse response from upstream service.", 502);
            }
        }

        private async Task<string?> TryHandleToolJsonAsync(string text, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Trim code fences if present
            var trimmed = text.Trim();
            if (trimmed.StartsWith("```") && trimmed.EndsWith("```"))
            {
                var firstLineBreak = trimmed.IndexOf('\n');
                if (firstLineBreak >= 0)
                {
                    trimmed = trimmed.Substring(firstLineBreak + 1).Trim();
                    if (trimmed.EndsWith("```")) trimmed = trimmed.Substring(0, trimmed.Length - 3).Trim();
                }
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object) return null;

                // Read tool type
                if (!root.TryGetProperty("tool", out var toolEl) || toolEl.ValueKind != JsonValueKind.String)
                    return null;

                var toolName = toolEl.GetString();
                if (string.IsNullOrWhiteSpace(toolName)) return null;

                _logger.LogInformation("Tool detected in model response: {Tool}", toolName);

                // Read the input/arguments as a JsonElement (can be string, object, array, etc.)
                JsonElement inputElement = default;
                // Prefer `arguments` (newer prompt) but fall back to `input` for backward compatibility
                if (root.TryGetProperty("arguments", out var argsEl))
                {
                    inputElement = argsEl;
                }
                else if (root.TryGetProperty("input", out var inputEl))
                {
                    inputElement = inputEl;
                }

                // If the model provided a plain string for a weather tool, wrap it into the expected object
                // e.g. convert "Halifax, NS" -> { "city": "Halifax, NS" }
                if (inputElement.ValueKind == JsonValueKind.String &&
                    (toolName.IndexOf("weather", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     string.Equals(toolName, "get_weather", StringComparison.OrdinalIgnoreCase)))
                {
                    var city = inputElement.GetString() ?? string.Empty;
                    using var wrapDoc = JsonDocument.Parse($"{{\"city\":{JsonSerializer.Serialize(city)} }}");
                    inputElement = wrapDoc.RootElement.Clone();
                }

                var inputForLog = inputElement.ValueKind == JsonValueKind.Undefined ? string.Empty : inputElement.ToString();
                _logger.LogInformation("Tool input parsed. Tool={Tool}, Input={Input}", toolName, inputForLog);

                try
                {
                    // Execute tool using JsonElement arguments
                    var result = await _toolExecution.ExecuteAsync(toolName, inputElement, cancellationToken).ConfigureAwait(false);

                    _logger.LogInformation("Tool {Tool} executed successfully. ResultLength={Len}", toolName, result?.Length ?? 0);
                    return result;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Tool execution failed for {Tool}", toolName);
                    throw new ApiException("Tool execution failed.", 500);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse tool JSON.");
                return null; // Not JSON — no tool
            }
        }

    }
}
