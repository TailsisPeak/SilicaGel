using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SilicaGel;

/// <summary>
/// Persisted IDE settings — stored as JSON in
///   %APPDATA%/SilicaGel/settings.json   (Windows)
///   ~/.config/SilicaGel/settings.json    (Linux/macOS)
/// Mirrors the most common VS Code settings keys so VS Code users feel at home.
/// </summary>
public sealed class AppSettings
{
    [JsonPropertyName("editor.fontFamily")]   public string FontFamily   { get; set; } = "Cascadia Code, Consolas, Menlo, monospace";
    [JsonPropertyName("editor.fontSize")]     public double FontSize     { get; set; } = 13;
    [JsonPropertyName("editor.tabSize")]      public int    TabSize      { get; set; } = 4;
    [JsonPropertyName("editor.wordWrap")]     public bool   WordWrap     { get; set; } = false;
    [JsonPropertyName("editor.lineNumbers")]  public bool   LineNumbers  { get; set; } = true;
    [JsonPropertyName("editor.minimap")]      public bool   Minimap      { get; set; } = false;
    [JsonPropertyName("editor.autoSave")]     public bool   AutoSave     { get; set; } = false;
    [JsonPropertyName("workbench.colorTheme")]public string Theme        { get; set; } = "Dark+ (default dark)";
    [JsonPropertyName("workbench.iconTheme")] public string IconTheme    { get; set; } = "default";
    [JsonPropertyName("workbench.zoomLevel")] public int    ZoomLevel    { get; set; } = 0;
    [JsonPropertyName("extensions.dir")]      public string ExtensionsDir{ get; set; } = "";
    [JsonPropertyName("silica.runOnSave")]    public bool   RunOnSave    { get; set; } = false;

    public static string ConfigDir
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                                                 Environment.SpecialFolderOption.Create);
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            var dir = Path.Combine(root, "SilicaGel");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string SettingsPath  => Path.Combine(ConfigDir, "settings.json");
    public static string DefaultExtDir => Path.Combine(ConfigDir, "extensions");

    private static readonly JsonSerializerOptions _opts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, _opts);
                if (s != null)
                {
                    if (string.IsNullOrEmpty(s.ExtensionsDir))
                        s.ExtensionsDir = DefaultExtDir;
                    return s;
                }
            }
        }
        catch { /* corrupted file → fall through to defaults */ }
        var def = new AppSettings { ExtensionsDir = DefaultExtDir };
        def.Save();
        return def;
    }

    public void Save()
    {
        try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, _opts)); }
        catch { /* ignore */ }
    }
}
