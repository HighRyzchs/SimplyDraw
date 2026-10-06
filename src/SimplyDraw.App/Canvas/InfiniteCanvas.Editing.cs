namespace SimplyDraw.Canvas;

public partial class InfiniteCanvas
{
    private Stroke CreateStroke(MarkKind kind)
    {
        var preset = BrushLibrary.Get(BrushId);
        var stroke = new Stroke
        {
            Kind = kind,
            Color = ColorHex,
            Size = Math.Clamp(BrushSize, preset.MinSize, kind == MarkKind.Text ? 240 : preset.MaxSize),
            Opacity = Math.Clamp(BrushOpacity, 0.02, 1)
        };
        BrushLibrary.Apply(preset, stroke);
        if (kind != MarkKind.Freehand)
        {
            stroke.Mode = RenderMode.Ribbon;
            stroke.Taper = false;
            stroke.DirectionalNib = false;
            stroke.PressureSize = false;
            stroke.PressureOpacity = false;
        }

        return stroke;
    }

    private void CommitStroke(Stroke stroke)
    {
        var layer = Document.ActiveLayer;
        if (!CanDrawOn(layer))
            return;

        layer.Strokes.Add(stroke);
        History.Push(new AddStrokeEdit(layer.Id, stroke));
        RebuildStrokes();
        MarkDirty();
    }

    private bool CanDrawOn(Layer layer)
    {
        if (!layer.Locked && layer.Visible)
            return true;

        Status("The active layer is locked or hidden.");
        return false;
    }

    private Vec2 MaybeSnap(Vec2 world) =>
        Document.SnapToGrid ? GridSnap.Snap(world, Document.GridSpacing, Document.Grid) : world;

    private void SelectOnly(string id)
    {
        _selection.Clear();
        _selection.Add(id);
        NotifySelection();
    }

    private void ToggleSelection(string id)
    {
        if (!_selection.Add(id))
            _selection.Remove(id);
        NotifySelection();
    }

    private bool IsSelected(string id) => _selection.Contains(id);

    private void ClearSelection()
    {
        if (_selection.Count == 0)
        {
            RedrawOverlay();
            return;
        }

        _selection.Clear();
        NotifySelection();
    }

    private void NotifySelection()
    {
        RedrawOverlay();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PruneSelection() => _selection.RemoveWhere(id => Document.FindStroke(id) == null);

    private Bounds SelectionBounds()
    {
        var bounds = Bounds.Empty;
        foreach (var id in _selection)
        {
            var found = Document.FindStroke(id);
            if (found != null)
                bounds = Bounds.Union(bounds, found.Value.Stroke.GetBounds());
        }

        return bounds;
    }

    private Dictionary<string, Stroke> CaptureSelection()
    {
        var map = new Dictionary<string, Stroke>();
        foreach (var id in _selection)
        {
            var found = Document.FindStroke(id);
            if (found == null || found.Value.Layer.Locked || !found.Value.Layer.Visible)
                continue;
            map[id] = found.Value.Stroke.Clone();
        }

        return map;
    }

    private void PreviewTranslate(Dictionary<string, Stroke> originals, double dx, double dy)
    {
        foreach (var (id, original) in originals)
        {
            var found = Document.FindStroke(id);
            if (found != null)
                StrokeTransform.Translate(found.Value.Stroke, original, dx, dy);
        }

        RebuildStrokes();
        RedrawOverlay();
    }

    private void PreviewScale(Dictionary<string, Stroke> originals, Vec2 center, double sx, double sy)
    {
        foreach (var (id, original) in originals)
        {
            var found = Document.FindStroke(id);
            if (found != null)
                StrokeTransform.Scale(found.Value.Stroke, original, center, sx, sy);
        }

        RebuildStrokes();
        RedrawOverlay();
    }

    private void PreviewRotate(Dictionary<string, Stroke> originals, Vec2 center, double radians)
    {
        foreach (var (id, original) in originals)
        {
            var found = Document.FindStroke(id);
            if (found != null)
                StrokeTransform.Rotate(found.Value.Stroke, original, center, radians);
        }

        RebuildStrokes();
        RedrawOverlay();
    }

    private void CommitTransform(Dictionary<string, Stroke> originals, string name)
    {
        var changes = new List<(string StrokeId, Stroke Before, Stroke After)>();
        foreach (var (id, original) in originals)
        {
            var found = Document.FindStroke(id);
            if (found == null || found.Value.Stroke.GeometryEquals(original))
                continue;
            changes.Add((id, original, found.Value.Stroke));
        }

        if (changes.Count == 0)
            return;

        History.Push(new MutateStrokesEdit(name, changes));
        MarkDirty();
    }

    private void RestoreGeometry(Dictionary<string, Stroke> originals)
    {
        foreach (var (id, original) in originals)
            Document.FindStroke(id)?.Stroke.CopyGeometryFrom(original);
        RebuildStrokes();
        RedrawOverlay();
    }

    private void SelectInBounds(Bounds rect, bool append)
    {
        if (!append)
            _selection.Clear();

        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible || layer.Locked)
                continue;
            foreach (var stroke in layer.Strokes)
            {
                if (HitTester.IntersectsBounds(stroke, rect))
                    _selection.Add(stroke.Id);
            }
        }

