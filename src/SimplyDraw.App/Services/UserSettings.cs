using System.Text.Json;

namespace SimplyDraw.Services;

public sealed class UserSettings
{
    public string BrushId { get; set; } = "ink";

    public double BrushSize { get; set; } = 5;

    public double BrushOpacity { get; set; } = 1;

    public double Smoothing { get; set; } = 0.48;

    public string Color { get; set; } = "#1C1917";

    public List<string> Palette { get; set; } = PaletteColors.Default();

    public List<string> RecentColors { get; set; } = new();

    public string Paper { get; set; } = "warm";

    public GridStyle Grid { get; set; } = GridStyle.Dots;

    public bool Snap { get; set; }

    public double GridSpacing { get; set; } = 32;

    public string TouchMode { get; set; } = "pan";

    public bool ShowHint { get; set; } = true;

    public double EraserRadius { get; set; } = 22;

    public bool EraserWholeStroke { get; set; }

    public string SelectStyle { get; set; } = "rectangle";

    public List<string> RecentFiles { get; set; } = new();

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 820;

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SimplyDraw");

    public static string SettingsPath => Path.Combine(Folder, "settings.json");

    public static string AutosavePath => Path.Combine(Folder, "autosave.sdraw");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new UserSettings();

            return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), Options) ?? new UserSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new UserSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, Options));
    }

    public void RememberFile(string path)
    {
        RecentFiles.RemoveAll(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > 8)
            RecentFiles.RemoveRange(8, RecentFiles.Count - 8);
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
}
