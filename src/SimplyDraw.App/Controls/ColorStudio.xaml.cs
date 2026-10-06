namespace SimplyDraw.Controls;

public partial class ColorStudio : UserControl
{
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;
    private bool _drag;
    private bool _syncing;
    private const double Outer = 108;
    private const double Inner = 30;

    public ColorStudio()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderWheel();
    }

    public event EventHandler<string>? ColorChosen;

    public event EventHandler<string>? ColorAdded;

    public string CurrentHex => Rgba.FromHsv(_hue, _saturation, _value).ToHex();

    public void SetColor(string hex)
    {
        var color = Rgba.Parse(hex);
        RgbToHsv(color.R, color.G, color.B, out _hue, out _saturation, out _value);
        _syncing = true;
        ValueSlider.Value = _value;
        HexBox.Text = CurrentHex;
        _syncing = false;
        RenderWheel();
    }

    private void RenderWheel()
    {
        const int size = 220;
        var pixels = new byte[size * size * 4];
        var center = (size - 1) / 2d;
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                var offset = (y * size + x) * 4;
                if (distance < Inner || distance > Outer)
                    continue;

                var hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
                var saturation = (distance - Inner) / (Outer - Inner);
                var color = Rgba.FromHsv(hue, saturation, _value);
                pixels[offset] = color.B;
                pixels[offset + 1] = color.G;
                pixels[offset + 2] = color.R;
                pixels[offset + 3] = 255;
            }
        }

        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Pbgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        bitmap.Freeze();
        WheelImage.Source = bitmap;
        MoveMarker();
    }

    private void MoveMarker()
    {
        var distance = Inner + _saturation * (Outer - Inner);
        var radians = _hue * Math.PI / 180;
        var x = 110 + Math.Cos(radians) * distance - 7;
        var y = 110 + Math.Sin(radians) * distance - 7;
        Marker.Margin = new Thickness(x, y, 0, 0);
        if (!_syncing)
            HexBox.Text = CurrentHex;
    }

    private void Pick(Point position)
    {
        var dx = position.X - 110;
        var dy = position.Y - 110;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance > Outer + 6)
            return;

        _hue = (Math.Atan2(dy, dx) * 180 / Math.PI + 360) % 360;
        _saturation = Math.Clamp((distance - Inner) / (Outer - Inner), 0, 1);
        MoveMarker();
    }

    private void WheelDown(object sender, MouseButtonEventArgs e)
    {
        _drag = true;
        WheelImage.CaptureMouse();
        Pick(e.GetPosition(WheelImage));
    }

    private void WheelMove(object sender, MouseEventArgs e)
    {
        if (_drag)
            Pick(e.GetPosition(WheelImage));
    }

    private void WheelUp(object sender, MouseButtonEventArgs e)
    {
        _drag = false;
        if (WheelImage.IsMouseCaptured)
            WheelImage.ReleaseMouseCapture();
    }

    private void ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _syncing)
            return;

        _value = ValueSlider.Value;
        RenderWheel();
    }

    private void HexLostFocus(object sender, RoutedEventArgs e) => ApplyHex();

    private void HexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            ApplyHex();
    }

    private void ApplyHex()
    {
        SetColor(HexBox.Text);
    }

    private void UseClick(object sender, RoutedEventArgs e) => ColorChosen?.Invoke(this, CurrentHex);

    private void AddClick(object sender, RoutedEventArgs e) => ColorAdded?.Invoke(this, CurrentHex);

    private static void RgbToHsv(byte red, byte green, byte blue, out double hue, out double saturation, out double value)
    {
        var r = red / 255d;
        var g = green / 255d;
        var b = blue / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        hue = 0;
        if (delta > 0.0001)
        {
            if (Math.Abs(max - r) < 0.0001)
                hue = 60 * (((g - b) / delta) % 6);
            else if (Math.Abs(max - g) < 0.0001)
                hue = 60 * (((b - r) / delta) + 2);
            else
                hue = 60 * (((r - g) / delta) + 4);
        }

        if (hue < 0)
            hue += 360;
        saturation = max <= 0 ? 0 : delta / max;
        value = max;
    }
}
