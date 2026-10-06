namespace SimplyDraw.Canvas;

public partial class InfiniteCanvas
{
    private interface ITool
    {
        string Id { get; }

        void Down(PointerSample sample);

        void Move(PointerSample sample);

        void Up(PointerSample sample);

        void Cancel();
    }

    private Dictionary<string, ITool> CreateTools() => new()
    {
        ["pen"] = new PenTool(this),
        ["eraser"] = new EraserTool(this),
        ["select"] = new SelectTool(this),
        ["hand"] = new HandTool(),
        ["line"] = new ShapeTool(this, MarkKind.Line, "line"),
        ["rect"] = new ShapeTool(this, MarkKind.Rectangle, "rect"),
        ["ellipse"] = new ShapeTool(this, MarkKind.Ellipse, "ellipse"),
        ["arrow"] = new ShapeTool(this, MarkKind.Arrow, "arrow"),
        ["text"] = new TextTool(this),
        ["pick"] = new PickTool(this)
    };

    private sealed class PenTool : ITool
    {
        private readonly InfiniteCanvas _canvas;
        private readonly StrokeStabilizer _stabilizer = new();
        private Stroke? _draft;
        private Vec2 _start;
        private bool _drawing;
        private bool _straight;

        public PenTool(InfiniteCanvas canvas) => _canvas = canvas;

        public string Id => "pen";

        public void Down(PointerSample sample)
        {
            if (!_canvas.CanDrawOn(_canvas.Document.ActiveLayer))
            {
                _drawing = false;
                return;
            }

            _drawing = true;
            _straight = sample.Shift;
            _start = sample.World;
            _stabilizer.Smoothing = _canvas.Smoothing;
            _stabilizer.MinDistance = Math.Max(0.12, 1.15 / _canvas.Camera.Zoom);
            _stabilizer.Start(ToPoint(sample));
            _draft = _canvas.CreateStroke(MarkKind.Freehand);
            Publish();
        }

        public void Move(PointerSample sample)
        {
            if (!_drawing || _draft == null)
                return;

            _straight = sample.Shift;
            if (_straight)
            {
                var end = GeometryUtil.ConstrainAngle(_start, sample.World);
                _draft.Points =
                [
                    new StrokePoint { X = _start.X, Y = _start.Y, Pressure = sample.Pressure },
                    new StrokePoint { X = end.X, Y = end.Y, Pressure = sample.Pressure }
                ];
            }
            else
            {
                _stabilizer.Add(ToPoint(sample));
                _draft.Points = _stabilizer.Points.Select(point => point.Clone()).ToList();
            }

            _canvas.SetPreview(_draft);
        }

        public void Up(PointerSample sample)
        {
            if (!_drawing || _draft == null)
                return;

            Move(sample);
            if (!_straight)
            {
                _stabilizer.Finish(ToPoint(sample));
                _draft.Points = StrokeSmoothing.Simplify(_stabilizer.Points, Math.Max(0.15, 0.55 / _canvas.Camera.Zoom));
            }

            _drawing = false;
            var committed = _draft.Clone(newId: true);
            _draft = null;
            _canvas.SetPreview(null);
            if (committed.Points.Count > 0)
                _canvas.CommitStroke(committed);
        }

        public void Cancel()
        {
            _drawing = false;
            _draft = null;
            _canvas.SetPreview(null);
        }

        private void Publish()
        {
            if (_draft == null)
                return;
            _draft.Points = _stabilizer.Points.Select(point => point.Clone()).ToList();
            _canvas.SetPreview(_draft);
        }

        private static StrokePoint ToPoint(PointerSample sample) => new()
        {
            X = sample.World.X,
            Y = sample.World.Y,
            Pressure = sample.IsPen ? Math.Clamp(sample.Pressure, 0.05, 1) : sample.Pressure
        };
    }

    private sealed class EraserTool : ITool
    {
        private readonly InfiniteCanvas _canvas;
        private bool _erasing;
        private Vec2 _last;

        public EraserTool(InfiniteCanvas canvas) => _canvas = canvas;

        public string Id => "eraser";

        public void Down(PointerSample sample)
        {
            _erasing = true;
            _last = sample.World;
            _canvas.EraseSegment(sample.World, sample.World);
        }

        public void Move(PointerSample sample)
        {
            if (!_erasing)
                return;
            _canvas.EraseSegment(_last, sample.World);
            _last = sample.World;
        }

        public void Up(PointerSample sample)
        {
            if (!_erasing)
                return;
            Move(sample);
            _erasing = false;
            _canvas.CommitErase();
        }

        public void Cancel()
        {
            _erasing = false;
            _canvas.RestoreEraseSnapshot();
        }
    }

