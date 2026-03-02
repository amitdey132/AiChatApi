using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AiChatApi.Services
{
    public class ConversationMemoryService : IConversationMemory
    {
        private readonly ConcurrentDictionary<string, List<string>> _store = new();

        public Task<IReadOnlyList<string>> GetMessagesAsync(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return Task.FromResult((IReadOnlyList<string>)new List<string>());
            var list = _store.GetOrAdd(sessionId, _ => new List<string>());
            IReadOnlyList<string> copy;
            lock (list)
            {
                copy = list.ToList();
            }
            return Task.FromResult(copy);
        }

        public Task AddMessageAsync(string sessionId, string message)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || message == null) return Task.CompletedTask;
            var list = _store.GetOrAdd(sessionId, _ => new List<string>());
            lock (list)
            {
                list.Add(message);
            }
            return Task.CompletedTask;
        }
    }
}
