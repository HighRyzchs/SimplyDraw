using SimplyDraw.Core.Brushes;
using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Demo;

public static class DemoSketch
{
    public static DrawingDocument Create()
    {
        var document = DrawingDocument.CreateNew();
        document.Name = "Demo sketch";
        document.Paper = "warm";
        document.Grid = GridStyle.Dots;
        document.GridSpacing = 28;
        document.ViewZoom = 1;
        document.ViewCenterX = 40;
        document.ViewCenterY = 10;

        var color = document.Layers[0];
        color.Name = "Color";
        var line = new Layer { Name = "Line" };
        document.Layers.Add(line);
        document.ActiveLayerIndex = 1;

        color.Strokes.Add(Freehand("soft", "#E25B45", 26, 0.28, Wave(-250, -150, 180, 36, 1.2, 22, 0.7)));
        color.Strokes.Add(Freehand("highlight", "#E2B15A", 42, 0.55, Wave(-80, -40, 360, 16, 0.6, 18, 0.4)));
        color.Strokes.Add(Freehand("marker", "#7D9A62", 34, 0.7, Wave(-220, 150, 520, 18, 1, 20, 0.5)));
        color.Strokes.Add(Freehand("spray", "#3E7CB1", 40, 0.35, Wave(180, -120, 160, 28, 2, 16, 0.6)));

        line.Strokes.Add(Shape(MarkKind.Ellipse, "technical", "#1C1917", 2.4, 1, (-30, 10), (150, 150)));
        line.Strokes.Add(Shape(MarkKind.Ellipse, "ink", "#1C1917", 3.2, 1, (-22, -8), (142, 28)));
        line.Strokes.Add(Freehand("fountain", "#1C1917", 6.5, 1, Handle(148, 78, 48, 38)));
        line.Strokes.Add(Freehand("ink", "#1C1917", 4.5, 1, Branch(-150, 120, -170, -80)));
        line.Strokes.Add(Freehand("nib", "#2F5D9F", 16, 1, Leaf(-168, -20, -70, -90)));
        line.Strokes.Add(Freehand("nib", "#2F8F7A", 14, 1, Leaf(-150, 10, -40, 70)));
        line.Strokes.Add(Shape(MarkKind.Rectangle, "technical", "#44403C", 2, 1, (230, 20), (430, 170)));
        line.Strokes.Add(Shape(MarkKind.Line, "technical", "#44403C", 1.6, 1, (230, 58), (430, 58)));
        line.Strokes.Add(Shape(MarkKind.Arrow, "ink", "#E25B45", 3, 1, (250, 130), (360, 90)));

        var title = new Stroke
        {
            Kind = MarkKind.Text,
            Color = "#1C1917",
            Opacity = 1,
            FontSize = 28,
            Text = "SimplyDraw",
            BoxWidth = 180,
            BoxHeight = 36
        };
        title.Points.Add(new StrokePoint { X = 248, Y = 78, Pressure = 1 });
        line.Strokes.Add(title);

        line.Strokes.Add(Freehand("pencil", "#44403C", 3.2, 0.85, Wave(-40, 190, 280, 10, 3, 28, 0.8)));
        return document;
    }

    private static Stroke Freehand(string brushId, string color, double size, double opacity, IEnumerable<(double X, double Y, double P)> points)
    {
        var stroke = new Stroke
        {
            Kind = MarkKind.Freehand,
            Color = color,
            Size = size,
            Opacity = opacity
        };
        BrushLibrary.Apply(BrushLibrary.Get(brushId), stroke);
        foreach (var point in points)
            stroke.Points.Add(new StrokePoint { X = point.X, Y = point.Y, Pressure = point.P });
        return stroke;
    }

    private static Stroke Shape(MarkKind kind, string brushId, string color, double size, double opacity, (double X, double Y) start, (double X, double Y) end)
    {
        var stroke = Freehand(brushId, color, size, opacity, new[]
        {
            (start.X, start.Y, 0.8),
            (end.X, end.Y, 0.8)
        });
        stroke.Kind = kind;
        stroke.Mode = RenderMode.Ribbon;
        stroke.Taper = false;
        stroke.DirectionalNib = false;
        return stroke;
    }

    private static IEnumerable<(double X, double Y, double P)> Wave(double x, double y, double length, double amplitude, double turns, int count, double pressure)
    {
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)(count - 1);
            var wave = Math.Sin(t * Math.PI * 2 * turns);
            yield return (x + length * t, y + wave * amplitude, pressure * (0.45 + 0.55 * Math.Sin(t * Math.PI)));
        }
    }

    private static IEnumerable<(double X, double Y, double P)> Handle(double cx, double cy, double radiusX, double radiusY)
    {
        const int count = 18;
        for (var i = 0; i < count; i++)
        {
            var t = Math.PI * (0.15 + 0.7 * (i / (double)(count - 1)));
            yield return (cx + Math.Sin(t) * radiusX, cy - Math.Cos(t) * radiusY, 0.3 + 0.6 * Math.Sin(t));
        }
    }

    private static IEnumerable<(double X, double Y, double P)> Branch(double x0, double y0, double x1, double y1)
    {
        const int count = 14;
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)(count - 1);
            var bend = Math.Sin(t * Math.PI) * 18;
            yield return (x0 + (x1 - x0) * t + bend, y0 + (y1 - y0) * t, 0.25 + 0.6 * (1 - t));
        }
    }

    private static IEnumerable<(double X, double Y, double P)> Leaf(double x0, double y0, double x1, double y1)
    {
        const int count = 16;
        for (var i = 0; i < count; i++)
        {
            var t = i / (double)(count - 1);
            var along = t;
            var side = Math.Sin(t * Math.PI) * 28;
            var dx = x1 - x0;
            var dy = y1 - y0;
            var length = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            var nx = -dy / length;
            var ny = dx / length;
            yield return (x0 + dx * along + nx * side, y0 + dy * along + ny * side, 0.2 + Math.Sin(t * Math.PI) * 0.8);
        }
    }
}
