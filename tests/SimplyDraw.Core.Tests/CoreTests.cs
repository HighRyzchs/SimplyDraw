using SimplyDraw.Core.Brushes;
using SimplyDraw.Core.Demo;
using SimplyDraw.Core.Editing;
using SimplyDraw.Core.Geometry;
using SimplyDraw.Core.IO;
using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Tests;

public class GeometryTests
{
    [Fact]
    public void ZoomAt_keeps_the_point_under_the_cursor()
    {
        var camera = new Camera { Zoom = 1, PanX = 10, PanY = -20 };
        var screen = new Vec2(240, 180);
        var before = camera.ScreenToWorld(screen);

        camera.ZoomAt(screen, 2.5);

        var after = camera.ScreenToWorld(screen);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
        Assert.Equal(2.5, camera.Zoom, 6);
    }

    [Fact]
    public void Ribbon_for_a_straight_stroke_has_the_requested_width()
    {
        var centers = new List<Vec2> { new(0, 0), new(100, 0), new(200, 0) };
        var radii = new List<double> { 5, 5, 5 };
        var outline = RibbonBuilder.Build(centers, radii);

        Assert.True(outline.Count >= 6);
        Assert.True(outline.Max(point => point.Y) > 4.5);
        Assert.True(outline.Min(point => point.Y) < -4.5);
        Assert.True(outline.Min(point => point.X) < 0);
        Assert.True(outline.Max(point => point.X) > 200);
    }

    [Fact]
    public void Hit_test_finds_a_nearby_stroke_and_misses_empty_space()
    {
        var stroke = InkLine();
        Assert.True(HitTester.Hits(stroke, new Vec2(50, 2), 1));
        Assert.False(HitTester.Hits(stroke, new Vec2(50, 40), 1));
    }

    [Fact]
    public void Eraser_splits_a_stroke_into_two_pieces()
    {
        var stroke = InkLine();
        var erased = EraserMath.TryErase(stroke, new Vec2(50, 0), 8, wholeStroke: false, out var pieces);

        Assert.True(erased);
        Assert.Equal(2, pieces.Count);
        Assert.True(pieces[0].Points[^1].X < 50);
        Assert.True(pieces[1].Points[0].X > 50);
    }

    [Fact]
    public void Whole_stroke_eraser_removes_a_hit_shape()
    {
        var stroke = new Stroke
        {
            Kind = MarkKind.Rectangle,
            Size = 4,
            PressureSize = false,
            Points =
            {
                new StrokePoint { X = 0, Y = 0 },
                new StrokePoint { X = 80, Y = 40 }
            }
        };

        var erased = EraserMath.TryErase(stroke, new Vec2(0, 20), 6, wholeStroke: true, out var pieces);
        Assert.True(erased);
        Assert.Empty(pieces);
    }

    [Fact]
    public void Simplify_keeps_endpoints_and_drops_a_straight_run()
    {
        var points = Enumerable.Range(0, 11)
            .Select(index => new StrokePoint { X = index * 10, Y = 0, Pressure = 0.5 })
            .ToList();
        points.Insert(6, new StrokePoint { X = 50, Y = 30, Pressure = 0.5 });

        var simplified = StrokeSmoothing.Simplify(points, 2);
        Assert.Equal(0, simplified[0].X);
        Assert.Equal(points[^1].X, simplified[^1].X);
        Assert.Contains(simplified, point => point.Y > 20);
        Assert.True(simplified.Count < points.Count);
    }

    [Fact]
    public void Rectangular_snap_rounds_to_the_spacing()
    {
        var snapped = GridSnap.Snap(new Vec2(17, 46), 16, GridStyle.Lines);
        Assert.Equal(16, snapped.X, 6);
        Assert.Equal(48, snapped.Y, 6);
    }

    private static Stroke InkLine()
    {
        var stroke = new Stroke
        {
            Size = 4,
            PressureSize = false,
            MinWidthFactor = 1,
            Hardness = 1
        };
        for (var x = 0; x <= 100; x += 10)
            stroke.Points.Add(new StrokePoint { X = x, Y = 0, Pressure = 0.8 });
        return stroke;
    }
}

