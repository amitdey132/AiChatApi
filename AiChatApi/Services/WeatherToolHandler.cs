using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Services
{
    public class WeatherToolHandler : IToolHandler
    {
        public string Name => "get_weather";

        private readonly IWeatherService _weatherService;
        private readonly ILogger<WeatherToolHandler> _logger;

        public WeatherToolHandler(IWeatherService weatherService, ILogger<WeatherToolHandler> logger)
        {
            _weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<string> HandleAsync(JsonElement arguments, CancellationToken cancellationToken = default)
        {
            // Expect arguments to contain a city field
            if (arguments.ValueKind != JsonValueKind.Object)
            {
                _logger.LogWarning("WeatherToolHandler received invalid arguments: {ArgsKind}", arguments.ValueKind);
                throw new ArgumentException("Invalid arguments for get_weather");
            }

            if (!arguments.TryGetProperty("city", out var cityEl) || cityEl.ValueKind != JsonValueKind.String)
            {
                _logger.LogWarning("WeatherToolHandler missing 'city' argument");
                throw new ArgumentException("'city' argument is required for get_weather");
            }

            var city = cityEl.GetString() ?? string.Empty;
            _logger.LogInformation("Executing weather tool for city={City}", city);

            var weatherJson = await _weatherService.GetWeatherAsync(city, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Weather tool result for city={City}: {ResultLength} chars", city, weatherJson?.Length ?? 0);

            return weatherJson;
        }
    }
}
