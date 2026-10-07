namespace SimplyDraw.Canvas;

public enum TouchMode
{
    Pan,
    Select
}

public enum SelectStyle
{
    Rectangle,
    Lasso
}

public readonly record struct TextPrompt(Point ScreenPoint, double FontSize);

internal readonly record struct PointerSample(Vec2 Screen, Vec2 World, double Pressure, bool IsPen, bool Shift, bool Alt);

public partial class InfiniteCanvas : FrameworkElement
{
    private enum Gesture
    {
        None,
        Tool,
        Pan,
        Pinch
    }

    private enum HandleKind
    {
        None,
        N,
        S,
        E,
        W,
        NE,
        NW,
        SE,
        SW,
        Rotate
    }

    private readonly VisualCollection _children;
    private readonly DrawingVisual _grid = new();
    private readonly ContainerVisual _world = new();
    private readonly DrawingVisual _preview = new();
    private readonly DrawingVisual _overlay = new();
    private readonly Dictionary<string, ITool> _tools;
    private readonly HashSet<string> _selection = new();
    private readonly List<Stroke> _clipboard = new();

    private ITool _tool;
    private Gesture _gesture;
    private bool _fromMouse;
    private bool _penDown;
    private bool _viewReady;
    private bool _pinchValid;
    private bool _ignoreLeftoverTouch;
    private int _activeStylusId = -1;
    private int _pasteCount;
    private double _dpi = 1;
    private double _panStartX;
    private double _panStartY;
    private double _pinchDistance = 1;
    private Vec2 _panAnchor;
    private Vec2 _pinchMid;
    private Vec2 _hover = new(-1000, -1000);
    private bool _hoverInside;
    private Bounds? _marquee;
    private List<Vec2>? _lasso;
    private bool _showLasso;
    private Dictionary<string, List<Stroke>>? _eraseBefore;
    private Vec2 _textWorld;

    public InfiniteCanvas()
    {
        _children = new VisualCollection(this);
        _children.Add(_grid);
        _children.Add(_world);
        _children.Add(_overlay);
        _tools = CreateTools();
        _tool = _tools["pen"];
        ToolId = "pen";
        Document = DrawingDocument.CreateNew();
        History = new History();

        Focusable = true;
        ClipToBounds = true;
        Stylus.SetIsPressAndHoldEnabled(this, false);
        Stylus.SetIsFlicksEnabled(this, false);
        Stylus.SetIsTapFeedbackEnabled(this, false);
        Loaded += OnLoaded;
    }

    public DrawingDocument Document { get; private set; }

    public Camera Camera { get; } = new();

    public History History { get; }

    public string ToolId { get; private set; }

    public string BrushId { get; private set; } = "ink";

    public string ColorHex { get; private set; } = "#1C1917";

    public double BrushSize { get; set; } = 5;

    public double BrushOpacity { get; set; } = 1;

    public double Smoothing { get; set; } = 0.48;

    public double EraserRadius { get; set; } = 22;

    public bool EraserWholeStroke { get; set; }

    public TouchMode TouchMode { get; set; } = TouchMode.Pan;

    public SelectStyle SelectStyle { get; set; } = SelectStyle.Rectangle;

    public bool SpaceHeld { get; set; }

    public bool IsDirty { get; private set; }

    public bool HasSelection => _selection.Count > 0;

    public bool HasCopiedMarks => _clipboard.Count > 0;

    public event EventHandler? Changed;

    public event EventHandler? StructureChanged;

    public event EventHandler? ViewChanged;

    public event EventHandler? SelectionChanged;

    public event EventHandler? ToolChanged;

    public event EventHandler? BrushChanged;

    public event EventHandler<string>? StatusChanged;

    public event EventHandler<string>? ColorPicked;

    public event EventHandler<TextPrompt>? TextRequested;

