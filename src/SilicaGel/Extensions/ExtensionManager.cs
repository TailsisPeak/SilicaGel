using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace SilicaGel;

public sealed class LoadedExtension
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public string Description { get; set; } = "";
    public string Path { get; set; } = "";
    /// <summary>"native" (.NET dll) or "vscode" (package.json).</summary>
    public string Kind { get; set; } = "vscode";
    public string Status { get; set; } = "loaded";
    public List<string> Contributions { get; set; } = new();
    public ISilicaGelExtension? Native { get; set; }
    public ExtensionManifest? Manifest { get; set; }
}

/// <summary>
/// Discovers and loads every extension under <see cref="AppSettings.ExtensionsDir"/>.
/// Two flavors are supported:
///   • Native — any .dll exporting a class implementing ISilicaGelExtension
///   • VS Code — any folder containing a package.json; themes, snippets, and
///     languages declared in `contributes` are honored. JavaScript activation
///     is intentionally skipped (we are not a Node host).
/// </summary>
public sealed class ExtensionManager
{
    private readonly AppSettings _settings;
    private readonly Action<string> _log;
    private readonly ExtensionContext _ctx;

    public List<LoadedExtension> Extensions { get; } = new();
    public Dictionary<string, List<(string Trigger, string Body)>> Snippets { get; } = new();
    public Dictionary<string, List<(string Text, string Desc)>>    Completions { get; } = new();
    public List<Action<string>> RunHandlers { get; } = new();

    public ExtensionManager(AppSettings settings, Action<string> log, Env? globalEnv = null)
    {
        _settings = settings;
        _log = log;
        _ctx = new ExtensionContext(this, globalEnv);
    }

    public void ReloadAll()
    {
        Extensions.Clear();
        Snippets.Clear();
        Completions.Clear();
        RunHandlers.Clear();

        var dir = string.IsNullOrEmpty(_settings.ExtensionsDir) ? AppSettings.DefaultExtDir : _settings.ExtensionsDir;
        Directory.CreateDirectory(dir);
        _log($"Scanning extensions in {dir}");

        foreach (var sub in Directory.GetDirectories(dir))
            TryLoadFolder(sub);

        foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories))
            TryLoadDll(dll);

        _log($"Loaded {Extensions.Count} extension(s).");
    }

    private void TryLoadFolder(string folder)
    {
        var pkg = Path.Combine(folder, "package.json");
        if (!File.Exists(pkg)) return;
        try
        {
            var json = File.ReadAllText(pkg);
            var m = JsonSerializer.Deserialize<ExtensionManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (m == null) return;
            var ext = new LoadedExtension
            {
                Id = m.Publisher.Length > 0 ? $"{m.Publisher}.{m.Name}" : m.Name,
                DisplayName = string.IsNullOrEmpty(m.DisplayName) ? m.Name : m.DisplayName,
                Publisher = m.Publisher,
                Version = m.Version,
                Description = m.Description,
                Path = folder,
                Kind = "vscode",
                Manifest = m,
            };
            ApplyContributions(ext, folder);
            Extensions.Add(ext);
            _log($"  ✓ {ext.DisplayName} v{ext.Version} (vscode)  {string.Join(", ", ext.Contributions)}");
        }
        catch (Exception ex) { _log($"  ✗ {Path.GetFileName(folder)}: {ex.Message}"); }
    }

    private void ApplyContributions(LoadedExtension ext, string folder)
    {
        var c = ext.Manifest?.Contributes;
        if (c == null) return;

        if (c.Languages != null)
            foreach (var l in c.Languages)
                ext.Contributions.Add($"language:{l.Id}");

        if (c.Grammars != null)
            foreach (var g in c.Grammars)
                ext.Contributions.Add($"grammar:{g.Language}");

        if (c.Themes != null)
            foreach (var t in c.Themes)
                ext.Contributions.Add($"theme:{t.Label}");

        if (c.Snippets != null)
            foreach (var sn in c.Snippets)
            {
                var p = Path.Combine(folder, sn.Path.TrimStart('.', '/', '\\'));
                if (!File.Exists(p)) continue;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(p));
                    var lang = NormalizeLang(sn.Language);
                    if (!Snippets.TryGetValue(lang, out var list)) Snippets[lang] = list = new();
                    foreach (var prop in doc.RootElement.EnumerateObject())
                    {
                        var name = prop.Name;
                        string trigger = prop.Value.TryGetProperty("prefix", out var pr) ? pr.ValueKind == JsonValueKind.String ? pr.GetString() ?? name : name : name;
                        string body = "";
                        if (prop.Value.TryGetProperty("body", out var b))
                        {
                            if (b.ValueKind == JsonValueKind.Array)
                                body = string.Join("\n", b.EnumerateArray().Select(x => x.GetString() ?? ""));
                            else body = b.GetString() ?? "";
                        }
                        list.Add((trigger, body));
                        // also surface the snippet trigger as a completion item
                        if (!Completions.TryGetValue(lang, out var cl)) Completions[lang] = cl = new();
                        cl.Add((trigger, $"snippet from {ext.DisplayName}"));
                    }
                    ext.Contributions.Add($"snippets:{sn.Language} ({Snippets[lang].Count})");
                }
                catch { /* skip malformed */ }
            }
    }

    private static string NormalizeLang(string lang) => lang.ToLowerInvariant() switch
    {
        "gel" => "Gel",
        "silica" or "sil" => "Silica",
        _ => lang,
    };

    private void TryLoadDll(string dll)
    {
        try
        {
            var asm = Assembly.LoadFrom(dll);
            foreach (var t in asm.GetTypes())
            {
                if (!typeof(ISilicaGelExtension).IsAssignableFrom(t) || t.IsAbstract) continue;
                var inst = (ISilicaGelExtension?)Activator.CreateInstance(t);
                if (inst == null) continue;
                var ext = new LoadedExtension
                {
                    Id = inst.Id, DisplayName = inst.DisplayName, Version = inst.Version,
                    Description = inst.Description, Path = dll, Kind = "native", Native = inst,
                };
                inst.Activate(_ctx);
                ext.Status = "active";
                Extensions.Add(ext);
                _log($"  ✓ {inst.DisplayName} v{inst.Version} (native)");
            }
        }
        catch (Exception ex) { _log($"  ✗ {Path.GetFileName(dll)}: {ex.Message}"); }
    }

    private sealed class ExtensionContext : IExtensionContext
    {
        private readonly ExtensionManager _m;
        private readonly Env? _env;
        public ExtensionContext(ExtensionManager m, Env? env) { _m = m; _env = env; }
        public void Log(string line) => _m._log(line);
        public void RegisterCompletion(string language, string text, string description)
        {
            if (!_m.Completions.TryGetValue(language, out var l)) _m.Completions[language] = l = new();
            l.Add((text, description));
        }
        public void RegisterSnippet(string language, string trigger, string body)
        {
            if (!_m.Snippets.TryGetValue(language, out var l)) _m.Snippets[language] = l = new();
            l.Add((trigger, body));
        }
        public void RegisterFunction(string name, Func<object?[], object?> impl)
            => _env?.DeclareLocal(name, impl);
        public void OnRun(Action<string> handler) => _m.RunHandlers.Add(handler);
    }
}
