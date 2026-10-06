namespace SimplyDraw;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
        Body.Text = string.Join(Environment.NewLine, new[]
        {
            "B pen    E eraser    S select    H hand",
            "L line   R rectangle O ellipse   A arrow",
            "T text   I pick color",
            "1–9 choose a brush",
            "[ ] change size",
            "Shift while drawing locks to a straight angle",
            "Alt while drawing picks a color",
            "Ctrl+Z undo    Ctrl+Y or Ctrl+Shift+Z redo",
            "Ctrl+C copy    Ctrl+X cut    Ctrl+V paste",
            "Ctrl+D duplicate    Delete removes the selection",
            "Ctrl+A select all    arrow keys nudge",
            "Ctrl+N new    Ctrl+O open    Ctrl+S save",
            "Ctrl+Shift+S save as    Ctrl+E export",
            "Ctrl+0 actual size    Ctrl+1 fit    Ctrl+Plus or wheel zoom",
            "Escape clears the selection and closes panels"
        });
    }
}
