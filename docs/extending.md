# Extending SimplyDraw

Brushes are data. Tools are a small interface. You do not need a new window to add either one.

## Add a brush

1. Open `src/SimplyDraw.Core/Brushes/BrushLibrary.cs`.
2. Add a `BrushPreset` to `BrushLibrary.All`.
3. Run the app. The brush list is built from that array.

```csharp
new BrushPreset
{
    Id = "chalk",
    Name = "Chalk",
    Blurb = "A dry, broken stroke.",
    DefaultSize = 12,
    DefaultOpacity = 0.8,
    Smoothing = 0.2,
    PressureSize = true,
    PressureOpacity = true,
    MinWidthFactor = 0.3,
    Hardness = 0.4,
    Mode = RenderMode.Stamps,
    StampSpacing = 0.35,
    Grain = 0.9
}
```

`Id` must be unique. It is stored on each stroke, so renaming an id later will not retarget old drawings; they keep the settings that were copied onto the stroke when it was drawn.

| Field | Effect |
| --- | --- |
| `Mode` | `Ribbon` is a filled outline. `Stamps` repeats soft or grainy dots along the centerline. |
| `PressureSize` / `MinWidthFactor` | How thin a light pen touch is, as a fraction of `Size`. |
| `PressureOpacity` | Light pressure also fades the stroke. This is per stamp for spray and pencil. |
| `Smoothing` | How far the live stroke lags behind the pen. `0` is raw. |
| `Taper` | Narrows the start and end of a ribbon. |
| `DirectionalNib` | Thins the ribbon when the stroke is not aligned with the nib. |
| `Grain` | Shifts stamps by a stable hash so the texture does not change on reload. |
| `Hardness` | `1` is a solid edge. Lower values add a soft halo. |

The window copies these fields onto the stroke in `CreateStroke`. Hit testing uses the same radius math as the painter, in `StrokeMetrics`.

## Add a tool

Tools live in `src/SimplyDraw.App/Canvas/InfiniteCanvas.Tools.cs`.

1. Implement the private `ITool` interface: `Down`, `Move`, `Up`, and `Cancel`.
2. Register it in `CreateTools` with a stable string id.
3. Add a row to the `tools` array in `MainWindow.BuildTools` so the button, tooltip, and icon show up.
4. If it should have a keyboard key, add that key in `MainWindow.OnPreviewKeyDown`.
5. Add an icon path in `src/SimplyDraw.App/Services/IconSet.cs` if you need a new glyph.

`Down` receives a `PointerSample` with screen position, world position, pressure, and Shift/Alt. Call `CreateStroke` and `SetPreview` while the gesture is active, then `CommitStroke` on `Up`. `Cancel` must drop the preview and must not leave a partial undo record.

The pen, mouse, and finger-select path all call the same tool. A finger never calls the pen tool. If your tool should ignore touch even in Select mode, check `sample.IsPen` and the current `TouchMode`.

Shape tools are one class, `ShapeTool`, parameterized by `MarkKind`. A new shape kind also needs a branch in `StrokePainter` and, if you want SVG, in `SvgWriter`.

## Tests

Geometry and file behavior belong in `tests/SimplyDraw.Core.Tests`. If a brush only changes data, the existing ribbon and stamp code already covers it. If you change outlining, erasing, or the file format, add a test next to the one that already describes that behavior.
