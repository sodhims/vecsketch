using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using VecSketch.Models;

namespace VecSketch.Services;

public enum AiProvider
{
    Anthropic,
    Ollama
}

public class AiDrawingService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    private readonly string _systemPrompt = """
        You are a vector graphics assistant. Given a description, output JSON commands to draw it.

        Available commands (use "type" as discriminator):
        - {"type":"setCanvas", "Width":n, "Height":n, "Fill":"#hex"}
        - {"type":"rect", "X":n, "Y":n, "W":n, "H":n, "Fill":"#hex", "Stroke":"#hex", "StrokeWidth":n}
        - {"type":"circle", "Cx":n, "Cy":n, "R":n, "Fill":"#hex", "Stroke":"#hex"}
        - {"type":"ellipse", "Cx":n, "Cy":n, "Rx":n, "Ry":n, "Fill":"#hex", "Stroke":"#hex"}
        - {"type":"line", "X1":n, "Y1":n, "X2":n, "Y2":n, "Stroke":"#hex", "StrokeWidth":n}
        - {"type":"path", "D":"svg path string", "Fill":"#hex", "Stroke":"#hex"}
        - {"type":"text", "X":n, "Y":n, "Content":"text", "Font":"Arial", "Size":n, "Fill":"#hex"}
        - {"type":"polygon", "Points":[{"X":n,"Y":n},...], "Fill":"#hex", "Stroke":"#hex"}
        - {"type":"arc", "Cx":n, "Cy":n, "R":n, "StartAngle":degrees, "EndAngle":degrees, "Stroke":"#hex"}
        - {"type":"bezier", "X1":n, "Y1":n, "Cx1":n, "Cy1":n, "Cx2":n, "Cy2":n, "X2":n, "Y2":n, "Stroke":"#hex"}
        - {"type":"group", "Children":[...commands...], "Id":"optional-name"}
        - {"type":"image", "X":n, "Y":n, "Width":n, "Height":n, "Src":"url or data uri"}

        Respond ONLY with valid JSON in this exact format:
        {"Thinking":"brief explanation of approach","Commands":[...array of commands...]}

        Guidelines:
        - Default canvas is 800x600 unless user specifies size
        - Center designs on the canvas
        - Use professional, pleasing colors
        - Use appropriate stroke widths (1-4 for fine details, 4-8 for emphasis)
        - For complex shapes, use SVG path "D" commands (M, L, C, Q, A, Z)
        - Layer elements logically (background first, details last)
        - Add visual depth with subtle color variations
        """;

    public string? ApiKey { get; set; }
    public AiProvider Provider { get; set; } = AiProvider.Ollama;
    public string OllamaEndpoint { get; set; } = "http://10.10.48.219:11434";
    public string OllamaModel { get; set; } = "deepseek-r1:14b";

    public AiDrawingService(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<AiDrawingResult> GenerateAsync(string prompt)
    {
        return Provider switch
        {
            AiProvider.Anthropic => await GenerateWithAnthropicAsync(prompt),
            AiProvider.Ollama => await GenerateWithOllamaAsync(prompt),
            _ => new AiDrawingResult { Success = false, Error = "Unknown provider" }
        };
    }

    public async Task<ApiKeyTestResult> TestApiKeyAsync(string apiKey)
    {
        try
        {
            // Send a minimal request to test the key
            var request = new ClaudeRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 10,
                System = "Reply with OK",
                Messages = new[]
                {
                    new ClaudeMessage { Role = "user", Content = "test" }
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Content = JsonContent.Create(request, options: _jsonOptions);

            var response = await _http.SendAsync(httpRequest);

            if (response.IsSuccessStatusCode)
            {
                return new ApiKeyTestResult { Success = true };
            }

            var errorBody = await response.Content.ReadAsStringAsync();

            // Parse common error cases
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return new ApiKeyTestResult { Success = false, Error = "Invalid API key" };
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new ApiKeyTestResult { Success = false, Error = "API key lacks permissions" };
            }

            return new ApiKeyTestResult { Success = false, Error = $"API error: {response.StatusCode}" };
        }
        catch (HttpRequestException ex)
        {
            return new ApiKeyTestResult { Success = false, Error = $"Network error: {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new ApiKeyTestResult { Success = false, Error = $"Error: {ex.Message}" };
        }
    }

    private async Task<AiDrawingResult> GenerateWithOllamaAsync(string prompt)
    {
        try
        {
            var request = new OllamaRequest
            {
                Model = OllamaModel,
                Prompt = $"{_systemPrompt}\n\nUser request: {prompt}",
                Stream = false
            };

            var endpoint = $"{OllamaEndpoint.TrimEnd('/')}/api/generate";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
            httpRequest.Content = JsonContent.Create(request, options: _jsonOptions);

            var response = await _http.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new AiDrawingResult
                {
                    Success = false,
                    Error = $"Ollama API error ({response.StatusCode}): {errorBody}"
                };
            }

            var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaResponse>(_jsonOptions);

            if (ollamaResponse == null || string.IsNullOrEmpty(ollamaResponse.Response))
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = "Empty response from Ollama"
                };
            }

            return ParseDrawingResponse(ollamaResponse.Response);
        }
        catch (HttpRequestException ex)
        {
            return new AiDrawingResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}. Check that Ollama is running at {OllamaEndpoint}"
            };
        }
        catch (Exception ex)
        {
            return new AiDrawingResult
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private async Task<AiDrawingResult> GenerateWithAnthropicAsync(string prompt)
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            return new AiDrawingResult
            {
                Success = false,
                Error = "API key not configured. Set your Anthropic API key first."
            };
        }

        try
        {
            var request = new ClaudeRequest
            {
                Model = "claude-sonnet-4-20250514",
                MaxTokens = 4096,
                System = _systemPrompt,
                Messages = new[]
                {
                    new ClaudeMessage { Role = "user", Content = prompt }
                }
            };

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            httpRequest.Headers.Add("x-api-key", ApiKey);
            httpRequest.Headers.Add("anthropic-version", "2023-06-01");
            httpRequest.Content = JsonContent.Create(request, options: _jsonOptions);

            var response = await _http.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                return new AiDrawingResult
                {
                    Success = false,
                    Error = $"API error ({response.StatusCode}): {errorBody}"
                };
            }

            var claudeResponse = await response.Content.ReadFromJsonAsync<ClaudeResponse>(_jsonOptions);

            if (claudeResponse?.Content == null || claudeResponse.Content.Length == 0)
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = "Empty response from Claude"
                };
            }

            return ParseDrawingResponse(claudeResponse.Content[0].Text);
        }
        catch (HttpRequestException ex)
        {
            return new AiDrawingResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new AiDrawingResult
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}"
            };
        }
    }

    private AiDrawingResult ParseDrawingResponse(string responseText)
    {
        try
        {
            // Try to extract JSON from the response (in case the model adds extra text)
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                responseText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
            }

            var drawingResponse = JsonSerializer.Deserialize<DrawingResponse>(responseText, _jsonOptions);

            if (drawingResponse == null)
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = "Failed to parse drawing commands",
                    RawResponse = responseText
                };
            }

            return new AiDrawingResult
            {
                Success = true,
                Response = drawingResponse,
                Thinking = drawingResponse.Thinking,
                RawResponse = responseText
            };
        }
        catch (JsonException ex)
        {
            return new AiDrawingResult
            {
                Success = false,
                Error = $"JSON parse error: {ex.Message}",
                RawResponse = responseText
            };
        }
    }
}

// Ollama API request/response models
public class OllamaRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = "";

    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

public class OllamaResponse
{
    [JsonPropertyName("response")]
    public string Response { get; set; } = "";

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

// Claude API request/response models
public class ClaudeRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("system")]
    public string System { get; set; } = "";

    [JsonPropertyName("messages")]
    public ClaudeMessage[] Messages { get; set; } = Array.Empty<ClaudeMessage>();
}

public class ClaudeMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

public class ClaudeResponse
{
    [JsonPropertyName("content")]
    public ClaudeContent[] Content { get; set; } = Array.Empty<ClaudeContent>();

    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
}

public class ClaudeContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";
}

// Result wrapper
public class AiDrawingResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public DrawingResponse? Response { get; init; }
    public string? Thinking { get; init; }
    public string? RawResponse { get; init; }
}

public class ApiKeyTestResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
}
