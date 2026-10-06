using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimplyDraw.Core.Brushes;
using SimplyDraw.Core.Geometry;
using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.IO;

public static class DrawingFile
{
    public const int CurrentVersion = 1;
    public const string Extension = ".sdraw";
    public const string Filter = "SimplyDraw board (*.sdraw)|*.sdraw";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Save(DrawingDocument document, string path)
    {
        document.Version = CurrentVersion;
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options));
    }

    public static DrawingDocument Load(string path)
    {
        var document = JsonSerializer.Deserialize<DrawingDocument>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("This drawing file is empty.");
        Normalize(document);
        return document;
    }

    public static void Normalize(DrawingDocument document)
    {
        document.EnsureLayer();
        document.ActiveLayerIndex = Math.Clamp(document.ActiveLayerIndex, 0, document.Layers.Count - 1);
        if (document.GridSpacing < 4)
            document.GridSpacing = 32;
        if (string.IsNullOrWhiteSpace(document.Paper))
            document.Paper = "warm";
        if (document.ViewZoom <= 0)
            document.ViewZoom = 1;

        foreach (var layer in document.Layers)
        {
            if (string.IsNullOrWhiteSpace(layer.Id))
                layer.Id = Guid.NewGuid().ToString("N");
            layer.Strokes ??= new List<Stroke>();
            layer.Opacity = Math.Clamp(layer.Opacity <= 0 ? 1 : layer.Opacity, 0.05, 1);
            if (string.IsNullOrWhiteSpace(layer.Name))
                layer.Name = "Layer";

            foreach (var stroke in layer.Strokes)
            {
                if (string.IsNullOrWhiteSpace(stroke.Id))
                    stroke.Id = Guid.NewGuid().ToString("N");
                stroke.Points ??= new List<StrokePoint>();
                if (stroke.Size <= 0)
                    stroke.Size = 4;
                if (stroke.Opacity <= 0)
                    stroke.Opacity = 1;
                if (string.IsNullOrWhiteSpace(stroke.Color))
                    stroke.Color = "#1C1917";
                if (stroke.FontSize <= 0)
                    stroke.FontSize = 32;
                if (stroke.MinWidthFactor <= 0)
                    stroke.MinWidthFactor = 0.2;
                if (stroke.StampSpacing <= 0)
                    stroke.StampSpacing = 0.4;
            }
        }
    }
}

public static class SvgWriter
{
    public static string Export(DrawingDocument document, Bounds? bounds = null, bool includePaper = true)
    {
        var area = bounds ?? document.ContentBounds();
        if (area.IsEmpty)
            area = new Bounds(0, 0, 800, 600);
        area = area.Inflate(24);

        var builder = new StringBuilder();
        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" ");
        builder.Append(CultureInfo.InvariantCulture, $"width=\"{area.Width:0.##}\" height=\"{area.Height:0.##}\" ");
        builder.Append(CultureInfo.InvariantCulture, $"viewBox=\"{area.MinX:0.##} {area.MinY:0.##} {area.Width:0.##} {area.Height:0.##}\">");
        builder.AppendLine();

        if (includePaper)
        {
            var paper = PaperThemes.Get(document.Paper);
            builder.AppendLine(CultureInfo.InvariantCulture, $"<rect x=\"{area.MinX:0.##}\" y=\"{area.MinY:0.##}\" width=\"{area.Width:0.##}\" height=\"{area.Height:0.##}\" fill=\"{paper.Background}\"/>");
        }

