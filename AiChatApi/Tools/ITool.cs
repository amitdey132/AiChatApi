using System.Threading;
using System.Threading.Tasks;

namespace AiChatApi.Tools
{
    public interface ITool
    {
        string Name { get; }
        string Description { get; }
        Task<string> ExecuteAsync(string input, CancellationToken cancellationToken = default);
    }
}
