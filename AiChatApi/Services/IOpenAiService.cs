using System.Threading;
using System.Threading.Tasks;

namespace AiChatApi.Services
{
    public interface IOpenAiService
    {
        Task<string> GetChatReplyAsync(string userMessage, CancellationToken cancellationToken = default);
    }
}
