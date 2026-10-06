namespace SimplyDraw.Canvas;

public static class SceneRenderer
{
    public static void Draw(DrawingContext context, DrawingDocument document, Camera camera, double width, double height, bool drawGrid, double pixelsPerDip)
    {
        DrawBackground(context, document, camera, width, height, drawGrid);
        context.PushTransform(new MatrixTransform(camera.Zoom, 0, 0, camera.Zoom, camera.PanX, camera.PanY));
        foreach (var layer in document.Layers)
        {
            if (!layer.Visible)
                continue;

            foreach (var stroke in layer.Strokes)
                StrokePainter.Paint(context, stroke, layer.Opacity, pixelsPerDip);
        }

        context.Pop();
    }

    public static void DrawBackground(DrawingContext context, DrawingDocument document, Camera camera, double width, double height, bool drawGrid)
    {
        if (width < 1 || height < 1)
            return;

        var theme = PaperThemes.Get(document.Paper);
        context.DrawRectangle(Frozen(theme.Background), null, new Rect(0, 0, width, height));
        if (!drawGrid || document.Grid == GridStyle.None || camera.Zoom <= 0)
            return;

        var spacing = document.GridSpacing;
        var guard = 0;
        while (spacing * camera.Zoom < 12 && guard++ < 16)
            spacing *= 2;
        guard = 0;
        while (spacing * camera.Zoom > 78 && guard++ < 16)
            spacing /= 2;

        var topLeft = camera.ScreenToWorld(Vec2.Zero);
        var bottomRight = camera.ScreenToWorld(new Vec2(width, height));
        var minX = Math.Min(topLeft.X, bottomRight.X) - spacing;
        var maxX = Math.Max(topLeft.X, bottomRight.X) + spacing;
        var minY = Math.Min(topLeft.Y, bottomRight.Y) - spacing;
        var maxY = Math.Max(topLeft.Y, bottomRight.Y) + spacing;
        var brush = Frozen(theme.Grid);
        var pen = new Pen(brush, 1);
        pen.Freeze();

        if (document.Grid == GridStyle.Dots)
            DrawDots(context, camera, brush, minX, minY, maxX, maxY, spacing);
        else if (document.Grid == GridStyle.Lines)
            DrawLines(context, camera, pen, minX, minY, maxX, maxY, spacing);
        else
            DrawIsometric(context, camera, pen, minX, minY, maxX, maxY, spacing);
    }

    public static BitmapSource RenderBitmap(DrawingDocument document, Camera camera, double width, double height, bool drawGrid, double scale)
    {
        scale = Math.Clamp(scale, 0.25, 4);
        var pixelWidth = (int)Math.Clamp(Math.Round(width * scale), 1, 12000);
        var pixelHeight = (int)Math.Clamp(Math.Round(height * scale), 1, 12000);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.PushTransform(new ScaleTransform(scale, scale));
            Draw(context, document, camera, width, height, drawGrid, 1);
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    public static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void DrawDots(DrawingContext context, Camera camera, Brush brush, double minX, double minY, double maxX, double maxY, double spacing)
    {
        var count = 0;
        for (var x = Math.Floor(minX / spacing) * spacing; x <= maxX && count < 12000; x += spacing)
        {
            for (var y = Math.Floor(minY / spacing) * spacing; y <= maxY && count < 12000; y += spacing)
            {
                var screen = camera.WorldToScreen(new Vec2(x, y));
                context.DrawEllipse(brush, null, new Point(screen.X, screen.Y), 1.25, 1.25);
                count++;
            }
        }
    }

    private static void DrawLines(DrawingContext context, Camera camera, Pen pen, double minX, double minY, double maxX, double maxY, double spacing)
    {
        for (var x = Math.Floor(minX / spacing) * spacing; x <= maxX; x += spacing)
        {
            var start = camera.WorldToScreen(new Vec2(x, minY));
            var end = camera.WorldToScreen(new Vec2(x, maxY));
            context.DrawLine(pen, new Point(start.X, start.Y), new Point(end.X, end.Y));
        }

        for (var y = Math.Floor(minY / spacing) * spacing; y <= maxY; y += spacing)
        {
            var start = camera.WorldToScreen(new Vec2(minX, y));
            var end = camera.WorldToScreen(new Vec2(maxX, y));
            context.DrawLine(pen, new Point(start.X, start.Y), new Point(end.X, end.Y));
        }
    }

    private static void DrawIsometric(DrawingContext context, Camera camera, Pen pen, double minX, double minY, double maxX, double maxY, double spacing)
    {
        DrawLines(context, camera, pen, minX, minY, maxX, maxY, spacing);
        DrawSlope(context, camera, pen, minX, minY, maxX, maxY, spacing, Math.Tan(Math.PI / 6));
        DrawSlope(context, camera, pen, minX, minY, maxX, maxY, spacing, -Math.Tan(Math.PI / 6));
    }

    private static void DrawSlope(DrawingContext context, Camera camera, Pen pen, double minX, double minY, double maxX, double maxY, double spacing, double slope)
    {
        var cosine = 1 / Math.Sqrt(1 + slope * slope);
        var step = spacing / cosine;
        double[] intercepts =
        [
            minY - slope * minX,
            minY - slope * maxX,
            maxY - slope * minX,
            maxY - slope * maxX
        ];
        var start = Math.Floor((intercepts.Min() - step) / step) * step;
        var end = intercepts.Max() + step;
        var guard = 0;
        for (var intercept = start; intercept <= end && guard++ < 500; intercept += step)
        {
            if (!ClipSlope(slope, intercept, minX, minY, maxX, maxY, out var a, out var b))
                continue;

            var startScreen = camera.WorldToScreen(a);
            var endScreen = camera.WorldToScreen(b);
            context.DrawLine(pen, new Point(startScreen.X, startScreen.Y), new Point(endScreen.X, endScreen.Y));
        }
    }

    private static bool ClipSlope(double slope, double intercept, double minX, double minY, double maxX, double maxY, out Vec2 start, out Vec2 end)
    {
        var hits = new List<Vec2>();
        Consider(minX, slope * minX + intercept);
        Consider(maxX, slope * maxX + intercept);
        if (Math.Abs(slope) > 1e-6)
        {
            Consider((minY - intercept) / slope, minY);
            Consider((maxY - intercept) / slope, maxY);
        }

        if (hits.Count < 2)
        {
            start = default;
            end = default;
            return false;
        }

        start = hits[0];
        end = hits[1];
        return true;

        void Consider(double x, double y)
        {
            if (x < minX - 0.5 || x > maxX + 0.5 || y < minY - 0.5 || y > maxY + 0.5)
                return;
            var point = new Vec2(x, y);
            if (hits.All(hit => hit.DistanceTo(point) > 0.5))
                hits.Add(point);
        }
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var color = Rgba.Parse(hex);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
