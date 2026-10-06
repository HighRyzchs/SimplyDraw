using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace SimplyDraw.Setup;

public partial class MainWindow : Window
{
    private readonly Installer _installer = new();
    private string? _downloadUrl;
    private Version? _latestVersion;
    private bool _installed;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        PathText.Text = _installer.AppPath;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void ActionClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (_installed && _downloadUrl == null)
        {
            LaunchApp();
            return;
        }

        if (string.IsNullOrWhiteSpace(_downloadUrl))
            return;

        if (_installer.IsInstalledAppRunning())
        {
            var close = MessageBox.Show(
                this,
                "SimplyDraw is open. Close it and install the update?",
                "SimplyDraw Setup",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);
            if (close != MessageBoxResult.OK)
                return;
            if (!await _installer.TryCloseInstalledAppAsync())
            {
                ShowStatus("SimplyDraw is still open. Close it, then run the update again.", error: true);
                return;
            }
        }

        await InstallAsync();
    }

    private async Task RefreshAsync()
    {
        SetBusy(true, "Checking the latest GitHub release…");
        try
        {
            if (string.IsNullOrEmpty(_installer.AssetName))
            {
                ShowStatus("This installer supports 64-bit Intel, AMD, and ARM Windows PCs.", error: true);
                return;
            }

            var release = await _installer.LatestAsync(CancellationToken.None);
            var asset = Installer.RequireAsset(release, _installer.AssetName);
            _downloadUrl = asset.BrowserDownloadUrl;
            _latestVersion = Installer.ParseVersion(release.TagName);
            var installed = _installer.InstalledVersion();
            InstalledText.Text = installed == null
                ? "Installed: not yet"
                : $"Installed: {installed.ToString(3)}";
            LatestText.Text = $"Latest on GitHub: {release.TagName}";
            _installed = false;

            if (installed == null)
            {
                ActionButton.Content = "Install";
                ShowStatus("This downloads the latest SimplyDraw and keeps it updated in one folder.");
            }
            else if (Installer.SameVersion(installed, _latestVersion))
            {
                ActionButton.Content = "Reinstall";
                ShowStatus("This copy is already the latest release. Reinstall replaces the same exe.");
            }
            else
            {
                ActionButton.Content = "Update";
                ShowStatus("Update replaces the installed SimplyDraw.exe. Your drawings stay where they are.");
            }

            ActionButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            _downloadUrl = null;
            ShowStatus("Could not reach the latest GitHub release. " + ex.Message, error: true);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async Task InstallAsync()
    {
        SetBusy(true, "Downloading the latest release…");
        Progress.Value = 0;
        try
        {
            var progress = new Progress<double>(value =>
            {
                Progress.IsIndeterminate = false;
                Progress.Value = value;
                Subtitle.Text = $"Downloading the latest release… {Math.Round(value * 100)}%";
            });
            await _installer.DownloadAsync(_downloadUrl!, progress, CancellationToken.None);
            var shortcutNote = "";
            try
            {
                _installer.CopySetupBesideApp();
                _installer.InstallShortcuts();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                shortcutNote = " The Start menu shortcut could not be created. " + ex.Message;
            }
            var installed = _installer.InstalledVersion();
            InstalledText.Text = installed == null
                ? "Installed: ready"
                : $"Installed: {installed.ToString(3)}";
            _installed = true;
            _downloadUrl = null;
            ActionButton.Content = "Open SimplyDraw";
            ActionButton.IsEnabled = true;
            ShowStatus("Installed. Run Update SimplyDraw from the Start menu the next time you want the latest release." + shortcutNote);
            Progress.Value = 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
        {
            ShowStatus("The update did not finish. " + ex.Message, error: true);
            ActionButton.IsEnabled = _downloadUrl != null;
        }
        finally
        {
            _busy = false;
            Progress.IsIndeterminate = false;
        }
    }

    private void LaunchApp()
    {
        if (!File.Exists(_installer.AppPath))
            return;

        Process.Start(new ProcessStartInfo(_installer.AppPath) { UseShellExecute = true });
        Close();
    }

    private void SetBusy(bool busy, string? status)
    {
        _busy = busy;
        ActionButton.IsEnabled = !busy && _downloadUrl != null;
        Progress.IsIndeterminate = busy;
        if (!string.IsNullOrWhiteSpace(status))
            ShowStatus(status, error: false);
    }

    private void ShowStatus(string text, bool error = false)
    {
        Subtitle.Text = text;
        Subtitle.Foreground = error
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9B, 0x3A, 0x2F))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5C, 0x56, 0x4E));
    }
}
