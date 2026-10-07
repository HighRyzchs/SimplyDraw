using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Geometry;

public static class StrokeMetrics
{
    public static double RadiusAt(Stroke stroke, int index)
    {
        if (stroke.Points.Count == 0)
            return Math.Max(0.2, stroke.Size * 0.5);

        index = Math.Clamp(index, 0, stroke.Points.Count - 1);
        var pressure = Math.Clamp(stroke.Points[index].Pressure, 0, 1);
        var factor = stroke.PressureSize
            ? stroke.MinWidthFactor + (1 - stroke.MinWidthFactor) * pressure
            : 1;
        var radius = Math.Max(0.15, stroke.Size * factor * 0.5);

        if (stroke.DirectionalNib && stroke.Points.Count > 1)
        {
            var before = Math.Max(0, index - 1);
            var after = Math.Min(stroke.Points.Count - 1, index + 1);
            var direction = (stroke.Points[after].ToVec() - stroke.Points[before].ToVec()).NormalizedOr(new Vec2(1, 0));
            var nib = new Vec2(Math.Cos(Math.PI / 4), Math.Sin(Math.PI / 4));
            var align = Math.Abs(Vec2.Dot(direction, nib));
            radius *= 0.22 + 0.78 * align;
        }

        if (stroke.Taper && stroke.Points.Count > 2)
        {
            var edge = Math.Min(index, stroke.Points.Count - 1 - index) / 8.0;
            radius *= Math.Clamp(edge, 0.12, 1);
        }

        return radius;
    }

    public static double[] Radii(Stroke stroke)
    {
        var radii = new double[stroke.Points.Count];
        for (var i = 0; i < radii.Length; i++)
            radii[i] = RadiusAt(stroke, i);
        return radii;
    }
}

public static class RibbonBuilder
{
    public static IReadOnlyList<Vec2> Build(IReadOnlyList<Vec2> centers, IReadOnlyList<double> radii)
    {
        if (centers.Count == 0 || radii.Count == 0)
            return Array.Empty<Vec2>();

        if (centers.Count == 1)
            return Circle(centers[0], Math.Max(0.2, radii[0]), 16);

        var count = Math.Min(centers.Count, radii.Count);
        var left = new Vec2[count];
        var right = new Vec2[count];
        for (var i = 0; i < count; i++)
        {
            var (direction, scale) = Offset(centers, i, count);
            var radius = Math.Max(0.15, radii[i]) * scale;
            left[i] = centers[i] + direction * radius;
            right[i] = centers[i] - direction * radius;
        }

        var polygon = new List<Vec2>(count * 2 + 16);
        var startOut = (centers[0] - centers[1]).NormalizedOr(new Vec2(-1, 0));
        var endOut = (centers[count - 1] - centers[count - 2]).NormalizedOr(new Vec2(1, 0));
        AddCap(polygon, centers[0], right[0], left[0], startOut, includeStart: true);
        for (var i = 1; i < count; i++)
            polygon.Add(left[i]);

        AddCap(polygon, centers[count - 1], left[count - 1], right[count - 1], endOut, includeStart: false);
        for (var i = count - 2; i >= 1; i--)
            polygon.Add(right[i]);

        return polygon;
    }

    private static (Vec2 Direction, double Scale) Offset(IReadOnlyList<Vec2> centers, int index, int count)
    {
        if (index <= 0 || index >= count - 1)
        {
            var tangent = index <= 0
                ? (centers[1] - centers[0]).NormalizedOr(new Vec2(1, 0))
                : (centers[count - 1] - centers[count - 2]).NormalizedOr(new Vec2(1, 0));
            return (new Vec2(-tangent.Y, tangent.X), 1);
        }

        var incoming = (centers[index] - centers[index - 1]).NormalizedOr(new Vec2(1, 0));
        var outgoing = (centers[index + 1] - centers[index]).NormalizedOr(new Vec2(1, 0));
        var normalIn = new Vec2(-incoming.Y, incoming.X);
        var normalOut = new Vec2(-outgoing.Y, outgoing.X);
        var miter = normalIn + normalOut;
        if (miter.LengthSquared < 1e-8)
            return (normalOut, 1);

        miter = miter.NormalizedOr(normalOut);
        if (Vec2.Dot(miter, normalOut) < 0)
            miter = -miter;

        var denominator = Math.Abs(Vec2.Dot(miter, normalOut));
        if (denominator < 0.35)
            return (normalOut, 1);

        return (miter, Math.Min(2.2, 1 / denominator));
    }

