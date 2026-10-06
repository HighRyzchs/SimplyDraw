using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Brushes;

public sealed class BrushPreset
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Blurb { get; init; }

    public double DefaultSize { get; init; }

    public double MinSize { get; init; } = 0.4;

    public double MaxSize { get; init; } = 80;

    public double DefaultOpacity { get; init; } = 1;

    public double Smoothing { get; init; } = 0.4;

    public bool PressureSize { get; init; } = true;

    public bool PressureOpacity { get; init; }

    public double MinWidthFactor { get; init; } = 0.2;

    public double Hardness { get; init; } = 1;

    public RenderMode Mode { get; init; } = RenderMode.Ribbon;

    public double StampSpacing { get; init; } = 0.4;

    public bool Taper { get; init; }

    public bool DirectionalNib { get; init; }

    public double Grain { get; init; }
}

public static class BrushLibrary
{
    public static IReadOnlyList<BrushPreset> All { get; } =
    [
        new BrushPreset
        {
            Id = "ink",
            Name = "Ink",
            Blurb = "A responsive pen with tapered ends.",
            DefaultSize = 5,
            Smoothing = 0.48,
            MinWidthFactor = 0.16,
            Taper = true
        },
        new BrushPreset
        {
            Id = "pencil",
            Name = "Pencil",
            Blurb = "Grainy and light, like graphite on paper.",
            DefaultSize = 3.5,
            DefaultOpacity = 0.9,
            Smoothing = 0.22,
            PressureOpacity = true,
            MinWidthFactor = 0.12,
            Hardness = 0.45,
            Mode = RenderMode.Stamps,
            StampSpacing = 0.28,
            Grain = 0.65
        },
        new BrushPreset
        {
            Id = "marker",
            Name = "Marker",
            Blurb = "Broad and even, useful for blocking in color.",
            DefaultSize = 18,
            DefaultOpacity = 0.55,
            Smoothing = 0.58,
            PressureSize = false,
            MinWidthFactor = 1,
            Hardness = 0.92,
            MaxSize = 96
        },
        new BrushPreset
        {
            Id = "soft",
            Name = "Soft brush",
            Blurb = "A round brush that blooms with pressure.",
            DefaultSize = 16,
            Smoothing = 0.64,
            MinWidthFactor = 0.08,
            Hardness = 0.28
        },
        new BrushPreset
        {
            Id = "highlight",
            Name = "Highlighter",
            Blurb = "A wide translucent stroke for emphasis.",
            DefaultSize = 28,
            DefaultOpacity = 0.34,
            Smoothing = 0.72,
            PressureSize = false,
            MinWidthFactor = 1,
            Hardness = 0.55,
            MaxSize = 120
        },
        new BrushPreset
        {
            Id = "technical",
            Name = "Technical",
            Blurb = "A fixed-width line for clean diagrams.",
            DefaultSize = 2.2,
            Smoothing = 0.84,
            PressureSize = false,
            MinWidthFactor = 1,
            MinSize = 0.4,
            MaxSize = 18
        },
        new BrushPreset
        {
            Id = "nib",
            Name = "Nib",
            Blurb = "A broad nib that thins as it turns.",
            DefaultSize = 14,
            Smoothing = 0.32,
            MinWidthFactor = 0.1,
            DirectionalNib = true,
            Taper = true
        },
        new BrushPreset
        {
            Id = "spray",
            Name = "Spray",
            Blurb = "Soft scattered dots for shade and texture.",
            DefaultSize = 34,
            DefaultOpacity = 0.45,
            Smoothing = 0.4,
            PressureSize = false,
            PressureOpacity = true,
            Hardness = 0.08,
            Mode = RenderMode.Stamps,
            StampSpacing = 0.22,
            Grain = 0.85,
            MaxSize = 140
        },
        new BrushPreset
        {
            Id = "fountain",
            Name = "Fountain",
            Blurb = "A flexible tip with strong thick and thin.",
            DefaultSize = 7,
            Smoothing = 0.5,
            MinWidthFactor = 0.05,
            Taper = true
        }
    ];

    public static BrushPreset Get(string? id) => All.FirstOrDefault(brush => brush.Id == id) ?? All[0];

    public static void Apply(BrushPreset preset, Stroke stroke)
    {
        stroke.BrushId = preset.Id;
        stroke.PressureSize = preset.PressureSize;
        stroke.PressureOpacity = preset.PressureOpacity;
        stroke.MinWidthFactor = preset.MinWidthFactor;
        stroke.Hardness = preset.Hardness;
        stroke.Mode = preset.Mode;
        stroke.StampSpacing = preset.StampSpacing;
        stroke.Taper = preset.Taper;
        stroke.DirectionalNib = preset.DirectionalNib;
        stroke.Grain = preset.Grain;
    }
}

public sealed record PaperTheme(string Id, string Name, string Background, string Grid, bool Dark);

public static class PaperThemes
{
    public static IReadOnlyList<PaperTheme> All { get; } =
    [
        new("warm", "Warm paper", "#F3EEE4", "#DDD4C4", false),
        new("white", "White", "#F7F7F5", "#E1E1DE", false),
        new("dark", "Night", "#171A1F", "#313846", true),
        new("blueprint", "Blueprint", "#1B3A4B", "#3E7494", true),
        new("kraft", "Kraft", "#E0C3A4", "#C9A684", false)
    ];

    public static PaperTheme Get(string? id) => All.FirstOrDefault(theme => theme.Id == id) ?? All[0];
}

public static class PaletteColors
{
    public static List<string> Default() =>
    [
        "#1C1917", "#44403C", "#78716C", "#A8A29E",
        "#E25B45", "#E8873A", "#E2B15A", "#7D9A62",
        "#2F8F7A", "#3E7CB1", "#2F5D9F", "#5C5A9A",
        "#8A4F8C", "#C45C74", "#F4EFE6", "#FFFFFF"
    ];
}
