namespace SimplyDraw.Canvas;

public static class StrokePainter
{
    private static readonly Dictionary<string, ImageSource> Images = new();

    public static void ClearImageCache() => Images.Clear();

    public static void Paint(DrawingContext context, Stroke stroke, double layerOpacity, double pixelsPerDip)
    {
        if (stroke.Kind == MarkKind.Image)
        {
            PaintImage(context, stroke, layerOpacity);
            return;
        }

        if (stroke.Kind == MarkKind.Text)
        {
            PaintText(context, stroke, layerOpacity, pixelsPerDip);
            return;
        }

        if (stroke.Kind is MarkKind.Rectangle or MarkKind.Ellipse or MarkKind.Line or MarkKind.Arrow)
        {
            PaintShape(context, stroke, layerOpacity);
            return;
        }

        if (stroke.Points.Count == 0)
            return;

        if (stroke.Mode == RenderMode.Stamps)
            PaintStamps(context, stroke, layerOpacity);
        else
            PaintRibbon(context, stroke, layerOpacity);
    }

    private static void PaintRibbon(DrawingContext context, Stroke stroke, double layerOpacity)
    {
        var centers = stroke.Points.Select(point => point.ToVec()).ToList();
        var radii = StrokeMetrics.Radii(stroke);
        var pressure = stroke.Points.Average(point => point.Pressure);
        if (stroke.Hardness < 0.92)
        {
            var wide = radii.Select(radius => radius * (1.2 + (1 - stroke.Hardness) * 0.85)).ToArray();
            DrawRibbon(context, centers, wide, Brush(stroke, layerOpacity * (0.16 + (1 - stroke.Hardness) * 0.2), pressure));
        }

        DrawRibbon(context, centers, radii, Brush(stroke, layerOpacity, pressure));
    }

