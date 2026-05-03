namespace Axion.Scripting;

public enum AxionLanguage { Gel, Silica, Blocks, CSharp }

/// <summary>Public façade for cross-language conversion. Goes through the shared AST.</summary>
public static class LanguageConverter
{
    public static ProgramNode Parse(string source, AxionLanguage lang) => lang switch
    {
        AxionLanguage.Gel    => Gel.Parse(source),
        AxionLanguage.Silica => Silica.Parse(source),
        AxionLanguage.Blocks => BlockProgram.FromJson(source).ToAst(),
        _ => throw new System.ArgumentException("Cannot parse C# (use Roslyn directly)."),
    };

    public static string Emit(ProgramNode ast, AxionLanguage lang, CSharpEmitter.Options? opts = null) => lang switch
    {
        AxionLanguage.Gel    => Gel.Emit(ast),
        AxionLanguage.Silica => Silica.Emit(ast),
        AxionLanguage.CSharp => CSharpEmitter.Emit(ast, opts),
        AxionLanguage.Blocks => "[/* block JSON not yet emitted from AST — round-trip from Blocks→AST→Blocks not lossless */]",
        _ => "",
    };

    /// <summary>Convenience: source-to-source conversion.</summary>
    public static string Convert(string source, AxionLanguage from, AxionLanguage to, CSharpEmitter.Options? opts = null)
        => Emit(Parse(source, from), to, opts);

    public static AxionLanguage DetectFromExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".gel"          => AxionLanguage.Gel,
            ".sil"          => AxionLanguage.Silica,
            ".blocks" or ".blocks.json" => AxionLanguage.Blocks,
            ".cs"           => AxionLanguage.CSharp,
            _               => AxionLanguage.Gel,
        };
    }
}
