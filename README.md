
# AiChatApi

A production-style ASP.NET Core Web API demonstrating an AI Agent pattern with tool calling, conversation memory, orchestration logic, and structured JSON responses.

This repository is useful as a portfolio piece or a starter template for building production-ready AI agents on .NET.

## Technologies

- .NET 10
- ASP.NET Core Web API
- System.Text.Json
- Polly (resilience / retry)
- Hugging Face Inference API (configurable)

## Highlights

- Agent orchestration (`AgentOrchestrator`) that drives the LLM, invokes tools, and maintains conversation memory.
- Tool abstraction (`AiChatApi.Tools.ITool`) with a sample `CalculatorTool` and an existing `WeatherToolHandler`/`ToolExecutionService`.
- `HuggingFaceService` implements `IOpenAiService` and supports both the HF inference API and the router/chat endpoint.
- In-memory conversation memory via `ConversationMemoryService` (replaceable with persistent stores).
- Correlation ID middleware, structured logging, and robust error handling.

## Repository layout (key files)

- `AiChatApi/Program.cs` — application startup, DI and middleware
- `AiChatApi/Controllers/AgentController.cs` — `POST /api/agent/chat` (agent entrypoint)
- `AiChatApi/Services/AgentOrchestrator.cs` — agent loop and orchestration
- `AiChatApi/Services/HuggingFaceService.cs` — LLM client and response parsing
- `AiChatApi/Tools/ITool.cs` and implementations (`CalculatorTool`)
- `AiChatApi/Services/ConversationMemoryService.cs` — in-memory memory store
- `AiChatApi/Services/ToolExecutionService.cs` — tool registry and execution guard
- `AiChatApi/appsettings.json` — sample configuration

## Quickstart (local)

1. Requirements

   - .NET 10 SDK (run `dotnet --info`)
   - A Hugging Face API key (or another LLM provider via `IOpenAiService`)

2. Provide secrets (recommended)

   - Environment variables (preferred):

     - Windows (PowerShell):
       ```powershell
       $env:HuggingFace__ApiKey = "hf_..."
       dotnet run --project AiChatApi
       ```

     - macOS / Linux (bash):
       ```bash
       export HuggingFace__ApiKey="hf_..."
       dotnet run --project AiChatApi
       ```

   - OR use `dotnet user-secrets` for local development only.

3. Run

```bash
dotnet run --project AiChatApi
```

Open the URL printed to the console and use the API endpoints below.

## API Endpoints

- `POST /api/chat` — simple passthrough to the LLM. Body: `{ "message": "..." }`. Response: `{ "reply": "..." }`.

- `POST /api/agent/chat` — agent endpoint. Body:

```json
{ "sessionId": "session-123", "message": "What's the weather in London?" }
```

Response:

```json
{ "finalAnswer": "..." }
```

## Agent behavior

1. Load conversation memory for the `SessionId`.
2. Build a prompt: system instruction + memory + user input. The agent instructs the LLM to return strict JSON with this schema:

```json
{
  "type": "answer" | "tool_call",
  "tool": "calculator" | "get_weather" | null,
  "arguments": { ... } | null,
  "response": "string" | null
}
```

3. If `type == "tool_call"`, the orchestrator finds the DI-registered tool by name, executes it once, feeds the tool result back to the LLM, and returns the final answer. The service accepts either an `arguments` object or a legacy `input` string; for weather calls the system prompt asks for `"tool": "get_weather"` with `{"city":"..."}`.
4. If the LLM returns `type == "answer"` or non-JSON, the orchestrator returns the assistant text directly.

## Configuration

Edit `AiChatApi/appsettings.json` or use environment variables. Example Hugging Face configuration:

```json
"HuggingFace": {
  "ApiKey": "<your-key>",
  "BaseUrl": "https://api-inference.huggingface.co/",
  "Model": "meta-llama/Llama-3.1-8B-Instruct:novita",
  "Temperature": 0.7,
  "MaxNewTokens": 256,
  "TimeoutSeconds": 30,
  "RetryCount": 3
}
```

## Security

- Never commit API keys. Use environment variables or a secrets manager (Azure Key Vault, AWS Secrets Manager) for production.
- `CalculatorTool` performs basic input validation. For production, prefer a dedicated, audited math expression evaluator.

## Extending the system

- To add a tool: implement `AiChatApi.Tools.ITool`, register it in `Program.cs` (e.g. `builder.Services.AddScoped<ITool, MyTool>()`) and the orchestrator will discover it.
 - To add a tool: implement `AiChatApi.Tools.ITool` and register it in `Program.cs` (for example `builder.Services.AddScoped<ITool, MyTool>()`). The project includes a `ToolHandlerAdapter` which adapts registered `ITool` implementations to the `IToolHandler` interface used by the orchestrator. You can also implement `IToolHandler` directly if you need structured `JsonElement` arguments.
- Replace `ConversationMemoryService` with a persistent implementation of `IConversationMemory` for durable sessions.

## Observability

- Use the `X-Correlation-ID` header to correlate requests. Middleware attaches the correlation id to logs and responses.
- Logs include structured data (message lengths, LLM responses, tool calls). Configure production sinks (Serilog, Application Insights) as needed.

## Testing

- Unit tests should mock `IOpenAiService` to validate orchestrator logic and tools.
- Add tests for `ToolExecutionService` and each `ITool` implementation.

## Next steps

- Add streaming and long-running job support.
- Persist memory with a scalable store (Redis, CosmosDB).
- Harden tool execution sandboxing and safety checks.

## License

- Add your LICENSE file of choice.

---

If you would like, I can add example unit tests, CI pipeline templates, or a Postman collection for the agent endpoint.