    public void SetTool(string id)
    {
        if (!_tools.ContainsKey(id) || ToolId == id)
        {
            RefreshCursor();
            return;
        }

        CancelInteraction();
        ToolId = id;
        _tool = _tools[id];
        RefreshCursor();
        RedrawOverlay();
        ToolChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetBrush(string id, bool applyDefaults)
    {
        var preset = BrushLibrary.Get(id);
        BrushId = preset.Id;
        if (applyDefaults)
        {
            BrushSize = preset.DefaultSize;
            BrushOpacity = preset.DefaultOpacity;
            Smoothing = preset.Smoothing;
        }

        BrushChanged?.Invoke(this, EventArgs.Empty);
        RedrawOverlay();
    }

    public void SetSpaceHeld(bool held)
    {
        SpaceHeld = held;
        if (_gesture == Gesture.None)
            RefreshCursor();
    }

    public void RefreshChrome() => RedrawOverlay();

    public void Redraw()
    {
        RebuildStrokes();
        RedrawGrid();
        RedrawOverlay();
    }

    public void SetColor(string hex)
    {
        ColorHex = Rgba.Parse(hex).ToHex();
        ColorPicked?.Invoke(this, ColorHex);
        RedrawOverlay();
    }

    public void CancelInteraction()
    {
        if (_gesture == Gesture.Tool)
            ActiveTool.Cancel();
        _overrideTool = null;
        RestoreEraseSnapshot();
        _gesture = Gesture.None;
        _fromMouse = false;
        _penDown = false;
        _pinchValid = false;
        _activeStylusId = -1;
        ClearSelectionPreview();
        SetPreview(null);
        if (IsMouseCaptured)
            Mouse.Capture(null);
        RefreshCursor();
        RedrawOverlay();
    }

    public void Undo()
    {
        CancelInteraction();
        if (!History.CanUndo)
            return;
        History.Undo(Document);
        FinishHistory("Undone.");
    }

    public void Redo()
    {
        CancelInteraction();
        if (!History.CanRedo)
            return;
        History.Redo(Document);
        FinishHistory("Redone.");
    }

    public void LoadDocument(DrawingDocument document)
    {
        CancelInteraction();
        Document = document;
        Document.EnsureLayer();
        History.Clear();
        _selection.Clear();
        _clipboard.Clear();
        StrokePainter.ClearImageCache();
        _viewReady = false;
        ApplyView();
        RebuildStrokes();
        ApplyCamera();
        IsDirty = false;
        RaiseStructure();
        Status("Opened drawing.");
    }

    public void CaptureView()
    {
        if (ActualWidth < 2 || ActualHeight < 2)
            return;

        var center = Camera.ScreenToWorld(new Vec2(ActualWidth / 2, ActualHeight / 2));
        Document.ViewZoom = Camera.Zoom;
        Document.ViewCenterX = center.X;
        Document.ViewCenterY = center.Y;
    }

    public void MarkClean()
    {
        IsDirty = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void FitView()
    {
        Camera.Fit(Document.ContentBounds(), ActualWidth, ActualHeight);
        ApplyCamera();
    }

    public void ZoomActual()
    {
        var world = Camera.ScreenToWorld(new Vec2(ActualWidth / 2, ActualHeight / 2));
        Camera.Zoom = 1;
        Camera.CenterOn(world, ActualWidth, ActualHeight);
        ApplyCamera();
    }

    public void ZoomBy(double factor) 
    {
        Camera.ZoomAt(new Vec2(Math.Max(1, ActualWidth) / 2, Math.Max(1, ActualHeight) / 2), factor);
        ApplyCamera();
    }

    public void DismissSelection() => ClearSelection();

    public void DeleteSelection()
    {
        if (_selection.Count == 0)
            return;

        var removed = new List<(string LayerId, int Index, Stroke Stroke)>();
        foreach (var layer in Document.Layers)
        {
            if (layer.Locked)
                continue;

            for (var index = 0; index < layer.Strokes.Count; index++)
            {
                if (_selection.Contains(layer.Strokes[index].Id))
                    removed.Add((layer.Id, index, layer.Strokes[index]));
            }
        }

        if (removed.Count == 0)
            return;

        foreach (var item in removed.OrderByDescending(item => item.Index))
            Document.FindLayer(item.LayerId)?.Strokes.RemoveAll(stroke => stroke.Id == item.Stroke.Id);

        _selection.Clear();
        History.Push(new RemoveStrokesEdit(removed));
        RebuildStrokes();
        MarkDirty();
        NotifySelection();
    }

    public void CopySelection()
    {
        _clipboard.Clear();
        _pasteCount = 0;
        foreach (var id in _selection)
        {
            var found = Document.FindStroke(id);
            if (found != null)
                _clipboard.Add(found.Value.Stroke.Clone());
        }

        if (_clipboard.Count > 0)
            Status($"Copied {_clipboard.Count} mark{(_clipboard.Count == 1 ? "" : "s")}.");
    }

    public void PasteClipboard()
    {
        if (_clipboard.Count == 0)
        {
            Status("Nothing is copied.");
            return;
        }

        var layer = Document.ActiveLayer;
        if (!CanDrawOn(layer))
            return;

        _pasteCount++;
        var offset = 18 / Camera.Zoom * _pasteCount;
        _selection.Clear();
        var created = new List<Stroke>();
        foreach (var source in _clipboard)
        {
            var clone = source.Clone(newId: true);
            StrokeTransform.Translate(clone, source, offset, offset);
            layer.Strokes.Add(clone);
            created.Add(clone);
            _selection.Add(clone.Id);
        }

        History.Push(new CompoundEdit("Paste", created.Select(stroke => new AddStrokeEdit(layer.Id, stroke, "Paste"))));
        RebuildStrokes();
        MarkDirty();
        NotifySelection();
    }

    public void DuplicateSelection()
    {
        CopySelection();
        _pasteCount = 0;
        PasteClipboard();
    }

    public void SelectAll()
    {
        _selection.Clear();
        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible || layer.Locked)
                continue;
            foreach (var stroke in layer.Strokes)
                _selection.Add(stroke.Id);
        }

        NotifySelection();
    }

