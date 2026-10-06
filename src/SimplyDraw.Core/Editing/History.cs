using SimplyDraw.Core.Model;

namespace SimplyDraw.Core.Editing;

public interface IEdit
{
    string Name { get; }

    void Undo(DrawingDocument document);

    void Redo(DrawingDocument document);
}

public sealed class History
{
    private readonly Stack<IEdit> _undo = new();
    private readonly Stack<IEdit> _redo = new();

    public int Limit { get; set; } = 200;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoName => _undo.Count > 0 ? _undo.Peek().Name : null;

    public string? RedoName => _redo.Count > 0 ? _redo.Peek().Name : null;

    public event Action? Changed;

    public void Push(IEdit edit)
    {
        _undo.Push(edit);
        _redo.Clear();
        Trim();
        Changed?.Invoke();
    }

    public void Undo(DrawingDocument document)
    {
        if (!CanUndo)
            return;

        var edit = _undo.Pop();
        edit.Undo(document);
        _redo.Push(edit);
        Changed?.Invoke();
    }

    public void Redo(DrawingDocument document)
    {
        if (!CanRedo)
            return;

        var edit = _redo.Pop();
        edit.Redo(document);
        _undo.Push(edit);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private void Trim()
    {
        if (_undo.Count <= Limit)
            return;

        var items = _undo.ToArray();
        _undo.Clear();
        for (var i = Limit - 1; i >= 0; i--)
            _undo.Push(items[i]);
    }
}

public sealed class CompoundEdit : IEdit
{
    private readonly List<IEdit> _edits;

    public CompoundEdit(string name, IEnumerable<IEdit> edits)
    {
        Name = name;
        _edits = edits.ToList();
    }

    public string Name { get; }

    public void Undo(DrawingDocument document)
    {
        for (var i = _edits.Count - 1; i >= 0; i--)
            _edits[i].Undo(document);
    }

    public void Redo(DrawingDocument document)
    {
        foreach (var edit in _edits)
            edit.Redo(document);
    }
}

public sealed class AddStrokeEdit : IEdit
{
    private readonly string _layerId;
    private readonly Stroke _stroke;

    public AddStrokeEdit(string layerId, Stroke stroke, string name = "Draw")
    {
        _layerId = layerId;
        _stroke = stroke;
        Name = name;
    }

    public string Name { get; }

    public void Undo(DrawingDocument document) =>
        document.FindLayer(_layerId)?.Strokes.RemoveAll(stroke => stroke.Id == _stroke.Id);

    public void Redo(DrawingDocument document)
    {
        var layer = document.FindLayer(_layerId);
        if (layer == null || layer.Strokes.Any(stroke => stroke.Id == _stroke.Id))
            return;

        layer.Strokes.Add(_stroke);
    }
}

public sealed class RemoveStrokesEdit : IEdit
{
    private readonly List<(string LayerId, int Index, Stroke Stroke)> _items;

    public RemoveStrokesEdit(IEnumerable<(string LayerId, int Index, Stroke Stroke)> items)
    {
        _items = items.ToList();
    }

    public string Name => "Delete";

    public void Undo(DrawingDocument document)
    {
        foreach (var group in _items.GroupBy(item => item.LayerId))
        {
            var layer = document.FindLayer(group.Key);
            if (layer == null)
                continue;

            foreach (var item in group.OrderBy(item => item.Index))
            {
                if (layer.Strokes.Any(stroke => stroke.Id == item.Stroke.Id))
                    continue;

                layer.Strokes.Insert(Math.Clamp(item.Index, 0, layer.Strokes.Count), item.Stroke);
            }
        }
    }

    public void Redo(DrawingDocument document)
    {
        foreach (var item in _items)
            document.FindLayer(item.LayerId)?.Strokes.RemoveAll(stroke => stroke.Id == item.Stroke.Id);
    }
}

public sealed class MutateStrokesEdit : IEdit
{
    private readonly List<(string StrokeId, Stroke Before, Stroke After)> _items;

    public MutateStrokesEdit(string name, IEnumerable<(string StrokeId, Stroke Before, Stroke After)> items)
    {
        Name = name;
        _items = items.Select(item => (item.StrokeId, item.Before.Clone(), item.After.Clone())).ToList();
    }

    public string Name { get; }

    public void Undo(DrawingDocument document)
    {
        foreach (var item in _items)
            Apply(document, item.StrokeId, item.Before);
    }

    public void Redo(DrawingDocument document)
    {
        foreach (var item in _items)
            Apply(document, item.StrokeId, item.After);
    }

    private static void Apply(DrawingDocument document, string id, Stroke state)
    {
        var found = document.FindStroke(id);
        found?.Stroke.CopyGeometryFrom(state);
    }
}

public sealed class ReplaceLayerStrokesEdit : IEdit
{
    private readonly string _layerId;
    private readonly List<Stroke> _before;
    private readonly List<Stroke> _after;

    public ReplaceLayerStrokesEdit(string name, string layerId, IReadOnlyList<Stroke> before, IReadOnlyList<Stroke> after)
    {
        Name = name;
        _layerId = layerId;
        _before = before.Select(stroke => stroke.Clone()).ToList();
        _after = after.Select(stroke => stroke.Clone()).ToList();
    }

    public string Name { get; }

