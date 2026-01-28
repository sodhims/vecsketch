using System.Net.Http.Json;
using System.Text.Json;
using VecSketch.Client.Models;
using VecSketch.Shared;

namespace VecSketch.Client.Services;

public class CascadeService
{
    private readonly HttpClient _http;
    private readonly HttpClient _directOllamaHttp;
    private readonly JsonSerializerOptions _jsonOptions;

    public CascadeMatrix CurrentMatrix { get; set; } = new();
    public List<CascadeMatrixPreset> Presets { get; private set; } = new();

    public event Action<CascadeStepProgress>? OnStepProgress;

    private readonly string _enhancerPrompt = """
        You are a prompt enhancer for a vector graphics AI. Take the user's simple description and create
        a detailed, structured prompt that will produce beautiful vector graphics.

        Guidelines:
        - Add specific colors (use hex codes when possible)
        - Specify positions and sizes relative to an 800x600 canvas
        - Add visual details like gradients, shadows, highlights
        - Structure the description from background to foreground
        - Be specific about shapes and their relationships

        Return ONLY the enhanced prompt, no explanations.
        """;

    private readonly string _drawingPrompt = """
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

    public CascadeService(HttpClient http)
    {
        _http = http;
        _directOllamaHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
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

        // Preset: Ollama Only (#1 = Ollama Local)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Ollama Local",
            Slot1Provider = "OllamaLocal",
            Slot1Model = "gemma3:4b"
        });

        // Preset: DeepSeek Only (#1 = DeepSeek)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "DeepSeek Only",
            Slot1Provider = "DeepSeek",
            Slot1Model = "deepseek-chat"
        });

        // Preset: OllamaLocal → Claude (#1 = Ollama enhancer, #2 = Claude drawer)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "OllamaLocal → Claude",
            Slot1Provider = "OllamaLocal",
            Slot1Model = "gemma3:4b",
            Slot2Provider = "Anthropic",
            Slot2Model = "claude-sonnet-4-20250514"
        });

        // Preset: OllamaLocal → DeepSeek (#1 = Ollama enhancer, #2 = DeepSeek drawer)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "OllamaLocal → DeepSeek",
            Slot1Provider = "OllamaLocal",
            Slot1Model = "gemma3:4b",
            Slot2Provider = "DeepSeek",
            Slot2Model = "deepseek-chat"
        });

        // Preset: Full Chain (#1 = OllamaLocal, #2 = DeepSeek, #3 = Claude)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "OllamaLocal → DeepSeek → Claude",
            Slot1Provider = "OllamaLocal",
            Slot1Model = "gemma3:4b",
            Slot2Provider = "DeepSeek",
            Slot2Model = "deepseek-chat",
            Slot3Provider = "Anthropic",
            Slot3Model = "claude-sonnet-4-20250514"
        });

        // Preset: OpenAI Only (#1 = OpenAI)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "OpenAI Only",
            Slot1Provider = "OpenAI",
            Slot1Model = "gpt-4o-mini"
        });

        // Preset: OllamaLocal → OpenAI (#1 = Ollama enhancer, #2 = OpenAI drawer)
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "OllamaLocal → OpenAI",
            Slot1Provider = "OllamaLocal",
            Slot1Model = "gemma3:4b",
            Slot2Provider = "OpenAI",
            Slot2Model = "gpt-4o-mini"
        });

        // Preset: Ollama Local (Llama) - for users with llama models
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "OllamaLocal (Llama 3.3)",
            Slot1Provider = "OllamaLocal",
            Slot1Model = "llama3.3"
        });

        // Preset: Remote Ollama - for network Ollama instances
        Presets.Add(new CascadeMatrixPreset
        {
            Name = "Ollama (Remote)",
            Slot1Provider = "Ollama",
            Slot1Model = "llama3.2",
            Slot1Endpoint = "http://your-server:11434"
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
                else if (slot.Provider == "OpenAI" && apiKeys.TryGetValue("openai", out var openaiKey))
                    slot.ApiKey = openaiKey;
                // DeepSeek doesn't require an API key
            }
        }

        // Check if ALL slots are OllamaLocal - if so, execute directly from browser
        var hasOllamaLocal = activeSlots.Any(s => s.Provider == "OllamaLocal");
        var allOllamaLocal = activeSlots.All(s => s.Provider == "OllamaLocal");

        if (allOllamaLocal)
        {
            return await ExecuteDirectOllamaAsync(prompt, activeSlots, reverse);
        }

        // If mixed providers with OllamaLocal, we can't route through server
        if (hasOllamaLocal)
        {
            return new CascadeExecuteResult
            {
                Success = false,
                Error = "Mixed OllamaLocal with server-side providers not yet supported. Use either all OllamaLocal or all server-side providers."
            };
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

    private async Task<CascadeExecuteResult> ExecuteDirectOllamaAsync(string prompt, List<CascadeSlot> slots, bool reverse)
    {
        var stepResults = new List<CascadeStepProgress>();
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var currentInput = prompt;

        if (reverse)
        {
            slots = slots.AsEnumerable().Reverse().ToList();
        }

        try
        {
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var isLast = i == slots.Count - 1;
                var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();

                // Determine the system prompt based on whether this is the final slot
                var systemPrompt = isLast ? _drawingPrompt : _enhancerPrompt;
                var role = isLast ? "drawer" : "enhancer";

                // Report step starting
                var stepProgress = new CascadeStepProgress
                {
                    StepId = $"slot-{slot.Position}",
                    StepName = $"#{slot.Position} OllamaLocal ({slot.Model ?? "unknown"}) - {role}",
                    StepIndex = i,
                    TotalSteps = slots.Count,
                    Status = CascadeStepStatus.Running,
                    Input = currentInput  // Store the input prompt
                };
                OnStepProgress?.Invoke(stepProgress);

                // Call local Ollama
                var ollamaRequest = new
                {
                    model = slot.Model ?? "llama3.2",
                    prompt = $"{systemPrompt}\n\nUser request: {currentInput}",
                    stream = false
                };

                var response = await _directOllamaHttp.PostAsJsonAsync(
                    "http://localhost:11434/api/generate",
                    ollamaRequest);

                stepStopwatch.Stop();

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    stepProgress.Status = CascadeStepStatus.Failed;
                    stepProgress.Error = $"Ollama error ({response.StatusCode}): {errorBody}";
                    stepProgress.ElapsedMs = stepStopwatch.ElapsedMilliseconds;
                    OnStepProgress?.Invoke(stepProgress);
                    stepResults.Add(stepProgress);

                    return new CascadeExecuteResult
                    {
                        Success = false,
                        Error = stepProgress.Error,
                        StepResults = stepResults,
                        TotalElapsedMs = totalStopwatch.ElapsedMilliseconds
                    };
                }

                var responseBody = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseBody);
                var aiResponse = doc.RootElement.GetProperty("response").GetString() ?? "";

                // Update step progress
                stepProgress.Output = aiResponse;
                stepProgress.Status = CascadeStepStatus.Completed;
                stepProgress.ElapsedMs = stepStopwatch.ElapsedMilliseconds;
                OnStepProgress?.Invoke(stepProgress);
                stepResults.Add(stepProgress);

                // Pass output to next step
                currentInput = aiResponse;
            }

            totalStopwatch.Stop();

            // Parse the final output as drawing commands
            var drawingResult = ParseDrawingResponse(currentInput);

            return new CascadeExecuteResult
            {
                Success = drawingResult.Success,
                Error = drawingResult.Error,
                DrawingResponse = drawingResult.Response,
                Thinking = drawingResult.Thinking,
                RawResponse = currentInput,
                StepResults = stepResults,
                TotalElapsedMs = totalStopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            return new CascadeExecuteResult
            {
                Success = false,
                Error = $"Cannot connect to local Ollama: {ex.Message}. Is Ollama running?",
                StepResults = stepResults,
                TotalElapsedMs = totalStopwatch.ElapsedMilliseconds
            };
        }
        catch (TaskCanceledException)
        {
            return new CascadeExecuteResult
            {
                Success = false,
                Error = "Request timed out - Ollama may be processing a large model. Try a smaller model or increase wait time.",
                StepResults = stepResults,
                TotalElapsedMs = totalStopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new CascadeExecuteResult
            {
                Success = false,
                Error = $"Unexpected error: {ex.Message}",
                StepResults = stepResults,
                TotalElapsedMs = totalStopwatch.ElapsedMilliseconds
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

    public async Task<OllamaModelsResult> GetLocalOllamaModelsAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var response = await _directOllamaHttp.GetAsync(
                "http://localhost:11434/api/tags",
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return new OllamaModelsResult
                {
                    Success = false,
                    Error = $"Ollama returned {response.StatusCode}"
                };
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var models = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var modelsArray))
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var nameProp))
                    {
                        models.Add(nameProp.GetString() ?? "");
                    }
                }
            }

            return new OllamaModelsResult
            {
                Success = true,
                Models = models
            };
        }
        catch (HttpRequestException ex)
        {
            return new OllamaModelsResult
            {
                Success = false,
                Error = $"Cannot connect to local Ollama: {ex.Message}"
            };
        }
        catch (TaskCanceledException)
        {
            return new OllamaModelsResult
            {
                Success = false,
                Error = "Connection timed out - is Ollama running on this machine?"
            };
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
