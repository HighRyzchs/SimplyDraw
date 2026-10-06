# Using SimplyDraw

## A new drawing

**SimplyDraw → New**, or Ctrl+N. The page starts empty, centered on the origin. Pan and zoom do not change the file until you save; the save remembers where you were looking.

**SimplyDraw → Demo sketch** loads a small sample so you can try every brush without starting from a blank page.

## Drawing

1. Choose a tool on the bottom bar. The pen tool is the one that uses brushes.
2. Pick a brush name to open the brush list. Choosing a brush also sets its usual size, opacity, and smoothing. You can change those afterward.
3. Draw with the pen or the left mouse button.

Hold **Shift** while drawing to lock the stroke to a straight 45-degree step. Hold **Alt** with the pen tool to pick a color from a mark already on the page.

A tap with the pen leaves a dot. Slow strokes keep more detail when you are zoomed in.

## Brushes

| Brush | Feel |
| --- | --- |
| Ink | A pen with tapered ends and pressure width. |
| Pencil | Grainy dots, lighter when the pen is light. |
| Marker | A wide, even, partly transparent stroke. |
| Soft brush | A round brush that grows with pressure. |
| Highlighter | A wide translucent band. |
| Technical | A fixed-width line for diagrams. |
| Nib | Thick and thin as the stroke turns. |
| Spray | Soft scattered dots. |
| Fountain | Strong thick-and-thin with tapered ends. |

`[` and `]` change the size. Keys `1` through `9` pick the first nine brushes.

## Color

The round swatch at the right of the bottom bar opens a fan of palette colors.

- Click a color to use it.
- Right-click a color to remove it. The palette keeps at least two.
- **+** opens the color wheel. **Use color** selects it. **Add to palette** keeps it in the fan. The fan holds up to 24 colors.

## Eraser

The eraser follows the size slider in the eraser options. By default it cuts a hole through vector strokes and leaves the remaining pieces. **Whole mark** deletes any mark the eraser touches, including shapes, text, and images.

Eraser strokes are one undo step.

## Select

**Box** drags a rectangle. **Lasso** draws a free shape. Shift adds to the current selection.

Drag a selected mark to move it. Shift keeps the move on one axis. Corner handles scale from the center. Side handles stretch on one axis. The round handle above the box rotates.

Delete, Ctrl+D, or the buttons on the select bar remove or duplicate the selection. Arrow keys nudge it. Ctrl+A selects every mark on visible, unlocked layers.

## Shapes and text

Line, rectangle, ellipse, and arrow use the current color and size. Hold Shift for a straight line, or for a square and a circle. If **Snap shapes to the grid** is on in View, the corners land on the grid. Freehand ink does not snap.

Text asks for words at the spot you tap. Enter commits. Shift+Enter adds a line. The size slider is the type size while the text tool is active.

**SimplyDraw → Import image** or a dropped PNG, JPEG, BMP, GIF, or TIFF places a picture in the middle of the view. You can move and scale it with the select tool.

## Layers

The front layer is at the top of the list. New marks go on the active layer. A locked or hidden layer will not accept marks; the status line says so.

The eye hides a layer. The lock keeps it visible but not editable. Opacity fades the whole layer. Up and down change the stacking order.

## View

**View** changes the paper, the grid, the grid spacing, and snap. The page can be warm paper, white, night, blueprint, or kraft. Grids are dots, lines, isometric, or off.

**Touch → Move** is the default: one finger pans. **Touch → Select** makes one finger select instead. Two fingers always pinch-zoom and pan.

Fit frames everything you have drawn. 100% is actual size, from the zoom buttons or Ctrl+0. The wheel zooms toward the pointer.

## Files

Drawings are saved as `.sdraw`. That file is JSON, so it is easy to keep in git. See [file-format.md](file-format.md).

Export writes a PNG of the current view or the whole drawing, with an optional grid, at 1×, 2×, or 3×. SVG export keeps the strokes as vectors.

While a drawing has unsaved changes, SimplyDraw writes `%AppData%\SimplyDraw\autosave.sdraw` about every 45 seconds. The next launch can open that file. A normal save or a clean exit removes it.
