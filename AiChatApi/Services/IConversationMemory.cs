using System.Collections.Generic;
using System.Threading.Tasks;

namespace AiChatApi.Services
{
    public interface IConversationMemory
    {
        Task<IReadOnlyList<string>> GetMessagesAsync(string sessionId);
        Task AddMessageAsync(string sessionId, string message);
    }
}