    private static void DrawRibbon(DrawingContext context, IReadOnlyList<Vec2> centers, IReadOnlyList<double> radii, Brush brush)
    {
        var polygon = RibbonBuilder.Build(centers, radii);
        if (polygon.Count < 3)
            return;

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(ToPoint(polygon[0]), true, true);
            geometryContext.PolyLineTo(polygon.Skip(1).Select(ToPoint).ToList(), true, true);
        }

        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);
    }

    private static void PaintStamps(DrawingContext context, Stroke stroke, double layerOpacity)
    {
        foreach (var stamp in StampLayout.Build(stroke))
        {
            var center = stamp.Center + GrainOffset.Offset(stroke.Id, stamp.Index, stroke.Grain, stamp.Radius);
            var radius = Math.Max(0.35, stamp.Radius);
            context.DrawEllipse(StampBrush(stroke, layerOpacity, stamp.Pressure), null, ToPoint(center), radius, radius);
        }
    }

    private static void PaintShape(DrawingContext context, Stroke stroke, double layerOpacity)
    {
        if (stroke.Points.Count < 2)
            return;

        var brush = Brush(stroke, layerOpacity, 1);
        var pen = new Pen(brush, Math.Max(0.4, stroke.Size))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        var start = stroke.Points[0].ToVec();
        var end = stroke.Points[^1].ToVec();

        switch (stroke.Kind)
        {
            case MarkKind.Line:
                context.DrawLine(pen, ToPoint(start), ToPoint(end));
                break;
            case MarkKind.Rectangle:
            {
                var box = Bounds.FromCorners(start, end);
                context.DrawRectangle(null, pen, new Rect(box.MinX, box.MinY, Math.Max(0.4, box.Width), Math.Max(0.4, box.Height)));
                break;
            }
            case MarkKind.Ellipse:
            {
                var box = Bounds.FromCorners(start, end);
                context.DrawEllipse(null, pen, new Point(box.Center.X, box.Center.Y), Math.Max(0.4, box.Width / 2), Math.Max(0.4, box.Height / 2));
                break;
            }
            case MarkKind.Arrow:
                PaintArrow(context, brush, pen, start, end, stroke.Size);
                break;
        }
    }

    private static void PaintArrow(DrawingContext context, Brush brush, Pen pen, Vec2 start, Vec2 end, double size)
    {
        var direction = (end - start).NormalizedOr(new Vec2(1, 0));
        var normal = new Vec2(-direction.Y, direction.X);
        var headLength = Math.Max(8, size * 4.2);
        var headWidth = Math.Max(5, size * 2.4);
        var neck = end - direction * headLength;
        context.DrawLine(pen, ToPoint(start), ToPoint(neck));

        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            geometryContext.BeginFigure(ToPoint(end), true, true);
            geometryContext.PolyLineTo(new List<Point>
            {
                ToPoint(neck + normal * headWidth),
                ToPoint(neck - normal * headWidth)
            }, true, true);
        }

        geometry.Freeze();
        context.DrawGeometry(brush, null, geometry);
    }

    private static void PaintText(DrawingContext context, Stroke stroke, double layerOpacity, double pixelsPerDip)
    {
        if (stroke.Points.Count == 0 || string.IsNullOrWhiteSpace(stroke.Text))
            return;

        var anchor = stroke.Points[0].ToVec();
        var formatted = new FormattedText(
            stroke.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            Math.Max(1, stroke.FontSize),
            Brush(stroke, layerOpacity, 1),
            Math.Max(1, pixelsPerDip));

        if (Math.Abs(stroke.Rotation) > 0.05)
            context.PushTransform(new RotateTransform(stroke.Rotation, anchor.X, anchor.Y));

        context.DrawText(formatted, ToPoint(anchor));
        if (Math.Abs(stroke.Rotation) > 0.05)
            context.Pop();
    }

    private static void PaintImage(DrawingContext context, Stroke stroke, double layerOpacity)
    {
        if (stroke.Points.Count == 0)
            return;

        var image = GetImage(stroke);
        if (image == null)
            return;

        var origin = stroke.Points[0].ToVec();
        var center = new Vec2(origin.X + stroke.BoxWidth / 2, origin.Y + stroke.BoxHeight / 2);
        var opacity = Math.Clamp(stroke.Opacity * layerOpacity, 0, 1);
        if (Math.Abs(stroke.Rotation) > 0.05)
            context.PushTransform(new RotateTransform(stroke.Rotation, center.X, center.Y));
        context.PushOpacity(opacity);
        context.DrawImage(image, new Rect(origin.X, origin.Y, Math.Max(1, stroke.BoxWidth), Math.Max(1, stroke.BoxHeight)));
        context.Pop();
        if (Math.Abs(stroke.Rotation) > 0.05)
            context.Pop();
    }

    private static ImageSource? GetImage(Stroke stroke)
    {
        if (stroke.ImageBytes is not { Length: > 0 })
            return null;
        if (Images.TryGetValue(stroke.Id, out var cached))
            return cached;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.StreamSource = new MemoryStream(stroke.ImageBytes);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            Images[stroke.Id] = image;
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException)
        {
            return null;
        }
    }

    private static SolidColorBrush Brush(Stroke stroke, double layerOpacity, double pressure)
    {
        var color = Rgba.Parse(stroke.Color);
        var pressureFactor = stroke.PressureOpacity ? Math.Clamp(pressure, 0.08, 1) : 1;
        var alpha = Math.Clamp(color.A / 255d * stroke.Opacity * layerOpacity * pressureFactor, 0, 1);
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Brush StampBrush(Stroke stroke, double layerOpacity, double pressure)
    {
        if (stroke.Hardness >= 0.97)
            return Brush(stroke, layerOpacity, pressure);

        var color = Rgba.Parse(stroke.Color);
        var pressureFactor = stroke.PressureOpacity ? Math.Clamp(pressure, 0.08, 1) : 1;
        var alpha = (byte)Math.Round(Math.Clamp(color.A / 255d * stroke.Opacity * layerOpacity * pressureFactor, 0, 1) * 255);
        var solid = Color.FromArgb(alpha, color.R, color.G, color.B);
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        var brush = new RadialGradientBrush();
        brush.GradientStops.Add(new GradientStop(solid, Math.Clamp(stroke.Hardness, 0, 0.92)));
        brush.GradientStops.Add(new GradientStop(clear, 1));
        brush.Freeze();
        return brush;
    }

    private static Point ToPoint(Vec2 point) => new(point.X, point.Y);
}
