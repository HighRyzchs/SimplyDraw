using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using SimplyDraw.Services;
using AppCanvas = SimplyDraw.Canvas;

namespace SimplyDraw;

public partial class MainWindow : Window
{
    private readonly UserSettings _settings;
    private readonly DispatcherTimer _autosave;
    private readonly bool _preview;
    private readonly Dictionary<string, ToggleButton> _toolButtons = new();
    private bool _syncing;
    private bool _rebuildingLayers;
    private bool _opacityDragging;
    private double _opacityStart = 1;
    private string? _path;
    private string _note = "";
    private DateTime _noteUntil;
    private double _textFont = 32;

    public MainWindow(bool preview = false)
    {
        _preview = preview;
        InitializeComponent();
        _settings = UserSettings.Load();
        Width = Math.Clamp(_settings.WindowWidth, MinWidth, 2400);
        Height = Math.Clamp(_settings.WindowHeight, MinHeight, 1600);
        PaintIcons();
        BuildTools();
        BuildBrushes();
        FillLookLists();
        Wire();
        ApplySettings();
        if (preview)
        {
            HintCard.Visibility = Visibility.Collapsed;
            Surface.LoadDocument(DemoSketch.Create());
        }
        else
        {
            HintCard.Visibility = _settings.ShowHint ? Visibility.Visible : Visibility.Collapsed;
            OfferAutosave();
            OpenStartupFile();
        }

        _autosave = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        _autosave.Tick += (_, _) => Autosave();
        _autosave.Start();
        Closing += OnClosing;
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
        Drop += OnDrop;
        DragOver += OnDragOver;
        RebuildLayers();
        RefreshAll();
    }

