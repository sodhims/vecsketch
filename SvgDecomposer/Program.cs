using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Svg;
using Svg.Pathing;
using Clipper2Lib;

namespace SvgDecomposer
{
    /// <summary>
    /// SVG Decomposer - Breaks overlapping SVG shapes into distinct Venn diagram regions
    ///
    /// Uses:
    ///   - Svg.NET for robust SVG parsing (handles transforms, all path commands, styles)
    ///   - Clipper2 for polygon boolean operations
    ///
    /// NuGet:
    ///   Install-Package Svg
    ///   Install-Package Clipper2
    /// </summary>
    public class SvgDecomposer
    {
        private const int CircleSegments = 72;
        private const int BezierSegments = 16;

        private static readonly string[] Colors = { "#e63946", "#2a9d8f", "#457b9d", "#f4a261", "#9b59b6" };

        public class Region
        {
            public string Label { get; set; }
            public int[] Indices { get; set; }
            public PathsD Polygons { get; set; }
            public double Area { get; set; }
        }

        public class DecomposeResult
        {
            public List<Region> Regions { get; set; }
            public string OutputDirectory { get; set; }
            public RectangleF ViewBox { get; set; }
        }

        #region SVG Parsing with Svg.NET

        /// <summary>
        /// Parse SVG file and extract all shapes as polygons
        /// </summary>
        public static List<PathD> ParseSvgFile(string filePath)
        {
            var doc = SvgDocument.Open(filePath);
            return ExtractPaths(doc);
        }

