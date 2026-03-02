using System.ComponentModel.DataAnnotations;

namespace AiChatApi.Models
{
    public class OpenAiSettings
    {
        [Required]
        public string ApiKey { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://api.openai.com/";

        public string Model { get; set; } = "gpt-4o-mini";

        public string SystemPrompt { get; set; } = "You are a senior .NET architect helping a developer.";

        [Range(1, 120)]
        public int TimeoutSeconds { get; set; } = 30;

        [Range(0, 10)]
        public int RetryCount { get; set; } = 3;
    }
}
