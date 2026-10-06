using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Geometry;

public static class HitTester
{
    public static bool Hits(Stroke stroke, Vec2 point, double slop)
    {
        var bounds = stroke.GetBounds().Inflate(slop + 2);
        if (!bounds.Contains(point) && stroke.Kind is not (MarkKind.Line or MarkKind.Arrow or MarkKind.Freehand))
            return false;

        return stroke.Kind switch
        {
            MarkKind.Text => stroke.GetBounds().Inflate(slop).Contains(point),
            MarkKind.Image => ImageContains(stroke, point, slop),
            MarkKind.Rectangle => DistanceToRectOutline(point, CornerBox(stroke)) <= stroke.Size * 0.5 + slop,
            MarkKind.Ellipse => EllipseHit(stroke, point, slop),
            MarkKind.Arrow => ArrowHit(stroke, point, slop),
            _ => PolylineHit(stroke, point, slop)
        };
    }

    public static bool IntersectsBounds(Stroke stroke, Bounds rect)
    {
        if (rect.IsEmpty || !stroke.GetBounds().Intersects(rect.Inflate(stroke.Size)))
            return false;

        if (stroke.Kind is MarkKind.Text or MarkKind.Image or MarkKind.Rectangle or MarkKind.Ellipse)
            return stroke.GetBounds().Intersects(rect);

        if (stroke.Points.Count == 0)
            return false;
        if (stroke.Points.Count == 1)
            return rect.Contains(stroke.Points[0].ToVec());

        for (var i = 0; i < stroke.Points.Count; i++)
        {
            if (rect.Contains(stroke.Points[i].ToVec()))
                return true;
        }

        for (var i = 1; i < stroke.Points.Count; i++)
        {
            if (GeometryUtil.SegmentIntersectsBounds(stroke.Points[i - 1].ToVec(), stroke.Points[i].ToVec(), rect))
                return true;
        }

        return false;
    }

    public static bool IntersectsPolygon(Stroke stroke, IReadOnlyList<Vec2> polygon)
    {
        if (polygon.Count < 3)
            return false;

        if (stroke.Points.Any(point => GeometryUtil.PointInPolygon(point.ToVec(), polygon)))
            return true;

        if (stroke.Kind is MarkKind.Text or MarkKind.Image or MarkKind.Rectangle or MarkKind.Ellipse)
            return GeometryUtil.PointInPolygon(stroke.GetBounds().Center, polygon);

        return false;
    }

    private static bool PolylineHit(Stroke stroke, Vec2 point, double slop)
    {
        if (stroke.Points.Count == 0)
            return false;
        if (stroke.Points.Count == 1)
            return point.DistanceTo(stroke.Points[0].ToVec()) <= StrokeMetrics.RadiusAt(stroke, 0) + slop;

        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var reach = Math.Max(StrokeMetrics.RadiusAt(stroke, i - 1), StrokeMetrics.RadiusAt(stroke, i)) + slop;
            if (GeometryUtil.DistanceToSegment(point, stroke.Points[i - 1].ToVec(), stroke.Points[i].ToVec()) <= reach)
                return true;
        }

