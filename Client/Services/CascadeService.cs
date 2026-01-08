using System.Net.Http.Json;
using System.Text.Json;
using VecSketch.Client.Models;
using VecSketch.Shared;

namespace VecSketch.Client.Services;

public class CascadeService
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public CascadeMatrix CurrentMatrix { get; set; } = new();
    public List<CascadeMatrixPreset> Presets { get; private set; } = new();

    public event Action<CascadeStepProgress>? OnStepProgress;

    public CascadeService(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        InitializePresets();
    }

    private void InitializePresets()
    {
        // Preset: Claude Only (#1 = Claude)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Claude Only",
            Slot1Provider = "Anthropic",
            Slot1Model = "claude-sonnet-4-20250514"
        });

        // Preset: Ollama Only (#1 = Ollama)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Ollama Only",
            Slot1Provider = "Ollama",
            Slot1Model = "gemma3:4b",
            Slot1Endpoint = "http://localhost:11434"
        });

        // Preset: DeepSeek Only (#1 = DeepSeek)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "DeepSeek Only",
            Slot1Provider = "DeepSeek",
            Slot1Model = "deepseek-chat"
        });

        // Preset: Ollama → Claude (#1 = Ollama enhancer, #2 = Claude drawer)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Ollama → Claude",
            Slot1Provider = "Ollama",
            Slot1Model = "gemma3:4b",
            Slot1Endpoint = "http://localhost:11434",
            Slot2Provider = "Anthropic",
            Slot2Model = "claude-sonnet-4-20250514"
        });

        // Preset: Ollama → DeepSeek (#1 = Ollama enhancer, #2 = DeepSeek drawer)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Ollama → DeepSeek",
            Slot1Provider = "Ollama",
            Slot1Model = "gemma3:4b",
            Slot1Endpoint = "http://localhost:11434",
            Slot2Provider = "DeepSeek",
            Slot2Model = "deepseek-chat"
        });

        // Preset: Full Chain (#1 = Ollama, #2 = DeepSeek, #3 = Claude)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Ollama → DeepSeek → Claude",
            Slot1Provider = "Ollama",
            Slot1Model = "gemma3:4b",
            Slot1Endpoint = "http://localhost:11434",
            Slot2Provider = "DeepSeek",
            Slot2Model = "deepseek-chat",
            Slot3Provider = "Anthropic",
            Slot3Model = "claude-sonnet-4-20250514"
        });

        // Apply first preset as default
        ApplyPreset(Presets[0]);
    }

    public void ApplyPreset(CascadeMatrixPreset preset)
    {
        CurrentMatrix = new CascadeMatrix
        {
            Name = preset.Name,
            Slot1 = new CascadeSlot
            {
                Position = 1,
                Provider = preset.Slot1Provider ?? "",
                Model = preset.Slot1Model,
                Endpoint = preset.Slot1Endpoint
            },
            Slot2 = new CascadeSlot
            {
                Position = 2,
                Provider = preset.Slot2Provider ?? "",
                Model = preset.Slot2Model,
                Endpoint = preset.Slot2Endpoint
            },
            Slot3 = new CascadeSlot
            {
                Position = 3,
                Provider = preset.Slot3Provider ?? "",
                Model = preset.Slot3Model,
                Endpoint = preset.Slot3Endpoint
            }
        };
    }

    public async Task<CascadeExecuteResult> ExecuteAsync(string prompt, Dictionary<string, string>? apiKeys = null, bool reverse = false)
    {
        var activeSlots = CurrentMatrix.GetActiveSlots();

        if (activeSlots.Count == 0)
        {
            return new CascadeExecuteResult
            {
                Success = false,
                Error = "No slots configured - add at least one provider"
            };
        }

        // Apply API keys to slots
        foreach (var slot in activeSlots)
        {
            if (apiKeys != null)
            {
                if (slot.Provider == "Anthropic" && apiKeys.TryGetValue("anthropic", out var anthropicKey))
                    slot.ApiKey = anthropicKey;
                else if (slot.Provider == "DeepSeek" && apiKeys.TryGetValue("deepseek", out var deepseekKey))
                    slot.ApiKey = deepseekKey;
            }
        }

        var request = new CascadeExecuteRequest
        {
            UserPrompt = prompt,
            Matrix = CurrentMatrix,
            Reverse = reverse
        };

        try
        {
            var response = await _http.PostAsJsonAsync("/api/ai/cascade", request, _jsonOptions);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new CascadeExecuteResult
                {
                    Success = false,
                    Error = $"Server error ({response.StatusCode}): {responseBody}"
                };
            }

            var cascadeResponse = JsonSerializer.Deserialize<CascadeExecuteResponse>(responseBody, _jsonOptions);

            if (cascadeResponse == null)
            {
                return new CascadeExecuteResult
                {
                    Success = false,
                    Error = "Failed to parse cascade response"
                };
            }

            // Notify progress for each step
            foreach (var stepResult in cascadeResponse.StepResults)
            {
                OnStepProgress?.Invoke(stepResult);
            }

            if (!cascadeResponse.Success)
            {
                return new CascadeExecuteResult
                {
                    Success = false,
                    Error = cascadeResponse.Error,
                    StepResults = cascadeResponse.StepResults,
                    TotalElapsedMs = cascadeResponse.TotalElapsedMs
                };
            }

            // Parse the final output as drawing commands
            var drawingResult = ParseDrawingResponse(cascadeResponse.FinalOutput ?? "");

            return new CascadeExecuteResult
            {
                Success = drawingResult.Success,
                Error = drawingResult.Error,
                DrawingResponse = drawingResult.Response,
                Thinking = drawingResult.Thinking,
                RawResponse = cascadeResponse.FinalOutput,
                StepResults = cascadeResponse.StepResults,
                TotalElapsedMs = cascadeResponse.TotalElapsedMs
            };
        }
        catch (HttpRequestException ex)
        {
            return new CascadeExecuteResult
            {
                Success = false,
                Error = $"Network error: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new CascadeExecuteResult
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
            var originalResponse = responseText;

            // DeepSeek-R1 wraps thinking in <think>...</think> tags - remove it
            var thinkEndIndex = responseText.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);
            if (thinkEndIndex >= 0)
            {
                responseText = responseText.Substring(thinkEndIndex + 8).Trim();
            }

            // Handle ```json ... ```
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

            // Find JSON with Commands
            var jsonText = ExtractJsonWithCommands(responseText);
            if (jsonText == null)
            {
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
        var searchStart = 0;
        while (true)
        {
            var braceStart = text.IndexOf('{', searchStart);
            if (braceStart < 0) return null;

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
                if (candidate.Contains("\"Commands\"", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Contains("\"commands\"", StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            searchStart = braceStart + 1;
        }
    }

    public void ClearSlot(int position)
    {
        var slot = position switch
        {
            1 => CurrentMatrix.Slot1,
            2 => CurrentMatrix.Slot2,
            3 => CurrentMatrix.Slot3,
            _ => null
        };

        if (slot != null)
        {
            slot.Provider = "";
            slot.Model = null;
            slot.ApiKey = null;
            slot.Endpoint = null;
        }
    }

    public void SetSlot(int position, string provider, string? model = null, string? endpoint = null)
    {
        var slot = position switch
        {
            1 => CurrentMatrix.Slot1,
            2 => CurrentMatrix.Slot2,
            3 => CurrentMatrix.Slot3,
            _ => null
        };

        if (slot != null)
        {
            slot.Provider = provider;
            slot.Model = model;
            slot.Endpoint = endpoint;
        }
    }

    /// <summary>
    /// Gets a description of how the current matrix will route prompts
    /// </summary>
    public string GetRoutingDescription()
    {
        var slots = CurrentMatrix.GetActiveSlots();

        if (slots.Count == 0)
            return "No providers configured";

        if (slots.Count == 1)
            return $"#{slots[0].Position} ({slots[0].Provider}) draws directly";

        var descriptions = new List<string>();
        for (int i = 0; i < slots.Count; i++)
        {
            var slot = slots[i];
            var isLast = i == slots.Count - 1;

            if (isLast)
                descriptions.Add($"#{slot.Position} ({slot.Provider}) draws");
            else
                descriptions.Add($"#{slot.Position} ({slot.Provider}) enhances");
        }

        return string.Join(" → ", descriptions);
    }
}

public class CascadeMatrixPreset
{
    public string Name { get; set; } = "";
    public string? Slot1Provider { get; set; }
    public string? Slot1Model { get; set; }
    public string? Slot1Endpoint { get; set; }
    public string? Slot2Provider { get; set; }
    public string? Slot2Model { get; set; }
    public string? Slot2Endpoint { get; set; }
    public string? Slot3Provider { get; set; }
    public string? Slot3Model { get; set; }
    public string? Slot3Endpoint { get; set; }
}

public class CascadeExecuteResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public DrawingResponse? DrawingResponse { get; init; }
    public string? Thinking { get; init; }
    public string? RawResponse { get; init; }
    public List<CascadeStepProgress> StepResults { get; init; } = new();
    public long TotalElapsedMs { get; init; }
}
