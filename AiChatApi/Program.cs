using System;
using System.Net.Http.Headers;
using AiChatApi.Middleware;
using AiChatApi.Models;
using AiChatApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
// Use the SDK-provided OpenAPI support to avoid Swashbuckle/assembly conflicts
builder.Services.AddOpenApi();

// Configure Hugging Face settings
builder.Services.Configure<HuggingFaceSettings>(builder.Configuration.GetSection("HuggingFace"));
builder.Services.AddOptions<HuggingFaceSettings>()
    .Bind(builder.Configuration.GetSection("HuggingFace"))
    .ValidateDataAnnotations()
    .Validate(s => !string.IsNullOrWhiteSpace(s.ApiKey), "HuggingFace ApiKey must be provided")
    .ValidateOnStart();

// Polly retry policy for transient failures and timeouts
static IAsyncPolicy<System.Net.Http.HttpResponseMessage> GetRetryPolicy(int retries)
{
    return HttpPolicyExtensions
        .HandleTransientHttpError() // 5xx and 408
        .Or<TaskCanceledException>()
        .WaitAndRetryAsync(retries, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));
}

// Register a named HttpClient for Hugging Face
var hfSettings = builder.Configuration.GetSection("HuggingFace").Get<HuggingFaceSettings>() ?? new HuggingFaceSettings();
var hfTimeout = TimeSpan.FromSeconds(hfSettings.TimeoutSeconds > 0 ? hfSettings.TimeoutSeconds : 30);
builder.Services.AddHttpClient("HuggingFace", client =>
{
    var baseUrl = builder.Configuration["HuggingFace:BaseUrl"] ?? hfSettings.BaseUrl;
    client.BaseAddress = new Uri(baseUrl);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = hfTimeout;
});

// Register tooling and weather services
builder.Services.AddScoped<IWeatherService, WeatherService>();
builder.Services.AddScoped<IToolHandler, WeatherToolHandler>();
builder.Services.AddScoped<ToolExecutionService>();

// Register ITool implementations
builder.Services.AddScoped<AiChatApi.Tools.ITool, AiChatApi.Tools.CalculatorTool>();

// Adapter: expose ITool implementations as IToolHandler so ToolExecutionService can find them
builder.Services.AddScoped<IToolHandler>(sp =>
{
    // Resolve a concrete ITool (e.g., CalculatorTool) and wrap it with the adapter
    var tool = sp.GetRequiredService<AiChatApi.Tools.ITool>();
    var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AiChatApi.Services.ToolHandlerAdapter>>();
    return new AiChatApi.Services.ToolHandlerAdapter(tool, logger);
});

// Conversation memory and agent orchestrator
builder.Services.AddSingleton<IConversationMemory, ConversationMemoryService>();
builder.Services.AddScoped<AgentOrchestrator>();

builder.Services.AddScoped<IOpenAiService, HuggingFaceService>();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

var app = builder.Build();

// Middleware: correlation id, exception handling
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    // Map the OpenAPI endpoint using the SDK helper
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
