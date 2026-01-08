using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using VecSketch.Shared;

namespace VecSketch.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AiController : ControllerBase
{
    private readonly HttpClient _http;
    private readonly ILogger<AiController> _logger;

    public AiController(IHttpClientFactory httpClientFactory, ILogger<AiController> logger)
    {
        _http = httpClientFactory.CreateClient();
        _logger = logger;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] AiGenerateRequest request)
    {
        try
        {
            if (request.Provider == "Anthropic")
            {
                return await GenerateWithAnthropic(request);
            }
            else if (request.Provider == "Ollama")
            {
                return await GenerateWithOllama(request);
            }

            return BadRequest(new { error = "Unknown provider" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI drawing");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("ollama-models")]
    public async Task<IActionResult> GetOllamaModels([FromQuery] string? endpoint)
    {
        try
        {
            var ollamaEndpoint = endpoint?.TrimEnd('/') ?? "http://localhost:11434";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"{ollamaEndpoint}/api/tags");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _http.SendAsync(httpRequest, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return Ok(new OllamaModelsResponse
                {
                    Success = false,
                    Error = $"Ollama returned {response.StatusCode}"
                });
            }

            using var doc = JsonDocument.Parse(responseBody);
            var models = new List<OllamaModel>();

            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    var name = model.GetProperty("name").GetString() ?? "";
                    var size = model.TryGetProperty("size", out var sizeVal) ? sizeVal.GetInt64() : 0;
                    var modifiedAt = model.TryGetProperty("modified_at", out var modVal) ? modVal.GetString() : null;

                    models.Add(new OllamaModel
                    {
                        Name = name,
                        Size = size,
                        ModifiedAt = modifiedAt
                    });
                }
            }

            return Ok(new OllamaModelsResponse
            {
                Success = true,
                Models = models
            });
        }
        catch (TaskCanceledException)
        {
            return Ok(new OllamaModelsResponse
            {
                Success = false,
                Error = "Connection timed out - is Ollama running?"
            });
        }
        catch (HttpRequestException ex)
        {
            return Ok(new OllamaModelsResponse
            {
                Success = false,
                Error = $"Cannot connect to Ollama: {ex.Message}"
            });
        }
    }

    [HttpPost("test-key")]
    public async Task<IActionResult> TestApiKey([FromBody] TestKeyRequest request)
    {
        try
        {
            var testRequest = new
            {
                model = "claude-sonnet-4-20250514",
                max_tokens = 10,
                system = "Reply with OK",
                messages = new[] { new { role = "user", content = "test" } }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", request.ApiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(testRequest),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _http.SendAsync(httpRequest);

            if (response.IsSuccessStatusCode)
            {
                return Ok(new { success = true });
            }

            var statusCode = (int)response.StatusCode;
            var error = statusCode switch
            {
                401 => "Invalid API key",
                403 => "API key lacks permissions",
                _ => $"API error: {response.StatusCode}"
            };

            return Ok(new { success = false, error });
        }
        catch (HttpRequestException ex)
        {
            return Ok(new { success = false, error = $"Network error: {ex.Message}" });
        }
    }

    private async Task<IActionResult> GenerateWithAnthropic(AiGenerateRequest request)
    {
        if (string.IsNullOrEmpty(request.ApiKey))
        {
            return BadRequest(new { error = "API key required for Anthropic" });
        }

        var claudeRequest = new
        {
            model = "claude-sonnet-4-20250514",
            max_tokens = 4096,
            system = request.SystemPrompt,
            messages = new[] { new { role = "user", content = request.Prompt } }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", request.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(claudeRequest),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, new { error = responseBody });
        }

        // Parse Claude response and extract text
        using var doc = JsonDocument.Parse(responseBody);
        var content = doc.RootElement.GetProperty("content");
        var text = content[0].GetProperty("text").GetString();

        return Ok(new { response = text });
    }

    private async Task<IActionResult> GenerateWithOllama(AiGenerateRequest request)
    {
        var endpoint = request.OllamaEndpoint?.TrimEnd('/') ?? "http://localhost:11434";
        var model = request.OllamaModel ?? "deepseek-r1:14b";

        var ollamaRequest = new
        {
            model = model,
            prompt = $"{request.SystemPrompt}\n\nUser request: {request.Prompt}",
            stream = false
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/generate");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(ollamaRequest),
            Encoding.UTF8,
            "application/json"
        );

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode, new { error = responseBody });
        }

        // Parse Ollama response
        using var doc = JsonDocument.Parse(responseBody);
        var text = doc.RootElement.GetProperty("response").GetString();

        return Ok(new { response = text });
    }

    // === Matrix-Based Cascade Execution ===

    [HttpPost("cascade")]
    public async Task<IActionResult> ExecuteCascade([FromBody] CascadeExecuteRequest request)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var stepResults = new List<CascadeStepProgress>();
        var currentPrompt = request.UserPrompt;

        // Get active slots from the matrix
        var activeSlots = request.Matrix.GetActiveSlots();

        if (activeSlots.Count == 0)
        {
            return Ok(new CascadeExecuteResponse
            {
                Success = false,
                Error = "No slots configured in the cascade matrix"
            });
        }

        // Reverse slot order if requested
        if (request.Reverse)
        {
            activeSlots.Reverse();
            _logger.LogInformation("Executing cascade in REVERSE order with {Count} active slots", activeSlots.Count);
        }
        else
        {
            _logger.LogInformation("Executing cascade with {Count} active slots", activeSlots.Count);
        }

        for (int i = 0; i < activeSlots.Count; i++)
        {
            var slot = activeSlots[i];
            var isLastSlot = i == activeSlots.Count - 1;
            var stepSw = System.Diagnostics.Stopwatch.StartNew();

            // Determine role based on position in the active slots
            // Last slot always draws, others enhance
            var role = isLastSlot ? "drawer" : "enhancer";
            var stepName = $"#{slot.Position} {slot.Provider} ({role})";

            var progress = new CascadeStepProgress
            {
                StepId = $"slot{slot.Position}",
                StepName = stepName,
                StepIndex = i,
                TotalSteps = activeSlots.Count,
                Status = CascadeStepStatus.Running
            };

            try
            {
                _logger.LogInformation("Cascade slot #{Position} ({Index}/{Total}): {Provider}/{Model} as {Role}",
                    slot.Position, i + 1, activeSlots.Count, slot.Provider, slot.Model, role);

                var systemPrompt = GetSystemPromptForSlot(isLastSlot);
                var result = await ExecuteSlotAsync(slot, currentPrompt, systemPrompt);

                stepSw.Stop();
                progress.ElapsedMs = stepSw.ElapsedMilliseconds;

                if (result.Success)
                {
                    progress.Status = CascadeStepStatus.Completed;
                    progress.Output = result.Output;
                    currentPrompt = result.Output ?? currentPrompt;

                    _logger.LogInformation("Slot #{Position} completed in {Ms}ms", slot.Position, progress.ElapsedMs);
                }
                else
                {
                    progress.Status = CascadeStepStatus.Failed;
                    progress.Error = result.Error;

                    _logger.LogWarning("Slot #{Position} failed: {Error}", slot.Position, result.Error);

                    stepResults.Add(progress);
                    sw.Stop();

                    return Ok(new CascadeExecuteResponse
                    {
                        Success = false,
                        Error = $"Slot #{slot.Position} ({slot.Provider}) failed: {result.Error}",
                        StepResults = stepResults,
                        TotalElapsedMs = sw.ElapsedMilliseconds
                    });
                }
            }
            catch (Exception ex)
            {
                stepSw.Stop();
                progress.Status = CascadeStepStatus.Failed;
                progress.Error = ex.Message;
                progress.ElapsedMs = stepSw.ElapsedMilliseconds;
                stepResults.Add(progress);

                _logger.LogError(ex, "Slot #{Position} threw exception", slot.Position);

                sw.Stop();
                return Ok(new CascadeExecuteResponse
                {
                    Success = false,
                    Error = $"Slot #{slot.Position} error: {ex.Message}",
                    StepResults = stepResults,
                    TotalElapsedMs = sw.ElapsedMilliseconds
                });
            }

            stepResults.Add(progress);
        }

        sw.Stop();

        return Ok(new CascadeExecuteResponse
        {
            Success = true,
            FinalOutput = currentPrompt,
            StepResults = stepResults,
            TotalElapsedMs = sw.ElapsedMilliseconds
        });
    }

    /// <summary>
    /// Gets system prompt based on whether this is the final (drawer) slot or an enhancer slot
    /// </summary>
    private string GetSystemPromptForSlot(bool isDrawer)
    {
        if (isDrawer)
        {
            return """
                You are a vector graphics assistant. Given a description, output JSON commands to draw it.

                Available commands (use "type" as discriminator):
                - {"type":"setCanvas", "Width":n, "Height":n, "Fill":"#hex"}
                - {"type":"rect", "X":n, "Y":n, "W":n, "H":n, "Fill":"#hex", "Stroke":"#hex", "StrokeWidth":n}
                - {"type":"circle", "Cx":n, "Cy":n, "R":n, "Fill":"#hex", "Stroke":"#hex"}
                - {"type":"ellipse", "Cx":n, "Cy":n, "Rx":n, "Ry":n, "Fill":"#hex", "Stroke":"#hex"}
                - {"type":"line", "X1":n, "Y1":n, "X2":n, "Y2":n, "Stroke":"#hex", "StrokeWidth":n}
                - {"type":"path", "D":"svg path string", "Fill":"#hex", "Stroke":"#hex", "StrokeWidth":n}
                - {"type":"text", "X":n, "Y":n, "Content":"text", "Font":"Arial", "Size":n, "Fill":"#hex"}
                - {"type":"polygon", "Points":[{"X":n,"Y":n},...], "Fill":"#hex", "Stroke":"#hex"}
                - {"type":"arc", "Cx":n, "Cy":n, "R":n, "StartAngle":degrees, "EndAngle":degrees, "Stroke":"#hex"}
                - {"type":"bezier", "X1":n, "Y1":n, "Cx1":n, "Cy1":n, "Cx2":n, "Cy2":n, "X2":n, "Y2":n, "Stroke":"#hex"}

                Respond ONLY with valid JSON: {"Thinking":"brief explanation","Commands":[...]}
                """;
        }
        else
        {
            return """
                You are a prompt enhancement specialist. Your job is to take a user's drawing request
                and expand it into a detailed, clear description that will help an AI create better vector graphics.

                Add details about:
                - Composition and layout
                - Style (minimalist, detailed, realistic, cartoon, etc.)
                - Colors and color palette suggestions
                - Important visual elements to include
                - Proportions and positioning
                - Specific coordinates and sizes (assume 800x600 canvas)

                Output ONLY the enhanced prompt, nothing else. Do not include explanations or meta-commentary.
                The next AI in the chain will use your output to generate drawing commands.
                """;
        }
    }

    private async Task<(bool Success, string? Output, string? Error)> ExecuteSlotAsync(
        CascadeSlot slot, string prompt, string systemPrompt)
    {
        try
        {
            string? output = null;

            if (slot.Provider == "Anthropic")
            {
                if (string.IsNullOrEmpty(slot.ApiKey))
                    return (false, null, "Anthropic API key required");
                output = await CallAnthropicAsync(slot.ApiKey, slot.Model ?? "claude-sonnet-4-20250514", systemPrompt, prompt);
            }
            else if (slot.Provider == "Ollama")
            {
                var endpoint = slot.Endpoint?.TrimEnd('/') ?? "http://localhost:11434";
                output = await CallOllamaAsync(endpoint, slot.Model ?? "gemma3:4b", systemPrompt, prompt);
            }
            else if (slot.Provider == "DeepSeek")
            {
                // DeepSeek works without API key for free tier
                output = await CallDeepSeekAsync(slot.ApiKey, slot.Model ?? "deepseek-chat", systemPrompt, prompt);
            }
            else if (slot.Provider == "OpenAI")
            {
                if (string.IsNullOrEmpty(slot.ApiKey))
                    return (false, null, "OpenAI API key required");
                output = await CallOpenAIAsync(slot.ApiKey, slot.Model ?? "gpt-4o-mini", systemPrompt, prompt);
            }
            else
            {
                return (false, null, $"Unknown provider: {slot.Provider}");
            }

            return (true, output, null);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    // Legacy support
    private string GetSystemPromptForRole(CascadeStepRole role, string? customPrompt)
    {
        if (!string.IsNullOrEmpty(customPrompt))
            return customPrompt;

        return role switch
        {
            CascadeStepRole.Drawer => GetSystemPromptForSlot(true),
            _ => GetSystemPromptForSlot(false)
        };
    }

    private async Task<string> CallAnthropicAsync(string apiKey, string model, string systemPrompt, string prompt)
    {
        var request = new
        {
            model = model,
            max_tokens = 4096,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.Add("x-api-key", apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Anthropic API error: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
    }

    private async Task<string> CallOllamaAsync(string endpoint, string model, string systemPrompt, string prompt)
    {
        var request = new
        {
            model = model,
            prompt = $"{systemPrompt}\n\nUser request: {prompt}",
            stream = false
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/api/generate");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Ollama API error: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.GetProperty("response").GetString() ?? "";
    }

    private async Task<string> CallDeepSeekAsync(string? apiKey, string model, string systemPrompt, string prompt)
    {
        var request = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = prompt }
            },
            max_tokens = 4096
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/v1/chat/completions");
        if (!string.IsNullOrEmpty(apiKey))
            httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"DeepSeek API error: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }

    private async Task<string> CallOpenAIAsync(string apiKey, string model, string systemPrompt, string prompt)
    {
        var request = new
        {
            model = model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = prompt }
            },
            max_tokens = 4096
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        httpRequest.Headers.Add("Authorization", $"Bearer {apiKey}");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(httpRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new Exception($"OpenAI API error: {responseBody}");

        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString() ?? "";
    }
}
