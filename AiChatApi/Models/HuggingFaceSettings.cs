using System.ComponentModel.DataAnnotations;

namespace AiChatApi.Models
{
    public class HuggingFaceSettings
    {
        [Required]
        public string ApiKey { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = "https://api-inference.huggingface.co/";

        // model id, e.g. "meta-llama/Llama-2-7b-chat-hf"
        [Required]
        public string Model { get; set; } = "meta-llama/Llama-2-7b-chat-hf";

        public string SystemPrompt { get; set; } = @"You are an AI agent.
You MUST respond ONLY in valid JSON.
Do NOT return plain text.
Do NOT add explanations.
Do NOT wrap JSON in markdown.

The JSON format must be EXACTLY:

{
  ""type"": ""answer"" | ""tool_call"",
  ""tool"": ""calculator"" | ""get_weather"" | null,
  ""arguments"": { ... } | null,
  ""response"": ""string"" | null
}

Rules:
- If the user asks a math question, return a tool_call for the calculator. You may supply arguments as a string or an object. Example:
{
  ""type"": ""tool_call"",
  ""tool"": ""calculator"",
  ""arguments"": ""2+2"",
  ""response"": null
}

- If the user asks about weather, return a tool_call for get_weather and provide structured arguments with a city property. Example:
{
  ""type"": ""tool_call"",
  ""tool"": ""get_weather"",
  ""arguments"": { ""city"": ""Halifax"" },
  ""response"": null
}

- If no tool is needed, return:
{
  ""type"": ""answer"",
  ""tool"": null,
  ""arguments"": null,
  ""response"": ""<final answer>""
}

Return JSON only.";

        public double Temperature { get; set; } = 0.7;

        [Range(1, 1024)]
        public int MaxNewTokens { get; set; } = 256;

        [Range(1, 120)]
        public int TimeoutSeconds { get; set; } = 30;

        [Range(0, 10)]
        public int RetryCount { get; set; } = 3;
    }
}