        /// <summary>
        /// Parse SVG content string and extract all shapes as polygons
        /// </summary>
        public static List<PathD> ParseSvgContent(string svgContent)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(svgContent));
            var doc = SvgDocument.Open<SvgDocument>(stream);
            return ExtractPaths(doc);
        }

        /// <summary>
        /// Extract all drawable paths from SVG document
        /// </summary>
        private static List<PathD> ExtractPaths(SvgDocument doc)
        {
            var paths = new List<PathD>();
            ExtractPathsRecursive(doc, paths, new Matrix());
            return paths;
        }

        /// <summary>
        /// Recursively traverse SVG DOM and extract paths with transforms applied
        /// </summary>
        private static void ExtractPathsRecursive(SvgElement element, List<PathD> paths, Matrix parentTransform)
        {
            // Combine parent transform with this element's transform
            var currentTransform = parentTransform.Clone();
            if (element.Transforms != null && element.Transforms.Count > 0)
            {
                foreach (var transform in element.Transforms)
                {
                    currentTransform.Multiply(transform.Matrix);
                }
            }

            // Extract path based on element type
            PathD path = null;

            if (element is SvgCircle circle)
            {
                path = CircleToPath(circle, currentTransform);
            }
            else if (element is SvgEllipse ellipse)
            {
                path = EllipseToPath(ellipse, currentTransform);
            }
            else if (element is SvgRectangle rect)
            {
                path = RectToPath(rect, currentTransform);
            }
            else if (element is SvgPath svgPath)
            {
                path = SvgPathToPath(svgPath, currentTransform);
            }
            else if (element is SvgPolyline polyline)
            {
                // Check SvgPolyline before SvgPolygon (polyline inherits from polygon)
                path = PolylineToPath(polyline, currentTransform);
            }
            else if (element is SvgPolygon polygon)
            {
                path = PolygonToPath(polygon, currentTransform);
            }

            if (path != null && path.Count >= 3)
            {
                paths.Add(path);
            }

            // Recurse into children (groups, etc.)
            if (element.Children != null)
            {
                foreach (var child in element.Children)
                {
                    ExtractPathsRecursive(child, paths, currentTransform);
                }
            }
        }

        private static PathD CircleToPath(SvgCircle circle, Matrix transform)
        {
            var cx = circle.CenterX.Value;
            var cy = circle.CenterY.Value;
            var r = circle.Radius.Value;

            if (r <= 0) return null;

            var path = new PathD();
            for (int i = 0; i < CircleSegments; i++)
            {
                var angle = 2 * Math.PI * i / CircleSegments;
                var pt = TransformPoint((float)(cx + r * Math.Cos(angle)), (float)(cy + r * Math.Sin(angle)), transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
            return path;
        }

        private static PathD EllipseToPath(SvgEllipse ellipse, Matrix transform)
        {
            var cx = ellipse.CenterX.Value;
            var cy = ellipse.CenterY.Value;
            var rx = ellipse.RadiusX.Value;
            var ry = ellipse.RadiusY.Value;

            if (rx <= 0 || ry <= 0) return null;

            var path = new PathD();
            for (int i = 0; i < CircleSegments; i++)
            {
                var angle = 2 * Math.PI * i / CircleSegments;
                var pt = TransformPoint((float)(cx + rx * Math.Cos(angle)), (float)(cy + ry * Math.Sin(angle)), transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
            return path;
        }

        private static PathD RectToPath(SvgRectangle rect, Matrix transform)
        {
            var x = rect.X.Value;
            var y = rect.Y.Value;
            var w = rect.Width.Value;
            var h = rect.Height.Value;

            if (w <= 0 || h <= 0) return null;

            var corners = new[]
            {
                new PointF(x, y),
                new PointF(x + w, y),
                new PointF(x + w, y + h),
                new PointF(x, y + h)
            };

            var path = new PathD();
            foreach (var corner in corners)
            {
                var pt = TransformPoint(corner.X, corner.Y, transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
            return path;
        }

        private static PathD PolygonToPath(SvgPolygon polygon, Matrix transform)
        {
            if (polygon.Points == null || polygon.Points.Count < 6) return null;

            var path = new PathD();
            for (int i = 0; i < polygon.Points.Count - 1; i += 2)
            {
                var pt = TransformPoint(polygon.Points[i], polygon.Points[i + 1], transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
            return path.Count >= 3 ? path : null;
        }

        private static PathD PolylineToPath(SvgPolyline polyline, Matrix transform)
        {
            if (polyline.Points == null || polyline.Points.Count < 6) return null;

            var path = new PathD();
            for (int i = 0; i < polyline.Points.Count - 1; i += 2)
            {
                var pt = TransformPoint(polyline.Points[i], polyline.Points[i + 1], transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
            return path.Count >= 3 ? path : null;
        }

        private static PathD SvgPathToPath(SvgPath svgPath, Matrix transform)
        {
            if (svgPath.PathData == null || svgPath.PathData.Count == 0) return null;

            var path = new PathD();
            PointF current = PointF.Empty;
            PointF start = PointF.Empty;
            PointF lastControl = PointF.Empty;

            foreach (var segment in svgPath.PathData)
            {
                switch (segment)
                {
                    case SvgMoveToSegment move:
                        current = new PointF(move.End.X, move.End.Y);
                        start = current;
                        var pt = TransformPoint(current.X, current.Y, transform);
                        path.Add(new PointD(pt.X, pt.Y));
                        break;

                    case SvgLineSegment line:
                        current = new PointF(line.End.X, line.End.Y);
                        pt = TransformPoint(current.X, current.Y, transform);
                        path.Add(new PointD(pt.X, pt.Y));
                        break;

                    case SvgCubicCurveSegment cubic:
                        AddCubicBezier(path, current,
                            new PointF(cubic.FirstControlPoint.X, cubic.FirstControlPoint.Y),
                            new PointF(cubic.SecondControlPoint.X, cubic.SecondControlPoint.Y),
                            new PointF(cubic.End.X, cubic.End.Y),
                            transform);
                        lastControl = new PointF(cubic.SecondControlPoint.X, cubic.SecondControlPoint.Y);
                        current = new PointF(cubic.End.X, cubic.End.Y);
                        break;

                    case SvgQuadraticCurveSegment quad:
                        AddQuadBezier(path, current,
                            new PointF(quad.ControlPoint.X, quad.ControlPoint.Y),
                            new PointF(quad.End.X, quad.End.Y),
                            transform);
                        lastControl = new PointF(quad.ControlPoint.X, quad.ControlPoint.Y);
                        current = new PointF(quad.End.X, quad.End.Y);
                        break;

                    case SvgArcSegment arc:
                        AddArc(path, current, arc, transform);
                        current = new PointF(arc.End.X, arc.End.Y);
                        break;

                    case SvgClosePathSegment _:
                        current = start;
                        break;

                    default:
                        if (segment.End != PointF.Empty)
                        {
                            current = new PointF(segment.End.X, segment.End.Y);
                            pt = TransformPoint(current.X, current.Y, transform);
                            path.Add(new PointD(pt.X, pt.Y));
                        }
                        break;
                }
            }

            // Remove duplicate consecutive points
            var cleaned = new PathD();
            foreach (var p in path)
            {
                if (cleaned.Count == 0 || Distance(cleaned[cleaned.Count - 1], p) > 0.001)
                    cleaned.Add(p);
            }

            return cleaned.Count >= 3 ? cleaned : null;
        }

        private static void AddCubicBezier(PathD path, PointF p0, PointF p1, PointF p2, PointF p3, Matrix transform)
        {
            for (int i = 1; i <= BezierSegments; i++)
            {
                double t = (double)i / BezierSegments;
                double mt = 1 - t;
                double x = mt * mt * mt * p0.X + 3 * mt * mt * t * p1.X + 3 * mt * t * t * p2.X + t * t * t * p3.X;
                double y = mt * mt * mt * p0.Y + 3 * mt * mt * t * p1.Y + 3 * mt * t * t * p2.Y + t * t * t * p3.Y;
                var pt = TransformPoint((float)x, (float)y, transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
        }

        private static void AddQuadBezier(PathD path, PointF p0, PointF p1, PointF p2, Matrix transform)
        {
            for (int i = 1; i <= BezierSegments; i++)
            {
                double t = (double)i / BezierSegments;
                double mt = 1 - t;
                double x = mt * mt * p0.X + 2 * mt * t * p1.X + t * t * p2.X;
                double y = mt * mt * p0.Y + 2 * mt * t * p1.Y + t * t * p2.Y;
                var pt = TransformPoint((float)x, (float)y, transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
        }

        private static void AddArc(PathD path, PointF start, SvgArcSegment arc, Matrix transform)
        {
            var end = new PointF(arc.End.X, arc.End.Y);
            double rx = Math.Abs(arc.RadiusX);
            double ry = Math.Abs(arc.RadiusY);
            var phi = arc.Angle * Math.PI / 180.0;
            var largeArc = arc.Size == SvgArcSize.Large;
            var sweep = arc.Sweep == SvgArcSweep.Positive;

            if (rx < 0.001 || ry < 0.001 || Distance(start, end) < 0.001)
            {
                var pt = TransformPoint(end.X, end.Y, transform);
                path.Add(new PointD(pt.X, pt.Y));
                return;
            }

            // Convert to center parameterization
            var cosPhi = Math.Cos(phi);
            var sinPhi = Math.Sin(phi);

            var dx = (start.X - end.X) / 2.0;
            var dy = (start.Y - end.Y) / 2.0;
            var x1p = cosPhi * dx + sinPhi * dy;
            var y1p = -sinPhi * dx + cosPhi * dy;

            // Correct radii
            var lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
            if (lambda > 1)
            {
                rx *= Math.Sqrt(lambda);
                ry *= Math.Sqrt(lambda);
            }

            var rxSq = rx * rx;
            var rySq = ry * ry;
            var x1pSq = x1p * x1p;
            var y1pSq = y1p * y1p;

            var sq = Math.Max(0, (rxSq * rySq - rxSq * y1pSq - rySq * x1pSq) / (rxSq * y1pSq + rySq * x1pSq));
            var coef = (largeArc != sweep ? 1 : -1) * Math.Sqrt(sq);
            var cxp = coef * rx * y1p / ry;
            var cyp = -coef * ry * x1p / rx;

            var cx = cosPhi * cxp - sinPhi * cyp + (start.X + end.X) / 2.0;
            var cy = sinPhi * cxp + cosPhi * cyp + (start.Y + end.Y) / 2.0;

            var theta1 = AngleBetween(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
            var dtheta = AngleBetween((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);

            if (!sweep && dtheta > 0) dtheta -= 2 * Math.PI;
            if (sweep && dtheta < 0) dtheta += 2 * Math.PI;

            var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(dtheta) / (Math.PI / 18)));
            for (int i = 1; i <= segments; i++)
            {
                var theta = theta1 + dtheta * i / segments;
                var xp = rx * Math.Cos(theta);
                var yp = ry * Math.Sin(theta);
                var x = cosPhi * xp - sinPhi * yp + cx;
                var y = sinPhi * xp + cosPhi * yp + cy;
                var pt = TransformPoint((float)x, (float)y, transform);
                path.Add(new PointD(pt.X, pt.Y));
            }
        }

        private static double AngleBetween(double ux, double uy, double vx, double vy)
        {
            var n = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
            if (n < 0.0001) return 0;
            var c = (ux * vx + uy * vy) / n;
            c = Math.Max(-1, Math.Min(1, c));
            var angle = Math.Acos(c);
            if (ux * vy - uy * vx < 0) angle = -angle;
            return angle;
        }

        private static PointF TransformPoint(float x, float y, Matrix transform)
        {
            var pts = new[] { new PointF(x, y) };
            transform.TransformPoints(pts);
            return pts[0];
        }

        private static double Distance(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        private static double Distance(PointD a, PointD b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        #endregion

        #region Boolean Operations (Clipper2)

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

        #endregion

        #region Region Computation

        public static List<Region> ComputeAllRegions(List<PathsD> shapes)
        {
            int n = shapes.Count;
            var regions = new List<Region>();

            Console.WriteLine($"\nComputing all {(1 << n) - 1} possible regions for {n} shapes...\n");

            for (int size = 1; size <= n; size++)
            {
                foreach (var combo in Combinations(n, size))
                {
                    string label = combo.Length == 1
                        ? $"S{combo[0] + 1}Only"
                        : string.Join("_intersect_", combo.Select(i => $"S{i + 1}")) + "Only";

                    // Intersection of all shapes in combo
                    PathsD region = shapes[combo[0]];
                    for (int i = 1; i < combo.Length; i++)
                    {
                        region = Intersect(region, shapes[combo[i]]);
                        if (region.Count == 0) break;
                    }

                    if (region.Count == 0)
                    {
                        Console.WriteLine($"  {label}: empty (shapes don't overlap)");
                        continue;
                    }

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
                        Console.WriteLine($"  {label}: {region.Count} polygon(s), area = {area:F2}");
                        regions.Add(new Region
                        {
                            Label = label,
                            Indices = combo,
                            Polygons = region,
                            Area = area
                        });
                    }
                    else
                    {
                        Console.WriteLine($"  {label}: empty after subtraction");
                    }
                }
            }

            return regions;
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

        #endregion

        #region SVG Generation

        private static string MixColors(int[] indices)
        {
            if (indices.Length == 1) return Colors[indices[0] % Colors.Length];

            int r = 0, g = 0, b = 0;
            foreach (var idx in indices)
            {
                var hex = Colors[idx % Colors.Length];
                r += Convert.ToInt32(hex.Substring(1, 2), 16);
                g += Convert.ToInt32(hex.Substring(3, 2), 16);
                b += Convert.ToInt32(hex.Substring(5, 2), 16);
            }
            r /= indices.Length;
            g /= indices.Length;
            b /= indices.Length;
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private static string PathsToSvgPath(PathsD paths)
        {
            var sb = new StringBuilder();
            foreach (var path in paths)
            {
                if (path.Count < 3) continue;
                sb.Append("M ");
                foreach (var pt in path)
                {
                    sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F2} {1:F2} ", pt.x, pt.y);
                }
                sb.Append("Z ");
            }
            return sb.ToString().Trim();
        }

        private static (double minX, double minY, double maxX, double maxY) GetBounds(List<PathsD> shapes)
        {
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var shape in shapes)
            {
                foreach (var path in shape)
                {
                    foreach (var pt in path)
                    {
                        minX = Math.Min(minX, pt.x);
                        minY = Math.Min(minY, pt.y);
                        maxX = Math.Max(maxX, pt.x);
                        maxY = Math.Max(maxY, pt.y);
                    }
                }
            }

            return (minX, minY, maxX, maxY);
        }

        public static string GenerateRegionSvg(Region region, double vbX, double vbY, double vbW, double vbH)
        {
            var color = MixColors(region.Indices);
            var pathD = PathsToSvgPath(region.Polygons);

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""{vbX.ToString(CultureInfo.InvariantCulture)} {vbY.ToString(CultureInfo.InvariantCulture)} {vbW.ToString(CultureInfo.InvariantCulture)} {vbH.ToString(CultureInfo.InvariantCulture)}"">
  <title>{region.Label}</title>
  <path d=""{pathD}"" fill=""{color}"" stroke=""#333"" stroke-width=""1""/>
</svg>";
        }

        public static string GenerateOverviewSvg(List<Region> regions, List<PathsD> shapes, double vbX, double vbY, double vbW, double vbH)
        {
            var sb = new StringBuilder();
            sb.AppendLine($@"<?xml version=""1.0"" encoding=""UTF-8""?>");
            sb.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" viewBox=""{vbX.ToString(CultureInfo.InvariantCulture)} {vbY.ToString(CultureInfo.InvariantCulture)} {vbW.ToString(CultureInfo.InvariantCulture)} {vbH.ToString(CultureInfo.InvariantCulture)}"">");
            sb.AppendLine(@"  <title>SVG Decomposition - All Regions</title>");
            sb.AppendLine(@"  <style>path:hover { fill-opacity: 1; stroke-width: 2; }</style>");

            // Original shape outlines
            for (int i = 0; i < shapes.Count; i++)
            {
                var pathD = PathsToSvgPath(shapes[i]);
                sb.AppendLine($@"  <path d=""{pathD}"" fill=""none"" stroke=""{Colors[i % Colors.Length]}"" stroke-width=""2"" stroke-dasharray=""5,3"" opacity=""0.6""><title>S{i + 1}</title></path>");
            }

            // All regions
            foreach (var region in regions)
            {
                var color = MixColors(region.Indices);
                var pathD = PathsToSvgPath(region.Polygons);
                sb.AppendLine($@"  <path d=""{pathD}"" fill=""{color}"" stroke=""#333"" stroke-width=""0.5"" fill-opacity=""0.8""><title>{region.Label}</title></path>");
            }

            sb.AppendLine(@"</svg>");
            return sb.ToString();
        }

        #endregion

        #region Main API

        /// <summary>
        /// Decompose SVG files into distinct Venn regions
        /// </summary>
        public static DecomposeResult Decompose(string[] svgFiles, string outputDir = "output")
        {
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("SVG DECOMPOSER");
            Console.WriteLine("  Parsing:     Svg.NET");
            Console.WriteLine("  Boolean ops: Clipper2");
            Console.WriteLine(new string('=', 60));

            if (svgFiles.Length < 2 || svgFiles.Length > 5)
                throw new ArgumentException("Please provide 2-5 SVG files");

            Console.WriteLine($"\nParsing {svgFiles.Length} SVG files...\n");

            var shapes = new List<PathsD>();
            foreach (var file in svgFiles)
            {
                if (!File.Exists(file))
                    throw new FileNotFoundException($"File not found: {file}");

                var paths = ParseSvgFile(file);
                if (paths.Count == 0)
                {
                    Console.WriteLine($"  Warning: No shapes found in {file}");
                    continue;
                }

                // Take first shape from each file
                var shape = new PathsD { paths[0] };
                shapes.Add(shape);

                var area = CalculateArea(shape);
                Console.WriteLine($"  S{shapes.Count}: {paths[0].Count} vertices, area = {area:F2}  ({Path.GetFileName(file)})");
            }

            if (shapes.Count < 2)
                throw new InvalidOperationException("Need at least 2 valid shapes");

            return ProcessShapes(shapes, outputDir);
        }

        /// <summary>
        /// Decompose SVG content strings into distinct Venn regions
        /// </summary>
        public static DecomposeResult DecomposeFromContent(string[] svgContents, string outputDir = "output")
        {
            Console.WriteLine(new string('=', 60));
            Console.WriteLine("SVG DECOMPOSER");
            Console.WriteLine("  Parsing:     Svg.NET");
            Console.WriteLine("  Boolean ops: Clipper2");
            Console.WriteLine(new string('=', 60));

            if (svgContents.Length < 2 || svgContents.Length > 5)
                throw new ArgumentException("Please provide 2-5 SVG contents");

            Console.WriteLine($"\nParsing {svgContents.Length} SVGs...\n");

            var shapes = new List<PathsD>();
            for (int i = 0; i < svgContents.Length; i++)
            {
                var paths = ParseSvgContent(svgContents[i]);
                if (paths.Count == 0)
                {
                    Console.WriteLine($"  Warning: No shapes found in SVG {i + 1}");
                    continue;
                }

                var shape = new PathsD { paths[0] };
                shapes.Add(shape);

                var area = CalculateArea(shape);
                Console.WriteLine($"  S{shapes.Count}: {paths[0].Count} vertices, area = {area:F2}");
            }

            if (shapes.Count < 2)
                throw new InvalidOperationException("Need at least 2 valid shapes");

            return ProcessShapes(shapes, outputDir);
        }

        private static DecomposeResult ProcessShapes(List<PathsD> shapes, string outputDir)
        {
            // Compute bounds
            var (minX, minY, maxX, maxY) = GetBounds(shapes);
            double vbX = minX - 20, vbY = minY - 20;
            double vbW = maxX - minX + 40, vbH = maxY - minY + 40;

            // Compute regions
            var regions = ComputeAllRegions(shapes);

            Console.WriteLine($"\n{new string('=', 60)}");
            Console.WriteLine($"RESULTS: {regions.Count} non-empty regions");
            Console.WriteLine(new string('=', 60));

            // Create output directory
            Directory.CreateDirectory(outputDir);

            // Generate files
            foreach (var region in regions)
            {
                var svg = GenerateRegionSvg(region, vbX, vbY, vbW, vbH);
                var filename = region.Label + ".svg";
                File.WriteAllText(Path.Combine(outputDir, filename), svg);
                Console.WriteLine($"  Created: {filename}");
            }

            var overview = GenerateOverviewSvg(regions, shapes, vbX, vbY, vbW, vbH);
            File.WriteAllText(Path.Combine(outputDir, "overview.svg"), overview);
            Console.WriteLine("  Created: overview.svg");

            // Write manifest
            var manifest = new StringBuilder();
            manifest.AppendLine("{");
            manifest.AppendLine($"  \"timestamp\": \"{DateTime.UtcNow:O}\",");
            manifest.AppendLine($"  \"inputCount\": {shapes.Count},");
            manifest.AppendLine("  \"regions\": [");
            for (int i = 0; i < regions.Count; i++)
            {
                var r = regions[i];
                var filename = r.Label + ".svg";
                manifest.Append($"    {{ \"label\": \"{r.Label}\", \"file\": \"{filename}\", \"area\": {r.Area.ToString(CultureInfo.InvariantCulture)} }}");
                manifest.AppendLine(i < regions.Count - 1 ? "," : "");
            }
            manifest.AppendLine("  ]");
            manifest.AppendLine("}");
            File.WriteAllText(Path.Combine(outputDir, "manifest.json"), manifest.ToString());
            Console.WriteLine("  Created: manifest.json");

            Console.WriteLine($"\nDone! Output written to: {outputDir}");

            return new DecomposeResult
            {
                Regions = regions,
                OutputDirectory = outputDir,
                ViewBox = new RectangleF((float)vbX, (float)vbY, (float)vbW, (float)vbH)
            };
        }

        #endregion
    }

    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length < 2 || args.Contains("-h") || args.Contains("--help"))
            {
                Console.WriteLine(@"
SVG Decomposer - Break overlapping shapes into distinct Venn regions

Usage:
  SvgDecomposer <svg1> <svg2> [svg3] [svg4] [svg5] [-o output_dir]

Options:
  -o    Output directory (default: ./output)
  -h    Show this help

Example:
  SvgDecomposer circle1.svg circle2.svg -o ./venn-output

NuGet dependencies:
  Install-Package Svg
  Install-Package Clipper2
");
                return args.Contains("-h") || args.Contains("--help") ? 0 : 1;
            }

            var files = new List<string>();
            string outputDir = "output";

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                    outputDir = args[++i];
                else if (!args[i].StartsWith("-"))
                    files.Add(args[i]);
            }

            try
            {
                SvgDecomposer.Decompose(files.ToArray(), outputDir);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }
    }
}
