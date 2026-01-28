namespace VecSketch.Client.Models;

public enum ActionType
{
    CreateElement,
    MoveElement,
    DeleteElement,
    ModifyElement,
    SelectElement,
    DeselectElement,
    GroupElements,
    UngroupElements,
    AddLayer,
    DeleteLayer,
    SelectLayer,
    SetCanvasSize,
    SetCanvasFill,
    Clear
}

public class RecordedAction
{
    public ActionType Type { get; set; }
    public long TimestampMs { get; set; } // Milliseconds since recording started
    public string? ElementId { get; set; }
    public string? ElementType { get; set; }
    public string? ElementJson { get; set; } // Serialized element data
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? DeltaX { get; set; }
    public double? DeltaY { get; set; }
    public int? LayerIndex { get; set; }
    public string? LayerName { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Fill { get; set; }
    public string? GroupId { get; set; }
    public List<string>? ElementIds { get; set; }
}

public class Recording
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Untitled Recording";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int CanvasWidth { get; set; }
    public int CanvasHeight { get; set; }
    public string? CanvasFill { get; set; }
    public List<RecordedAction> Actions { get; set; } = new();
    public long DurationMs => Actions.Count > 0 ? Actions.Max(a => a.TimestampMs) : 0;
}
