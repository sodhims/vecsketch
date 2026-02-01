using System.Globalization;
using System.Text;
using Clipper2Lib;
using VecSketch.Client.Models;
using ClipperPointD = Clipper2Lib.PointD;

namespace VecSketch.Client.Services;

/// <summary>
/// Decomposes overlapping shapes into distinct Venn diagram regions using Clipper2 boolean operations.
/// </summary>
public static class ShapeDecomposer
{
    private const int CircleSegments = 72;

    private static readonly string[] Colors = { "#e63946", "#2a9d8f", "#457b9d", "#f4a261", "#9b59b6" };

    public class DecomposedRegion
    {
        public required string Label { get; init; }
        public required int[] SourceIndices { get; init; }
        public required string PathData { get; init; }
        public required string Color { get; init; }
        public double Area { get; init; }
    }

    /// <summary>
    /// Decompose a list of shapes into distinct non-overlapping regions.
    /// </summary>
    public static List<DecomposedRegion> Decompose(List<VectorElement> elements)
    {
        if (elements.Count < 2)
            return new List<DecomposedRegion>();

        // Convert elements to Clipper2 polygons
        var shapes = new List<PathsD>();
        var originalColors = new List<string>();

        foreach (var el in elements)
        {
            var polygon = ElementToPolygon(el);
            if (polygon != null && polygon.Count >= 3)
            {
                shapes.Add(new PathsD { polygon });
                originalColors.Add(GetElementFillColor(el));
            }
        }

        if (shapes.Count < 2)
            return new List<DecomposedRegion>();

        return ComputeAllRegions(shapes, originalColors);
    }

    private static PathD? ElementToPolygon(VectorElement element)
    {
        return element switch
        {
            CircleElement circle => CircleToPolygon(circle.Cx, circle.Cy, circle.Radius),
            EllipseElement ellipse => EllipseToPolygon(ellipse.Cx, ellipse.Cy, ellipse.Rx, ellipse.Ry),
            RectElement rect => RectToPolygon(rect.X, rect.Y, rect.Width, rect.Height),
            PolygonElement polygon => RegularPolygonToPolygon(polygon.Cx, polygon.Cy, polygon.Radius, polygon.Sides),
            TriangleElement triangle => TriangleToPolygon(triangle),
            SvgPathElement svgPath => SvgPathToPolygon(svgPath.D),
            _ => null
        };
    }

