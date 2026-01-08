using System.Net.Http.Json;
using System.Text.Json;
using VecSketch.Client.Models;
using VecSketch.Shared;

namespace VecSketch.Client.Services;

public enum AiProvider
{
    Anthropic,
    Ollama,
    OllamaLocal
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
    public AiProvider Provider { get; set; } = AiProvider.OllamaLocal;
    public string OllamaEndpoint { get; set; } = "http://10.10.48.219:11434";
    public string OllamaModel { get; set; } = "llama3.2";

    public string EffectiveOllamaEndpoint => Provider == AiProvider.OllamaLocal
        ? "http://localhost:11434"
        : OllamaEndpoint;

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
                Provider = Provider == AiProvider.OllamaLocal ? "Ollama" : Provider.ToString(),
                Prompt = prompt,
                SystemPrompt = _systemPrompt,
                ApiKey = ApiKey,
                OllamaEndpoint = EffectiveOllamaEndpoint,
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

    public async Task<OllamaModelsResult> GetOllamaModelsAsync(string? endpoint = null)
    {
        try
        {
            var effectiveEndpoint = endpoint ?? EffectiveOllamaEndpoint;
            var url = $"/api/ai/ollama-models?endpoint={Uri.EscapeDataString(effectiveEndpoint)}";

            var response = await _http.GetFromJsonAsync<OllamaModelsResponse>(url, _jsonOptions);

            if (response == null)
            {
                return new OllamaModelsResult
                {
                    Success = false,
                    Error = "No response from server"
                };
            }

            return new OllamaModelsResult
            {
                Success = response.Success,
                Error = response.Error,
                Models = response.Models.Select(m => m.Name).ToList()
            };
        }
        catch (HttpRequestException ex)
        {
            return new OllamaModelsResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new OllamaModelsResult
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
            var originalResponse = responseText;

            // DeepSeek-R1 wraps thinking in <think>...</think> tags - remove it
            var thinkEndIndex = responseText.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEndIndex >= 0)
            {
                responseText = responseText.Substring(thinkEndIndex + 8).Trim();
            }

            // Also handle if wrapped in ```json ... ```
            if (responseText.Contains("```json"))
            {
                var jsonBlockStart = responseText.IndexOf("```json") + 7;
                var jsonBlockEnd = responseText.IndexOf("```", jsonBlockStart);
                if (jsonBlockEnd > jsonBlockStart)
                {
                    responseText = responseText.Substring(jsonBlockStart, jsonBlockEnd - jsonBlockStart).Trim();
                }
            }
            else if (responseText.Contains("```"))
            {
                var codeBlockStart = responseText.IndexOf("```") + 3;
                // Skip language identifier if present (e.g., ```json\n)
                var newlineAfterTicks = responseText.IndexOf('\n', codeBlockStart);
                if (newlineAfterTicks > 0 && newlineAfterTicks - codeBlockStart < 10)
                {
                    codeBlockStart = newlineAfterTicks + 1;
                }
                var codeBlockEnd = responseText.IndexOf("```", codeBlockStart);
                if (codeBlockEnd > codeBlockStart)
                {
                    responseText = responseText.Substring(codeBlockStart, codeBlockEnd - codeBlockStart).Trim();
                }
            }

            // Find the JSON object containing "Commands" array
            var jsonText = ExtractJsonWithCommands(responseText);
            if (jsonText == null)
            {
                // Fallback: try first { to last }
                var jsonStart = responseText.IndexOf('{');
                var jsonEnd = responseText.LastIndexOf('}');
                if (jsonStart >= 0 && jsonEnd > jsonStart)
                {
                    jsonText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                }
                else
                {
                    return new AiDrawingResult
                    {
                        Success = false,
                        Error = "No JSON found in response",
                        RawResponse = originalResponse
                    };
                }
            }

            var drawingResponse = JsonSerializer.Deserialize<DrawingResponse>(jsonText, _jsonOptions);

            if (drawingResponse == null)
            {
                return new AiDrawingResult
                {
                    Success = false,
                    Error = "Failed to parse drawing commands",
                    RawResponse = originalResponse
                };
            }

            return new AiDrawingResult
            {
                Success = true,
                Response = drawingResponse,
                Thinking = drawingResponse.Thinking,
                RawResponse = originalResponse
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

    private string? ExtractJsonWithCommands(string text)
    {
        // Find a JSON object that contains "Commands"
        var searchStart = 0;
        while (true)
        {
            var braceStart = text.IndexOf('{', searchStart);
            if (braceStart < 0) return null;

            // Find matching closing brace
            var depth = 1;
            var pos = braceStart + 1;
            while (pos < text.Length && depth > 0)
            {
                if (text[pos] == '{') depth++;
                else if (text[pos] == '}') depth--;
                pos++;
            }

            if (depth == 0)
            {
                var candidate = text.Substring(braceStart, pos - braceStart);
                // Check if this JSON has a Commands array
                if (candidate.Contains("\"Commands\"", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Contains("\"commands\"", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            searchStart = braceStart + 1;
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

public class OllamaModelsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public List<string> Models { get; init; } = new();
}
