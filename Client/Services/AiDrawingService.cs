using System.Net.Http.Json;
using System.Text.Json;
using VecSketch.Client.Models;
using VecSketch.Shared;

namespace VecSketch.Client.Services;

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
        try
        {
            var request = new AiGenerateRequest
            {
                Provider = Provider.ToString(),
                Prompt = prompt,
                SystemPrompt = _systemPrompt,
                ApiKey = ApiKey,
                OllamaEndpoint = OllamaEndpoint,
                OllamaModel = OllamaModel
            };

            var response = await _http.PostAsJsonAsync("/api/ai/generate", request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = $"Server error ({response.StatusCode}): {responseBody}"
                };
            }

            using var doc = JsonDocument.Parse(responseBody);

            if (doc.RootElement.TryGetProperty("error", out var errorProp))
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = errorProp.GetString()
                };
            }

            var aiResponse = doc.RootElement.GetProperty("response").GetString();
            if (string.IsNullOrEmpty(aiResponse))
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = "Empty response from AI"
                };
            }

            return ParseDrawingResponse(aiResponse);
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

    public async Task<ApiKeyTestResult> TestApiKeyAsync(string apiKey)
    {
        try
        {
            var request = new TestKeyRequest { ApiKey = apiKey };
            var response = await _http.PostAsJsonAsync("/api/ai/test-key", request);
            var result = await response.Content.ReadFromJsonAsync<TestKeyResponse>(_jsonOptions);

            return new ApiKeyTestResult
            {
                Success = result?.Success ?? false,
                Error = result?.Error
            };
        }
        catch (HttpRequestException ex)
        {
            return new ApiKeyTestResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new ApiKeyTestResult
            {
                Success = false,
                Error = $"Error: {ex.Message}"
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
