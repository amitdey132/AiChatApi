namespace AiChatApi.Models
{
    public class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
    }
}