    public void Undo(DrawingDocument document) => Set(document, _before);

    public void Redo(DrawingDocument document) => Set(document, _after);

    private void Set(DrawingDocument document, List<Stroke> strokes)
    {
        var layer = document.FindLayer(_layerId);
        if (layer == null)
            return;

        layer.Strokes = strokes.Select(stroke => stroke.Clone()).ToList();
    }
}

public sealed class InsertLayerEdit : IEdit
{
    private readonly int _index;
    private readonly Layer _layer;

    public InsertLayerEdit(int index, Layer layer, string name = "Add layer")
    {
        _index = index;
        _layer = layer;
        Name = name;
    }

    public string Name { get; }

    public void Redo(DrawingDocument document)
    {
        if (document.Layers.Any(layer => layer.Id == _layer.Id))
            return;

        document.Layers.Insert(Math.Clamp(_index, 0, document.Layers.Count), _layer);
    }

    public void Undo(DrawingDocument document)
    {
        document.Layers.RemoveAll(layer => layer.Id == _layer.Id);
        document.EnsureLayer();
        document.ActiveLayerIndex = Math.Clamp(document.ActiveLayerIndex, 0, document.Layers.Count - 1);
    }
}

public sealed class RemoveLayerEdit : IEdit
{
    private readonly int _index;
    private readonly Layer _layer;

    public RemoveLayerEdit(int index, Layer layer)
    {
        _index = index;
        _layer = layer;
    }

    public string Name => "Delete layer";

    public void Undo(DrawingDocument document)
    {
        if (document.Layers.Any(layer => layer.Id == _layer.Id))
            return;

        document.Layers.Insert(Math.Clamp(_index, 0, document.Layers.Count), _layer);
    }

    public void Redo(DrawingDocument document)
    {
        document.Layers.RemoveAll(layer => layer.Id == _layer.Id);
        document.EnsureLayer();
        document.ActiveLayerIndex = Math.Clamp(document.ActiveLayerIndex, 0, document.Layers.Count - 1);
    }
}

public sealed class MoveLayerEdit : IEdit
{
    private readonly int _from;
    private readonly int _to;

    public MoveLayerEdit(int from, int to)
    {
        _from = from;
        _to = to;
    }

    public string Name => "Reorder layer";

    public void Redo(DrawingDocument document) => Move(document, _from, _to);

    public void Undo(DrawingDocument document) => Move(document, _to, _from);

    private static void Move(DrawingDocument document, int from, int to)
    {
        if (from < 0 || to < 0 || from >= document.Layers.Count || to >= document.Layers.Count || from == to)
            return;

        var layer = document.Layers[from];
        document.Layers.RemoveAt(from);
        document.Layers.Insert(to, layer);
    }
}

public sealed class LayerPropsEdit : IEdit
{
    public LayerPropsEdit(
        string layerId,
        string beforeName,
        string afterName,
        bool beforeVisible,
        bool afterVisible,
        bool beforeLocked,
        bool afterLocked,
        double beforeOpacity,
        double afterOpacity)
    {
        LayerId = layerId;
        BeforeName = beforeName;
        AfterName = afterName;
        BeforeVisible = beforeVisible;
        AfterVisible = afterVisible;
        BeforeLocked = beforeLocked;
        AfterLocked = afterLocked;
        BeforeOpacity = beforeOpacity;
        AfterOpacity = afterOpacity;
    }

    public string Name => "Layer";

    public string LayerId { get; }

    public string BeforeName { get; }

    public string AfterName { get; }

    public bool BeforeVisible { get; }

    public bool AfterVisible { get; }

    public bool BeforeLocked { get; }

    public bool AfterLocked { get; }

    public double BeforeOpacity { get; }

    public double AfterOpacity { get; }

    public void Undo(DrawingDocument document) => Apply(document, BeforeName, BeforeVisible, BeforeLocked, BeforeOpacity);

    public void Redo(DrawingDocument document) => Apply(document, AfterName, AfterVisible, AfterLocked, AfterOpacity);

    private void Apply(DrawingDocument document, string name, bool visible, bool locked, double opacity)
    {
        var layer = document.FindLayer(LayerId);
        if (layer == null)
            return;

        layer.Name = name;
        layer.Visible = visible;
        layer.Locked = locked;
        layer.Opacity = opacity;
    }
}

public sealed class BoardPropsEdit : IEdit
{
    public string Name => "Canvas";

    public GridStyle BeforeGrid { get; init; }

    public GridStyle AfterGrid { get; init; }

    public bool BeforeSnap { get; init; }

    public bool AfterSnap { get; init; }

    public double BeforeSpacing { get; init; }

    public double AfterSpacing { get; init; }

    public string BeforePaper { get; init; } = "warm";

    public string AfterPaper { get; init; } = "warm";

    public void Undo(DrawingDocument document) => Apply(document, BeforeGrid, BeforeSnap, BeforeSpacing, BeforePaper);

    public void Redo(DrawingDocument document) => Apply(document, AfterGrid, AfterSnap, AfterSpacing, AfterPaper);

    private static void Apply(DrawingDocument document, GridStyle grid, bool snap, double spacing, string paper)
    {
        document.Grid = grid;
        document.SnapToGrid = snap;
        document.GridSpacing = spacing;
        document.Paper = paper;
    }
}
