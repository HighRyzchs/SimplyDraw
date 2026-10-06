namespace SimplyDraw;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var preview = e.Args.Length >= 2 && e.Args[0] == "--render-preview";
        var window = new MainWindow(preview);
        MainWindow = window;
        window.Show();
        if (!preview)
            return;

        window.Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(800);
            window.SavePreview(e.Args[1]);
            Shutdown();
        });
    }
}
