using System.Collections.Generic;

namespace SilicaGel;

/// <summary>
/// Public extension contract.  A native Silica Gel extension is a .NET class
/// implementing this interface in a .dll dropped into the extensions folder.
///
/// VS Code extensions (Node-based) cannot run their JavaScript here — but we
/// still recognize their package.json and load their themes, snippets, and
/// language declarations so a great deal of the VS Code marketplace works
/// for syntax/theming purposes.
/// </summary>
public interface ISilicaGelExtension
{
    string Id { get; }
    string DisplayName { get; }
    string Version { get; }
    string Description { get; }
    void Activate(IExtensionContext ctx);
    void Deactivate();
}

/// <summary>API surface available to native extensions.</summary>
public interface IExtensionContext
{
    /// <summary>Append a line to the IDE OUTPUT panel.</summary>
    void Log(string line);
    /// <summary>Register a completion suggestion that always shows up.</summary>
    void RegisterCompletion(string language, string text, string description);
    /// <summary>Register a snippet expansion.</summary>
    void RegisterSnippet(string language, string trigger, string body);
    /// <summary>Register a function in the script interpreter so scripts can call it.</summary>
    void RegisterFunction(string name, System.Func<object?[], object?> impl);
    /// <summary>Run an arbitrary callback whenever the user hits Run.</summary>
    void OnRun(System.Action<string> handler);
}
