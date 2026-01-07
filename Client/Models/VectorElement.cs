namespace VecSketch.Client.Models;

public abstract class VectorElement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Stroke { get; set; } = "#000000";
    public double StrokeWidth { get; set; } = 2;
    public string Fill { get; set; } = "none";
    public bool IsSelected { get; set; } = false;
    public abstract string ToSvg();
    public abstract BoundingBox GetBounds();

    public virtual bool HitTest(double x, double y, double tolerance = 5)
    {
        var bounds = GetBounds();
        return x >= bounds.X - tolerance && x <= bounds.X + bounds.Width + tolerance &&
               y >= bounds.Y - tolerance && y <= bounds.Y + bounds.Height + tolerance;
    }

    public virtual bool IntersectsRect(double rx, double ry, double rw, double rh)
    {
        var bounds = GetBounds();
        return !(bounds.X > rx + rw || bounds.X + bounds.Width < rx ||
                 bounds.Y > ry + rh || bounds.Y + bounds.Height < ry);
    }

    protected string GetSelectionStyle() => IsSelected ? " stroke-dasharray=\"4 2\" filter=\"url(#selection-glow)\"" : "";
}

public record BoundingBox(double X, double Y, double Width, double Height);

public class LineElement : VectorElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }

    public override BoundingBox GetBounds() => new(Math.Min(X1, X2), Math.Min(Y1, Y2), Math.Abs(X2 - X1), Math.Abs(Y2 - Y1));
    public override string ToSvg() =>
        $"<line x1=\"{X1}\" y1=\"{Y1}\" x2=\"{X2}\" y2=\"{Y2}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\"{GetSelectionStyle()} />";
}

public class PathElement : VectorElement
{
    public List<Point> Points { get; set; } = new();
    public double Pressure { get; set; } = 1.0;

    public override BoundingBox GetBounds()
    {
        if (Points.Count == 0) return new(0, 0, 0, 0);
        var minX = Points.Min(p => p.X);
        var minY = Points.Min(p => p.Y);
        var maxX = Points.Max(p => p.X);
        var maxY = Points.Max(p => p.Y);
        return new(minX, minY, maxX - minX, maxY - minY);
    }

    public override string ToSvg()
    {
        if (Points.Count < 2) return string.Empty;
        var d = $"M {Points[0].X} {Points[0].Y}";
        for (int i = 1; i < Points.Count; i++)
            d += $" L {Points[i].X} {Points[i].Y}";
        return $"<path d=\"{d}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{GetSelectionStyle()} />";
    }
}

public class RectElement : VectorElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    public override BoundingBox GetBounds() => new(X, Y, Width, Height);
    public override string ToSvg() =>
        $"<rect x=\"{X}\" y=\"{Y}\" width=\"{Width}\" height=\"{Height}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\"{GetSelectionStyle()} />";
}

public class CircleElement : VectorElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Radius { get; set; }

    public override BoundingBox GetBounds() => new(Cx - Radius, Cy - Radius, Radius * 2, Radius * 2);
    public override string ToSvg() =>
        $"<circle cx=\"{Cx}\" cy=\"{Cy}\" r=\"{Radius}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\"{GetSelectionStyle()} />";
}

public class EllipseElement : VectorElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Rx { get; set; }
    public double Ry { get; set; }

    public override BoundingBox GetBounds() => new(Cx - Rx, Cy - Ry, Rx * 2, Ry * 2);
    public override string ToSvg() =>
        $"<ellipse cx=\"{Cx}\" cy=\"{Cy}\" rx=\"{Rx}\" ry=\"{Ry}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\"{GetSelectionStyle()} />";
}

public class TriangleElement : VectorElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double X3 { get; set; }
    public double Y3 { get; set; }

    public override BoundingBox GetBounds()
    {
        var minX = Math.Min(X1, Math.Min(X2, X3));
        var minY = Math.Min(Y1, Math.Min(Y2, Y3));
        var maxX = Math.Max(X1, Math.Max(X2, X3));
        var maxY = Math.Max(Y1, Math.Max(Y2, Y3));
        return new(minX, minY, maxX - minX, maxY - minY);
    }
    public override string ToSvg() =>
        $"<polygon points=\"{X1},{Y1} {X2},{Y2} {X3},{Y3}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\"{GetSelectionStyle()} />";
}

public class PolygonElement : VectorElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double Radius { get; set; }
    public int Sides { get; set; } = 6;

    public override BoundingBox GetBounds() => new(Cx - Radius, Cy - Radius, Radius * 2, Radius * 2);
    public override string ToSvg()
    {
        var points = new List<string>();
        for (int i = 0; i < Sides; i++)
        {
            var angle = (Math.PI * 2 * i / Sides) - Math.PI / 2;
            var x = Cx + Radius * Math.Cos(angle);
            var y = Cy + Radius * Math.Sin(angle);
            points.Add($"{x:F2},{y:F2}");
        }
        return $"<polygon points=\"{string.Join(" ", points)}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\"{GetSelectionStyle()} />";
    }
}

public class StarElement : VectorElement
{
    public double Cx { get; set; }
    public double Cy { get; set; }
    public double OuterRadius { get; set; }
    public double InnerRadius { get; set; }
    public int Points { get; set; } = 5;

    public override BoundingBox GetBounds() => new(Cx - OuterRadius, Cy - OuterRadius, OuterRadius * 2, OuterRadius * 2);
    public override string ToSvg()
    {
        var pointsList = new List<string>();
        for (int i = 0; i < Points * 2; i++)
        {
            var angle = (Math.PI * i / Points) - Math.PI / 2;
            var radius = i % 2 == 0 ? OuterRadius : InnerRadius;
            var x = Cx + radius * Math.Cos(angle);
            var y = Cy + radius * Math.Sin(angle);
            pointsList.Add($"{x:F2},{y:F2}");
        }
        return $"<polygon points=\"{string.Join(" ", pointsList)}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\"{GetSelectionStyle()} />";
    }
}