    public void NudgeSelection(double dx, double dy)
    {
        var originals = CaptureSelection();
        if (originals.Count == 0)
            return;

        PreviewTranslate(originals, dx, dy);
        CommitTransform(originals, "Nudge");
    }

    public void SmoothSelection()
    {
        var originals = CaptureSelection();
        var any = false;
        foreach (var (id, original) in originals)
        {
            if (original.Kind != MarkKind.Freehand || original.Points.Count < 4)
                continue;

            var found = Document.FindStroke(id);
            if (found == null)
                continue;

            found.Value.Stroke.Points = StrokeSmoothing.Chaikin(original.Points);
            any = true;
        }

        if (!any)
            return;

        CommitTransform(originals, "Smooth");
        RebuildStrokes();
        RedrawOverlay();
    }

    public void PlaceImage(byte[] bytes, double pixelWidth, double pixelHeight)
    {
        if (bytes.Length == 0 || !CanDrawOn(Document.ActiveLayer))
            return;

        var aspect = pixelHeight <= 1 ? 1 : pixelWidth / pixelHeight;
        var worldHeight = Math.Max(40, ActualHeight * 0.42 / Math.Max(Camera.Zoom, 0.02));
        var worldWidth = worldHeight * aspect;
        var center = Camera.ScreenToWorld(new Vec2(ActualWidth / 2, ActualHeight / 2));
        var stroke = CreateStroke(MarkKind.Image);
        stroke.ImageBytes = bytes;
        stroke.BoxWidth = worldWidth;
        stroke.BoxHeight = worldHeight;
        stroke.Points.Add(new StrokePoint
        {
            X = center.X - worldWidth / 2,
            Y = center.Y - worldHeight / 2,
            Pressure = 1
        });
        CommitStroke(stroke);
        SelectOnly(stroke.Id);
        SetTool("select");
        Status("Image placed. Drag it to move, or use the handles to resize.");
    }

    public void CommitText(string text, double width, double height, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(text) || !CanDrawOn(Document.ActiveLayer))
            return;

