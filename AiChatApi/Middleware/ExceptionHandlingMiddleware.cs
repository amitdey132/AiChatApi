using System;
using System.Net;
using System.Threading.Tasks;
using AiChatApi.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Middleware
{
    public class ExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _logger;

        public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var correlationId = context.Items.ContainsKey(CorrelationIdMiddleware.ContextKey)
                    ? context.Items[CorrelationIdMiddleware.ContextKey]?.ToString() ?? string.Empty
                    : string.Empty;

                switch (ex)
                {
                    case ApiException apiEx:
                        _logger.LogWarning(ex, "API exception handled: {Message}", apiEx.Message);
                        await WriteErrorResponse(context, apiEx.StatusCode, apiEx.Message, correlationId).ConfigureAwait(false);
                        break;
                    case TaskCanceledException tcEx:
                        _logger.LogWarning(tcEx, "Request timed out or was cancelled");
                        await WriteErrorResponse(context, (int)HttpStatusCode.GatewayTimeout, "Request timed out.", correlationId).ConfigureAwait(false);
                        break;
                    case HttpRequestException httpEx:
                        _logger.LogError(httpEx, "HTTP error when calling upstream service");
                        await WriteErrorResponse(context, (int)HttpStatusCode.BadGateway, "Upstream service error.", correlationId).ConfigureAwait(false);
                        break;
                    default:
                        _logger.LogError(ex, "Unhandled exception");
                        await WriteErrorResponse(context, (int)HttpStatusCode.InternalServerError, "An error occurred while processing your request.", correlationId).ConfigureAwait(false);
                        break;
                }
            }
        }

        private static async Task WriteErrorResponse(HttpContext context, int statusCode, string message, string correlationId)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            var error = new ErrorResponse { Error = message, CorrelationId = correlationId };
            var json = System.Text.Json.JsonSerializer.Serialize(error);
            await context.Response.WriteAsync(json).ConfigureAwait(false);
        }
    }
}