public class ArrowElement : VectorElement
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public double HeadSize { get; set; } = 15;

    public override BoundingBox GetBounds() => new(Math.Min(X1, X2) - HeadSize, Math.Min(Y1, Y2) - HeadSize,
        Math.Abs(X2 - X1) + HeadSize * 2, Math.Abs(Y2 - Y1) + HeadSize * 2);
    public override string ToSvg()
    {
        var angle = Math.Atan2(Y2 - Y1, X2 - X1);
        var headAngle1 = angle + Math.PI * 0.85;
        var headAngle2 = angle - Math.PI * 0.85;

        var headX1 = X2 + HeadSize * Math.Cos(headAngle1);
        var headY1 = Y2 + HeadSize * Math.Sin(headAngle1);
        var headX2 = X2 + HeadSize * Math.Cos(headAngle2);
        var headY2 = Y2 + HeadSize * Math.Sin(headAngle2);
        var stroke = IsSelected ? "#0066ff" : Stroke;

        return $@"<g{GetSelectionStyle()}>
            <line x1=""{X1}"" y1=""{Y1}"" x2=""{X2}"" y2=""{Y2}"" stroke=""{stroke}"" stroke-width=""{StrokeWidth}"" />
            <polygon points=""{X2},{Y2} {headX1:F2},{headY1:F2} {headX2:F2},{headY2:F2}"" fill=""{stroke}"" stroke=""{stroke}"" stroke-width=""1"" />
        </g>";
    }
}

public class ImageElement : VectorElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string DataUrl { get; set; } = string.Empty;

    public override BoundingBox GetBounds() => new(X, Y, Width, Height);
    public override string ToSvg() =>
        IsSelected
            ? $"<g><image x=\"{X}\" y=\"{Y}\" width=\"{Width}\" height=\"{Height}\" href=\"{DataUrl}\" preserveAspectRatio=\"xMidYMid meet\" /><rect x=\"{X}\" y=\"{Y}\" width=\"{Width}\" height=\"{Height}\" fill=\"none\" stroke=\"#0066ff\" stroke-width=\"2\" stroke-dasharray=\"4 2\" /></g>"
            : $"<image x=\"{X}\" y=\"{Y}\" width=\"{Width}\" height=\"{Height}\" href=\"{DataUrl}\" preserveAspectRatio=\"xMidYMid meet\" />";
}

public class TextElement : VectorElement
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = 16;
    public string FontFamily { get; set; } = "Arial, sans-serif";

    public override BoundingBox GetBounds() => new(X, Y - FontSize, Text.Length * FontSize * 0.6, FontSize * 1.2);
    public override string ToSvg() =>
        $"<text x=\"{X}\" y=\"{Y}\" fill=\"{(IsSelected ? "#0066ff" : Fill)}\" font-size=\"{FontSize}\" font-family=\"{FontFamily}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{(StrokeWidth > 0.5 ? 0.5 : 0)}\"{GetSelectionStyle()}>{System.Net.WebUtility.HtmlEncode(Text)}</text>";
}

/// <summary>
/// SVG path element using raw SVG path data string (d attribute).
/// Supports complex shapes, arcs, beziers, etc.
/// </summary>
public class SvgPathElement : VectorElement
{
    public string D { get; set; } = string.Empty;

    public override BoundingBox GetBounds()
    {
        // Parse path data to find bounds (simplified - handles M, L, C commands)
        var bounds = ParsePathBounds(D);
        return bounds;
    }

    public override string ToSvg() =>
        $"<path d=\"{D}\" stroke=\"{(IsSelected ? "#0066ff" : Stroke)}\" stroke-width=\"{StrokeWidth}\" fill=\"{Fill}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{GetSelectionStyle()} />";

    private static BoundingBox ParsePathBounds(string d)
    {
        if (string.IsNullOrEmpty(d))
            return new BoundingBox(0, 0, 0, 0);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        double currentX = 0, currentY = 0;

        var parts = d.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        char lastCommand = 'M';

        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim();
            if (string.IsNullOrEmpty(part)) continue;

            char cmd = part[0];
            if (char.IsLetter(cmd))
            {
                lastCommand = cmd;
                part = part.Substring(1);
                if (string.IsNullOrEmpty(part) && i + 1 < parts.Length)
                {
                    i++;
                    part = parts[i];
                }
            }

            if (!double.TryParse(part, out double x)) continue;
            if (i + 1 >= parts.Length) break;

            i++;
            if (!double.TryParse(parts[i], out double y)) continue;

            switch (char.ToUpper(lastCommand))
            {
                case 'M':
                case 'L':
                case 'C':
                case 'S':
                case 'Q':
                case 'T':
                    if (char.IsUpper(lastCommand))
                    {
                        currentX = x;
                        currentY = y;
                    }
                    else
                    {
                        currentX += x;
                        currentY += y;
                    }
                    minX = Math.Min(minX, currentX);
                    minY = Math.Min(minY, currentY);
                    maxX = Math.Max(maxX, currentX);
                    maxY = Math.Max(maxY, currentY);
                    break;
            }
        }

        if (minX == double.MaxValue)
            return new BoundingBox(0, 0, 0, 0);

        return new BoundingBox(minX, minY, maxX - minX, maxY - minY);
    }
}

public record Point(double X, double Y);

public enum DrawingTool 
{ 
    Select, 
    Line, 
    Pencil, 
    Rectangle, 
    Circle, 
    Ellipse,
    Triangle,
    Polygon,
    Star,
    Arrow,
    Text
}