    private sealed class SelectTool : ITool
    {
        private readonly InfiniteCanvas _canvas;
        private Drag _drag;
        private HandleKind _handle;
        private Vec2 _origin;
        private Bounds _bounds;
        private Dictionary<string, Stroke> _before = new();
        private readonly List<Vec2> _lasso = new();

        private enum Drag
        {
            None,
            Rect,
            Lasso,
            Move,
            Scale,
            Rotate
        }

        public SelectTool(InfiniteCanvas canvas) => _canvas = canvas;

        public string Id => "select";

        public void Down(PointerSample sample)
        {
            _origin = sample.World;
            _handle = _canvas.HitHandle(sample.Screen);
            if (_handle == HandleKind.Rotate && _canvas.HasSelection)
            {
                BeginTransform(Drag.Rotate);
                return;
            }

            if (_handle != HandleKind.None && _canvas.HasSelection)
            {
                BeginTransform(Drag.Scale);
                return;
            }

            var hit = _canvas.HitTop(sample.World, 6 / _canvas.Camera.Zoom);
            if (hit != null)
            {
                if (sample.Shift)
                {
                    _canvas.ToggleSelection(hit.Id);
                    if (!_canvas.IsSelected(hit.Id))
                    {
                        _drag = Drag.None;
                        return;
                    }
                }
                else if (!_canvas.IsSelected(hit.Id))
                {
                    _canvas.SelectOnly(hit.Id);
                }

                BeginTransform(Drag.Move);
                return;
            }

            if (!sample.Shift)
                _canvas.ClearSelection();

            _drag = _canvas.SelectStyle == SelectStyle.Lasso ? Drag.Lasso : Drag.Rect;
            _lasso.Clear();
            _lasso.Add(sample.World);
            _canvas.SetSelectionPreview(Bounds.FromCorners(sample.World, sample.World), _lasso, _drag == Drag.Lasso);
        }

        public void Move(PointerSample sample)
        {
            switch (_drag)
            {
                case Drag.Rect:
                    var end = sample.Shift ? Square(_origin, sample.World) : sample.World;
                    _canvas.SetSelectionPreview(Bounds.FromCorners(_origin, end), null, false);
                    break;
                case Drag.Lasso:
                    if (_lasso.Count == 0 || _lasso[^1].DistanceTo(sample.World) > 1.5 / _canvas.Camera.Zoom)
                        _lasso.Add(sample.World);
                    _canvas.SetSelectionPreview(null, _lasso, true);
                    break;
                case Drag.Move:
                    var dx = sample.World.X - _origin.X;
                    var dy = sample.World.Y - _origin.Y;
                    if (sample.Shift)
                    {
                        if (Math.Abs(dx) >= Math.Abs(dy))
                            dy = 0;
                        else
                            dx = 0;
                    }

                    _canvas.PreviewTranslate(_before, dx, dy);
                    break;
                case Drag.Scale:
                    PreviewScale(sample.World);
                    break;
                case Drag.Rotate:
                    var center = _bounds.Center;
                    var start = Math.Atan2(_origin.Y - center.Y, _origin.X - center.X);
                    var current = Math.Atan2(sample.World.Y - center.Y, sample.World.X - center.X);
                    _canvas.PreviewRotate(_before, center, current - start);
                    break;
            }
        }

        public void Up(PointerSample sample)
        {
            Move(sample);
            switch (_drag)
            {
                case Drag.Rect when _canvas._marquee is { } rect && _origin.DistanceTo(sample.World) >= 3 / _canvas.Camera.Zoom:
                    _canvas.SelectInBounds(rect, sample.Shift);
                    break;
                case Drag.Lasso when _lasso.Count >= 3:
                    _canvas.SelectInPolygon(_lasso, sample.Shift);
                    break;
                case Drag.Move:
                case Drag.Scale:
                case Drag.Rotate:
                    _canvas.CommitTransform(_before, _drag switch
                    {
                        Drag.Rotate => "Rotate",
                        Drag.Scale => "Scale",
                        _ => "Move"
                    });
                    break;
            }

            Finish();
        }

        public void Cancel()
        {
            if (_before.Count > 0 && _drag is Drag.Move or Drag.Scale or Drag.Rotate)
                _canvas.RestoreGeometry(_before);
            Finish();
        }

        private void BeginTransform(Drag drag)
        {
            _drag = drag;
            _before = _canvas.CaptureSelection();
            _bounds = _canvas.SelectionBounds();
        }

