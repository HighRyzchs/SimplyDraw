using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Geometry;

public static class StrokeSmoothing
{
    public static List<StrokePoint> Simplify(IReadOnlyList<StrokePoint> points, double tolerance)
    {
        if (points.Count < 3 || tolerance <= 0)
            return points.Select(point => point.Clone()).ToList();

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(points, 0, points.Count - 1, tolerance, keep);

        var result = new List<StrokePoint>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i])
                result.Add(points[i].Clone());
        }

        return result;
    }

    public static List<StrokePoint> Chaikin(IReadOnlyList<StrokePoint> points)
    {
        if (points.Count < 3)
            return points.Select(point => point.Clone()).ToList();

        var result = new List<StrokePoint> { points[0].Clone() };
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = points[i];
            var b = points[i + 1];
            result.Add(Mix(a, b, 0.75, 0.25));
            result.Add(Mix(a, b, 0.25, 0.75));
        }

        result.Add(points[^1].Clone());
        return result;
    }

    private static void SimplifyRange(IReadOnlyList<StrokePoint> points, int start, int end, double tolerance, bool[] keep)
    {
        double max = 0;
        var index = -1;
        var a = points[start].ToVec();
        var b = points[end].ToVec();
        for (var i = start + 1; i < end; i++)
        {
            var distance = GeometryUtil.DistanceToSegment(points[i].ToVec(), a, b);
            if (distance > max)
            {
                max = distance;
                index = i;
            }
        }

        if (index < 0 || max <= tolerance)
            return;

        keep[index] = true;
        SimplifyRange(points, start, index, tolerance, keep);
        SimplifyRange(points, index, end, tolerance, keep);
    }

    private static StrokePoint Mix(StrokePoint a, StrokePoint b, double weightA, double weightB) => new()
    {
        X = a.X * weightA + b.X * weightB,
        Y = a.Y * weightA + b.Y * weightB,
        Pressure = a.Pressure * weightA + b.Pressure * weightB
    };
}

public sealed class StrokeStabilizer
{
    private readonly List<StrokePoint> _points = new();
    private Vec2 _filtered;
    private bool _started;

    public double Smoothing { get; set; } = 0.4;

    public double MinDistance { get; set; } = 0.8;

    public IReadOnlyList<StrokePoint> Points => _points;

    public void Start(StrokePoint point)
    {
        _points.Clear();
        _points.Add(point.Clone());
        _filtered = point.ToVec();
        _started = true;
    }

    public void Add(StrokePoint raw)
    {
        if (!_started)
        {
            Start(raw);
            return;
        }

        var follow = 1 - Math.Clamp(Smoothing, 0, 0.95) * 0.92;
        var target = raw.ToVec();
        _filtered += (target - _filtered) * follow;
        if (_filtered.DistanceTo(_points[^1].ToVec()) < MinDistance)
            return;

        _points.Add(new StrokePoint { X = _filtered.X, Y = _filtered.Y, Pressure = raw.Pressure });
    }

    public void Finish(StrokePoint raw)
    {
        if (!_started)
            return;

        if (raw.ToVec().DistanceTo(_points[^1].ToVec()) >= MinDistance * 0.35)
            _points.Add(raw.Clone());
    }
}

public static class StrokeTransform
{
    public static void Translate(Stroke target, Stroke original, double dx, double dy)
    {
        target.CopyGeometryFrom(original);
        foreach (var point in target.Points)
        {
            point.X += dx;
            point.Y += dy;
        }
    }

    public static void Scale(Stroke target, Stroke original, Vec2 center, double sx, double sy)
    {
        target.CopyGeometryFrom(original);
        sx = Math.Max(0.05, Math.Abs(sx));
        sy = Math.Max(0.05, Math.Abs(sy));

        if (original.Kind == MarkKind.Image && original.Points.Count > 0)
        {
            var origin = original.Points[0].ToVec();
            var imageCenter = new Vec2(origin.X + original.BoxWidth / 2, origin.Y + original.BoxHeight / 2);
            var moved = new Vec2(center.X + (imageCenter.X - center.X) * sx, center.Y + (imageCenter.Y - center.Y) * sy);
            target.BoxWidth = original.BoxWidth * sx;
            target.BoxHeight = original.BoxHeight * sy;
            target.Points[0].X = moved.X - target.BoxWidth / 2;
            target.Points[0].Y = moved.Y - target.BoxHeight / 2;
            return;
        }

        if (original.Kind == MarkKind.Text && original.Points.Count > 0)
        {
            var anchor = original.Points[0].ToVec();
            var moved = new Vec2(center.X + (anchor.X - center.X) * sx, center.Y + (anchor.Y - center.Y) * sy);
            target.Points[0].X = moved.X;
            target.Points[0].Y = moved.Y;
            var uniform = (sx + sy) * 0.5;
            target.FontSize = Math.Max(2, original.FontSize * uniform);
            target.BoxWidth = original.TextWidth() * sx;
            target.BoxHeight = original.TextHeight() * sy;
            return;
        }

        foreach (var point in target.Points)
        {
            point.X = center.X + (point.X - center.X) * sx;
            point.Y = center.Y + (point.Y - center.Y) * sy;
        }
    }

    public static void Rotate(Stroke target, Stroke original, Vec2 center, double radians)
    {
        target.CopyGeometryFrom(original);
        if (original.Points.Count == 0)
            return;

        if (original.Kind == MarkKind.Image)
        {
            var origin = original.Points[0].ToVec();
            var imageCenter = new Vec2(origin.X + original.BoxWidth / 2, origin.Y + original.BoxHeight / 2);
            var moved = Vec2.RotateAround(imageCenter, center, radians);
            target.Rotation = original.Rotation + radians * 180 / Math.PI;
            target.Points[0].X = moved.X - target.BoxWidth / 2;
            target.Points[0].Y = moved.Y - target.BoxHeight / 2;
            return;
        }

        if (original.Kind == MarkKind.Text)
        {
            var moved = Vec2.RotateAround(original.Points[0].ToVec(), center, radians);
            target.Points[0].X = moved.X;
            target.Points[0].Y = moved.Y;
            target.Rotation = original.Rotation + radians * 180 / Math.PI;
            return;
        }

        foreach (var point in target.Points)
        {
            var moved = Vec2.RotateAround(point.ToVec(), center, radians);
            point.X = moved.X;
            point.Y = moved.Y;
        }
    }
}