    private static PathD CircleToPolygon(double cx, double cy, double radius)
    {
        if (radius <= 0) return new PathD();

        var path = new PathD();
        for (int i = 0; i < CircleSegments; i++)
        {
            var angle = 2 * Math.PI * i / CircleSegments;
            path.Add(new ClipperPointD(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)));
        }
        return path;
    }

    private static PathD EllipseToPolygon(double cx, double cy, double rx, double ry)
    {
        if (rx <= 0 || ry <= 0) return new PathD();

        var path = new PathD();
        for (int i = 0; i < CircleSegments; i++)
        {
            var angle = 2 * Math.PI * i / CircleSegments;
            path.Add(new ClipperPointD(cx + rx * Math.Cos(angle), cy + ry * Math.Sin(angle)));
        }
        return path;
    }

    private static PathD RectToPolygon(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) return new PathD();

        return new PathD
        {
            new ClipperPointD(x, y),
            new ClipperPointD(x + width, y),
            new ClipperPointD(x + width, y + height),
            new ClipperPointD(x, y + height)
        };
    }

    private static PathD RegularPolygonToPolygon(double cx, double cy, double radius, int sides)
    {
        if (radius <= 0 || sides < 3) return new PathD();

        var path = new PathD();
        for (int i = 0; i < sides; i++)
        {
            var angle = (Math.PI * 2 * i / sides) - Math.PI / 2;
            path.Add(new ClipperPointD(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle)));
        }
        return path;
    }

    private static PathD TriangleToPolygon(TriangleElement triangle)
    {
        return new PathD
        {
            new ClipperPointD(triangle.X1, triangle.Y1),
            new ClipperPointD(triangle.X2, triangle.Y2),
            new ClipperPointD(triangle.X3, triangle.Y3)
        };
    }

    private static PathD SvgPathToPolygon(string d)
    {
        if (string.IsNullOrEmpty(d))
            return new PathD();

        var path = new PathD();
        double currentX = 0, currentY = 0;
        double startX = 0, startY = 0;

        // Extract all numbers from the path using regex
        var numberPattern = new System.Text.RegularExpressions.Regex(@"-?\d+\.?\d*(?:[eE][+-]?\d+)?");
        var numbers = new List<double>();
        foreach (System.Text.RegularExpressions.Match match in numberPattern.Matches(d))
        {
            if (double.TryParse(match.Value, System.Globalization.NumberStyles.Float,
                CultureInfo.InvariantCulture, out double num))
                numbers.Add(num);
        }

        // Extract commands
        var commandPattern = new System.Text.RegularExpressions.Regex(@"[MmLlHhVvCcSsQqTtAaZz]");
        var commands = new List<(char cmd, int startIndex)>();

        foreach (System.Text.RegularExpressions.Match match in commandPattern.Matches(d))
        {
            int numbersBeforeThis = 0;
            var beforeCmd = d.Substring(0, match.Index);
            foreach (System.Text.RegularExpressions.Match numMatch in numberPattern.Matches(beforeCmd))
                numbersBeforeThis++;
            commands.Add((match.Value[0], numbersBeforeThis));
        }

        for (int cmdIdx = 0; cmdIdx < commands.Count; cmdIdx++)
        {
            char cmd = commands[cmdIdx].cmd;
            int startNumIdx = commands[cmdIdx].startIndex;
            int endNumIdx = cmdIdx + 1 < commands.Count ? commands[cmdIdx + 1].startIndex : numbers.Count;
            var cmdNumbers = numbers.Skip(startNumIdx).Take(endNumIdx - startNumIdx).ToList();
            int i = 0;

            while (i < cmdNumbers.Count || cmd == 'Z' || cmd == 'z')
            {
                switch (cmd)
                {
                    case 'M':
                        if (i + 1 < cmdNumbers.Count) { currentX = cmdNumbers[i]; currentY = cmdNumbers[i + 1]; startX = currentX; startY = currentY; path.Add(new ClipperPointD(currentX, currentY)); i += 2; cmd = 'L'; }
                        else goto done;
                        break;
                    case 'm':
                        if (i + 1 < cmdNumbers.Count) { currentX += cmdNumbers[i]; currentY += cmdNumbers[i + 1]; startX = currentX; startY = currentY; path.Add(new ClipperPointD(currentX, currentY)); i += 2; cmd = 'l'; }
                        else goto done;
                        break;
                    case 'L':
                        if (i + 1 < cmdNumbers.Count) { currentX = cmdNumbers[i]; currentY = cmdNumbers[i + 1]; path.Add(new ClipperPointD(currentX, currentY)); i += 2; }
                        else goto done;
                        break;
                    case 'l':
                        if (i + 1 < cmdNumbers.Count) { currentX += cmdNumbers[i]; currentY += cmdNumbers[i + 1]; path.Add(new ClipperPointD(currentX, currentY)); i += 2; }
                        else goto done;
                        break;
                    case 'H':
                        if (i < cmdNumbers.Count) { currentX = cmdNumbers[i]; path.Add(new ClipperPointD(currentX, currentY)); i++; }
                        else goto done;
                        break;
                    case 'h':
                        if (i < cmdNumbers.Count) { currentX += cmdNumbers[i]; path.Add(new ClipperPointD(currentX, currentY)); i++; }
                        else goto done;
                        break;
                    case 'V':
                        if (i < cmdNumbers.Count) { currentY = cmdNumbers[i]; path.Add(new ClipperPointD(currentX, currentY)); i++; }
                        else goto done;
                        break;
                    case 'v':
                        if (i < cmdNumbers.Count) { currentY += cmdNumbers[i]; path.Add(new ClipperPointD(currentX, currentY)); i++; }
                        else goto done;
                        break;
                    case 'C': // Cubic bezier - sample points along curve
                        if (i + 5 < cmdNumbers.Count)
                        {
                            double x1 = cmdNumbers[i], y1 = cmdNumbers[i+1];
                            double x2 = cmdNumbers[i+2], y2 = cmdNumbers[i+3];
                            double x3 = cmdNumbers[i+4], y3 = cmdNumbers[i+5];
                            for (int t = 1; t <= 8; t++)
                            {
                                double tt = t / 8.0;
                                double mt = 1 - tt;
                                double px = mt*mt*mt*currentX + 3*mt*mt*tt*x1 + 3*mt*tt*tt*x2 + tt*tt*tt*x3;
                                double py = mt*mt*mt*currentY + 3*mt*mt*tt*y1 + 3*mt*tt*tt*y2 + tt*tt*tt*y3;
                                path.Add(new ClipperPointD(px, py));
                            }
                            currentX = x3; currentY = y3; i += 6;
                        }
                        else goto done;
                        break;
                    case 'c':
                        if (i + 5 < cmdNumbers.Count)
                        {
                            double x1 = currentX + cmdNumbers[i], y1 = currentY + cmdNumbers[i+1];
                            double x2 = currentX + cmdNumbers[i+2], y2 = currentY + cmdNumbers[i+3];
                            double x3 = currentX + cmdNumbers[i+4], y3 = currentY + cmdNumbers[i+5];
                            for (int t = 1; t <= 8; t++)
                            {
                                double tt = t / 8.0;
                                double mt = 1 - tt;
                                double px = mt*mt*mt*currentX + 3*mt*mt*tt*x1 + 3*mt*tt*tt*x2 + tt*tt*tt*x3;
                                double py = mt*mt*mt*currentY + 3*mt*mt*tt*y1 + 3*mt*tt*tt*y2 + tt*tt*tt*y3;
                                path.Add(new ClipperPointD(px, py));
                            }
                            currentX = x3; currentY = y3; i += 6;
                        }
                        else goto done;
                        break;
                    case 'Q': // Quadratic bezier
                        if (i + 3 < cmdNumbers.Count)
                        {
                            double x1 = cmdNumbers[i], y1 = cmdNumbers[i+1];
                            double x2 = cmdNumbers[i+2], y2 = cmdNumbers[i+3];
                            for (int t = 1; t <= 8; t++)
                            {
                                double tt = t / 8.0;
                                double mt = 1 - tt;
                                double px = mt*mt*currentX + 2*mt*tt*x1 + tt*tt*x2;
                                double py = mt*mt*currentY + 2*mt*tt*y1 + tt*tt*y2;
                                path.Add(new ClipperPointD(px, py));
                            }
                            currentX = x2; currentY = y2; i += 4;
                        }
                        else goto done;
                        break;
                    case 'q':
                        if (i + 3 < cmdNumbers.Count)
                        {
                            double x1 = currentX + cmdNumbers[i], y1 = currentY + cmdNumbers[i+1];
                            double x2 = currentX + cmdNumbers[i+2], y2 = currentY + cmdNumbers[i+3];
                            for (int t = 1; t <= 8; t++)
                            {
                                double tt = t / 8.0;
                                double mt = 1 - tt;
                                double px = mt*mt*currentX + 2*mt*tt*x1 + tt*tt*x2;
                                double py = mt*mt*currentY + 2*mt*tt*y1 + tt*tt*y2;
                                path.Add(new ClipperPointD(px, py));
                            }
                            currentX = x2; currentY = y2; i += 4;
                        }
                        else goto done;
                        break;
                    case 'S': // Smooth cubic
                    case 's':
                        if (i + 3 < cmdNumbers.Count) { currentX = cmd == 'S' ? cmdNumbers[i+2] : currentX + cmdNumbers[i+2]; currentY = cmd == 'S' ? cmdNumbers[i+3] : currentY + cmdNumbers[i+3]; path.Add(new ClipperPointD(currentX, currentY)); i += 4; }
                        else goto done;
                        break;
                    case 'T': // Smooth quadratic
                    case 't':
                        if (i + 1 < cmdNumbers.Count) { currentX = cmd == 'T' ? cmdNumbers[i] : currentX + cmdNumbers[i]; currentY = cmd == 'T' ? cmdNumbers[i+1] : currentY + cmdNumbers[i+1]; path.Add(new ClipperPointD(currentX, currentY)); i += 2; }
                        else goto done;
                        break;
                    case 'A': // Arc - simplified: just add endpoint
                    case 'a':
                        if (i + 6 < cmdNumbers.Count) { currentX = cmd == 'A' ? cmdNumbers[i+5] : currentX + cmdNumbers[i+5]; currentY = cmd == 'A' ? cmdNumbers[i+6] : currentY + cmdNumbers[i+6]; path.Add(new ClipperPointD(currentX, currentY)); i += 7; }
                        else goto done;
                        break;
                    case 'Z':
                    case 'z':
                        currentX = startX; currentY = startY;
                        goto done;
                    default:
                        goto done;
                }
            }
            done:;
        }

        return path;
    }

    private static string GetElementFillColor(VectorElement element)
    {
        // Use fill color if set, otherwise use stroke color
        if (!string.IsNullOrEmpty(element.Fill) && element.Fill != "none")
            return element.Fill;
        return element.Stroke;
    }

    private static List<DecomposedRegion> ComputeAllRegions(List<PathsD> shapes, List<string> colors)
    {
        int n = shapes.Count;
        var regions = new List<DecomposedRegion>();

        // Compute all possible regions (2^n - 1 combinations)
        for (int size = 1; size <= n; size++)
        {
            foreach (var combo in Combinations(n, size))
            {
                string label = combo.Length == 1
                    ? $"S{combo[0] + 1}Only"
                    : string.Join("∩", combo.Select(i => $"S{i + 1}")) + "Only";

                // Intersection of all shapes in combo
                PathsD region = shapes[combo[0]];
                for (int i = 1; i < combo.Length; i++)
                {
                    region = Intersect(region, shapes[combo[i]]);
                    if (region.Count == 0) break;
                }

                if (region.Count == 0)
                    continue;

                // Subtract all shapes NOT in combo
                for (int i = 0; i < n; i++)
                {
                    if (combo.Contains(i)) continue;
                    region = Difference(region, shapes[i]);
                    if (region.Count == 0) break;
                }

                double area = CalculateArea(region);
                if (region.Count > 0 && area > 0.01)
                {
                    var pathData = PathsToSvgPath(region);
                    var color = MixColors(combo, colors);

                    regions.Add(new DecomposedRegion
                    {
                        Label = label,
                        SourceIndices = combo,
                        PathData = pathData,
                        Color = color,
                        Area = area
                    });
                }
            }
        }

        return regions;
    }

    private static PathsD Intersect(PathsD a, PathsD b)
    {
        var clipper = new ClipperD();
        clipper.AddSubject(a);
        clipper.AddClip(b);
        var result = new PathsD();
        clipper.Execute(ClipType.Intersection, FillRule.NonZero, result);
        return result;
    }

    private static PathsD Difference(PathsD a, PathsD b)
    {
        var clipper = new ClipperD();
        clipper.AddSubject(a);
        clipper.AddClip(b);
        var result = new PathsD();
        clipper.Execute(ClipType.Difference, FillRule.NonZero, result);
        return result;
    }

    private static double CalculateArea(PathsD paths)
    {
        double total = 0;
        foreach (var path in paths)
        {
            total += Math.Abs(Clipper.Area(path));
        }
        return total;
    }

    private static string MixColors(int[] indices, List<string> originalColors)
    {
        if (indices.Length == 1 && indices[0] < originalColors.Count)
        {
            var color = originalColors[indices[0]];
            if (IsValidHexColor(color)) return color;
            return Colors[indices[0] % Colors.Length];
        }

        int r = 0, g = 0, b = 0;
        int count = 0;

        foreach (var idx in indices)
        {
            string hex;
            if (idx < originalColors.Count && IsValidHexColor(originalColors[idx]))
                hex = originalColors[idx];
            else
                hex = Colors[idx % Colors.Length];

            try
            {
                r += Convert.ToInt32(hex.Substring(1, 2), 16);
                g += Convert.ToInt32(hex.Substring(3, 2), 16);
                b += Convert.ToInt32(hex.Substring(5, 2), 16);
                count++;
            }
            catch { }
        }

        if (count == 0) return Colors[0];

        r /= count;
        g /= count;
        b /= count;
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static bool IsValidHexColor(string color)
    {
        return !string.IsNullOrEmpty(color) &&
               color.StartsWith("#") &&
               color.Length == 7;
    }

    private static string PathsToSvgPath(PathsD paths)
    {
        var sb = new StringBuilder();
        foreach (var path in paths)
        {
            if (path.Count < 3) continue;

            sb.Append("M ");
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F2} {1:F2} ", path[0].x, path[0].y);

            for (int i = 1; i < path.Count; i++)
            {
                sb.AppendFormat(CultureInfo.InvariantCulture, "L {0:F2} {1:F2} ", path[i].x, path[i].y);
            }
            sb.Append("Z ");
        }
        return sb.ToString().Trim();
    }

    private static IEnumerable<int[]> Combinations(int n, int k)
    {
        var combo = new int[k];
        for (int i = 0; i < k; i++) combo[i] = i;

        while (true)
        {
            yield return (int[])combo.Clone();

            int i = k - 1;
            while (i >= 0 && combo[i] == n - k + i) i--;
            if (i < 0) break;

            combo[i]++;
            for (int j = i + 1; j < k; j++)
                combo[j] = combo[j - 1] + 1;
        }
    }
}