    private static void AddCap(List<Vec2> polygon, Vec2 center, Vec2 from, Vec2 to, Vec2 outward, bool includeStart)
    {
        var startVector = from - center;
        var endVector = to - center;
        var radius = Math.Max(0.15, (startVector.Length + endVector.Length) * 0.5);
        var startAngle = Math.Atan2(startVector.Y, startVector.X);
        var endAngle = Math.Atan2(endVector.Y, endVector.X);
        var delta = endAngle - startAngle;
        while (delta > Math.PI)
            delta -= Math.PI * 2;
        while (delta <= -Math.PI)
            delta += Math.PI * 2;

        var mid = startAngle + delta * 0.5;
        var midDirection = new Vec2(Math.Cos(mid), Math.Sin(mid));
        if (Vec2.Dot(midDirection, outward) < 0)
            delta += delta >= 0 ? -Math.PI * 2 : Math.PI * 2;

        const int steps = 5;
        var first = includeStart ? 0 : 1;
        for (var step = first; step <= steps; step++)
        {
            var angle = startAngle + delta * (step / (double)steps);
            polygon.Add(new Vec2(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius));
        }
    }

    private static List<Vec2> Circle(Vec2 center, double radius, int steps)
    {
        var points = new List<Vec2>(steps);
        for (var i = 0; i < steps; i++)
        {
            var angle = i / (double)steps * Math.PI * 2;
            points.Add(new Vec2(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius));
        }

        return points;
    }
}

public static class StampLayout
{
    public readonly record struct Stamp(Vec2 Center, double Radius, double Pressure, int Index);

    public static List<Stamp> Build(Stroke stroke)
    {
        var result = new List<Stamp>();
        if (stroke.Points.Count == 0)
            return result;

        var spacing = Math.Max(0.8, stroke.Size * Math.Clamp(stroke.StampSpacing, 0.05, 2));
        if (stroke.Points.Count == 1)
        {
            result.Add(new Stamp(stroke.Points[0].ToVec(), StrokeMetrics.RadiusAt(stroke, 0), stroke.Points[0].Pressure, 0));
            return result;
        }

        var traveled = 0d;
        var next = 0d;
        var index = 0;
        for (var i = 1; i < stroke.Points.Count; i++)
        {
            var start = stroke.Points[i - 1].ToVec();
            var end = stroke.Points[i].ToVec();
            var length = start.DistanceTo(end);
            if (length < 1e-6)
                continue;

            while (next <= traveled + length)
            {
                var t = (next - traveled) / length;
                var position = Vec2.Lerp(start, end, Math.Clamp(t, 0, 1));
                var pressure = stroke.Points[i - 1].Pressure + (stroke.Points[i].Pressure - stroke.Points[i - 1].Pressure) * t;
                var radius = StrokeMetrics.RadiusAt(stroke, t < 0.5 ? i - 1 : i);
                result.Add(new Stamp(position, radius, pressure, index));
                index++;
                next += spacing;
                if (result.Count > 4000)
                    return result;
            }

            traveled += length;
        }

        if (result.Count == 0)
        {
            var last = stroke.Points.Count - 1;
            result.Add(new Stamp(stroke.Points[last].ToVec(), StrokeMetrics.RadiusAt(stroke, last), stroke.Points[last].Pressure, 0));
        }

        return result;
    }
}

public static class GrainOffset
{
    public static Vec2 Offset(string seed, int index, double amount, double radius)
    {
        if (amount <= 0.001 || radius <= 0)
            return Vec2.Zero;

        var hash = Hash(seed, index);
        var x = ((hash & 0xFFFF) / 65535d) * 2 - 1;
        var y = (((hash >> 16) & 0xFFFF) / 65535d) * 2 - 1;
        return new Vec2(x, y) * radius * amount;
    }

    private static uint Hash(string seed, int index)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var character in seed)
            {
                hash ^= character;
                hash *= 16777619;
            }

            hash ^= (uint)index;
            hash *= 16777619;
            return hash;
        }
    }
}