    public void SavePreview(string path)
    {
        UpdateLayout();
        var width = Math.Max(1, (int)ActualWidth);
        var height = Math.Max(1, (int)ActualHeight);
        var dpi = VisualTreeHelper.GetDpi(this);
        width = Math.Max(1, (int)Math.Round(ActualWidth * dpi.DpiScaleX));
        height = Math.Max(1, (int)Math.Round(ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(this);
        AppCanvas.SceneRenderer.SavePng(bitmap, path);
    }

    private void Wire()
    {
        Surface.Changed += (_, _) => RefreshAll();
        Surface.StructureChanged += (_, _) => RebuildLayers();
        Surface.ViewChanged += (_, _) => UpdateZoom();
        Surface.ToolChanged += (_, _) => SyncTools();
        Surface.BrushChanged += (_, _) => SyncBrushControls();
        Surface.SelectionChanged += (_, _) => UpdateStatus();
        Surface.StatusChanged += (_, message) =>
        {
            _note = message;
            _noteUntil = DateTime.UtcNow.AddSeconds(4);
            UpdateStatus();
        };
        Surface.ColorPicked += (_, hex) =>
        {
            RememberColor(hex);
            UpdateOrb();
        };
        Surface.TextRequested += (_, prompt) => OpenText(prompt);
        Studio.ColorChosen += (_, hex) =>
        {
            ChooseColor(hex);
            StudioPopup.IsOpen = false;
        };
        Studio.ColorAdded += (_, hex) => AddPaletteColor(hex);
        SourceInitialized += (_, _) =>
        {
            if (PresentationSource.FromVisual(this) is HwndSource source)
                source.AddHook(TabletHook);
        };
    }

    private void ApplySettings()
    {
        _syncing = true;
        Surface.TouchMode = _settings.TouchMode == "select" ? AppCanvas.TouchMode.Select : AppCanvas.TouchMode.Pan;
        Surface.EraserRadius = _settings.EraserRadius;
        Surface.EraserWholeStroke = _settings.EraserWholeStroke;
        Surface.SelectStyle = _settings.SelectStyle == "lasso" ? AppCanvas.SelectStyle.Lasso : AppCanvas.SelectStyle.Rectangle;
        Surface.SetBrush(string.IsNullOrWhiteSpace(_settings.BrushId) ? "ink" : _settings.BrushId, applyDefaults: false);
        Surface.BrushSize = _settings.BrushSize;
        Surface.BrushOpacity = _settings.BrushOpacity;
        Surface.Smoothing = _settings.Smoothing;
        Surface.SetColor(string.IsNullOrWhiteSpace(_settings.Color) ? "#1C1917" : _settings.Color);
        Surface.Document.Paper = _settings.Paper;
        Surface.Document.Grid = _settings.Grid;
        Surface.Document.SnapToGrid = _settings.Snap;
        Surface.Document.GridSpacing = _settings.GridSpacing <= 0 ? 32 : _settings.GridSpacing;
        _syncing = false;
        Surface.Redraw();
        SyncLook();
        SyncTools();
        SyncBrushControls();
        UpdateOrb();
    }

    private void PaintIcons()
    {
        Paint(NewButton, "new");
        Paint(OpenButton, "open");
        Paint(SaveButton, "save");
        Paint(UndoButton, "undo");
        Paint(RedoButton, "redo");
        ZoomOutButton.Content = "−";
        ZoomOutButton.FontSize = 18;
        ZoomInButton.Content = "+";
        ZoomInButton.FontSize = 18;
        Paint(FitButton, "fit");
        Paint(CloseLayersButton, "close");
        Paint(AddLayerButton, "plus");
        Paint(DuplicateLayerButton, "copy");
        Paint(UpLayerButton, "up");
        Paint(DownLayerButton, "down");
        Paint(DeleteLayerButton, "trash");
        Paint(DuplicateButton, "copy");
        Paint(DeleteSelectionButton, "trash");
    }

    private static void Paint(Button button, string key)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = IconSet.Get(key),
            StrokeThickness = 1.7,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform
        };
        path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
        });
        button.Content = path;
    }

    private void BuildTools()
    {
        (string Id, string Icon, string Tip, string Key)[] tools =
        [
            ("pen", "pen", "Pen", "B"),
            ("eraser", "eraser", "Eraser", "E"),
            ("select", "select", "Select", "S"),
            ("hand", "hand", "Move page", "H"),
            ("line", "line", "Line", "L"),
            ("rect", "rect", "Rectangle", "R"),
            ("ellipse", "ellipse", "Ellipse", "O"),
            ("arrow", "arrow", "Arrow", "A"),
            ("text", "text", "Text", "T"),
            ("pick", "pick", "Pick color", "I")
        ];

        foreach (var tool in tools)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = IconSet.Get(tool.Icon),
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform
            };
            path.SetBinding(System.Windows.Shapes.Shape.StrokeProperty, new Binding(nameof(Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1)
            });
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ToolToggle"),
                Content = path,
                ToolTip = $"{tool.Tip} ({tool.Key})",
                Focusable = false
            };
            var id = tool.Id;
            button.Click += (_, _) =>
            {
                Surface.SetTool(id);
                SyncTools();
            };
            _toolButtons[id] = button;
            ToolHost.Children.Add(button);
        }
    }

    private void BuildBrushes()
    {
        foreach (var brush in BrushLibrary.All)
        {
            var id = brush.Id;
            var button = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                ToolTip = brush.Blurb
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = brush.Name, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("InkBrush") });
            stack.Children.Add(new TextBlock { Text = brush.Blurb, FontSize = 12, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap });
            button.Content = stack;
            button.Click += (_, _) =>
            {
                Surface.SetBrush(id, applyDefaults: true);
                BrushPopup.IsOpen = false;
                if (Surface.ToolId != "pen")
                    Surface.SetTool("pen");
                SyncTools();
                SyncBrushControls();
            };
            BrushList.Children.Add(button);
        }
    }

    private void FillLookLists()
    {
        _syncing = true;
        foreach (var theme in PaperThemes.All)
            PaperBox.Items.Add(new ComboBoxItem { Content = theme.Name, Tag = theme.Id });
        foreach (var grid in Enum.GetValues<GridStyle>())
            GridBox.Items.Add(new ComboBoxItem { Content = grid.ToString(), Tag = grid });
        _syncing = false;
    }

    private void RefreshAll()
    {
        UpdateTitle();
        UpdateZoom();
        UpdateUndo();
        UpdateStatus();
        ApplyPaperChrome();
        if (!_rebuildingLayers)
            SyncLayerOpacity();
    }

    private void UpdateTitle()
    {
        var name = string.IsNullOrWhiteSpace(_path) ? Surface.Document.Name : Path.GetFileNameWithoutExtension(_path);
        Title = Surface.IsDirty ? $"SimplyDraw — {name}*" : $"SimplyDraw — {name}";
    }

    private void UpdateZoom() => ZoomLabel.Text = $"{Math.Round(Surface.Camera.Zoom * 100)}%";

    private void UpdateUndo()
    {
        UndoButton.IsEnabled = Surface.History.CanUndo;
        RedoButton.IsEnabled = Surface.History.CanRedo;
        UndoButton.ToolTip = Surface.History.CanUndo ? $"Undo {Surface.History.UndoName} (Ctrl+Z)" : "Undo";
        RedoButton.ToolTip = Surface.History.CanRedo ? $"Redo {Surface.History.RedoName} (Ctrl+Y)" : "Redo";
    }

    private void UpdateStatus()
    {
        var brush = BrushLibrary.Get(Surface.BrushId);
        var text = $"{brush.Name}  ·  {Surface.BrushSize:0.#} px  ·  {Math.Round(Surface.Camera.Zoom * 100)}%  ·  {Surface.Document.ActiveLayer.Name}";
        if (!string.IsNullOrWhiteSpace(_note) && DateTime.UtcNow < _noteUntil)
            text = _note + "   " + text;
        StatusText.Text = text;
    }

    private void ApplyPaperChrome()
    {
        var theme = PaperThemes.Get(Surface.Document.Paper);
        var color = Rgba.Parse(theme.Background);
        Background = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
        StatusText.Foreground = theme.Dark ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x8A, 0x81, 0x75));
    }

    private void SyncTools()
    {
        foreach (var (id, button) in _toolButtons)
            button.IsChecked = id == Surface.ToolId;

        TouchMoveButton.IsChecked = Surface.TouchMode == AppCanvas.TouchMode.Pan;
        TouchSelectButton.IsChecked = Surface.TouchMode == AppCanvas.TouchMode.Select;
        RectSelectButton.IsChecked = Surface.SelectStyle == AppCanvas.SelectStyle.Rectangle;
        LassoSelectButton.IsChecked = Surface.SelectStyle == AppCanvas.SelectStyle.Lasso;
        WholeStrokeToggle.IsChecked = Surface.EraserWholeStroke;
        var drawing = Surface.ToolId is "pen" or "line" or "rect" or "ellipse" or "arrow" or "text";
        DrawOptions.Visibility = drawing ? Visibility.Visible : Visibility.Collapsed;
        EraseOptions.Visibility = Surface.ToolId == "eraser" ? Visibility.Visible : Visibility.Collapsed;
        SelectOptions.Visibility = Surface.ToolId == "select" ? Visibility.Visible : Visibility.Collapsed;
        SyncBrushControls();
        UpdateStatus();
    }

    private void SyncBrushControls()
    {
        var preset = BrushLibrary.Get(Surface.BrushId);
        BrushButton.Content = preset.Name;
        _syncing = true;
        if (Surface.ToolId == "text")
        {
            SizeSlider.Minimum = 10;
            SizeSlider.Maximum = 180;
            SizeSlider.Value = Math.Clamp(Surface.BrushSize < 10 ? 32 : Surface.BrushSize, 10, 180);
            Surface.BrushSize = SizeSlider.Value;
        }
        else
        {
            SizeSlider.Minimum = preset.MinSize;
            SizeSlider.Maximum = preset.MaxSize;
            SizeSlider.Value = Math.Clamp(Surface.BrushSize, preset.MinSize, preset.MaxSize);
            Surface.BrushSize = SizeSlider.Value;
        }

        OpacitySlider.Value = Math.Clamp(Surface.BrushOpacity, 0.05, 1);
        SmoothSlider.Value = Math.Clamp(Surface.Smoothing, 0, 0.92);
        EraserSlider.Value = Math.Clamp(Surface.EraserRadius, 4, 120);
        _syncing = false;
        SizeLabel.Text = Surface.ToolId == "text" ? $"{Surface.BrushSize:0} pt" : $"{Surface.BrushSize:0.#}";
        OpacityLabel.Text = $"{Math.Round(Surface.BrushOpacity * 100)}%";
        SmoothLabel.Text = $"{Math.Round(Surface.Smoothing * 100)}";
    }

    private void SyncLook()
    {
        _syncing = true;
        SelectTag(PaperBox, Surface.Document.Paper);
        SelectTag(GridBox, Surface.Document.Grid);
        SpacingSlider.Value = Math.Clamp(Surface.Document.GridSpacing, SpacingSlider.Minimum, SpacingSlider.Maximum);
        SnapBox.IsChecked = Surface.Document.SnapToGrid;
        _syncing = false;
    }

    private static void SelectTag(ComboBox box, object tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if (Equals(item.Tag, tag))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private void RebuildLayers()
    {
        _rebuildingLayers = true;
        LayerRows.Children.Clear();
        for (var index = Surface.Document.Layers.Count - 1; index >= 0; index--)
        {
            var layerIndex = index;
            var layer = Surface.Document.Layers[index];
            var active = layerIndex == Surface.Document.ActiveLayerIndex;
            var row = new Border
            {
                Background = active ? (Brush)FindResource("AccentSoftBrush") : Brushes.Transparent,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 2, 0, 2),
                Cursor = Cursors.Hand
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var eye = SmallIcon(layer.Visible ? "eye" : "close", () => ToggleLayer(layer.Id, visible: !layer.Visible));
            var locker = SmallIcon("lock", () => ToggleLayer(layer.Id, locked: !layer.Locked));
            locker.Opacity = layer.Locked ? 1 : 0.35;
            var name = new TextBox
            {
                Text = layer.Name,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = (Brush)FindResource("InkBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            var capturedId = layer.Id;
            var capturedName = layer.Name;
            var capturedVisible = layer.Visible;
            var capturedLocked = layer.Locked;
            var capturedOpacity = layer.Opacity;
            name.GotFocus += (_, _) => Surface.ActivateLayer(layerIndex);
            name.LostFocus += (_, _) =>
            {
                if (_rebuildingLayers)
                    return;
                var current = Surface.Document.FindLayer(capturedId);
                if (current == null || current.Name == name.Text)
                    return;
                Surface.CommitLayerProps(
                    capturedId,
                    name.Text,
                    current.Visible,
                    current.Locked,
                    current.Opacity,
                    new Layer { Name = capturedName, Visible = capturedVisible, Locked = capturedLocked, Opacity = capturedOpacity });
            };
            name.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                    Surface.Focus();
            };
            row.MouseLeftButtonDown += (_, _) => Surface.ActivateLayer(layerIndex);
            Grid.SetColumn(eye, 0);
            Grid.SetColumn(locker, 1);
            Grid.SetColumn(name, 2);
            grid.Children.Add(eye);
            grid.Children.Add(locker);
            grid.Children.Add(name);
            row.Child = grid;
            LayerRows.Children.Add(row);
        }

        _rebuildingLayers = false;
        SyncLayerOpacity();
    }

    private Button SmallIcon(string key, Action action)
    {
        var button = new Button { Style = (Style)FindResource("IconButton"), Width = 28, Height = 28, Focusable = false };
        Paint(button, key);
        button.Click += (_, e) =>
        {
            e.Handled = true;
            action();
        };
        return button;
    }

    private void ToggleLayer(string id, bool? visible = null, bool? locked = null)
    {
        var layer = Surface.Document.FindLayer(id);
        if (layer == null)
            return;

        var before = new Layer { Name = layer.Name, Visible = layer.Visible, Locked = layer.Locked, Opacity = layer.Opacity };
        Surface.CommitLayerProps(id, layer.Name, visible ?? layer.Visible, locked ?? layer.Locked, layer.Opacity, before);
    }

    private void SyncLayerOpacity()
    {
        if (_opacityDragging)
            return;
        _syncing = true;
        LayerOpacity.Value = Math.Clamp(Surface.Document.ActiveLayer.Opacity, 0.05, 1);
        _syncing = false;
    }

    private void ChooseColor(string hex)
    {
        Surface.SetColor(hex);
        RememberColor(hex);
        ColorPopup.IsOpen = false;
        UpdateOrb();
    }

    private void RememberColor(string hex)
    {
        _settings.Color = hex;
        _settings.RecentColors.RemoveAll(item => string.Equals(item, hex, StringComparison.OrdinalIgnoreCase));
        _settings.RecentColors.Insert(0, hex);
        if (_settings.RecentColors.Count > 8)
            _settings.RecentColors.RemoveRange(8, _settings.RecentColors.Count - 8);
    }

    private void UpdateOrb()
    {
        var color = Rgba.Parse(Surface.ColorHex);
        ColorOrb.Fill = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
    }

    private void ShowColorFan()
    {
        ColorFan.Children.Clear();
        var colors = _settings.Palette.Count == 0 ? PaletteColors.Default() : _settings.Palette;
        const double width = 440;
        const double height = 250;
        var centerX = width / 2;
        var centerY = height - 18;
        const double radius = 168;
        for (var index = 0; index < colors.Count; index++)
        {
            var t = colors.Count == 1 ? 0.5 : index / (double)(colors.Count - 1);
            var angle = Math.PI * (1.08 + 0.84 * t);
            var hex = colors[index];
            var swatch = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(MediaColor(hex)),
                BorderBrush = Brushes.White,
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                ToolTip = hex + "  ·  right-click to remove"
            };
            swatch.MouseLeftButtonUp += (_, _) => ChooseColor(hex);
            swatch.MouseRightButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (_settings.Palette.Count <= 2)
                    return;
                _settings.Palette.RemoveAll(item => string.Equals(item, hex, StringComparison.OrdinalIgnoreCase));
                ShowColorFan();
            };
            System.Windows.Controls.Canvas.SetLeft(swatch, centerX + Math.Cos(angle) * radius - 17);
            System.Windows.Controls.Canvas.SetTop(swatch, centerY + Math.Sin(angle) * radius - 17);
            ColorFan.Children.Add(swatch);
        }

        var add = new Button
        {
            Content = "+",
            Width = 34,
            Height = 34,
            FontSize = 18,
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("LineBrush"),
            Cursor = Cursors.Hand,
            ToolTip = "Add a color"
        };
        add.Click += (_, _) =>
        {
            ColorPopup.IsOpen = false;
            Studio.SetColor(Surface.ColorHex);
            StudioPopup.IsOpen = true;
        };
        System.Windows.Controls.Canvas.SetLeft(add, centerX - 17);
        System.Windows.Controls.Canvas.SetTop(add, centerY - radius - 52);
        ColorFan.Children.Add(add);
        ColorPopup.IsOpen = true;
    }

    private void AddPaletteColor(string hex)
    {
        if (_settings.Palette.Any(item => string.Equals(item, hex, StringComparison.OrdinalIgnoreCase)))
        {
            _note = "That color is already in the palette.";
            _noteUntil = DateTime.UtcNow.AddSeconds(3);
            UpdateStatus();
            return;
        }

        if (_settings.Palette.Count >= 24)
            _settings.Palette.RemoveAt(0);
        _settings.Palette.Add(Rgba.Parse(hex).ToHex());
        ChooseColor(hex);
    }

    private static Color MediaColor(string hex)
    {
        var color = Rgba.Parse(hex);
        return Color.FromRgb(color.R, color.G, color.B);
    }

    private void OpenText(AppCanvas.TextPrompt prompt)
    {
        _textFont = prompt.FontSize;
        InlineText.Text = "";
        InlineText.FontSize = Math.Clamp(prompt.FontSize * Surface.Camera.Zoom, 12, 96);
        InlineText.Foreground = new SolidColorBrush(MediaColor(Surface.ColorHex));
        TextPopup.HorizontalOffset = prompt.ScreenPoint.X;
        TextPopup.VerticalOffset = prompt.ScreenPoint.Y;
        TextPopup.IsOpen = true;
        Dispatcher.BeginInvoke(() => InlineText.Focus());
    }

    private void CommitInlineText()
    {
        if (!TextPopup.IsOpen)
            return;
        var text = InlineText.Text;
        TextPopup.IsOpen = false;
        if (string.IsNullOrWhiteSpace(text))
            return;

        var zoom = Math.Max(0.02, Surface.Camera.Zoom);
        Surface.CommitText(text, Math.Max(8, InlineText.ActualWidth / zoom), Math.Max(8, InlineText.ActualHeight / zoom), _textFont);
    }

    private bool ConfirmDiscard()
    {
        if (!Surface.IsDirty)
            return true;

        var result = MessageBox.Show(this, "Save changes to this drawing?", "SimplyDraw", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.No => true,
            _ => Save()
        };
    }

    private bool Save() => string.IsNullOrWhiteSpace(_path) ? SaveAs() : WriteFile(_path);

    private bool SaveAs()
    {
        var dialog = new SaveFileDialog
        {
            Filter = DrawingFile.Filter,
            DefaultExt = ".sdraw",
            FileName = string.IsNullOrWhiteSpace(_path) ? "Untitled" : Path.GetFileName(_path)
        };
        if (dialog.ShowDialog(this) != true)
            return false;
        return WriteFile(dialog.FileName);
    }

    private bool WriteFile(string path)
    {
        try
        {
            Surface.CaptureView();
            Surface.Document.Name = Path.GetFileNameWithoutExtension(path);
            DrawingFile.Save(Surface.Document, path);
            _path = path;
            Surface.MarkClean();
            _settings.RememberFile(path);
            TryDeleteAutosave();
            PersistSettings();
            UpdateTitle();
            _note = "Saved.";
            _noteUntil = DateTime.UtcNow.AddSeconds(3);
            UpdateStatus();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "SimplyDraw", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private void OpenPath(string path, bool confirm = true)
    {
        if (confirm && !ConfirmDiscard())
            return;

        try
        {
            var document = DrawingFile.Load(path);
            _path = path;
            Surface.LoadDocument(document);
            _settings.RememberFile(path);
            PersistSettings();
            SyncLook();
            _note = "Opened.";
            _noteUntil = DateTime.UtcNow.AddSeconds(3);
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidDataException)
        {
            MessageBox.Show(this, "That file could not be opened.\n" + ex.Message, "SimplyDraw", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void NewDocument()
    {
        if (!ConfirmDiscard())
            return;

        _path = null;
        var document = DrawingDocument.CreateNew();
        document.Paper = PaperThemes.Get(SelectedTag(PaperBox) as string).Id;
        document.Grid = SelectedTag(GridBox) is GridStyle grid ? grid : GridStyle.Dots;
        document.SnapToGrid = SnapBox.IsChecked == true;
        document.GridSpacing = SpacingSlider.Value;
        Surface.LoadDocument(document);
        SyncLook();
    }

    private void OfferAutosave()
    {
        if (!File.Exists(UserSettings.AutosavePath))
            return;

        try
        {
            var document = DrawingFile.Load(UserSettings.AutosavePath);
            if (document.Layers.All(layer => layer.Strokes.Count == 0))
            {
                TryDeleteAutosave();
                return;
            }

            var recover = MessageBox.Show(this, "An unsaved drawing was found. Open it?", "SimplyDraw", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (recover != MessageBoxResult.Yes)
            {
                TryDeleteAutosave();
                return;
            }

            _path = null;
            Surface.LoadDocument(document);
            Surface.Redraw();
            _note = "Recovered the autosaved drawing.";
            _noteUntil = DateTime.UtcNow.AddSeconds(4);
        }
        catch (Exception)
        {
            TryDeleteAutosave();
        }
    }

    private void Autosave()
    {
        if (_preview || !Surface.IsDirty)
            return;

        try
        {
            Directory.CreateDirectory(UserSettings.Folder);
            Surface.CaptureView();
            DrawingFile.Save(Surface.Document, UserSettings.AutosavePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteAutosave()
    {
        try
        {
            if (File.Exists(UserSettings.AutosavePath))
                File.Delete(UserSettings.AutosavePath);
        }
        catch (IOException)
        {
        }
    }

    private void PersistSettings()
    {
        _settings.BrushId = Surface.BrushId;
        _settings.BrushSize = Surface.BrushSize;
        _settings.BrushOpacity = Surface.BrushOpacity;
        _settings.Smoothing = Surface.Smoothing;
        _settings.Color = Surface.ColorHex;
        _settings.Paper = Surface.Document.Paper;
        _settings.Grid = Surface.Document.Grid;
        _settings.Snap = Surface.Document.SnapToGrid;
        _settings.GridSpacing = Surface.Document.GridSpacing;
        _settings.TouchMode = Surface.TouchMode == AppCanvas.TouchMode.Select ? "select" : "pan";
        _settings.EraserRadius = Surface.EraserRadius;
        _settings.EraserWholeStroke = Surface.EraserWholeStroke;
        _settings.SelectStyle = Surface.SelectStyle == AppCanvas.SelectStyle.Lasso ? "lasso" : "rectangle";
        _settings.ShowHint = HintCard.Visibility == Visibility.Visible;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }

        _settings.Save();
    }

    private void OpenStartupFile()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]) && args[1].EndsWith(".sdraw", StringComparison.OrdinalIgnoreCase))
            OpenPath(args[1], confirm: false);
    }

    private void ImportImage()
    {
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff" };
        if (dialog.ShowDialog(this) == true)
            PlaceImageFile(dialog.FileName);
    }

    private void PlaceImageFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            double width = 800, height = 600;
            using var stream = File.OpenRead(path);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad).Frames[0];
            width = frame.PixelWidth;
            height = frame.PixelHeight;
            Surface.PlaceImage(bytes, width, height);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            MessageBox.Show(this, "That image could not be placed.", "SimplyDraw");
        }
    }

    private void PasteImage()
    {
        if (!Clipboard.ContainsImage())
            return;
        var source = Clipboard.GetImage();
        if (source == null)
            return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        Surface.PlaceImage(stream.ToArray(), source.PixelWidth, source.PixelHeight);
    }

    private void ExportDrawing()
    {
        var dialog = new ExportWindow { Owner = this };
        if (dialog.ShowDialog() != true)
            return;

        var filter = dialog.Svg ? "SVG (*.svg)|*.svg" : "PNG (*.png)|*.png";
        var save = new SaveFileDialog { Filter = filter, FileName = string.IsNullOrWhiteSpace(_path) ? "drawing" : Path.GetFileNameWithoutExtension(_path) };
        if (save.ShowDialog(this) != true)
            return;

        try
        {
            if (dialog.Svg)
            {
                Bounds? bounds = null;
                if (!dialog.Entire)
                {
                    var min = Surface.Camera.ScreenToWorld(Vec2.Zero);
                    var max = Surface.Camera.ScreenToWorld(new Vec2(Surface.ActualWidth, Surface.ActualHeight));
                    bounds = Bounds.FromCorners(min, max);
                }

                File.WriteAllText(save.FileName, SvgWriter.Export(Surface.Document, bounds));
            }
            else
            {
                var camera = new Camera();
                double width;
                double height;
                if (dialog.Entire)
                {
                    var content = Surface.Document.ContentBounds();
                    if (content.IsEmpty)
                        content = new Bounds(-100, -100, 100, 100);
                    width = Math.Clamp(content.Width + 80, 200, 5000);
                    height = Math.Clamp(content.Height + 80, 200, 5000);
                    camera.Fit(content, width, height, 36);
                }
                else
                {
                    camera.Zoom = Surface.Camera.Zoom;
                    camera.PanX = Surface.Camera.PanX;
                    camera.PanY = Surface.Camera.PanY;
                    width = Math.Max(1, Surface.ActualWidth);
                    height = Math.Max(1, Surface.ActualHeight);
                }

                var bitmap = AppCanvas.SceneRenderer.RenderBitmap(Surface.Document, camera, width, height, dialog.IncludeGrid, dialog.Scale);
                AppCanvas.SceneRenderer.SavePng(bitmap, save.FileName);
            }

            _note = "Exported.";
            _noteUntil = DateTime.UtcNow.AddSeconds(3);
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "SimplyDraw");
        }
    }

    private void ApplyLook()
    {
        if (_syncing || !IsLoaded || SnapBox == null || PaperBox.SelectedItem == null || GridBox.SelectedItem == null)
            return;

        var paper = SelectedTag(PaperBox) as string ?? Surface.Document.Paper;
        var grid = SelectedTag(GridBox) is GridStyle value ? value : Surface.Document.Grid;
        Surface.SetCanvasLook(grid, SnapBox.IsChecked == true, SpacingSlider.Value, paper);
        ApplyPaperChrome();
    }

    private static object? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag;

    private void ShowRecent()
    {
        RecentList.Children.Clear();
        if (_settings.RecentFiles.Count == 0)
        {
            RecentList.Children.Add(new TextBlock { Text = "None yet", Margin = new Thickness(10, 2, 10, 6), Foreground = (Brush)FindResource("MutedBrush") });
            return;
        }

        foreach (var file in _settings.RecentFiles.Where(File.Exists))
        {
            var path = file;
            var button = new Button
            {
                Content = Path.GetFileName(path),
                Style = (Style)FindResource("MenuButton"),
                ToolTip = path
            };
            button.Click += (_, _) =>
            {
                FilePopup.IsOpen = false;
                OpenPath(path);
            };
            RecentList.Children.Add(button);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (TextPopup.IsOpen)
        {
            if (e.Key == Key.Escape)
            {
                TextPopup.IsOpen = false;
                e.Handled = true;
            }

            return;
        }

        if (Keyboard.FocusedElement is TextBox)
            return;

        if (e.Key == Key.Space)
        {
            Surface.SetSpaceHeld(true);
            e.Handled = true;
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (ctrl && e.Key == Key.Z && !shift)
        {
            Surface.Undo();
            e.Handled = true;
            return;
        }

        if (ctrl && (e.Key == Key.Y || (e.Key == Key.Z && shift)))
        {
            Surface.Redo();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S && shift)
        {
            SaveAs();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.S)
        {
            Save();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.O)
        {
            OpenClick(this, e);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.N)
        {
            NewDocument();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.E)
        {
            ExportDrawing();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.A)
        {
            Surface.SelectAll();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.D)
        {
            Surface.DuplicateSelection();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.C)
        {
            Surface.CopySelection();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.X)
        {
            Surface.CopySelection();
            Surface.DeleteSelection();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.V)
        {
            if (Surface.HasCopiedMarks)
                Surface.PasteClipboard();
            else
                PasteImage();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is Key.D0 or Key.NumPad0)
        {
            Surface.ZoomActual();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is Key.D1 or Key.NumPad1)
        {
            Surface.FitView();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is Key.OemPlus or Key.Add)
        {
            Surface.ZoomBy(1.1);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key is Key.OemMinus or Key.Subtract)
        {
            Surface.ZoomBy(1 / 1.1);
            e.Handled = true;
            return;
        }

        if (!ctrl && e.Key is Key.Delete or Key.Back)
        {
            Surface.DeleteSelection();
            e.Handled = true;
            return;
        }

        if (!ctrl && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Surface.HasSelection)
        {
            var step = (shift ? 10 : 1) / Math.Max(0.02, Surface.Camera.Zoom);
            var dx = e.Key == Key.Left ? -step : e.Key == Key.Right ? step : 0;
            var dy = e.Key == Key.Up ? -step : e.Key == Key.Down ? step : 0;
            Surface.NudgeSelection(dx, dy);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            Surface.CancelInteraction();
            Surface.DismissSelection();
            ClosePopups();
            e.Handled = true;
            return;
        }

        if (ctrl || shift)
            return;

        var tool = e.Key switch
        {
            Key.B => "pen",
            Key.E => "eraser",
            Key.S => "select",
            Key.H => "hand",
            Key.L => "line",
            Key.R => "rect",
            Key.O => "ellipse",
            Key.A => "arrow",
            Key.T => "text",
            Key.I => "pick",
            _ => null
        };
        if (tool != null)
        {
            Surface.SetTool(tool);
            SyncTools();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.D1 or Key.D2 or Key.D3 or Key.D4 or Key.D5 or Key.D6 or Key.D7 or Key.D8 or Key.D9 or Key.NumPad1 or Key.NumPad2 or Key.NumPad3 or Key.NumPad4 or Key.NumPad5 or Key.NumPad6 or Key.NumPad7 or Key.NumPad8 or Key.NumPad9)
        {
            var number = e.Key is Key.NumPad1 or Key.D1 ? 1
                : e.Key is Key.NumPad2 or Key.D2 ? 2
                : e.Key is Key.NumPad3 or Key.D3 ? 3
                : e.Key is Key.NumPad4 or Key.D4 ? 4
                : e.Key is Key.NumPad5 or Key.D5 ? 5
                : e.Key is Key.NumPad6 or Key.D6 ? 6
                : e.Key is Key.NumPad7 or Key.D7 ? 7
                : e.Key is Key.NumPad8 or Key.D8 ? 8
                : 9;
            if (number <= BrushLibrary.All.Count)
            {
                Surface.SetBrush(BrushLibrary.All[number - 1].Id, applyDefaults: true);
                SyncBrushControls();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Oem4)
        {
            Surface.BrushSize = Math.Max(SizeSlider.Minimum, Surface.BrushSize / 1.15);
            SyncBrushControls();
            Surface.RefreshChrome();
            e.Handled = true;
        }
        else if (e.Key == Key.Oem6)
        {
            Surface.BrushSize = Math.Min(SizeSlider.Maximum, Surface.BrushSize * 1.15);
            SyncBrushControls();
            Surface.RefreshChrome();
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
            Surface.SetSpaceHeld(false);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_preview)
            return;
        if (!ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        PersistSettings();
        TryDeleteAutosave();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".sdraw")
            {
                OpenPath(file);
                return;
            }

            if (extension is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".tif" or ".tiff")
                PlaceImageFile(file);
        }
    }

    private void ClosePopups()
    {
        FilePopup.IsOpen = false;
        ViewPopup.IsOpen = false;
        BrushPopup.IsOpen = false;
        ColorPopup.IsOpen = false;
        StudioPopup.IsOpen = false;
        TextPopup.IsOpen = false;
    }

    private static IntPtr TabletHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x02CC)
            return IntPtr.Zero;

        handled = true;
        return new IntPtr(0x00010001 | 0x00000008);
    }

    private void LogoClick(object sender, RoutedEventArgs e)
    {
        ShowRecent();
        FilePopup.IsOpen = true;
    }

    private void NewClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        NewDocument();
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        var dialog = new OpenFileDialog { Filter = DrawingFile.Filter };
        if (dialog.ShowDialog(this) == true)
            OpenPath(dialog.FileName);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        Save();
    }

    private void SaveAsClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        SaveAs();
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        ImportImage();
    }

    private void DemoClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        if (!ConfirmDiscard())
            return;
        _path = null;
        Surface.LoadDocument(DemoSketch.Create());
        SyncLook();
    }

    private void UndoClick(object sender, RoutedEventArgs e) => Surface.Undo();

    private void RedoClick(object sender, RoutedEventArgs e) => Surface.Redo();

    private void ZoomOutClick(object sender, RoutedEventArgs e) => Surface.ZoomBy(1 / 1.1);

    private void ZoomInClick(object sender, RoutedEventArgs e) => Surface.ZoomBy(1.1);

    private void FitClick(object sender, RoutedEventArgs e) => Surface.FitView();

    private void TouchMoveClick(object sender, RoutedEventArgs e)
    {
        Surface.TouchMode = AppCanvas.TouchMode.Pan;
        SyncTools();
    }

    private void TouchSelectClick(object sender, RoutedEventArgs e)
    {
        Surface.TouchMode = AppCanvas.TouchMode.Select;
        SyncTools();
    }

    private void ViewClick(object sender, RoutedEventArgs e)
    {
        SyncLook();
        ViewPopup.IsOpen = true;
    }

    private void ExportClick(object sender, RoutedEventArgs e) => ExportDrawing();

    private void CloseLayersClick(object sender, RoutedEventArgs e)
    {
        LayersCard.Visibility = Visibility.Collapsed;
        LayersToggle.Visibility = Visibility.Visible;
    }

    private void ShowLayersClick(object sender, RoutedEventArgs e)
    {
        LayersCard.Visibility = Visibility.Visible;
        LayersToggle.Visibility = Visibility.Collapsed;
        RebuildLayers();
    }

    private void AddLayerClick(object sender, RoutedEventArgs e) => Surface.AddLayer();

    private void DuplicateLayerClick(object sender, RoutedEventArgs e) => Surface.DuplicateActiveLayer();

    private void UpLayerClick(object sender, RoutedEventArgs e) => Surface.MoveActiveLayer(1);

    private void DownLayerClick(object sender, RoutedEventArgs e) => Surface.MoveActiveLayer(-1);

    private void DeleteLayerClick(object sender, RoutedEventArgs e) => Surface.DeleteActiveLayer();

    private void LayerOpacityDown(object sender, MouseButtonEventArgs e)
    {
        _opacityDragging = true;
        _opacityStart = Surface.Document.ActiveLayer.Opacity;
    }

    private void LayerOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !_opacityDragging)
            return;
        Surface.PreviewLayerOpacity(Surface.Document.ActiveLayer.Id, LayerOpacity.Value);
    }

    private void LayerOpacityUp(object sender, MouseButtonEventArgs e)
    {
        if (!_opacityDragging)
            return;
        _opacityDragging = false;
        var layer = Surface.Document.ActiveLayer;
        Surface.CommitLayerProps(
            layer.Id,
            layer.Name,
            layer.Visible,
            layer.Locked,
            LayerOpacity.Value,
            new Layer { Name = layer.Name, Visible = layer.Visible, Locked = layer.Locked, Opacity = _opacityStart });
    }

    private void DismissHintClick(object sender, RoutedEventArgs e)
    {
        HintCard.Visibility = Visibility.Collapsed;
        _settings.ShowHint = false;
        PersistSettings();
    }

    private void BrushClick(object sender, RoutedEventArgs e) => BrushPopup.IsOpen = !BrushPopup.IsOpen;

    private void BrushSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded)
            return;
        Surface.BrushSize = SizeSlider.Value;
        SizeLabel.Text = Surface.ToolId == "text" ? $"{Surface.BrushSize:0} pt" : $"{Surface.BrushSize:0.#}";
        Surface.RefreshChrome();
    }

    private void OpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded)
            return;
        Surface.BrushOpacity = OpacitySlider.Value;
        OpacityLabel.Text = $"{Math.Round(Surface.BrushOpacity * 100)}%";
    }

    private void SmoothChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded)
            return;
        Surface.Smoothing = SmoothSlider.Value;
        SmoothLabel.Text = $"{Math.Round(Surface.Smoothing * 100)}";
    }

    private void EraserChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded)
            return;
        Surface.EraserRadius = EraserSlider.Value;
        Surface.RefreshChrome();
    }

    private void WholeStrokeClick(object sender, RoutedEventArgs e) =>
        Surface.EraserWholeStroke = WholeStrokeToggle.IsChecked == true;

    private void RectSelectClick(object sender, RoutedEventArgs e)
    {
        Surface.SelectStyle = AppCanvas.SelectStyle.Rectangle;
        SyncTools();
    }

    private void LassoSelectClick(object sender, RoutedEventArgs e)
    {
        Surface.SelectStyle = AppCanvas.SelectStyle.Lasso;
        SyncTools();
    }

    private void DuplicateClick(object sender, RoutedEventArgs e) => Surface.DuplicateSelection();

    private void DeleteSelectionClick(object sender, RoutedEventArgs e) => Surface.DeleteSelection();

    private void ColorClick(object sender, RoutedEventArgs e) => ShowColorFan();

    private void PaperChanged(object sender, SelectionChangedEventArgs e) => ApplyLook();

    private void GridChanged(object sender, SelectionChangedEventArgs e) => ApplyLook();

    private void SpacingChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyLook();

    private void SnapChanged(object sender, RoutedEventArgs e) => ApplyLook();

    private void ShortcutsClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        new ShortcutsWindow { Owner = this }.Show();
    }

    private void AboutClick(object sender, RoutedEventArgs e)
    {
        FilePopup.IsOpen = false;
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        MessageBox.Show(this,
            $"SimplyDraw {version}\n\nAn infinite canvas for Windows pen and touch.\n\nDraw with the pen. A finger moves the page, or selects when Touch is set to Select.",
            "SimplyDraw");
    }

    private void InlineTextLostFocus(object sender, RoutedEventArgs e) => CommitInlineText();

    private void InlineTextKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            Surface.Focus();
        }
        else if (e.Key == Key.Escape)
        {
            TextPopup.IsOpen = false;
            e.Handled = true;
        }
    }
}
