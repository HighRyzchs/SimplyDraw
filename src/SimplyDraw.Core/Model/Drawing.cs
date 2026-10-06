using SimplyDraw.Core.Geometry;

namespace SimplyDraw.Core.Model;

public enum GridStyle
{
    None,
    Dots,
    Lines,
    Isometric
}

public enum MarkKind
{
    Freehand,
    Line,
    Rectangle,
    Ellipse,
    Arrow,
    Text,
    Image
}

public enum RenderMode
{
    Ribbon,
    Stamps
}

public sealed class StrokePoint
{
    public double X { get; set; }

    public double Y { get; set; }

    public double Pressure { get; set; } = 0.5;

    public Vec2 ToVec() => new(X, Y);

    public StrokePoint Clone() => new() { X = X, Y = Y, Pressure = Pressure };
}

public sealed class Stroke
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public MarkKind Kind { get; set; } = MarkKind.Freehand;

    public string BrushId { get; set; } = "ink";

    public string Color { get; set; } = "#1C1917";

    public double Size { get; set; } = 4;

    public double Opacity { get; set; } = 1;

    public bool PressureSize { get; set; } = true;

    public bool PressureOpacity { get; set; }

    public double MinWidthFactor { get; set; } = 0.2;

    public double Hardness { get; set; } = 1;

    public RenderMode Mode { get; set; } = RenderMode.Ribbon;

    public double StampSpacing { get; set; } = 0.4;

    public bool Taper { get; set; }

    public bool DirectionalNib { get; set; }

    public double Grain { get; set; }

    public List<StrokePoint> Points { get; set; } = new();

    public string? Text { get; set; }

    public double FontSize { get; set; } = 32;

    public double BoxWidth { get; set; }

    public double BoxHeight { get; set; }

    public double Rotation { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public byte[]? ImageBytes { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("image")]
    public string? ImageBase64
    {
        get => ImageBytes is { Length: > 0 } ? Convert.ToBase64String(ImageBytes) : null;
        set
        {
            if (string.IsNullOrEmpty(value))
            {
                ImageBytes = null;
                return;
            }

            try
            {
                ImageBytes = Convert.FromBase64String(value);
            }
            catch (FormatException)
            {
                ImageBytes = null;
            }
        }
    }

    public Stroke Clone(bool newId = false)
    {
        return new Stroke
        {
            Id = newId ? Guid.NewGuid().ToString("N") : Id,
            Kind = Kind,
            BrushId = BrushId,
            Color = Color,
            Size = Size,
            Opacity = Opacity,
            PressureSize = PressureSize,
            PressureOpacity = PressureOpacity,
            MinWidthFactor = MinWidthFactor,
            Hardness = Hardness,
            Mode = Mode,
            StampSpacing = StampSpacing,
            Taper = Taper,
            DirectionalNib = DirectionalNib,
            Grain = Grain,
            Points = Points.Select(point => point.Clone()).ToList(),
            Text = Text,
            FontSize = FontSize,
            BoxWidth = BoxWidth,
            BoxHeight = BoxHeight,
            Rotation = Rotation,
            ImageBytes = ImageBytes == null ? null : (byte[])ImageBytes.Clone()
        };
    }

    public void CopyGeometryFrom(Stroke other)
    {
        Points = other.Points.Select(point => point.Clone()).ToList();
        BoxWidth = other.BoxWidth;
        BoxHeight = other.BoxHeight;
        Rotation = other.Rotation;
        FontSize = other.FontSize;
        Text = other.Text;
    }

    public bool GeometryEquals(Stroke other)
    {
        if (Points.Count != other.Points.Count ||
            Math.Abs(BoxWidth - other.BoxWidth) > 1e-4 ||
            Math.Abs(BoxHeight - other.BoxHeight) > 1e-4 ||
            Math.Abs(Rotation - other.Rotation) > 1e-4 ||
            Math.Abs(FontSize - other.FontSize) > 1e-4)
        {
            return false;
        }

        for (var i = 0; i < Points.Count; i++)
        {
            if (Math.Abs(Points[i].X - other.Points[i].X) > 1e-4 ||
                Math.Abs(Points[i].Y - other.Points[i].Y) > 1e-4)
            {
                return false;
            }
        }

        return true;
    }

    public Bounds GetBounds()
    {
        return Kind switch
        {
            MarkKind.Image when Points.Count > 0 => RotatedBox(Points[0].ToVec(), BoxWidth, BoxHeight, Rotation, aroundCenter: true),
            MarkKind.Text when Points.Count > 0 => RotatedBox(Points[0].ToVec(), TextWidth(), TextHeight(), Rotation, aroundCenter: false),
            MarkKind.Rectangle or MarkKind.Ellipse when Points.Count >= 2 => Bounds
                .FromCorners(Points[0].ToVec(), Points[1].ToVec())
                .Inflate(Size * 0.5 + 1),
            _ => PolylineBounds()
        };
    }

    public double TextWidth() => BoxWidth > 1 ? BoxWidth : Math.Max(8, (Text?.Length ?? 1) * FontSize * 0.55);

    public double TextHeight() => BoxHeight > 1 ? BoxHeight : FontSize * 1.25;

    private Bounds PolylineBounds()
    {
        if (Points.Count == 0)
            return Bounds.Empty;

        var bounds = Bounds.Empty;
        foreach (var point in Points)
            bounds = bounds.Include(point.ToVec());

        return bounds.Inflate(Math.Max(Size, 1) * 0.65 + 2);
    }

    private static Bounds RotatedBox(Vec2 origin, double width, double height, double degrees, bool aroundCenter)
    {
        width = Math.Max(0, width);
        height = Math.Max(0, height);
        var center = new Vec2(origin.X + width / 2, origin.Y + height / 2);
        var pivot = aroundCenter ? center : origin;
        var radians = degrees * Math.PI / 180;
        Vec2[] corners =
        [
            origin,
            new(origin.X + width, origin.Y),
            new(origin.X + width, origin.Y + height),
            new(origin.X, origin.Y + height)
        ];

        var bounds = Bounds.Empty;
        foreach (var corner in corners)
            bounds = bounds.Include(Vec2.RotateAround(corner, pivot, radians));
        return bounds;
    }
}

