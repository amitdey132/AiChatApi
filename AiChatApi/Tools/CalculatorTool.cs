using System;
using System.Data;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Tools
{
    public class CalculatorTool : ITool
    {
        private readonly ILogger<CalculatorTool> _logger;

        public CalculatorTool(ILogger<CalculatorTool> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string Name => "calculator";

        public string Description => "Evaluates mathematical expressions (supports + - * / and parentheses).";

        public Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(input)) return Task.FromResult(string.Empty);

            // Validate allowed characters to reduce risk
            var allowed = new Regex(@"^[0-9+\-*/().\s]+$");
            if (!allowed.IsMatch(input))
            {
                _logger.LogWarning("CalculatorTool: input contains invalid characters");
                throw new ArgumentException("Invalid characters in expression.");
            }

            try
            {
                // Use DataTable Compute as a simple evaluator (validated above)
                var dt = new DataTable();
                // Disallow use of DataColumn/expressions by only computing the expression string
                var result = dt.Compute(input, string.Empty);
                return Task.FromResult(result?.ToString() ?? string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CalculatorTool failed to evaluate expression");
                throw new ArgumentException("Failed to evaluate expression.");
            }
        }
    }
}
