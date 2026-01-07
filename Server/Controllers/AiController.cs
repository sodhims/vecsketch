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
}
