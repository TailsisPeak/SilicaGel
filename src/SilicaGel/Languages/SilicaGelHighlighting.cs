using System.IO;
using System.Reflection;
using System.Xml;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace SilicaGel;

/// <summary>Registers .gel and .sil syntax highlighting with AvaloniaEdit's manager at startup.</summary>
public static class SilicaGelHighlighting
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        Register("Gel",    new[] { ".gel" }, "SilicaGel.Languages.Gel.xshd");
        Register("Silica", new[] { ".sil" }, "SilicaGel.Languages.Silica.xshd");
    }

    private static void Register(string name, string[] extensions, string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null) return;
        using var reader = XmlReader.Create(stream);
        var def = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting(name, extensions, def);
    }
}
