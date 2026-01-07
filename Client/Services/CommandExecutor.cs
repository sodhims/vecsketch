using System.Text.Json;
using VecSketch.Client.Models;

namespace VecSketch.Client.Services;

/// <summary>
/// Executes DrawCommands and converts them to VectorElements.
/// Supports parsing JSON command strings and executing command objects.
/// </summary>
public class CommandExecutor
{
    private readonly JsonSerializerOptions _jsonOptions;

    // Default style state
    private string? _defaultFill;
    private string? _defaultStroke;
    private double? _defaultStrokeWidth;
    private double? _defaultOpacity;
    private string? _defaultFont;
    private double? _defaultFontSize;

    public CommandExecutor()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Parse and execute a JSON drawing response.
    /// </summary>
    public CommandResult ExecuteJson(string json, List<VectorElement> elements, ref double canvasWidth, ref double canvasHeight, ref string canvasFill)
    {
        try
        {
            var response = JsonSerializer.Deserialize<DrawingResponse>(json, _jsonOptions);
            if (response == null)
                return new CommandResult { Success = false, Error = "Failed to parse JSON" };

            return Execute(response.Commands, elements, ref canvasWidth, ref canvasHeight, ref canvasFill);
        }
        catch (JsonException ex)
        {
            return new CommandResult { Success = false, Error = $"JSON parse error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Execute a list of draw commands.
    /// </summary>
    public CommandResult Execute(List<DrawCommand> commands, List<VectorElement> elements, ref double canvasWidth, ref double canvasHeight, ref string canvasFill)
    {
        var result = new CommandResult { Success = true };
        var createdIds = new List<string>();
        int created = 0, modified = 0, deleted = 0;

        foreach (var cmd in commands)
        {
            try
            {
                var (c, m, d, ids) = ExecuteCommand(cmd, elements, ref canvasWidth, ref canvasHeight, ref canvasFill);
                created += c;
                modified += m;
                deleted += d;
                createdIds.AddRange(ids);
            }
            catch (Exception ex)
            {
                return new CommandResult
                {
                    Success = false,
                    Error = $"Error executing {cmd.GetType().Name}: {ex.Message}",
                    ElementsCreated = created,
                    ElementsModified = modified,
                    ElementsDeleted = deleted,
                    CreatedIds = createdIds
                };
            }
        }

        return new CommandResult
        {
            Success = true,
            ElementsCreated = created,
            ElementsModified = modified,
            ElementsDeleted = deleted,
            CreatedIds = createdIds
        };
    }

    private (int created, int modified, int deleted, List<string> ids) ExecuteCommand(
        DrawCommand cmd,
        List<VectorElement> elements,
        ref double canvasWidth,
        ref double canvasHeight,
        ref string canvasFill)
    {
        var ids = new List<string>();

        switch (cmd)
        {
            case SetCanvas setCanvas:
                canvasWidth = setCanvas.Width;
                canvasHeight = setCanvas.Height;
                canvasFill = setCanvas.Fill;
                return (0, 1, 0, ids);

            case Clear:
                var count = elements.Count;
                elements.Clear();
                return (0, 0, count, ids);

            case SetStyle setStyle:
                _defaultFill = setStyle.Fill ?? _defaultFill;
                _defaultStroke = setStyle.Stroke ?? _defaultStroke;
                _defaultStrokeWidth = setStyle.StrokeWidth ?? _defaultStrokeWidth;
                _defaultOpacity = setStyle.Opacity ?? _defaultOpacity;
                _defaultFont = setStyle.Font ?? _defaultFont;
                _defaultFontSize = setStyle.FontSize ?? _defaultFontSize;
                return (0, 0, 0, ids);

            case DrawRect rect:
                var rectElem = new RectElement
                {
                    Id = rect.Id ?? Guid.NewGuid().ToString(),
                    X = rect.X,
                    Y = rect.Y,
                    Width = rect.W,
                    Height = rect.H,
                    Fill = rect.Fill ?? _defaultFill ?? "none",
                    Stroke = rect.Stroke ?? _defaultStroke ?? "#000000",
                    StrokeWidth = rect.StrokeWidth > 0 ? rect.StrokeWidth : _defaultStrokeWidth ?? 2
                };
                elements.Add(rectElem);
                ids.Add(rectElem.Id);
                return (1, 0, 0, ids);

            case DrawCircle circle:
                var circleElem = new CircleElement
                {
                    Id = circle.Id ?? Guid.NewGuid().ToString(),
                    Cx = circle.Cx,
                    Cy = circle.Cy,
                    Radius = circle.R,
                    Fill = circle.Fill ?? _defaultFill ?? "none",
                    Stroke = circle.Stroke ?? _defaultStroke ?? "#000000",
                    StrokeWidth = circle.StrokeWidth > 0 ? circle.StrokeWidth : _defaultStrokeWidth ?? 2
                };
                elements.Add(circleElem);
                ids.Add(circleElem.Id);
                return (1, 0, 0, ids);

            case DrawEllipse ellipse:
                var ellipseElem = new EllipseElement
                {
                    Id = ellipse.Id ?? Guid.NewGuid().ToString(),
                    Cx = ellipse.Cx,
                    Cy = ellipse.Cy,
                    Rx = ellipse.Rx,
                    Ry = ellipse.Ry,
                    Fill = ellipse.Fill ?? _defaultFill ?? "none",
                    Stroke = ellipse.Stroke ?? _defaultStroke ?? "#000000",
                    StrokeWidth = ellipse.StrokeWidth > 0 ? ellipse.StrokeWidth : _defaultStrokeWidth ?? 2
                };
                elements.Add(ellipseElem);
                ids.Add(ellipseElem.Id);
                return (1, 0, 0, ids);

            case DrawLine line:
                var lineElem = new LineElement
                {
                    Id = line.Id ?? Guid.NewGuid().ToString(),
                    X1 = line.X1,
                    Y1 = line.Y1,
                    X2 = line.X2,
                    Y2 = line.Y2,
                    Stroke = line.Stroke ?? _defaultStroke ?? "#000000",
                    StrokeWidth = line.StrokeWidth > 0 ? line.StrokeWidth : _defaultStrokeWidth ?? 2,
                    Fill = "none"
                };
                elements.Add(lineElem);
                ids.Add(lineElem.Id);
                return (1, 0, 0, ids);

            case DrawPolyline polyline:
                var polylineElem = new PathElement
                {
                    Id = polyline.Id ?? Guid.NewGuid().ToString(),
                    Points = polyline.Points.Select(p => new Point(p.X, p.Y)).ToList(),
                    Stroke = polyline.Stroke ?? _defaultStroke ?? "#000000",
                    StrokeWidth = polyline.StrokeWidth > 0 ? polyline.StrokeWidth : _defaultStrokeWidth ?? 2,
                    Fill = polyline.Fill ?? "none"
                };
                elements.Add(polylineElem);
                ids.Add(polylineElem.Id);
                return (1, 0, 0, ids);

            case DrawPolygon polygon:
                var polygonElem = CreatePolygonFromPoints(polygon);
                elements.Add(polygonElem);
                ids.Add(polygonElem.Id);
                return (1, 0, 0, ids);

            case DrawPath path:
                var pathElem = new SvgPathElement
                {
                    Id = path.Id ?? Guid.NewGuid().ToString(),
                    D = path.D,
                    Fill = path.Fill ?? _defaultFill ?? "none",
                    Stroke = path.Stroke ?? _defaultStroke ?? "#000000",
                    StrokeWidth = path.StrokeWidth > 0 ? path.StrokeWidth : _defaultStrokeWidth ?? 2
                };
                elements.Add(pathElem);
                ids.Add(pathElem.Id);
                return (1, 0, 0, ids);

            case DrawArc arc:
                var arcPath = CreateArcPath(arc);
                elements.Add(arcPath);
                ids.Add(arcPath.Id);
                return (1, 0, 0, ids);

            case DrawBezier bezier:
                var bezierPath = CreateBezierPath(bezier);
                elements.Add(bezierPath);
                ids.Add(bezierPath.Id);
                return (1, 0, 0, ids);

            case DrawText text:
                var textElem = new TextElement
                {
                    Id = text.Id ?? Guid.NewGuid().ToString(),
                    X = text.X,
                    Y = text.Y,
                    Text = text.Content,
                    FontFamily = text.Font ?? _defaultFont ?? "Arial, sans-serif",
                    FontSize = text.Size > 0 ? text.Size : _defaultFontSize ?? 16,
                    Fill = text.Fill ?? _defaultFill ?? "#000000",
                    Stroke = "none",
                    StrokeWidth = 0
                };
                elements.Add(textElem);
                ids.Add(textElem.Id);
                return (1, 0, 0, ids);

            case DrawImage image:
                var imageElem = new ImageElement
                {
                    Id = image.Id ?? Guid.NewGuid().ToString(),
                    X = image.X,
                    Y = image.Y,
                    Width = image.Width,
                    Height = image.Height,
                    DataUrl = image.Src
                };
                elements.Add(imageElem);
                ids.Add(imageElem.Id);
                return (1, 0, 0, ids);

            case Group group:
                int groupCreated = 0;
                foreach (var child in group.Children)
                {
                    var (c, m, d, childIds) = ExecuteCommand(child, elements, ref canvasWidth, ref canvasHeight, ref canvasFill);
                    groupCreated += c;
                    ids.AddRange(childIds);
                }
                return (groupCreated, 0, 0, ids);

            case Transform transform:
                if (transform.TargetId != null)
                {
                    var target = elements.FirstOrDefault(e => e.Id == transform.TargetId);
                    if (target != null)
                    {
                        ApplyTransform(target, transform);
                        return (0, 1, 0, ids);
                    }
                }
                else if (transform.Children != null)
                {
                    foreach (var child in transform.Children)
                    {
                        var (c, m, d, childIds) = ExecuteCommand(child, elements, ref canvasWidth, ref canvasHeight, ref canvasFill);
                        ids.AddRange(childIds);
                        // Apply transform to newly created elements
                        foreach (var id in childIds)
                        {
                            var elem = elements.FirstOrDefault(e => e.Id == id);
                            if (elem != null)
                                ApplyTransform(elem, transform);
                        }
                    }
                }
                return (ids.Count, 0, 0, ids);

            case Delete delete:
                var toDelete = elements.FirstOrDefault(e => e.Id == delete.Id);
                if (toDelete != null)
                {
                    elements.Remove(toDelete);
                    return (0, 0, 1, ids);
                }
                return (0, 0, 0, ids);

            case Duplicate duplicate:
                var toDupe = elements.FirstOrDefault(e => e.Id == duplicate.Id);
                if (toDupe != null)
                {
                    var clone = CloneElement(toDupe, duplicate.OffsetX, duplicate.OffsetY);
                    if (clone != null)
                    {
                        elements.Add(clone);
                        ids.Add(clone.Id);
                        return (1, 0, 0, ids);
                    }
                }
                return (0, 0, 0, ids);

            case BringToFront bringToFront:
                var toFront = elements.FirstOrDefault(e => e.Id == bringToFront.Id);
                if (toFront != null)
                {
                    elements.Remove(toFront);
                    elements.Add(toFront);
                    return (0, 1, 0, ids);
                }
                return (0, 0, 0, ids);

            case SendToBack sendToBack:
                var toBack = elements.FirstOrDefault(e => e.Id == sendToBack.Id);
                if (toBack != null)
                {
                    elements.Remove(toBack);
                    elements.Insert(0, toBack);
                    return (0, 1, 0, ids);
                }
                return (0, 0, 0, ids);

            default:
                throw new NotSupportedException($"Unknown command type: {cmd.GetType().Name}");
        }
    }

    private TriangleElement CreatePolygonFromPoints(DrawPolygon polygon)
    {
        // For simplicity, convert polygon to triangle if 3 points, otherwise use first 3
        var points = polygon.Points;
        if (points.Count < 3)
            throw new ArgumentException("Polygon requires at least 3 points");

        return new TriangleElement
        {
            Id = polygon.Id ?? Guid.NewGuid().ToString(),
            X1 = points[0].X,
            Y1 = points[0].Y,
            X2 = points[1].X,
            Y2 = points[1].Y,
            X3 = points[2].X,
            Y3 = points[2].Y,
            Fill = polygon.Fill ?? _defaultFill ?? "none",
            Stroke = polygon.Stroke ?? _defaultStroke ?? "#000000",
            StrokeWidth = polygon.StrokeWidth > 0 ? polygon.StrokeWidth : _defaultStrokeWidth ?? 2
        };
    }

    private SvgPathElement CreateArcPath(DrawArc arc)
    {
        // Convert arc to SVG path
        var startRad = arc.StartAngle * Math.PI / 180;
        var endRad = arc.EndAngle * Math.PI / 180;

        var x1 = arc.Cx + arc.R * Math.Cos(startRad);
        var y1 = arc.Cy + arc.R * Math.Sin(startRad);
        var x2 = arc.Cx + arc.R * Math.Cos(endRad);
        var y2 = arc.Cy + arc.R * Math.Sin(endRad);

        var largeArc = Math.Abs(arc.EndAngle - arc.StartAngle) > 180 ? 1 : 0;
        var sweep = arc.EndAngle > arc.StartAngle ? 1 : 0;

        var d = $"M {x1:F2} {y1:F2} A {arc.R} {arc.R} 0 {largeArc} {sweep} {x2:F2} {y2:F2}";

        return new SvgPathElement
        {
            Id = arc.Id ?? Guid.NewGuid().ToString(),
            D = d,
            Fill = arc.Fill ?? "none",
            Stroke = arc.Stroke ?? _defaultStroke ?? "#000000",
            StrokeWidth = arc.StrokeWidth > 0 ? arc.StrokeWidth : _defaultStrokeWidth ?? 2
        };
    }

    private SvgPathElement CreateBezierPath(DrawBezier bezier)
    {
        var d = $"M {bezier.X1:F2} {bezier.Y1:F2} C {bezier.Cx1:F2} {bezier.Cy1:F2}, {bezier.Cx2:F2} {bezier.Cy2:F2}, {bezier.X2:F2} {bezier.Y2:F2}";

        return new SvgPathElement
        {
            Id = bezier.Id ?? Guid.NewGuid().ToString(),
            D = d,
            Fill = bezier.Fill ?? "none",
            Stroke = bezier.Stroke ?? _defaultStroke ?? "#000000",
            StrokeWidth = bezier.StrokeWidth > 0 ? bezier.StrokeWidth : _defaultStrokeWidth ?? 2
        };
    }

    private void ApplyTransform(VectorElement element, Transform transform)
    {
        // Apply translation to element based on its type
        switch (element)
        {
            case RectElement rect:
                rect.X += transform.TranslateX;
                rect.Y += transform.TranslateY;
                rect.Width *= transform.ScaleX;
                rect.Height *= transform.ScaleY;
                break;
            case CircleElement circle:
                circle.Cx += transform.TranslateX;
                circle.Cy += transform.TranslateY;
                circle.Radius *= Math.Max(transform.ScaleX, transform.ScaleY);
                break;
            case EllipseElement ellipse:
                ellipse.Cx += transform.TranslateX;
                ellipse.Cy += transform.TranslateY;
                ellipse.Rx *= transform.ScaleX;
                ellipse.Ry *= transform.ScaleY;
                break;
            case LineElement line:
                line.X1 += transform.TranslateX;
                line.Y1 += transform.TranslateY;
                line.X2 += transform.TranslateX;
                line.Y2 += transform.TranslateY;
                break;
            case TextElement text:
                text.X += transform.TranslateX;
                text.Y += transform.TranslateY;
                break;
            case ImageElement image:
                image.X += transform.TranslateX;
                image.Y += transform.TranslateY;
                image.Width *= transform.ScaleX;
                image.Height *= transform.ScaleY;
                break;
            case PathElement path:
                for (int i = 0; i < path.Points.Count; i++)
                {
                    var p = path.Points[i];
                    path.Points[i] = new Point(
                        p.X + transform.TranslateX,
                        p.Y + transform.TranslateY
                    );
                }
                break;
        }
    }

    private VectorElement? CloneElement(VectorElement element, double offsetX, double offsetY)
    {
        var newId = Guid.NewGuid().ToString();

        switch (element)
        {
            case RectElement rect:
                return new RectElement
                {
                    Id = newId,
                    X = rect.X + offsetX,
                    Y = rect.Y + offsetY,
                    Width = rect.Width,
                    Height = rect.Height,
                    Fill = rect.Fill,
                    Stroke = rect.Stroke,
                    StrokeWidth = rect.StrokeWidth
                };
            case CircleElement circle:
                return new CircleElement
                {
                    Id = newId,
                    Cx = circle.Cx + offsetX,
                    Cy = circle.Cy + offsetY,
                    Radius = circle.Radius,
                    Fill = circle.Fill,
                    Stroke = circle.Stroke,
                    StrokeWidth = circle.StrokeWidth
                };
            case EllipseElement ellipse:
                return new EllipseElement
                {
                    Id = newId,
                    Cx = ellipse.Cx + offsetX,
                    Cy = ellipse.Cy + offsetY,
                    Rx = ellipse.Rx,
                    Ry = ellipse.Ry,
                    Fill = ellipse.Fill,
                    Stroke = ellipse.Stroke,
                    StrokeWidth = ellipse.StrokeWidth
                };
            case LineElement line:
                return new LineElement
                {
                    Id = newId,
                    X1 = line.X1 + offsetX,
                    Y1 = line.Y1 + offsetY,
                    X2 = line.X2 + offsetX,
                    Y2 = line.Y2 + offsetY,
                    Stroke = line.Stroke,
                    StrokeWidth = line.StrokeWidth
                };
            case TextElement text:
                return new TextElement
                {
                    Id = newId,
                    X = text.X + offsetX,
                    Y = text.Y + offsetY,
                    Text = text.Text,
                    FontSize = text.FontSize,
                    FontFamily = text.FontFamily,
                    Fill = text.Fill,
                    Stroke = text.Stroke,
                    StrokeWidth = text.StrokeWidth
                };
            case ImageElement image:
                return new ImageElement
                {
                    Id = newId,
                    X = image.X + offsetX,
                    Y = image.Y + offsetY,
                    Width = image.Width,
                    Height = image.Height,
                    DataUrl = image.DataUrl
                };
            default:
                return null;
        }
    }

    /// <summary>
    /// Reset default styles to initial values.
    /// </summary>
    public void ResetStyles()
    {
        _defaultFill = null;
        _defaultStroke = null;
        _defaultStrokeWidth = null;
        _defaultOpacity = null;
        _defaultFont = null;
        _defaultFontSize = null;
    }
}