        return false;
    }

    private static bool EllipseHit(Stroke stroke, Vec2 point, double slop)
    {
        if (stroke.Points.Count < 2)
            return false;

        var box = Bounds.FromCorners(stroke.Points[0].ToVec(), stroke.Points[1].ToVec());
        var radiusX = Math.Max(0.5, box.Width * 0.5);
        var radiusY = Math.Max(0.5, box.Height * 0.5);
        var center = box.Center;
        var reach = stroke.Size * 0.5 + slop;
        var previous = new Vec2(center.X + radiusX, center.Y);
        const int steps = 48;
        for (var i = 1; i <= steps; i++)
        {
            var angle = i / (double)steps * Math.PI * 2;
            var current = new Vec2(center.X + Math.Cos(angle) * radiusX, center.Y + Math.Sin(angle) * radiusY);
            if (GeometryUtil.DistanceToSegment(point, previous, current) <= reach)
                return true;
            previous = current;
        }

        return false;
    }

    private static bool ArrowHit(Stroke stroke, Vec2 point, double slop)
    {
        if (PolylineHit(stroke, point, slop))
            return true;
        if (stroke.Points.Count < 2)
            return false;

        var start = stroke.Points[0].ToVec();
        var end = stroke.Points[^1].ToVec();
        var direction = (end - start).NormalizedOr(new Vec2(1, 0));
        var normal = new Vec2(-direction.Y, direction.X);
        var length = Math.Max(10, stroke.Size * 4);
        var width = Math.Max(6, stroke.Size * 2.2);
        var back = end - direction * length;
        var left = back + normal * width;
        var right = back - normal * width;
        var reach = slop + 2;
        return GeometryUtil.DistanceToSegment(point, end, left) <= reach ||
               GeometryUtil.DistanceToSegment(point, end, right) <= reach ||
               GeometryUtil.DistanceToSegment(point, left, right) <= reach ||
               GeometryUtil.PointInPolygon(point, new[] { end, left, right });
    }

    private static bool ImageContains(Stroke stroke, Vec2 point, double slop)
    {
        if (stroke.Points.Count == 0)
            return false;

        var origin = stroke.Points[0].ToVec();
        var center = new Vec2(origin.X + stroke.BoxWidth / 2, origin.Y + stroke.BoxHeight / 2);
        var local = Vec2.RotateAround(point, center, -stroke.Rotation * Math.PI / 180);
        return Math.Abs(local.X - center.X) <= stroke.BoxWidth / 2 + slop &&
               Math.Abs(local.Y - center.Y) <= stroke.BoxHeight / 2 + slop;
    }

    private static Bounds CornerBox(Stroke stroke)
    {
        if (stroke.Points.Count < 2)
            return Bounds.Empty;
        return Bounds.FromCorners(stroke.Points[0].ToVec(), stroke.Points[1].ToVec());
    }

    private static double DistanceToRectOutline(Vec2 point, Bounds rect)
    {
        if (rect.IsEmpty)
            return double.PositiveInfinity;

        var outsideX = Math.Max(Math.Max(rect.MinX - point.X, point.X - rect.MaxX), 0);
        var outsideY = Math.Max(Math.Max(rect.MinY - point.Y, point.Y - rect.MaxY), 0);
        if (outsideX > 0 || outsideY > 0)
            return Math.Sqrt(outsideX * outsideX + outsideY * outsideY);

        var left = point.X - rect.MinX;
        var right = rect.MaxX - point.X;
        var top = point.Y - rect.MinY;
        var bottom = rect.MaxY - point.Y;
        return Math.Min(Math.Min(left, right), Math.Min(top, bottom));
    }
}

public static class EraserMath
{
    public static bool TryErase(Stroke stroke, Vec2 center, double radius, bool wholeStroke, out List<Stroke> pieces)
    {
        if (wholeStroke || stroke.Kind is not (MarkKind.Freehand or MarkKind.Line or MarkKind.Arrow))
        {
            if (HitTester.Hits(stroke, center, radius))
            {
                pieces = new List<Stroke>();
                return true;
            }

            pieces = new List<Stroke> { stroke };
            return false;
        }

        if (stroke.Points.Count == 0)
        {
            pieces = new List<Stroke> { stroke };
            return false;
        }

        var keep = new bool[stroke.Points.Count];
        for (var i = 0; i < stroke.Points.Count; i++)
        {
            var reach = radius + StrokeMetrics.RadiusAt(stroke, i);
            keep[i] = stroke.Points[i].ToVec().DistanceTo(center) > reach;
        }

        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var reach = radius + Math.Max(StrokeMetrics.RadiusAt(stroke, i - 1), StrokeMetrics.RadiusAt(stroke, i));
            if (GeometryUtil.DistanceToSegment(center, stroke.Points[i - 1].ToVec(), stroke.Points[i].ToVec()) <= reach)
            {
                keep[i - 1] = false;
                keep[i] = false;
            }
        }

        if (keep.All(value => value))
        {
            pieces = new List<Stroke> { stroke };
            return false;
        }

        pieces = new List<Stroke>();
        List<StrokePoint>? run = null;
        for (var i = 0; i < keep.Length; i++)
        {
            if (keep[i])
            {
                run ??= new List<StrokePoint>();
                run.Add(stroke.Points[i].Clone());
            }
            else if (run != null)
            {
                Flush(stroke, run, pieces);
                run = null;
            }
        }

        if (run != null)
            Flush(stroke, run, pieces);

        return true;
    }

    private static void Flush(Stroke source, List<StrokePoint> run, List<Stroke> pieces)
    {
        if (run.Count == 0)
            return;

        var clone = source.Clone(newId: true);
        clone.Points = run;
        pieces.Add(clone);
    }
}