        var stroke = CreateStroke(MarkKind.Text);
        stroke.Text = text.Trim();
        stroke.FontSize = Math.Max(8, fontSize);
        stroke.BoxWidth = Math.Max(8, width);
        stroke.BoxHeight = Math.Max(8, height);
        stroke.Points.Add(new StrokePoint { X = _textWorld.X, Y = _textWorld.Y, Pressure = 1 });
        CommitStroke(stroke);
    }

    public void SetCanvasLook(GridStyle grid, bool snap, double spacing, string paper)
    {
        spacing = Math.Clamp(spacing, 8, 160);
        if (Document.Grid == grid &&
            Document.SnapToGrid == snap &&
            Math.Abs(Document.GridSpacing - spacing) < 0.01 &&
            Document.Paper == paper)
        {
            return;
        }

        History.Push(new BoardPropsEdit
        {
            BeforeGrid = Document.Grid,
            AfterGrid = grid,
            BeforeSnap = Document.SnapToGrid,
            AfterSnap = snap,
            BeforeSpacing = Document.GridSpacing,
            AfterSpacing = spacing,
            BeforePaper = Document.Paper,
            AfterPaper = paper
        });
        Document.Grid = grid;
        Document.SnapToGrid = snap;
        Document.GridSpacing = spacing;
        Document.Paper = paper;
        RedrawGrid();
        MarkDirty();
    }

    public void ActivateLayer(int index)
    {
        if (index < 0 || index >= Document.Layers.Count || Document.ActiveLayerIndex == index)
            return;

        Document.ActiveLayerIndex = index;
        RaiseStructure();
    }

    public void AddLayer()
    {
        var layer = new Layer { Name = $"Layer {Document.Layers.Count + 1}" };
        var index = Math.Clamp(Document.ActiveLayerIndex + 1, 0, Document.Layers.Count);
        Document.Layers.Insert(index, layer);
        Document.ActiveLayerIndex = index;
        History.Push(new InsertLayerEdit(index, layer));
        MarkDirty();
        RaiseStructure();
    }

    public void DeleteActiveLayer()
    {
        if (Document.Layers.Count <= 1)
        {
            Status("At least one layer is required.");
            return;
        }

        var index = Document.ActiveLayerIndex;
        var layer = Document.Layers[index];
        Document.Layers.RemoveAt(index);
        Document.ActiveLayerIndex = Math.Clamp(index - 1, 0, Document.Layers.Count - 1);
        History.Push(new RemoveLayerEdit(index, layer));
        RebuildStrokes();
        MarkDirty();
        RaiseStructure();
    }

    public void MoveActiveLayer(int direction)
    {
        var from = Document.ActiveLayerIndex;
        var to = from + direction;
        if (to < 0 || to >= Document.Layers.Count)
            return;

        var layer = Document.Layers[from];
        Document.Layers.RemoveAt(from);
        Document.Layers.Insert(to, layer);
        Document.ActiveLayerIndex = to;
        History.Push(new MoveLayerEdit(from, to));
        RebuildStrokes();
        MarkDirty();
        RaiseStructure();
    }

    public void DuplicateActiveLayer()
    {
        var source = Document.ActiveLayer;
        var clone = source.Clone(newIds: true);
        clone.Name = source.Name + " copy";
        var index = Document.ActiveLayerIndex + 1;
        Document.Layers.Insert(index, clone);
        Document.ActiveLayerIndex = index;
        History.Push(new InsertLayerEdit(index, clone, "Duplicate layer"));
        RebuildStrokes();
        MarkDirty();
        RaiseStructure();
    }

    public void CommitLayerProps(string layerId, string name, bool visible, bool locked, double opacity, Layer previous)
    {
        var layer = Document.FindLayer(layerId);
        if (layer == null)
            return;

        name = string.IsNullOrWhiteSpace(name) ? "Layer" : name.Trim();
        opacity = Math.Clamp(opacity, 0.05, 1);
        if (layer.Name == name && layer.Visible == visible && layer.Locked == locked && Math.Abs(layer.Opacity - opacity) < 0.001 &&
            previous.Name == name && previous.Visible == visible && previous.Locked == locked && Math.Abs(previous.Opacity - opacity) < 0.001)
        {
            return;
        }

        layer.Name = name;
        layer.Visible = visible;
        layer.Locked = locked;
        layer.Opacity = opacity;
        History.Push(new LayerPropsEdit(layerId, previous.Name, name, previous.Visible, visible, previous.Locked, locked, previous.Opacity, opacity));
        RebuildStrokes();
        MarkDirty();
        RaiseStructure();
    }

    public void PreviewLayerOpacity(string layerId, double opacity)
    {
        var layer = Document.FindLayer(layerId);
        if (layer == null)
            return;

        layer.Opacity = Math.Clamp(opacity, 0.05, 1);
        RebuildStrokes();
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var bounds = new Rect(RenderSize);
        return bounds.Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null!;
    }

    protected override int VisualChildrenCount => _children.Count;

    protected override Visual GetVisualChild(int index) => _children[index];

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        if (!_viewReady)
            ApplyView();
        ApplyCamera();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (!_viewReady)
            ApplyView();
        RebuildStrokes();
        ApplyCamera();
        RefreshCursor();
    }

    private void ApplyView()
    {
        if (ActualWidth < 2 || ActualHeight < 2)
            return;

        Camera.Zoom = Document.ViewZoom <= 0 ? 1 : Math.Clamp(Document.ViewZoom, Camera.MinZoom, Camera.MaxZoom);
        Camera.CenterOn(new Vec2(Document.ViewCenterX, Document.ViewCenterY), ActualWidth, ActualHeight);
        _viewReady = true;
    }

    private void ApplyCamera()
    {
        _world.Transform = new MatrixTransform(Camera.Zoom, 0, 0, Camera.Zoom, Camera.PanX, Camera.PanY);
        RedrawGrid();
        RedrawOverlay();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RebuildStrokes()
    {
        _world.Children.Clear();
        foreach (var layer in Document.Layers)
        {
            if (!layer.Visible)
                continue;

            var host = new ContainerVisual();
            foreach (var stroke in layer.Strokes)
                host.Children.Add(CreateVisual(stroke, layer.Opacity));
            _world.Children.Add(host);
        }

        _world.Children.Add(_preview);
    }

    private DrawingVisual CreateVisual(Stroke stroke, double layerOpacity)
    {
        var visual = new DrawingVisual();
        if (stroke.Kind == MarkKind.Image)
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);

        using (var context = visual.RenderOpen())
            StrokePainter.Paint(context, stroke, layerOpacity, _dpi);
        return visual;
    }

    private void SetPreview(Stroke? stroke)
    {
        using var context = _preview.RenderOpen();
        if (stroke != null)
            StrokePainter.Paint(context, stroke, 1, _dpi);
    }

    private void RedrawGrid()
    {
        using var context = _grid.RenderOpen();
        SceneRenderer.DrawBackground(context, Document, Camera, ActualWidth, ActualHeight, true);
    }

    private void RedrawOverlay()
    {
        using var context = _overlay.RenderOpen();
        var accent = Frozen("#C4623A");
        var white = Frozen("#FFFFFF");
        var pen = new Pen(accent, 1.4);
        pen.Freeze();
        var dashed = new Pen(accent, 1.2) { DashStyle = DashStyles.Dash };
        dashed.Freeze();

        if (_showLasso && _lasso is { Count: > 1 })
        {
            var geometry = new StreamGeometry();
            using (var geometryContext = geometry.Open())
            {
                var first = Camera.WorldToScreen(_lasso[0]);
                geometryContext.BeginFigure(new Point(first.X, first.Y), false, false);
                geometryContext.PolyLineTo(_lasso.Skip(1).Select(point =>
                {
                    var screen = Camera.WorldToScreen(point);
                    return new Point(screen.X, screen.Y);
                }).ToList(), true, false);
            }

            context.DrawGeometry(null, dashed, geometry);
        }

        if (_marquee is { IsEmpty: false } marquee)
        {
            var rect = ToScreen(marquee);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(36, 196, 98, 58)), dashed, rect);
        }

        var bounds = SelectionBounds();
        if (!bounds.IsEmpty && _selection.Count > 0)
        {
            var rect = ToScreen(bounds);
            context.DrawRectangle(null, pen, rect);
            foreach (var handle in HandlePoints(rect))
            {
                context.DrawRectangle(white, pen, new Rect(handle.X - 4.5, handle.Y - 4.5, 9, 9));
            }

            var top = new Point(rect.Left + rect.Width / 2, rect.Top);
            var rotate = new Point(top.X, top.Y - 28);
            context.DrawLine(pen, top, rotate);
            context.DrawEllipse(white, pen, rotate, 6, 6);
        }

        if (_hoverInside && _gesture != Gesture.Tool)
            DrawHover(context, accent);
    }

    private void DrawHover(DrawingContext context, Brush accent)
    {
        var pen = new Pen(accent, 1.15);
        pen.Freeze();
        if (ToolId == "eraser")
        {
            context.DrawEllipse(null, pen, new Point(_hover.X, _hover.Y), EraserRadius, EraserRadius);
            return;
        }

        if (ToolId is "pen" or "line" or "rect" or "ellipse" or "arrow")
        {
            var radius = Math.Clamp(BrushSize * Camera.Zoom * 0.5, 1.5, 180);
            context.DrawEllipse(null, pen, new Point(_hover.X, _hover.Y), radius, radius);
        }
    }

    private void FinishHistory(string message)
    {
        PruneSelection();
        StrokePainter.ClearImageCache();
        RebuildStrokes();
        RedrawGrid();
        RedrawOverlay();
        MarkDirty();
        RaiseStructure();
        Status(message);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseStructure() => StructureChanged?.Invoke(this, EventArgs.Empty);

    private void Status(string message) => StatusChanged?.Invoke(this, message);

    private void RefreshCursor()
    {
        Cursor = _gesture == Gesture.Pan || (SpaceHeld && _gesture == Gesture.None) || ToolId == "hand"
            ? Cursors.Hand
            : ToolId switch
            {
                "eraser" => Cursors.None,
                "text" => Cursors.IBeam,
                "pick" => Cursors.Cross,
                "select" => Cursors.Arrow,
                "line" or "rect" or "ellipse" or "arrow" => Cursors.Cross,
                _ => Cursors.Pen
            };
    }

    private static SolidColorBrush Frozen(string hex)
    {
        var color = Rgba.Parse(hex);
        var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }
}
