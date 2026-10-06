namespace SimplyDraw;

public partial class ExportWindow : Window
{
    public ExportWindow()
    {
        InitializeComponent();
    }

    public bool Entire => EntireOption.IsChecked == true;

    public bool Svg => SvgOption.IsChecked == true;

    public bool IncludeGrid => GridBox.IsChecked == true;

    public double Scale => ScaleBox.SelectedIndex switch
    {
        0 => 1,
        2 => 3,
        _ => 2
    };

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
