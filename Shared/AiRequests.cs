namespace VecSketch.Shared;

public class AiGenerateRequest
{
    public string Provider { get; set; } = "Ollama";
    public string Prompt { get; set; } = "";
    public string SystemPrompt { get; set; } = "";
    public string? ApiKey { get; set; }
    public string? OllamaEndpoint { get; set; }
    public string? OllamaModel { get; set; }
}

public class TestKeyRequest
{
    public string ApiKey { get; set; } = "";
}

public class AiGenerateResponse
{
    public string? Response { get; set; }
    public string? Error { get; set; }
}

public class TestKeyResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
}

public class OllamaModelsResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<OllamaModel> Models { get; set; } = new();
}

public class OllamaModel
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string? ModifiedAt { get; set; }
}

// === Cascade Architecture (Matrix-Based) ===

/// <summary>
/// Matrix slot for cascade - determines role based on position and what slots are filled
/// </summary>
public class CascadeSlot
{
    public int Position { get; set; } // 1, 2, or 3
    public string Provider { get; set; } = ""; // Ollama, Anthropic, DeepSeek, or empty
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; } // For Ollama
    public bool IsEmpty => string.IsNullOrEmpty(Provider);
}

/// <summary>
/// Matrix-based cascade configuration
/// Routing logic:
/// - Only #1 filled → #1 draws
/// - #1 and #2 filled → #1 enhances, #2 draws
/// - #1, #2, #3 filled → #1 enhances for #2, #2 enhances for #3, #3 draws
/// - #2 only → #2 draws
/// - #2 and #3 → #2 enhances, #3 draws
/// - etc.
/// </summary>
public class CascadeMatrix
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Custom";
    public CascadeSlot Slot1 { get; set; } = new() { Position = 1 };
    public CascadeSlot Slot2 { get; set; } = new() { Position = 2 };
    public CascadeSlot Slot3 { get; set; } = new() { Position = 3 };

    /// <summary>
    /// Get active slots in order (non-empty only)
    /// </summary>
    public List<CascadeSlot> GetActiveSlots()
    {
        var slots = new List<CascadeSlot>();
        if (!Slot1.IsEmpty) slots.Add(Slot1);
        if (!Slot2.IsEmpty) slots.Add(Slot2);
        if (!Slot3.IsEmpty) slots.Add(Slot3);
        return slots;
    }
}

/// <summary>
/// Request to execute a matrix-based cascade
/// </summary>
public class CascadeExecuteRequest
{
    public string UserPrompt { get; set; } = "";
    public CascadeMatrix Matrix { get; set; } = new();
    public bool Reverse { get; set; } = false;
}

// Legacy types kept for compatibility
public enum CascadeStepRole
{
    PromptEnhancer,
    PromptTranslator,
    Drawer
}

public class CascadeStep
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public CascadeStepRole Role { get; set; }
    public string Provider { get; set; } = "Ollama";
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
    public string? Endpoint { get; set; }
    public string? SystemPrompt { get; set; }
    public bool Enabled { get; set; } = true;
}

public class CascadeConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Default";
    public List<CascadeStep> Steps { get; set; } = new();
    public bool IsDefault { get; set; }
}

/// <summary>
/// Progress update for a cascade step
/// </summary>
public class CascadeStepProgress
{
    public string StepId { get; set; } = "";
    public string StepName { get; set; } = "";
    public int StepIndex { get; set; }
    public int TotalSteps { get; set; }
    public CascadeStepStatus Status { get; set; }
    public string? Input { get; set; }  // The prompt sent to this step
    public string? Output { get; set; }
    public string? Error { get; set; }
    public long ElapsedMs { get; set; }
}

public enum CascadeStepStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}

/// <summary>
/// Final result of cascade execution
/// </summary>
public class CascadeExecuteResponse
{
    public bool Success { get; set; }
    public string? FinalOutput { get; set; }
    public string? Error { get; set; }
    public List<CascadeStepProgress> StepResults { get; set; } = new();
    public long TotalElapsedMs { get; set; }
}