public sealed class Layer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "Layer";

    public bool Visible { get; set; } = true;

    public bool Locked { get; set; }

    public double Opacity { get; set; } = 1;

    public List<Stroke> Strokes { get; set; } = new();

    public Layer Clone(bool newIds)
    {
        return new Layer
        {
            Id = newIds ? Guid.NewGuid().ToString("N") : Id,
            Name = Name,
            Visible = Visible,
            Locked = Locked,
            Opacity = Opacity,
            Strokes = Strokes.Select(stroke => stroke.Clone(newIds)).ToList()
        };
    }
}

public sealed class DrawingDocument
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = "Untitled";

    public GridStyle Grid { get; set; } = GridStyle.Dots;

    public bool SnapToGrid { get; set; }

    public double GridSpacing { get; set; } = 32;

    public string Paper { get; set; } = "warm";

    public double ViewZoom { get; set; } = 1;

    public double ViewCenterX { get; set; }

    public double ViewCenterY { get; set; }

    public List<Layer> Layers { get; set; } = new();

    public int ActiveLayerIndex { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public Layer ActiveLayer
    {
        get
        {
            EnsureLayer();
            ActiveLayerIndex = Math.Clamp(ActiveLayerIndex, 0, Layers.Count - 1);
            return Layers[ActiveLayerIndex];
        }
    }

    public static DrawingDocument CreateNew()
    {
        return new DrawingDocument
        {
            Layers = { new Layer { Name = "Layer 1" } }
        };
    }

    public Layer? FindLayer(string id) => Layers.FirstOrDefault(layer => layer.Id == id);

    public (Layer Layer, Stroke Stroke)? FindStroke(string id)
    {
        foreach (var layer in Layers)
        {
            var stroke = layer.Strokes.FirstOrDefault(item => item.Id == id);
            if (stroke != null)
                return (layer, stroke);
        }

        return null;
    }

    public Bounds ContentBounds()
    {
        var bounds = Bounds.Empty;
        foreach (var layer in Layers)
        {
            if (!layer.Visible)
                continue;

            foreach (var stroke in layer.Strokes)
                bounds = Bounds.Union(bounds, stroke.GetBounds());
        }

        return bounds;
    }

    public void EnsureLayer()
    {
        if (Layers.Count == 0)
            Layers.Add(new Layer { Name = "Layer 1" });
    }
}