        private void PreviewScale(Vec2 world)
        {
            if (_bounds.IsEmpty)
                return;

            double sx = 1, sy = 1;
            switch (_handle)
            {
                case HandleKind.E:
                    sx = (world.X - _bounds.MinX) / Math.Max(1, _bounds.Width);
                    break;
                case HandleKind.W:
                    sx = (_bounds.MaxX - world.X) / Math.Max(1, _bounds.Width);
                    break;
                case HandleKind.S:
                    sy = (world.Y - _bounds.MinY) / Math.Max(1, _bounds.Height);
                    break;
                case HandleKind.N:
                    sy = (_bounds.MaxY - world.Y) / Math.Max(1, _bounds.Height);
                    break;
                default:
                    var start = Math.Max(1, _bounds.Center.DistanceTo(_origin));
                    sx = sy = _bounds.Center.DistanceTo(world) / start;
                    break;
            }

            _canvas.PreviewScale(_before, _bounds.Center, sx, sy);
        }

        private void Finish()
        {
            _drag = Drag.None;
            _before = new Dictionary<string, Stroke>();
            _lasso.Clear();
            _canvas.ClearSelectionPreview();
            _canvas.RedrawOverlay();
        }

        private static Vec2 Square(Vec2 start, Vec2 end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var signX = dx < 0 ? -1 : 1;
            var signY = dy < 0 ? -1 : 1;
            return new Vec2(start.X + side * signX, start.Y + side * signY);
        }
    }

    private sealed class ShapeTool : ITool
    {
        private readonly InfiniteCanvas _canvas;
        private readonly MarkKind _kind;
        private Stroke? _draft;
        private Vec2 _start;
        private bool _drawing;

        public ShapeTool(InfiniteCanvas canvas, MarkKind kind, string id)
        {
            _canvas = canvas;
            _kind = kind;
            Id = id;
        }

        public string Id { get; }

        public void Down(PointerSample sample)
        {
            if (!_canvas.CanDrawOn(_canvas.Document.ActiveLayer))
            {
                _drawing = false;
                return;
            }

            _drawing = true;
            _start = _canvas.MaybeSnap(sample.World);
            _draft = _canvas.CreateStroke(_kind);
            _draft.Points.Add(new StrokePoint { X = _start.X, Y = _start.Y, Pressure = 1 });
            _draft.Points.Add(new StrokePoint { X = _start.X, Y = _start.Y, Pressure = 1 });
            _canvas.SetPreview(_draft);
        }

        public void Move(PointerSample sample)
        {
            if (!_drawing || _draft == null)
                return;

            var end = sample.World;
            if (_kind is MarkKind.Rectangle or MarkKind.Ellipse && sample.Shift)
                end = Square(_start, end);
            else if (sample.Shift)
                end = GeometryUtil.ConstrainAngle(_start, end);
            else
                end = _canvas.MaybeSnap(end);

            _draft.Points[1].X = end.X;
            _draft.Points[1].Y = end.Y;
            _canvas.SetPreview(_draft);
        }

        public void Up(PointerSample sample)
        {
            if (!_drawing || _draft == null)
                return;

            Move(sample);
            _drawing = false;
            var committed = _draft.Clone(newId: true);
            _draft = null;
            _canvas.SetPreview(null);
            if (committed.Points[0].ToVec().DistanceTo(committed.Points[1].ToVec()) < 0.4 / _canvas.Camera.Zoom)
                return;

            _canvas.CommitStroke(committed);
        }

        public void Cancel()
        {
            _drawing = false;
            _draft = null;
            _canvas.SetPreview(null);
        }

        private static Vec2 Square(Vec2 start, Vec2 end)
        {
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new Vec2(start.X + side * (dx < 0 ? -1 : 1), start.Y + side * (dy < 0 ? -1 : 1));
        }
    }

    private sealed class TextTool : ITool
    {
        private readonly InfiniteCanvas _canvas;

        public TextTool(InfiniteCanvas canvas) => _canvas = canvas;

        public string Id => "text";

        public void Down(PointerSample sample) => _canvas.BeginText(sample);

        public void Move(PointerSample sample)
        {
        }

        public void Up(PointerSample sample)
        {
        }

        public void Cancel()
        {
        }
    }

    private sealed class PickTool : ITool
    {
        private readonly InfiniteCanvas _canvas;

        public PickTool(InfiniteCanvas canvas) => _canvas = canvas;

        public string Id => "pick";

        public void Down(PointerSample sample)
        {
            var hit = _canvas.HitTop(sample.World, 8 / Math.Max(0.02, _canvas.Camera.Zoom));
            if (hit == null)
            {
                _canvas.Status("No mark under the pointer.");
                return;
            }

            _canvas.SetColor(hit.Color);
            _canvas.Status("Color picked.");
        }

        public void Move(PointerSample sample)
        {
        }

        public void Up(PointerSample sample)
        {
        }

        public void Cancel()
        {
        }
    }

    private sealed class HandTool : ITool
    {
        public string Id => "hand";

        public void Down(PointerSample sample)
        {
        }

        public void Move(PointerSample sample)
        {
        }

        public void Up(PointerSample sample)
        {
        }

        public void Cancel()
        {
        }
    }
}
