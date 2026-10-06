# `.sdraw` files

A SimplyDraw file is UTF-8 JSON. The extension is `.sdraw`. Version 1 is the only version written today. Unknown fields are ignored by the reader, and missing optional fields fall back to the defaults on each type.

## Top level

| Field | Meaning |
| --- | --- |
| `version` | File version. Currently `1`. |
| `name` | Drawing name. |
| `paper` | `warm`, `white`, `dark`, `blueprint`, or `kraft`. |
| `grid` | `none`, `dots`, `lines`, or `isometric`. |
| `snapToGrid` | Snap shape corners to the grid. |
| `gridSpacing` | Grid step in world units. |
| `viewZoom`, `viewCenterX`, `viewCenterY` | Camera restored on open. |
| `activeLayerIndex` | Layer that receives new marks. |
| `layers` | Back to front. |

## Layer

| Field | Meaning |
| --- | --- |
| `id` | Stable id. |
| `name` | Shown in the layer list. |
| `visible`, `locked` | Hidden layers are not drawn. Locked layers are not edited. |
| `opacity` | Multiplied into every mark on the layer. |
| `strokes` | Marks, in paint order. |

## Stroke

| Field | Meaning |
| --- | --- |
| `id` | Stable id. |
| `kind` | `freehand`, `line`, `rectangle`, `ellipse`, `arrow`, `text`, or `image`. |
| `brushId` | Which preset was used. The look below is stored on the stroke itself. |
| `color` | `#RRGGBB` or `#RRGGBBAA`. |
| `size`, `opacity` | Width and strength. |
| `pressureSize`, `pressureOpacity`, `minWidthFactor` | How pressure changes width and strength. |
| `hardness` | Edge softness. `1` is solid. |
| `mode` | `ribbon` or `stamps`. |
| `stampSpacing`, `grain`, `taper`, `directionalNib` | Stamp and ribbon shaping. |
| `points` | `{ "x", "y", "pressure" }`. Shapes use two points. Text and images use one anchor. |
| `text`, `fontSize`, `boxWidth`, `boxHeight`, `rotation` | Text, and the box used by text and images. |
| `image` | Base64 image bytes for an image mark. Omitted when empty. |

World coordinates are the same units the canvas uses at 100% zoom. Positive X is right. Positive Y is down.

## Example

```json
{
  "version": 1,
  "name": "Untitled",
  "paper": "warm",
  "grid": "dots",
  "gridSpacing": 32,
  "viewZoom": 1,
  "layers": [
    {
      "name": "Layer 1",
      "visible": true,
      "opacity": 1,
      "strokes": [
        {
          "kind": "freehand",
          "brushId": "ink",
          "color": "#1C1917",
          "size": 5,
          "opacity": 1,
          "points": [
            { "x": 0, "y": 0, "pressure": 0.4 },
            { "x": 40, "y": 12, "pressure": 0.9 }
          ]
        }
      ]
    }
  ]
}
```
