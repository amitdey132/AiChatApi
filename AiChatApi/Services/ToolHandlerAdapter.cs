using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AiChatApi.Tools;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Services
{
    // Adapts legacy ITool implementations to IToolHandler so ToolExecutionService can invoke them.
    public class ToolHandlerAdapter : IToolHandler
    {
        private readonly ITool _tool;
        private readonly ILogger<ToolHandlerAdapter> _logger;

        public ToolHandlerAdapter(ITool tool, ILogger<ToolHandlerAdapter> logger)
        {
            _tool = tool ?? throw new ArgumentNullException(nameof(tool));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => _tool.Name;

        public async Task<string> HandleAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            // Convert JsonElement into a simple string input for ITool.ExecuteAsync
            string input;
            switch (arguments.ValueKind)
            {
                case JsonValueKind.String:
                    input = arguments.GetString() ?? string.Empty;
                    break;
                case JsonValueKind.Object:
                case JsonValueKind.Array:
                    // Pass the JSON text form to the tool
                    input = arguments.ToString();
                    break;
                case JsonValueKind.Null:
                case JsonValueKind.Undefined:
                    input = string.Empty;
                    break;
                default:
                    input = arguments.ToString();
                    break;
            }

            _logger.LogInformation("Adapter invoking tool {Tool} with input length={Len}", Name, input?.Length ?? 0);
            return await _tool.ExecuteAsync(input, cancellationToken).ConfigureAwait(false);
        }
    }
}
