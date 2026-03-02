using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Services
{
    public class ToolExecutionService
    {
        private readonly IDictionary<string, IToolHandler> _handlers;
        private readonly ILogger<ToolExecutionService> _logger;
        private bool _hasExecuted = false;

        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        public ToolExecutionService(IEnumerable<IToolHandler> handlers, ILogger<ToolExecutionService> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _handlers = new Dictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in handlers)
            {
                if (h == null) continue;
                if (!_handlers.ContainsKey(h.Name)) _handlers[h.Name] = h;
            }
        }

        public async Task<string> ExecuteAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken = default)
        {
            if (_hasExecuted)
            {
                _logger.LogWarning("ToolExecutionService: tool already executed for this request. Skipping {Tool}", toolName);
                throw new InvalidOperationException("Only one tool invocation is allowed per request.");
            }

            if (!_handlers.TryGetValue(toolName, out var handler))
            {
                // Attempt a normalized lookup to handle variations like "get_weather" vs "weather"
                var normalizedRequested = NormalizeName(toolName);
                handler = _handlers.Values.FirstOrDefault(h =>
                    string.Equals(h.Name, toolName, StringComparison.OrdinalIgnoreCase) ||
                    NormalizeName(h.Name) == normalizedRequested ||
                    NormalizeName(h.Name).Contains(normalizedRequested) ||
                    normalizedRequested.Contains(NormalizeName(h.Name)));

                if (handler == null)
                {
                    _logger.LogWarning("No handler registered for tool={Tool}", toolName);
                    throw new KeyNotFoundException($"No handler registered for tool '{toolName}'");
                }
                _logger.LogInformation("Resolved tool '{Requested}' to handler '{Handler}' via normalized lookup", toolName, handler.Name);
            }

            _hasExecuted = true;
            _logger.LogInformation("Invoking tool handler for {Tool}", toolName);
            var result = await handler.HandleAsync(arguments, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Tool {Tool} executed. ResultLength={Len}", toolName, result?.Length ?? 0);
            return result;
        }
    }
}
