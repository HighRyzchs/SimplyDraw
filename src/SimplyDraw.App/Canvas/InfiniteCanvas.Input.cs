namespace SimplyDraw.Canvas;

public partial class InfiniteCanvas
{
    private readonly Dictionary<int, Vec2> _touches = new();
    private ITool? _overrideTool;
    private PointerSample _lastSample;
    private Point _lastMousePoint;
    private DateTime _lastMouseTime = DateTime.UtcNow;

    protected override void OnStylusDown(StylusDownEventArgs e)
    {
        Focus();
        if (IsTouch(e))
        {
            e.Handled = true;
            FingerDown(e);
            return;
        }

        e.Handled = true;
        if (_touches.Count > 0)
        {
            _touches.Clear();
            CancelInteraction();
        }

        _penDown = true;
        _activeStylusId = e.StylusDevice.Id;
        e.StylusDevice.Capture(this);
        var samples = ReadStylus(e, true);
        BeginTool(samples[0], fromMouse: false);
        for (var index = 1; index < samples.Count; index++)
            MoveTool(samples[index]);
    }

    protected override void OnStylusMove(StylusEventArgs e)
    {
        if (IsTouch(e))
        {
            e.Handled = true;
            FingerMove(e);
            return;
        }

        var samples = ReadStylus(e, true);
        if (e.InAir)
        {
            if (samples.Count > 0)
                Hover(samples[^1].Screen);
            return;
        }

        e.Handled = true;
        if (_activeStylusId != -1 && e.StylusDevice.Id != _activeStylusId)
            return;

        foreach (var sample in samples)
            MoveTool(sample);
    }

    protected override void OnStylusUp(StylusEventArgs e)
    {
        if (IsTouch(e))
        {
            e.Handled = true;
            FingerUp(e);
            return;
        }

        if (_activeStylusId != -1 && e.StylusDevice.Id != _activeStylusId)
            return;

        e.Handled = true;
        var samples = ReadStylus(e, true);
        if (_gesture == Gesture.Tool)
        {
            for (var index = 0; index < samples.Count - 1; index++)
                MoveTool(samples[index]);
            EndTool(samples[^1]);
        }

        _penDown = false;
        _activeStylusId = -1;
        if (e.StylusDevice.Captured == this)
            e.StylusDevice.Capture(null);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null)
            return;

        Focus();
        var point = e.GetPosition(this);
        _lastMousePoint = point;
        _lastMouseTime = DateTime.UtcNow;
        if (e.ChangedButton is MouseButton.Middle or MouseButton.Right || (e.ChangedButton == MouseButton.Left && SpaceHeld))
        {
            StartPan(ToVec(point), fromMouse: true);
            Mouse.Capture(this);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
            return;

        BeginTool(Sample(ToVec(point), MousePressure(point), isPen: false), fromMouse: true);
        Mouse.Capture(this);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (e.StylusDevice != null)
            return;

        var point = e.GetPosition(this);
        var screen = ToVec(point);
        _hoverInside = true;
        if (_gesture == Gesture.Pan && _fromMouse)
            MovePan(screen);
        else if (_gesture == Gesture.Tool && _fromMouse)
            MoveTool(Sample(screen, MousePressure(point), isPen: false));
        else
            Hover(screen);
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null)
            return;

        var point = e.GetPosition(this);
        var sample = Sample(ToVec(point), MousePressure(point), isPen: false);
        if (!_fromMouse)
            return;

        if (_gesture == Gesture.Tool && e.ChangedButton == MouseButton.Left)
            EndTool(sample);
        else if (_gesture == Gesture.Pan && e.ChangedButton is MouseButton.Left or MouseButton.Middle or MouseButton.Right)
        {
            _gesture = Gesture.None;
            _fromMouse = false;
            RefreshCursor();
        }

