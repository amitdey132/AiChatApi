using System.ComponentModel.DataAnnotations;

namespace AiChatApi.Models
{
    public class AgentRequest
    {
        [Required]
        public string SessionId { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;
    }
}
