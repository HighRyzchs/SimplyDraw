using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SimplyDraw.Setup;

public sealed class Installer
{
    public const string Repository = "HighRyzchs/SimplyDraw";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string InstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        "SimplyDraw");

    public string AppPath => Path.Combine(InstallDirectory, "SimplyDraw.exe");

    public string SetupPath => Path.Combine(InstallDirectory, "SimplyDrawSetup.exe");

    public string AssetName { get; } = AssetNameFor(RuntimeInformation.OSArchitecture);

    public static string AssetNameFor(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => "SimplyDraw-win-arm64.exe",
        Architecture.X64 => "SimplyDraw-win-x64.exe",
        _ => ""
    };

    public Version? InstalledVersion()
    {
        if (!File.Exists(AppPath))
            return null;

        var info = FileVersionInfo.GetVersionInfo(AppPath);
        return ParseVersion(string.IsNullOrWhiteSpace(info.FileVersion) ? info.ProductVersion : info.FileVersion);
    }

    public async Task<Release> LatestAsync(CancellationToken cancellation)
    {
        using var client = CreateClient(githubApi: true);
        using var response = await client.GetAsync(
            $"https://api.github.com/repos/{Repository}/releases/latest",
            cancellation);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        var release = await JsonSerializer.DeserializeAsync<Release>(stream, JsonOptions, cancellation)
            ?? throw new InvalidDataException("GitHub returned an empty release.");
        if (string.IsNullOrWhiteSpace(release.TagName))
            throw new InvalidDataException("The latest GitHub release has no version tag.");
        return release;
    }

    public static ReleaseAsset RequireAsset(Release release, string assetName)
    {
        var asset = release.Assets.FirstOrDefault(item =>
            string.Equals(item.Name, assetName, StringComparison.OrdinalIgnoreCase));
        if (asset == null || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            throw new InvalidDataException($"The latest release does not include {assetName}.");
        return asset;
    }

    public async Task DownloadAsync(string url, IProgress<double>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(InstallDirectory);
        var partial = AppPath + ".partial";
        try
        {
            using var client = CreateClient(githubApi: false);
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[128 * 1024];
                long received = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    received += read;
                    if (total > 0)
                        progress?.Report(Math.Clamp(received / (double)total, 0, 1));
                }
            }

            if (new FileInfo(partial).Length < 1024 * 1024)
                throw new InvalidDataException("The download was incomplete.");

            File.Move(partial, AppPath, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    public void InstallShortcuts()
    {
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
        CreateShortcut(Path.Combine(programs, "SimplyDraw.lnk"), AppPath);
        var setup = File.Exists(SetupPath) ? SetupPath : Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(setup))
            CreateShortcut(Path.Combine(programs, "Update SimplyDraw.lnk"), setup);
    }

    public void CopySetupBesideApp()
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current) || string.Equals(current, SetupPath, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(InstallDirectory);
        File.Copy(current, SetupPath, overwrite: true);
    }

    public bool IsInstalledAppRunning()
    {
        foreach (var process in Process.GetProcessesByName("SimplyDraw"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path != null && string.Equals(path, AppPath, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        return false;
    }

    public async Task<bool> TryCloseInstalledAppAsync()
    {
        var processes = new List<Process>();
        foreach (var process in Process.GetProcessesByName("SimplyDraw"))
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (path != null && string.Equals(path, AppPath, StringComparison.OrdinalIgnoreCase))
                    processes.Add(process);
                else
                    process.Dispose();
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                process.Dispose();
            }
        }

        if (processes.Count == 0)
            return true;

        foreach (var process in processes)
            process.CloseMainWindow();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && processes.Any(process => !process.HasExited))
            await Task.Delay(200);

        var closed = processes.All(process => process.HasExited);
        foreach (var process in processes)
            process.Dispose();
        return closed;
    }

    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var cleaned = text.Trim().TrimStart('v', 'V');
        var dash = cleaned.IndexOf('-');
        if (dash > 0)
            cleaned = cleaned[..dash];

        if (!Version.TryParse(cleaned, out var version))
            return null;

        return new Version(
            version.Major,
            version.Minor,
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }

    public static bool SameVersion(Version? installed, Version? latest) =>
        installed != null && latest != null && installed.CompareTo(latest) == 0;

    private static HttpClient CreateClient(bool githubApi)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SimplyDrawSetup", "0.1"));
        if (githubApi)
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static void CreateShortcut(string shortcutPath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null)
            return;

        var shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetPath;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
            shortcut.IconLocation = targetPath;
            shortcut.Save();
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class Release
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<ReleaseAsset> Assets { get; set; } = new();
}

public sealed class ReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
