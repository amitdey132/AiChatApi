using System;
using System.Threading;
using System.Threading.Tasks;
using AiChatApi.Models;
using AiChatApi.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly IOpenAiService _openAiService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IOpenAiService openAiService, ILogger<ChatController> logger)
        {
            _openAiService = openAiService ?? throw new ArgumentNullException(nameof(openAiService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Send a chat message to the AI assistant.
        /// </summary>
        /// <param name="request">Chat request containing the user message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Chat response with assistant reply.</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(Models.ErrorResponse), StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> Post([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "Message is required." });
            }

            // Log receipt (do not log full message)
            var correlationId = HttpContext.Items.ContainsKey("CorrelationId") ? HttpContext.Items["CorrelationId"]?.ToString() : HttpContext.TraceIdentifier;
            _logger.LogInformation("Received chat request. MessageLength={Length}, CorrelationId={CorrelationId}", request.Message?.Length ?? 0, correlationId);

            var reply = await _openAiService.GetChatReplyAsync(request.Message, cancellationToken).ConfigureAwait(false);
            var response = new ChatResponse { Reply = reply };
            return Ok(response);
        }
    }
}
