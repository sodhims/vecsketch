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
