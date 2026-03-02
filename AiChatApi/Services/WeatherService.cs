using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Services
{
    public class WeatherService : IWeatherService
    {
        private readonly ILogger<WeatherService> _logger;

        public WeatherService(ILogger<WeatherService> logger)
        {
            _logger = logger;
        }

        public Task<string> GetWeatherAsync(string city, CancellationToken cancellationToken = default)
        {
            // Simulate weather lookup. In production replace with real API call.
            _logger.LogInformation("Getting weather for city={City}", city);

            var result = $"{{ \"city\": \"{city}\", \"temperature_c\": 25, \"condition\": \"Sunny\" }}";
            return Task.FromResult(result);
        }
    }
}