        foreach (var layer in document.Layers)
        {
            if (!layer.Visible)
                continue;

            builder.AppendLine(CultureInfo.InvariantCulture, $"<g opacity=\"{layer.Opacity:0.###}\">");
            foreach (var stroke in layer.Strokes)
                AppendStroke(builder, stroke);
            builder.AppendLine("</g>");
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendStroke(StringBuilder builder, Stroke stroke)
    {
        var color = Rgba.Parse(stroke.Color);
        var opacity = Math.Clamp(stroke.Opacity * (color.A / 255d), 0, 1).ToString("0.###", CultureInfo.InvariantCulture);
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        switch (stroke.Kind)
        {
            case MarkKind.Line when stroke.Points.Count >= 2:
                Line(builder, stroke.Points[0].ToVec(), stroke.Points[^1].ToVec(), hex, opacity, stroke.Size);
                break;
            case MarkKind.Rectangle when stroke.Points.Count >= 2:
                Rect(builder, stroke, hex, opacity);
                break;
            case MarkKind.Ellipse when stroke.Points.Count >= 2:
                Ellipse(builder, stroke, hex, opacity);
                break;
            case MarkKind.Arrow when stroke.Points.Count >= 2:
                Arrow(builder, stroke, hex, opacity);
                break;
            case MarkKind.Text when stroke.Points.Count > 0 && !string.IsNullOrEmpty(stroke.Text):
                Text(builder, stroke, hex, opacity);
                break;
            case MarkKind.Image when stroke.Points.Count > 0 && !string.IsNullOrEmpty(stroke.ImageBase64):
                Image(builder, stroke, opacity);
                break;
            default:
                if (stroke.Mode == RenderMode.Stamps)
                    Stamps(builder, stroke, hex, opacity);
                else
                    Ribbon(builder, stroke, hex, opacity);
                break;
        }
    }

    private static void Ribbon(StringBuilder builder, Stroke stroke, string hex, string opacity)
    {
        if (stroke.Points.Count == 0)
            return;

        var centers = stroke.Points.Select(point => point.ToVec()).ToList();
        var polygon = RibbonBuilder.Build(centers, StrokeMetrics.Radii(stroke));
        if (polygon.Count < 3)
            return;

        builder.Append("<polygon points=\"");
        AppendPoints(builder, polygon);
        builder.AppendLine(CultureInfo.InvariantCulture, $"\" fill=\"{hex}\" fill-opacity=\"{opacity}\" stroke=\"none\"/>");
    }

    private static void Stamps(StringBuilder builder, Stroke stroke, string hex, string opacity)
    {
        foreach (var stamp in StampLayout.Build(stroke))
        {
            var center = stamp.Center + GrainOffset.Offset(stroke.Id, stamp.Index, stroke.Grain, stamp.Radius);
            var radius = Math.Max(0.2, stamp.Radius);
            builder.AppendLine(CultureInfo.InvariantCulture, $"<circle cx=\"{center.X:0.##}\" cy=\"{center.Y:0.##}\" r=\"{radius:0.##}\" fill=\"{hex}\" fill-opacity=\"{opacity}\"/>");
        }
    }

    private static void Line(StringBuilder builder, Vec2 start, Vec2 end, string hex, string opacity, double size)
    {
        builder.AppendLine(CultureInfo.InvariantCulture, $"<line x1=\"{start.X:0.##}\" y1=\"{start.Y:0.##}\" x2=\"{end.X:0.##}\" y2=\"{end.Y:0.##}\" stroke=\"{hex}\" stroke-opacity=\"{opacity}\" stroke-width=\"{size:0.##}\" stroke-linecap=\"round\"/>");
    }

    private static void Rect(StringBuilder builder, Stroke stroke, string hex, string opacity)
    {
        var box = Bounds.FromCorners(stroke.Points[0].ToVec(), stroke.Points[1].ToVec());
        builder.AppendLine(CultureInfo.InvariantCulture, $"<rect x=\"{box.MinX:0.##}\" y=\"{box.MinY:0.##}\" width=\"{box.Width:0.##}\" height=\"{box.Height:0.##}\" fill=\"none\" stroke=\"{hex}\" stroke-opacity=\"{opacity}\" stroke-width=\"{stroke.Size:0.##}\"/>");
    }

    private static void Ellipse(StringBuilder builder, Stroke stroke, string hex, string opacity)
    {
        var box = Bounds.FromCorners(stroke.Points[0].ToVec(), stroke.Points[1].ToVec());
        builder.AppendLine(CultureInfo.InvariantCulture, $"<ellipse cx=\"{box.Center.X:0.##}\" cy=\"{box.Center.Y:0.##}\" rx=\"{box.Width / 2:0.##}\" ry=\"{box.Height / 2:0.##}\" fill=\"none\" stroke=\"{hex}\" stroke-opacity=\"{opacity}\" stroke-width=\"{stroke.Size:0.##}\"/>");
    }

    private static void Arrow(StringBuilder builder, Stroke stroke, string hex, string opacity)
    {
        var start = stroke.Points[0].ToVec();
        var end = stroke.Points[^1].ToVec();
        var direction = (end - start).NormalizedOr(new Vec2(1, 0));
        var normal = new Vec2(-direction.Y, direction.X);
        var length = Math.Max(10, stroke.Size * 4);
        var width = Math.Max(6, stroke.Size * 2.2);
        var back = end - direction * length;
        Line(builder, start, back, hex, opacity, stroke.Size);
        var left = back + normal * width;
        var right = back - normal * width;
        builder.Append("<polygon points=\"");
        AppendPoints(builder, new[] { end, left, right });
        builder.AppendLine(CultureInfo.InvariantCulture, $"\" fill=\"{hex}\" fill-opacity=\"{opacity}\"/>");
    }

    private static void Text(StringBuilder builder, Stroke stroke, string hex, string opacity)
    {
        var anchor = stroke.Points[0];
        var transform = Math.Abs(stroke.Rotation) < 0.01
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" transform=\"rotate({stroke.Rotation:0.##} {anchor.X:0.##} {anchor.Y:0.##})\"");
        var baseline = anchor.Y + stroke.FontSize * 0.82;
        builder.AppendLine(CultureInfo.InvariantCulture, $"<text x=\"{anchor.X:0.##}\" y=\"{baseline:0.##}\" fill=\"{hex}\" fill-opacity=\"{opacity}\" font-family=\"Segoe UI, sans-serif\" font-size=\"{stroke.FontSize:0.##}\"{transform}>{Escape(stroke.Text)}</text>");
    }

    private static void Image(StringBuilder builder, Stroke stroke, string opacity)
    {
        var origin = stroke.Points[0];
        var centerX = origin.X + stroke.BoxWidth / 2;
        var centerY = origin.Y + stroke.BoxHeight / 2;
        var transform = Math.Abs(stroke.Rotation) < 0.01
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" transform=\"rotate({stroke.Rotation:0.##} {centerX:0.##} {centerY:0.##})\"");
        builder.AppendLine(CultureInfo.InvariantCulture, $"<image x=\"{origin.X:0.##}\" y=\"{origin.Y:0.##}\" width=\"{stroke.BoxWidth:0.##}\" height=\"{stroke.BoxHeight:0.##}\" opacity=\"{opacity}\" href=\"data:image/png;base64,{stroke.ImageBase64}\"{transform}/>");
    }

    private static void AppendPoints(StringBuilder builder, IReadOnlyList<Vec2> points)
    {
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0)
                builder.Append(' ');
            builder.Append(CultureInfo.InvariantCulture, $"{points[i].X:0.##},{points[i].Y:0.##}");
        }
    }

    private static string Escape(string? text) =>
        (text ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
