using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Geometry;

public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 Zero { get; } = new(0, 0);

    public double LengthSquared => X * X + Y * Y;

    public double Length => Math.Sqrt(LengthSquared);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Vec2 operator *(Vec2 a, double scalar) => new(a.X * scalar, a.Y * scalar);

    public static Vec2 operator *(double scalar, Vec2 a) => new(a.X * scalar, a.Y * scalar);

    public static Vec2 operator /(Vec2 a, double scalar) => new(a.X / scalar, a.Y / scalar);

    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);

    public double DistanceTo(Vec2 other) => (this - other).Length;

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    public Vec2 NormalizedOr(Vec2 fallback)
    {
        var length = Length;
        return length < 1e-9 ? fallback : new Vec2(X / length, Y / length);
    }

    public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => a + (b - a) * t;

    public static Vec2 RotateAround(Vec2 point, Vec2 center, double radians)
    {
        var sin = Math.Sin(radians);
        var cos = Math.Cos(radians);
        var delta = point - center;
        return new Vec2(center.X + delta.X * cos - delta.Y * sin, center.Y + delta.X * sin + delta.Y * cos);
    }
}

public readonly record struct Bounds(double MinX, double MinY, double MaxX, double MaxY)
{
    public static Bounds Empty { get; } = new(
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity);

    public bool IsEmpty => MaxX < MinX || MaxY < MinY || double.IsInfinity(MinX);

    public double Width => IsEmpty ? 0 : MaxX - MinX;

    public double Height => IsEmpty ? 0 : MaxY - MinY;

    public Vec2 Center => new((MinX + MaxX) * 0.5, (MinY + MaxY) * 0.5);

    public Bounds Inflate(double amount) =>
        IsEmpty ? this : new Bounds(MinX - amount, MinY - amount, MaxX + amount, MaxY + amount);

    public bool Contains(Vec2 point) =>
        !IsEmpty && point.X >= MinX && point.X <= MaxX && point.Y >= MinY && point.Y <= MaxY;

    public bool Intersects(Bounds other)
    {
        if (IsEmpty || other.IsEmpty)
            return false;

        return MinX <= other.MaxX && MaxX >= other.MinX && MinY <= other.MaxY && MaxY >= other.MinY;
    }

    public Bounds Include(Vec2 point)
    {
        if (IsEmpty)
            return new Bounds(point.X, point.Y, point.X, point.Y);

        return new Bounds(
            Math.Min(MinX, point.X),
            Math.Min(MinY, point.Y),
            Math.Max(MaxX, point.X),
            Math.Max(MaxY, point.Y));
    }

    public static Bounds Union(Bounds a, Bounds b)
    {
        if (a.IsEmpty)
            return b;
        if (b.IsEmpty)
            return a;

        return new Bounds(
            Math.Min(a.MinX, b.MinX),
            Math.Min(a.MinY, b.MinY),
            Math.Max(a.MaxX, b.MaxX),
            Math.Max(a.MaxY, b.MaxY));
    }

    public static Bounds FromCorners(Vec2 a, Vec2 b) => new(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X),
        Math.Max(a.Y, b.Y));

    public static Bounds FromPoints(IEnumerable<Vec2> points)
    {
        var bounds = Empty;
        foreach (var point in points)
            bounds = bounds.Include(point);
        return bounds;
    }
}

public sealed class Camera
{
    public const double MinZoom = 0.02;
    public const double MaxZoom = 64;

    public double Zoom { get; set; } = 1;

    public double PanX { get; set; }

    public double PanY { get; set; }

    public Vec2 ScreenToWorld(Vec2 screen) => new((screen.X - PanX) / Zoom, (screen.Y - PanY) / Zoom);

    public Vec2 WorldToScreen(Vec2 world) => new(world.X * Zoom + PanX, world.Y * Zoom + PanY);

    public void PanBy(double dx, double dy)
    {
        PanX += dx;
        PanY += dy;
    }

    public void ZoomAt(Vec2 screen, double factor)
    {
        if (factor <= 0 || double.IsNaN(factor) || double.IsInfinity(factor))
            return;

        var before = ScreenToWorld(screen);
        var next = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(next - Zoom) < 1e-12)
            return;

