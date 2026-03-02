using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiChatApi.Services
{
    public interface IToolHandler
    {
        string Name { get; }
        Task<string> HandleAsync(JsonElement arguments, CancellationToken cancellationToken = default);
    }
}
