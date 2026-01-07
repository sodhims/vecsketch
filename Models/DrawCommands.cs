using System.Text.Json.Serialization;

namespace VecSketch.Models;

/// <summary>
/// Base class for all drawing commands that the editor understands.
/// Uses polymorphic JSON serialization for command parsing.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SetCanvas), "setCanvas")]
[JsonDerivedType(typeof(DrawRect), "rect")]
[JsonDerivedType(typeof(DrawCircle), "circle")]
[JsonDerivedType(typeof(DrawEllipse), "ellipse")]
[JsonDerivedType(typeof(DrawLine), "line")]
[JsonDerivedType(typeof(DrawPolyline), "polyline")]
[JsonDerivedType(typeof(DrawPolygon), "polygon")]
[JsonDerivedType(typeof(DrawPath), "path")]
[JsonDerivedType(typeof(DrawText), "text")]
[JsonDerivedType(typeof(DrawImage), "image")]
[JsonDerivedType(typeof(DrawArc), "arc")]
[JsonDerivedType(typeof(DrawBezier), "bezier")]
[JsonDerivedType(typeof(Group), "group")]
[JsonDerivedType(typeof(Transform), "transform")]
[JsonDerivedType(typeof(SetStyle), "setStyle")]
[JsonDerivedType(typeof(Clear), "clear")]
[JsonDerivedType(typeof(Delete), "delete")]
[JsonDerivedType(typeof(Duplicate), "duplicate")]
[JsonDerivedType(typeof(BringToFront), "bringToFront")]
[JsonDerivedType(typeof(SendToBack), "sendToBack")]
public abstract record DrawCommand;

// ============================================
// Canvas Commands
// ============================================

/// <summary>Set canvas dimensions and background</summary>
public record SetCanvas(
    double Width,
    double Height,
    string Fill = "white"
) : DrawCommand;

/// <summary>Clear all elements from the canvas</summary>
public record Clear : DrawCommand;

// ============================================
// Basic Shape Commands
// ============================================

/// <summary>Draw a rectangle</summary>
public record DrawRect(
    double X,
    double Y,
    double W,
    double H,
    string? Fill = null,
    string? Stroke = null,
    double StrokeWidth = 1,
    double Rx = 0,  // Corner radius X
    double Ry = 0,  // Corner radius Y
    double Rotation = 0,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw a circle</summary>
public record DrawCircle(
    double Cx,
    double Cy,
    double R,
    string? Fill = null,
    string? Stroke = null,
    double StrokeWidth = 1,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw an ellipse</summary>
public record DrawEllipse(
    double Cx,
    double Cy,
    double Rx,
    double Ry,
    string? Fill = null,
    string? Stroke = null,
    double StrokeWidth = 1,
    double Rotation = 0,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw a line</summary>
public record DrawLine(
    double X1,
    double Y1,
    double X2,
    double Y2,
    string Stroke = "black",
    double StrokeWidth = 1,
    string? StrokeDashArray = null,
    string LineCap = "round",  // butt, round, square
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw a polyline (connected line segments, not closed)</summary>
public record DrawPolyline(
    List<PointD> Points,
    string Stroke = "black",
    double StrokeWidth = 1,
    string? Fill = null,
    string LineCap = "round",
    string LineJoin = "round",  // miter, round, bevel
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw a polygon (closed shape)</summary>
public record DrawPolygon(
    List<PointD> Points,
    string? Fill = null,
    string? Stroke = null,
    double StrokeWidth = 1,
    string LineJoin = "round",
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

// ============================================
// Advanced Shape Commands
// ============================================

/// <summary>Draw using SVG path syntax</summary>
public record DrawPath(
    string D,  // SVG path data
    string? Fill = null,
    string? Stroke = null,
    double StrokeWidth = 1,
    string? StrokeDashArray = null,
    string FillRule = "nonzero",  // nonzero, evenodd
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw an arc</summary>
public record DrawArc(
    double Cx,
    double Cy,
    double R,
    double StartAngle,  // in degrees
    double EndAngle,    // in degrees
    string Stroke = "black",
    double StrokeWidth = 1,
    string? Fill = null,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw a cubic bezier curve</summary>
public record DrawBezier(
    double X1, double Y1,    // Start point
    double Cx1, double Cy1,  // Control point 1
    double Cx2, double Cy2,  // Control point 2
    double X2, double Y2,    // End point
    string Stroke = "black",
    double StrokeWidth = 1,
    string? Fill = null,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

// ============================================
// Text & Image Commands
// ============================================

/// <summary>Draw text</summary>
public record DrawText(
    double X,
    double Y,
    string Content,
    string Font = "Arial",
    double Size = 16,
    string Fill = "black",
    string Anchor = "start",  // start, middle, end
    string Baseline = "auto", // auto, middle, hanging
    bool Bold = false,
    bool Italic = false,
    double Rotation = 0,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

/// <summary>Draw an image</summary>
public record DrawImage(
    double X,
    double Y,
    double Width,
    double Height,
    string Src,  // URL or data URI
    bool PreserveAspectRatio = true,
    double Rotation = 0,
    double Opacity = 1,
    string? Id = null
) : DrawCommand;

// ============================================
// Grouping & Transform Commands
// ============================================

/// <summary>Group multiple commands together</summary>
public record Group(
    List<DrawCommand> Children,
    string? Id = null,
    double Opacity = 1
) : DrawCommand;

/// <summary>Apply transform to subsequent commands or by ID</summary>
public record Transform(
    string? TargetId = null,  // If null, wraps Children
    double TranslateX = 0,
    double TranslateY = 0,
    double ScaleX = 1,
    double ScaleY = 1,
    double Rotation = 0,
    double OriginX = 0,
    double OriginY = 0,
    List<DrawCommand>? Children = null
) : DrawCommand;

// ============================================
// Style Commands
// ============================================

/// <summary>Set default style for subsequent commands</summary>
public record SetStyle(
    string? Fill = null,
    string? Stroke = null,
    double? StrokeWidth = null,
    double? Opacity = null,
    string? Font = null,
    double? FontSize = null
) : DrawCommand;

// ============================================
// Manipulation Commands
// ============================================

/// <summary>Delete element by ID</summary>
public record Delete(string Id) : DrawCommand;

/// <summary>Duplicate element by ID</summary>
public record Duplicate(
    string Id,
    double OffsetX = 10,
    double OffsetY = 10
) : DrawCommand;

/// <summary>Bring element to front</summary>
public record BringToFront(string Id) : DrawCommand;

/// <summary>Send element to back</summary>
public record SendToBack(string Id) : DrawCommand;

// ============================================
// Helper Types
// ============================================

/// <summary>Point with double coordinates</summary>
public record PointD(double X, double Y);

// ============================================
// Response Types
// ============================================

/// <summary>Full drawing response with optional reasoning</summary>
public record DrawingResponse
{
    public string? Thinking { get; init; }
    public List<DrawCommand> Commands { get; init; } = new();
}

/// <summary>Result of executing commands</summary>
public record CommandResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int ElementsCreated { get; init; }
    public int ElementsModified { get; init; }
    public int ElementsDeleted { get; init; }
    public List<string> CreatedIds { get; init; } = new();
}