        if (IsMouseCaptured)
            Mouse.Capture(null);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        Camera.ZoomAt(ToVec(e.GetPosition(this)), e.Delta > 0 ? 1.08 : 1 / 1.08);
        ApplyCamera();
        e.Handled = true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hoverInside = false;
        RedrawOverlay();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!_fromMouse || _gesture == Gesture.None)
            return;

        if (_gesture == Gesture.Tool)
            EndTool(_lastSample);
        else
        {
            _gesture = Gesture.None;
            _fromMouse = false;
            RefreshCursor();
        }
    }

    private void FingerDown(StylusDownEventArgs e)
    {
        if (_penDown || _ignoreLeftoverTouch)
            return;

        var position = ToVec(e.GetPosition(this));
        _touches[e.StylusDevice.Id] = position;
        Hover(position);
        if (_touches.Count >= 2)
        {
            PromoteToPinch();
            return;
        }

        if (_gesture != Gesture.None)
            return;

        if (TouchMode == TouchMode.Select)
            BeginTool(Sample(position, 0.5, isPen: false), fromMouse: false, toolOverride: _tools["select"]);
        else
            StartPan(position, fromMouse: false);
    }

    private void FingerMove(StylusEventArgs e)
    {
        if (_penDown || !_touches.ContainsKey(e.StylusDevice.Id))
            return;

        _touches[e.StylusDevice.Id] = ToVec(e.GetPosition(this));
        if (_touches.Count >= 2)
        {
            if (_gesture != Gesture.Pinch)
                PromoteToPinch();
            else
                UpdatePinch();
            return;
        }

        var position = _touches[e.StylusDevice.Id];
        if (_gesture == Gesture.Pan)
            MovePan(position);
        else if (_gesture == Gesture.Tool)
            MoveTool(Sample(position, 0.5, isPen: false));
        else
            Hover(position);
    }

    private void FingerUp(StylusEventArgs e)
    {
        var position = ToVec(e.GetPosition(this));
        _touches.Remove(e.StylusDevice.Id);
        if (_touches.Count >= 2)
        {
            _pinchValid = false;
            return;
        }

        if (_touches.Count == 1)
        {
            if (_gesture == Gesture.Pinch)
            {
                _gesture = Gesture.None;
                _pinchValid = false;
                _ignoreLeftoverTouch = true;
            }

            return;
        }

        _ignoreLeftoverTouch = false;
        if (_gesture == Gesture.Tool && !_fromMouse && !_penDown)
            EndTool(Sample(position, 0.5, isPen: false));
        else if (_gesture is Gesture.Pan or Gesture.Pinch)
        {
            _gesture = Gesture.None;
            _pinchValid = false;
            _fromMouse = false;
            RefreshCursor();
        }
    }

    private void PromoteToPinch()
    {
        if (_gesture == Gesture.Tool)
            ActiveTool.Cancel();
        RestoreEraseSnapshot();
        ClearSelectionPreview();
        _overrideTool = null;
        _gesture = Gesture.Pinch;
        _pinchValid = false;
        UpdatePinch();
    }

    private void BeginTool(PointerSample sample, bool fromMouse, ITool? toolOverride = null)
    {
        if (_gesture != Gesture.None)
            return;

        _lastSample = sample;
        _hover = sample.Screen;
        _hoverInside = true;
        if (toolOverride == null && (ToolId == "hand" || (SpaceHeld && fromMouse)))
        {
            StartPan(sample.Screen, fromMouse);
            return;
        }

        if (sample.Alt && ToolId == "pen" && toolOverride == null)
        {
            _overrideTool = _tools["pick"];
            _gesture = Gesture.Tool;
            _fromMouse = fromMouse;
            _overrideTool.Down(sample);
            return;
        }

        _overrideTool = toolOverride;
        _gesture = Gesture.Tool;
        _fromMouse = fromMouse;
        ActiveTool.Down(sample);
    }

    private void MoveTool(PointerSample sample)
    {
        _lastSample = sample;
        _hover = sample.Screen;
        _hoverInside = true;
        if (_gesture == Gesture.Tool)
            ActiveTool.Move(sample);
    }

    private void EndTool(PointerSample sample)
    {
        if (_gesture == Gesture.Tool)
            ActiveTool.Up(sample);

        _overrideTool = null;
        _gesture = Gesture.None;
        _fromMouse = false;
        _lastSample = sample;
        RefreshCursor();
        RedrawOverlay();
    }

    private void StartPan(Vec2 screen, bool fromMouse)
    {
        _gesture = Gesture.Pan;
        _fromMouse = fromMouse;
        _panAnchor = screen;
        _panStartX = Camera.PanX;
        _panStartY = Camera.PanY;
        Cursor = Cursors.Hand;
    }

    private void MovePan(Vec2 screen)
    {
        Camera.PanX = _panStartX + screen.X - _panAnchor.X;
        Camera.PanY = _panStartY + screen.Y - _panAnchor.Y;
        ApplyCamera();
    }

    private void UpdatePinch()
    {
        if (_touches.Count < 2)
            return;

        var points = _touches.Values.Take(2).ToArray();
        var mid = new Vec2((points[0].X + points[1].X) / 2, (points[0].Y + points[1].Y) / 2);
        var distance = Math.Max(1, points[0].DistanceTo(points[1]));
        if (!_pinchValid)
        {
            _pinchMid = mid;
            _pinchDistance = distance;
            _pinchValid = true;
            return;
        }

        Camera.PanBy(mid.X - _pinchMid.X, mid.Y - _pinchMid.Y);
        Camera.ZoomAt(mid, distance / _pinchDistance);
        _pinchMid = mid;
        _pinchDistance = distance;
        ApplyCamera();
    }

    private ITool ActiveTool => _overrideTool ?? _tool;

    private void Hover(Vec2 screen)
    {
        _hover = screen;
        _hoverInside = true;
        RedrawOverlay();
    }

    private PointerSample Sample(Vec2 screen, double pressure, bool isPen)
    {
        var modifiers = Keyboard.Modifiers;
        return new PointerSample(
            screen,
            Camera.ScreenToWorld(screen),
            Math.Clamp(isPen ? Math.Max(0.05, pressure) : pressure, 0, 1),
            isPen,
            modifiers.HasFlag(ModifierKeys.Shift),
            modifiers.HasFlag(ModifierKeys.Alt));
    }

    private List<PointerSample> ReadStylus(StylusEventArgs args, bool isPen)
    {
        var samples = new List<PointerSample>();
        try
        {
            foreach (StylusPoint point in args.GetStylusPoints(this))
            {
                var pressure = 0.55;
                if (point.Description.HasProperty(StylusPointProperties.NormalPressure))
                    pressure = point.PressureFactor;
                samples.Add(Sample(new Vec2(point.X, point.Y), pressure, isPen));
            }
        }
        catch (Exception)
        {
            samples.Clear();
        }

        if (samples.Count == 0)
            samples.Add(Sample(ToVec(args.GetPosition(this)), 0.55, isPen));
        return samples;
    }

    private double MousePressure(Point point)
    {
        var now = DateTime.UtcNow;
        var seconds = (now - _lastMouseTime).TotalSeconds;
        var distance = new Vec2(point.X - _lastMousePoint.X, point.Y - _lastMousePoint.Y).Length;
        _lastMousePoint = point;
        _lastMouseTime = now;
        if (seconds <= 0 || seconds > 0.3)
            return 0.65;

        return Math.Clamp(1.05 - distance / seconds / 1600, 0.18, 1);
    }

    private static bool IsTouch(StylusEventArgs args)
    {
        try
        {
            return args.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Vec2 ToVec(Point point) => new(point.X, point.Y);
}
