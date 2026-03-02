using System.Threading;
using System.Threading.Tasks;
using AiChatApi.Models;
using AiChatApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AgentController : ControllerBase
    {
        private readonly AgentOrchestrator _orchestrator;
        private readonly ILogger<AgentController> _logger;

        public AgentController(AgentOrchestrator orchestrator, ILogger<AgentController> logger)
        {
            _orchestrator = orchestrator;
            _logger = logger;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Post([FromBody] AgentRequest request, CancellationToken cancellationToken)
        {
            if (request == null) return BadRequest(new { error = "Request body is required." });
            if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Message))
            {
                return BadRequest(new { error = "SessionId and Message are required." });
            }

            try
            {
                var res = await _orchestrator.ProcessAsync(request, cancellationToken).ConfigureAwait(false);
                return Ok(res);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Agent processing failed for session {SessionId}", request?.SessionId);
                return StatusCode(500, new { error = "Agent processing failed." });
            }
        }
    }
}
