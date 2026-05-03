using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SilicaGel;

/// <summary>
/// VS Code package.json manifest — partial schema covering what we use:
/// metadata, language declarations, themes, snippets, and grammars.
/// Anything we don't understand is preserved in <see cref="Raw"/>.
/// </summary>
public sealed class ExtensionManifest
{
    [JsonPropertyName("name")]        public string Name        { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("publisher")]   public string Publisher   { get; set; } = "";
    [JsonPropertyName("version")]     public string Version     { get; set; } = "0.0.0";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("engines")]     public Dictionary<string, string>? Engines { get; set; }
    [JsonPropertyName("contributes")] public Contributes? Contributes { get; set; }
    [JsonPropertyName("main")]        public string? Main { get; set; }
}

public sealed class Contributes
{
    [JsonPropertyName("languages")] public List<LangDecl>?    Languages { get; set; }
    [JsonPropertyName("grammars")]  public List<GrammarDecl>? Grammars  { get; set; }
    [JsonPropertyName("themes")]    public List<ThemeDecl>?   Themes    { get; set; }
    [JsonPropertyName("snippets")]  public List<SnippetDecl>? Snippets  { get; set; }
}

public sealed class LangDecl
{
    [JsonPropertyName("id")]         public string Id { get; set; } = "";
    [JsonPropertyName("aliases")]    public List<string>? Aliases { get; set; }
    [JsonPropertyName("extensions")] public List<string>? Extensions { get; set; }
}

public sealed class GrammarDecl
{
    [JsonPropertyName("language")]  public string Language { get; set; } = "";
    [JsonPropertyName("scopeName")] public string ScopeName { get; set; } = "";
    [JsonPropertyName("path")]      public string Path { get; set; } = "";
}

public sealed class ThemeDecl
{
    [JsonPropertyName("label")]  public string Label  { get; set; } = "";
    [JsonPropertyName("uiTheme")]public string UiTheme{ get; set; } = "vs-dark";
    [JsonPropertyName("path")]   public string Path   { get; set; } = "";
}

public sealed class SnippetDecl
{
    [JsonPropertyName("language")] public string Language { get; set; } = "";
    [JsonPropertyName("path")]     public string Path     { get; set; } = "";
}
