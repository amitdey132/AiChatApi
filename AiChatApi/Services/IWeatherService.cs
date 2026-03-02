using System.Threading;
using System.Threading.Tasks;

namespace AiChatApi.Services
{
    public interface IWeatherService
    {
        Task<string> GetWeatherAsync(string city, CancellationToken cancellationToken = default);
    }
}