public class DocumentTests
{
    [Fact]
    public void File_round_trip_preserves_strokes_layers_and_view()
    {
        var document = DrawingDocument.CreateNew();
        document.Name = "Round trip";
        document.Paper = "blueprint";
        document.Grid = GridStyle.Isometric;
        document.ViewZoom = 1.5;
        document.ViewCenterX = 12;
        document.ViewCenterY = -4;
        document.ActiveLayer.Name = "Ink";
        document.ActiveLayer.Strokes.Add(new Stroke
        {
            BrushId = "fountain",
            Color = "#E25B45",
            Size = 6,
            Points =
            {
                new StrokePoint { X = 1, Y = 2, Pressure = 0.2 },
                new StrokePoint { X = 8, Y = 9, Pressure = 0.9 }
            }
        });

        var path = Path.Combine(Path.GetTempPath(), $"simplydraw-{Guid.NewGuid():N}.sdraw");
        try
        {
            DrawingFile.Save(document, path);
            var loaded = DrawingFile.Load(path);
            Assert.Equal("Round trip", loaded.Name);
            Assert.Equal("blueprint", loaded.Paper);
            Assert.Equal(GridStyle.Isometric, loaded.Grid);
            Assert.Equal(1.5, loaded.ViewZoom, 6);
            Assert.Equal("Ink", loaded.ActiveLayer.Name);
            Assert.Equal("#E25B45", loaded.ActiveLayer.Strokes[0].Color);
            Assert.Equal(8, loaded.ActiveLayer.Strokes[0].Points[1].X, 6);
            Assert.Equal(0.9, loaded.ActiveLayer.Strokes[0].Points[1].Pressure, 6);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Undo_removes_a_committed_stroke_and_redo_restores_it()
    {
        var document = DrawingDocument.CreateNew();
        var stroke = new Stroke();
        stroke.Points.Add(new StrokePoint { X = 3, Y = 4 });
        document.ActiveLayer.Strokes.Add(stroke);
        var history = new History();
        history.Push(new AddStrokeEdit(document.ActiveLayer.Id, stroke));

        history.Undo(document);
        Assert.Empty(document.ActiveLayer.Strokes);

        history.Redo(document);
        Assert.Single(document.ActiveLayer.Strokes);
        Assert.Equal(stroke.Id, document.ActiveLayer.Strokes[0].Id);
    }

    [Fact]
    public void Svg_export_contains_paper_and_a_mark()
    {
        var document = DrawingDocument.CreateNew();
        document.ActiveLayer.Strokes.Add(new Stroke
        {
            Color = "#112233",
            Size = 4,
            PressureSize = false,
            Points =
            {
                new StrokePoint { X = 0, Y = 0, Pressure = 1 },
                new StrokePoint { X = 40, Y = 0, Pressure = 1 }
            }
        });

        var svg = SvgWriter.Export(document);
        Assert.Contains("<svg", svg, StringComparison.Ordinal);
        Assert.Contains("#112233", svg, StringComparison.Ordinal);
        Assert.Contains("<polygon", svg, StringComparison.Ordinal);
        Assert.Contains("fill-rule=\"nonzero\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Brush_ids_are_unique_and_demo_has_content()
    {
        Assert.Equal(BrushLibrary.All.Count, BrushLibrary.All.Select(brush => brush.Id).Distinct().Count());
        var demo = DemoSketch.Create();
        Assert.False(demo.ContentBounds().IsEmpty);
        Assert.True(demo.Layers.Sum(layer => layer.Strokes.Count) >= 8);
    }

    [Fact]
    public void Color_parser_accepts_short_and_long_hex()
    {
        var shortHex = Rgba.Parse("#abc");
        Assert.Equal(0xAA, shortHex.R);
        Assert.Equal(0xBB, shortHex.G);
        Assert.Equal(0xCC, shortHex.B);

        var hsv = Rgba.FromHsv(0, 1, 1);
        Assert.Equal(255, hsv.R);
        Assert.Equal(0, hsv.G);
    }
}
