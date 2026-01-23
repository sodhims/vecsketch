using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using System.Text;

namespace VecSketch.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ImportController : ControllerBase
{
    private readonly ILogger<ImportController> _logger;

    public ImportController(ILogger<ImportController> logger)
    {
        _logger = logger;
    }

    [HttpPost("ai")]
    [RequestSizeLimit(50_000_000)] // 50MB limit
    public async Task<IActionResult> ImportAiFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "No file provided" });

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".ai" && extension != ".pdf")
            return BadRequest(new { error = "Only .ai and .pdf files are supported" });

        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            var elements = ExtractVectorElements(stream);

            return Ok(new ImportResult
            {
                Success = true,
                Elements = elements,
                Message = $"Imported {elements.Count} elements from {file.FileName}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing AI/PDF file: {FileName}", file.FileName);
            return Ok(new ImportResult
            {
                Success = false,
                Elements = new List<ImportedElement>(),
                Message = $"Error: {ex.Message}. Make sure the file was saved with 'Create PDF Compatible File' option."
            });
        }
    }

    private List<ImportedElement> ExtractVectorElements(Stream stream)
    {
        var elements = new List<ImportedElement>();

        using var document = PdfDocument.Open(stream);

        foreach (var page in document.GetPages())
        {
            var pageHeight = page.Height;

            // Extract paths by parsing the content stream operations
            var paths = ExtractPathsFromOperations(page, pageHeight);
            elements.AddRange(paths);

            // Extract images
            var images = ExtractImagesFromPage(page, pageHeight);
            elements.AddRange(images);
        }

        return elements;
    }

    private List<ImportedElement> ExtractPathsFromOperations(Page page, double pageHeight)
    {
        var elements = new List<ImportedElement>();

        try
        {
            var currentPath = new StringBuilder();
            double currentX = 0, currentY = 0;
            double startX = 0, startY = 0;
            string currentStroke = "#000000";
            string currentFill = "none";
            double strokeWidth = 1;
            bool hasPath = false;

            foreach (var op in page.Operations)
            {
                var opName = op.Operator;

                switch (opName)
                {
                    // Move to - m x y
                    case "m":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 2)
                            {
                                currentX = operands[0];
                                currentY = pageHeight - operands[1];
                                startX = currentX;
                                startY = currentY;
                                currentPath.Append($"M {currentX:F2} {currentY:F2} ");
                                hasPath = true;
                            }
                        }
                        break;

                    // Line to - l x y
                    case "l":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 2)
                            {
                                currentX = operands[0];
                                currentY = pageHeight - operands[1];
                                currentPath.Append($"L {currentX:F2} {currentY:F2} ");
                                hasPath = true;
                            }
                        }
                        break;

                    // Cubic bezier curve - c x1 y1 x2 y2 x3 y3
                    case "c":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 6)
                            {
                                var y1 = pageHeight - operands[1];
                                var y2 = pageHeight - operands[3];
                                var y3 = pageHeight - operands[5];
                                currentPath.Append($"C {operands[0]:F2} {y1:F2} {operands[2]:F2} {y2:F2} {operands[4]:F2} {y3:F2} ");
                                currentX = operands[4];
                                currentY = y3;
                                hasPath = true;
                            }
                        }
                        break;

                    // Quadratic curve shorthand - v y2 y3
                    case "v":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 4)
                            {
                                var y2 = pageHeight - operands[1];
                                var y3 = pageHeight - operands[3];
                                currentPath.Append($"C {currentX:F2} {currentY:F2} {operands[0]:F2} {y2:F2} {operands[2]:F2} {y3:F2} ");
                                currentX = operands[2];
                                currentY = y3;
                                hasPath = true;
                            }
                        }
                        break;

                    // Close path
                    case "h":
                        currentPath.Append("Z ");
                        currentX = startX;
                        currentY = startY;
                        break;

                    // Stroke path
                    case "S":
                    case "s":
                        if (hasPath && currentPath.Length > 0)
                        {
                            elements.Add(new ImportedElement
                            {
                                Type = "path",
                                D = currentPath.ToString().Trim(),
                                Stroke = currentStroke,
                                StrokeWidth = strokeWidth,
                                Fill = "none"
                            });
                        }
                        currentPath.Clear();
                        hasPath = false;
                        break;

                    // Fill path
                    case "f":
                    case "F":
                    case "f*":
                        if (hasPath && currentPath.Length > 0)
                        {
                            elements.Add(new ImportedElement
                            {
                                Type = "path",
                                D = currentPath.ToString().Trim(),
                                Stroke = "none",
                                StrokeWidth = 0,
                                Fill = currentFill != "none" ? currentFill : "#cccccc"
                            });
                        }
                        currentPath.Clear();
                        hasPath = false;
                        break;

                    // Fill and stroke path
                    case "B":
                    case "B*":
                    case "b":
                    case "b*":
                        if (hasPath && currentPath.Length > 0)
                        {
                            elements.Add(new ImportedElement
                            {
                                Type = "path",
                                D = currentPath.ToString().Trim(),
                                Stroke = currentStroke,
                                StrokeWidth = strokeWidth,
                                Fill = currentFill != "none" ? currentFill : "#cccccc"
                            });
                        }
                        currentPath.Clear();
                        hasPath = false;
                        break;

                    // End path without fill or stroke
                    case "n":
                        currentPath.Clear();
                        hasPath = false;
                        break;

                    // Rectangle shorthand - re x y w h
                    case "re":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 4)
                            {
                                var x = operands[0];
                                var y = pageHeight - operands[1] - operands[3];
                                var w = operands[2];
                                var h = operands[3];
                                currentPath.Append($"M {x:F2} {y:F2} L {x + w:F2} {y:F2} L {x + w:F2} {y + h:F2} L {x:F2} {y + h:F2} Z ");
                                hasPath = true;
                            }
                        }
                        break;

                    // Set stroke color (RGB) - RG r g b
                    case "RG":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 3)
                            {
                                var r = (int)(operands[0] * 255);
                                var g = (int)(operands[1] * 255);
                                var b = (int)(operands[2] * 255);
                                currentStroke = $"#{r:X2}{g:X2}{b:X2}";
                            }
                        }
                        break;

                    // Set fill color (RGB) - rg r g b
                    case "rg":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 3)
                            {
                                var r = (int)(operands[0] * 255);
                                var g = (int)(operands[1] * 255);
                                var b = (int)(operands[2] * 255);
                                currentFill = $"#{r:X2}{g:X2}{b:X2}";
                            }
                        }
                        break;

                    // Set line width - w width
                    case "w":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 1)
                            {
                                strokeWidth = operands[0];
                            }
                        }
                        break;

                    // Set gray stroke - G gray
                    case "G":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 1)
                            {
                                var gray = (int)(operands[0] * 255);
                                currentStroke = $"#{gray:X2}{gray:X2}{gray:X2}";
                            }
                        }
                        break;

                    // Set gray fill - g gray
                    case "g":
                        {
                            var operands = ParseOperands(op);
                            if (operands.Length >= 1)
                            {
                                var gray = (int)(operands[0] * 255);
                                currentFill = $"#{gray:X2}{gray:X2}{gray:X2}";
                            }
                        }
                        break;
                }
            }

            // Handle any remaining path
            if (hasPath && currentPath.Length > 0)
            {
                elements.Add(new ImportedElement
                {
                    Type = "path",
                    D = currentPath.ToString().Trim(),
                    Stroke = currentStroke,
                    StrokeWidth = strokeWidth,
                    Fill = currentFill
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting paths from page");
        }

        return elements;
    }

    private double[] ParseOperands(object op)
    {
        // Try to extract numeric operands from the operation
        var result = new List<double>();
        try
        {
            var text = op.ToString() ?? "";
            // Parse numbers from the string representation
            var parts = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (double.TryParse(part, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var val))
                {
                    result.Add(val);
                }
            }
        }
        catch { }
        return result.ToArray();
    }

    private List<ImportedElement> ExtractImagesFromPage(Page page, double pageHeight)
    {
        var elements = new List<ImportedElement>();

        try
        {
            foreach (var image in page.GetImages())
            {
                var bounds = image.Bounds;

                // Try to get image data as base64
                string? dataUrl = null;
                if (image.TryGetPng(out var pngBytes))
                {
                    dataUrl = $"data:image/png;base64,{Convert.ToBase64String(pngBytes)}";
                }
                else if (image.RawBytes.Count > 0)
                {
                    dataUrl = $"data:image/jpeg;base64,{Convert.ToBase64String(image.RawBytes.ToArray())}";
                }

                if (dataUrl != null)
                {
                    elements.Add(new ImportedElement
                    {
                        Type = "image",
                        X = (double)bounds.Left,
                        Y = pageHeight - (double)bounds.Top,
                        Width = (double)bounds.Width,
                        Height = (double)bounds.Height,
                        DataUrl = dataUrl
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting images from page");
        }

        return elements;
    }
}

public class ImportResult
{
    public bool Success { get; set; }
    public List<ImportedElement> Elements { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public class ImportedElement
{
    public string Type { get; set; } = string.Empty;
    public string? D { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string? DataUrl { get; set; }
    public string Stroke { get; set; } = "#000000";
    public double StrokeWidth { get; set; } = 1;
    public string Fill { get; set; } = "none";
}