        Zoom = next;
        PanX = screen.X - before.X * Zoom;
        PanY = screen.Y - before.Y * Zoom;
    }

    public void CenterOn(Vec2 world, double viewWidth, double viewHeight)
    {
        PanX = viewWidth * 0.5 - world.X * Zoom;
        PanY = viewHeight * 0.5 - world.Y * Zoom;
    }

    public void Fit(Bounds bounds, double viewWidth, double viewHeight, double padding = 72)
    {
        if (viewWidth < 2 || viewHeight < 2)
            return;

        if (bounds.IsEmpty)
        {
            Zoom = 1;
            CenterOn(Vec2.Zero, viewWidth, viewHeight);
            return;
        }

        var availableWidth = Math.Max(1, viewWidth - padding * 2);
        var availableHeight = Math.Max(1, viewHeight - padding * 2);
        Zoom = Math.Clamp(
            Math.Min(availableWidth / Math.Max(1, bounds.Width), availableHeight / Math.Max(1, bounds.Height)),
            MinZoom,
            MaxZoom);
        CenterOn(bounds.Center, viewWidth, viewHeight);
    }
}

public static class GeometryUtil
{
    public static Vec2 ConstrainAngle(Vec2 origin, Vec2 point)
    {
        var delta = point - origin;
        var length = delta.Length;
        if (length < 1e-9)
            return point;

        var angle = Math.Atan2(delta.Y, delta.X);
        var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        return origin + new Vec2(Math.Cos(snapped) * length, Math.Sin(snapped) * length);
    }

    public static double DistanceToSegment(Vec2 point, Vec2 start, Vec2 end)
    {
        var edge = end - start;
        var lengthSquared = edge.LengthSquared;
        if (lengthSquared < 1e-12)
            return point.DistanceTo(start);

        var t = Math.Clamp(Vec2.Dot(point - start, edge) / lengthSquared, 0, 1);
        return point.DistanceTo(start + edge * t);
    }

    public static bool PointInPolygon(Vec2 point, IReadOnlyList<Vec2> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            var a = polygon[i];
            var b = polygon[j];
            if ((a.Y > point.Y) != (b.Y > point.Y) &&
                point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y + 1e-12) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static bool SegmentsIntersect(Vec2 a1, Vec2 a2, Vec2 b1, Vec2 b2)
    {
        var d1 = Vec2.Cross(a2 - a1, b1 - a1);
        var d2 = Vec2.Cross(a2 - a1, b2 - a1);
        var d3 = Vec2.Cross(b2 - b1, a1 - b1);
        var d4 = Vec2.Cross(b2 - b1, a2 - b1);
        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    public static bool SegmentIntersectsBounds(Vec2 start, Vec2 end, Bounds box)
    {
        if (box.IsEmpty)
            return false;
        if (box.Contains(start) || box.Contains(end))
            return true;

        var topLeft = new Vec2(box.MinX, box.MinY);
        var topRight = new Vec2(box.MaxX, box.MinY);
        var bottomRight = new Vec2(box.MaxX, box.MaxY);
        var bottomLeft = new Vec2(box.MinX, box.MaxY);
        return SegmentsIntersect(start, end, topLeft, topRight) ||
               SegmentsIntersect(start, end, topRight, bottomRight) ||
               SegmentsIntersect(start, end, bottomRight, bottomLeft) ||
               SegmentsIntersect(start, end, bottomLeft, topLeft);
    }
}

public static class GridSnap
{
    public static Vec2 Snap(Vec2 point, double spacing, GridStyle style)
    {
        if (spacing <= 1 || style == GridStyle.None)
            return point;

        if (style != GridStyle.Isometric)
        {
            return new Vec2(
                Math.Round(point.X / spacing) * spacing,
                Math.Round(point.Y / spacing) * spacing);
        }

        var cos = Math.Cos(Math.PI / 6);
        var sin = Math.Sin(Math.PI / 6);
        var u = (point.X / cos + point.Y / sin) / 2;
        var v = (point.Y / sin - point.X / cos) / 2;
        u = Math.Round(u / spacing) * spacing;
        v = Math.Round(v / spacing) * spacing;
        return new Vec2((u - v) * cos, (u + v) * sin);
    }
}
