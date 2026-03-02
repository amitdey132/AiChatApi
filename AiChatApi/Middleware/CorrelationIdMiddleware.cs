using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace AiChatApi.Middleware
{
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        public const string HeaderKey = "X-Correlation-ID";
        public const string ContextKey = "CorrelationId";

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = GetOrCreateCorrelationId(context);

            // Add to response headers
            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(HeaderKey))
                    context.Response.Headers.Add(HeaderKey, correlationId);
                return Task.CompletedTask;
            });

            // Put in items for access
            context.Items[ContextKey] = correlationId;

            using (_logger.BeginScope(new Dictionary<string, object> { [ContextKey] = correlationId }))
            {
                await _next(context).ConfigureAwait(false);
            }
        }

        private static string GetOrCreateCorrelationId(HttpContext context)
        {
            if (context.Request.Headers.TryGetValue(HeaderKey, out var values))
            {
                var header = values.ToString();
                if (!string.IsNullOrWhiteSpace(header)) return header;
            }

            return Guid.NewGuid().ToString("D");
        }
    }
}