        NotifySelection();
    }

    private void SelectInPolygon(IReadOnlyList<Vec2> polygon, bool append)
    {
        if (!append)
            _selection.Clear();

        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible || layer.Locked)
                continue;
            foreach (var stroke in layer.Strokes)
            {
                if (HitTester.IntersectsPolygon(stroke, polygon))
                    _selection.Add(stroke.Id);
            }
        }

        NotifySelection();
    }

    private Stroke? HitTop(Vec2 world, double slop)
    {
        for (var layerIndex = Document.Layers.Count - 1; layerIndex >= 0; layerIndex--)
        {
            var layer = Document.Layers[layerIndex];
            if (!layer.Visible || layer.Locked)
                continue;

            for (var strokeIndex = layer.Strokes.Count - 1; strokeIndex >= 0; strokeIndex--)
            {
                var stroke = layer.Strokes[strokeIndex];
                if (HitTester.Hits(stroke, world, slop))
                    return stroke;
            }
        }

        return null;
    }

    private HandleKind HitHandle(Vec2 screen)
    {
        if (_selection.Count == 0)
            return HandleKind.None;

        var bounds = SelectionBounds();
        if (bounds.IsEmpty)
            return HandleKind.None;

        var rect = ToScreen(bounds);
        var rotate = new Vec2(rect.Left + rect.Width / 2, rect.Top - 28);
        if (screen.DistanceTo(rotate) <= 12)
            return HandleKind.Rotate;

        (HandleKind Kind, Vec2 Point)[] handles =
        [
            (HandleKind.NW, new(rect.Left, rect.Top)),
            (HandleKind.N, new(rect.Left + rect.Width / 2, rect.Top)),
            (HandleKind.NE, new(rect.Right, rect.Top)),
            (HandleKind.E, new(rect.Right, rect.Top + rect.Height / 2)),
            (HandleKind.SE, new(rect.Right, rect.Bottom)),
            (HandleKind.S, new(rect.Left + rect.Width / 2, rect.Bottom)),
            (HandleKind.SW, new(rect.Left, rect.Bottom)),
            (HandleKind.W, new(rect.Left, rect.Top + rect.Height / 2))
        ];

        foreach (var handle in handles)
        {
            if (screen.DistanceTo(handle.Point) <= 10)
                return handle.Kind;
        }

        return HandleKind.None;
    }

    private Rect ToScreen(Bounds bounds)
    {
        var min = Camera.WorldToScreen(new Vec2(bounds.MinX, bounds.MinY));
        var max = Camera.WorldToScreen(new Vec2(bounds.MaxX, bounds.MaxY));
        var x = Math.Min(min.X, max.X);
        var y = Math.Min(min.Y, max.Y);
        return new Rect(x, y, Math.Max(1, Math.Abs(max.X - min.X)), Math.Max(1, Math.Abs(max.Y - min.Y)));
    }

    private static IEnumerable<Point> HandlePoints(Rect rect)
    {
        yield return rect.TopLeft;
        yield return new Point(rect.Left + rect.Width / 2, rect.Top);
        yield return rect.TopRight;
        yield return new Point(rect.Right, rect.Top + rect.Height / 2);
        yield return rect.BottomRight;
        yield return new Point(rect.Left + rect.Width / 2, rect.Bottom);
        yield return rect.BottomLeft;
        yield return new Point(rect.Left, rect.Top + rect.Height / 2);
    }

    private void BeginText(PointerSample sample)
    {
        if (!CanDrawOn(Document.ActiveLayer))
            return;

        _textWorld = sample.World;
        var font = BrushSize >= 10 ? Math.Clamp(BrushSize, 10, 240) : 32;
        TextRequested?.Invoke(this, new TextPrompt(new Point(sample.Screen.X, sample.Screen.Y), font));
    }

    private void EraseSegment(Vec2 from, Vec2 to)
    {
        _eraseBefore ??= SnapshotEditableStrokes();
        var radius = EraserRadius / Math.Max(0.02, Camera.Zoom);
        var distance = from.DistanceTo(to);
        var step = Math.Max(0.8, EraserRadius * 0.45) / Math.Max(0.02, Camera.Zoom);
        var count = Math.Max(1, (int)Math.Ceiling(distance / Math.Max(step, 0.1)));
        var changed = false;
        for (var i = 0; i <= count; i++)
            changed |= EraseAt(Vec2.Lerp(from, to, i / (double)count), radius);

        if (changed)
            RebuildStrokes();
        RedrawOverlay();
    }

    private bool EraseAt(Vec2 world, double radius)
    {
        var changed = false;
        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible || layer.Locked)
                continue;

            var next = new List<Stroke>(layer.Strokes.Count);
            var layerChanged = false;
            foreach (var stroke in layer.Strokes)
            {
                if (EraserMath.TryErase(stroke, world, radius, EraserWholeStroke, out var pieces))
                    layerChanged = true;
                next.AddRange(pieces);
            }

            if (!layerChanged)
                continue;

            layer.Strokes = next;
            changed = true;
        }

        return changed;
    }

    private void CommitErase()
    {
        if (_eraseBefore == null)
            return;

        var before = _eraseBefore;
        _eraseBefore = null;
        var edits = new List<IEdit>();
        foreach (var (layerId, strokes) in before)
        {
            var layer = Document.FindLayer(layerId);
            if (layer != null && !SameStrokeList(strokes, layer.Strokes))
                edits.Add(new ReplaceLayerStrokesEdit("Erase", layerId, strokes, layer.Strokes));
        }

        if (edits.Count == 0)
            return;

        History.Push(edits.Count == 1 ? edits[0] : new CompoundEdit("Erase", edits));
        MarkDirty();
    }

    private void RestoreEraseSnapshot()
    {
        if (_eraseBefore == null)
            return;

        foreach (var (layerId, strokes) in _eraseBefore)
        {
            var layer = Document.FindLayer(layerId);
            if (layer != null)
                layer.Strokes = strokes.Select(stroke => stroke.Clone()).ToList();
        }

        _eraseBefore = null;
        RebuildStrokes();
    }

    private Dictionary<string, List<Stroke>> SnapshotEditableStrokes()
    {
        var snapshot = new Dictionary<string, List<Stroke>>();
        foreach (var layer in Document.Layers)
        {
            if (layer.Visible && !layer.Locked)
                snapshot[layer.Id] = layer.Strokes.Select(stroke => stroke.Clone()).ToList();
        }

        return snapshot;
    }

    private static bool SameStrokeList(IReadOnlyList<Stroke> before, IReadOnlyList<Stroke> after)
    {
        if (before.Count != after.Count)
            return false;

        for (var i = 0; i < before.Count; i++)
        {
            if (before[i].Id != after[i].Id || before[i].Points.Count != after[i].Points.Count)
                return false;
        }

        return true;
    }

    private void ClearSelectionPreview()
    {
        _marquee = null;
        _lasso = null;
        _showLasso = false;
    }

    private void SetSelectionPreview(Bounds? marquee, List<Vec2>? lasso, bool lassoMode)
    {
        _marquee = marquee;
        _lasso = lasso == null ? null : new List<Vec2>(lasso);
        _showLasso = lassoMode;
        RedrawOverlay();
    }
}
