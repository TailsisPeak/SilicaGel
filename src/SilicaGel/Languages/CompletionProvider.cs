using System.Collections.Generic;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using Avalonia.Media;

namespace SilicaGel;

/// <summary>A single completion item for the Silica Gel completion popup.</summary>
public sealed class CompletionItem : ICompletionData
{
    public CompletionItem(string text, string description = "", string category = "")
    { Text = text; Description = description; Category = category; }

    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description { get; }
    public string Category { get; }
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, System.EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, Text);
}

/// <summary>Static catalog used to seed the completion popup for Gel and Silica.</summary>
public static class Completions
{
    public static IReadOnlyList<CompletionItem> ForGel { get; } = new[]
    {
        new CompletionItem("if",       "Conditional",     "keyword"),
        new CompletionItem("elif",     "Else-if",         "keyword"),
        new CompletionItem("else",     "Else branch",     "keyword"),
        new CompletionItem("while",    "While loop",      "keyword"),
        new CompletionItem("for",      "For-each loop",   "keyword"),
        new CompletionItem("func",     "Function",        "keyword"),
        new CompletionItem("return",   "Return value",    "keyword"),
        new CompletionItem("import",   "Import module",   "keyword"),
        new CompletionItem("print",    "print(...)",      "builtin"),
        new CompletionItem("Time.DeltaTime",  "Frame Δt",       "engine"),
        new CompletionItem("Time.ElapsedTime","Time since start","engine"),
        new CompletionItem("Input.GetKey",    "Key held?",      "engine"),
        new CompletionItem("Input.GetAxis",   "Axis -1..1",     "engine"),
        new CompletionItem("Transform.Position","World position","engine"),
        new CompletionItem("Transform.Rotate", "Rotate by Vec",  "engine"),
        new CompletionItem("Vec.V",       "Vec.V(x,y,z)",       "engine"),
        new CompletionItem("Mathf.Sin",   "Sine",               "engine"),
        new CompletionItem("Mathf.Cos",   "Cosine",             "engine"),
        new CompletionItem("Log.Info",    "Info log",           "engine"),
    };

    public static IReadOnlyList<CompletionItem> ForSilica { get; } = new[]
    {
        // Control flow
        new CompletionItem("if",      "Conditional",  "keyword"),
        new CompletionItem("else",    "Else branch",  "keyword"),
        new CompletionItem("match",   "Pattern match","keyword"),
        new CompletionItem("while",   "While loop",   "keyword"),
        new CompletionItem("loop",    "Infinite loop","keyword"),
        new CompletionItem("for",     "For loop",     "keyword"),
        new CompletionItem("break",   "Exit loop",    "keyword"),
        new CompletionItem("continue","Skip iteration","keyword"),
        new CompletionItem("return",  "Return",       "keyword"),
        // Declarations
        new CompletionItem("func",    "Function",     "keyword"),
        new CompletionItem("group",   "Group type",   "keyword"),
        new CompletionItem("global",  "Global var",   "keyword"),
        new CompletionItem("import",  "Import",       "keyword"),
        // Operators
        new CompletionItem("is",      "Equal to",     "keyword"),
        new CompletionItem("is not",  "Not equal",    "keyword"),
        new CompletionItem("not",     "Logical NOT",  "keyword"),
        new CompletionItem("xor",     "Exclusive OR", "keyword"),
        // Types
        new CompletionItem("int",     "64-bit integer","type"),
        new CompletionItem("int8",    "8-bit integer", "type"),
        new CompletionItem("int16",   "16-bit integer","type"),
        new CompletionItem("int32",   "32-bit integer","type"),
        new CompletionItem("decimal", "64-bit decimal","type"),
        new CompletionItem("decimal32","32-bit decimal","type"),
        new CompletionItem("decimal128","128-bit decimal","type"),
        new CompletionItem("exact",   "Exact decimal","type"),
        new CompletionItem("bool",    "Boolean",      "type"),
        new CompletionItem("txt",     "Text",         "type"),
        new CompletionItem("bit",     "Single bit",   "type"),
        new CompletionItem("byte",    "8-bit value",  "type"),
        new CompletionItem("vector2", "2D vector",    "type"),
        new CompletionItem("vector3", "3D vector",    "type"),
        new CompletionItem("vector4", "4D vector",    "type"),
        new CompletionItem("matrix4", "4x4 matrix",   "type"),
        // Built-ins (stdlib)
        new CompletionItem("print",   "print(value)", "builtin"),
        new CompletionItem("print.log","Log to file", "builtin"),
        new CompletionItem("typeof",  "typeof(x)",    "builtin"),
        new CompletionItem("convert", "convert(x,t)", "builtin"),
        new CompletionItem("quit",    "Stop program", "builtin"),
        new CompletionItem("range",   "range(s,e)",   "builtin"),
        // Math
        new CompletionItem("sqrt",    "Square root",  "math"),
        new CompletionItem("abs",     "Absolute val", "math"),
        new CompletionItem("floor",   "Round down",   "math"),
        new CompletionItem("ceil",    "Round up",     "math"),
        new CompletionItem("round",   "Round nearest","math"),
        new CompletionItem("clamp",   "Clamp(x,a,b)", "math"),
        new CompletionItem("sin",     "Sine (deg)",   "math"),
        new CompletionItem("cos",     "Cosine (deg)", "math"),
        new CompletionItem("tan",     "Tangent (deg)","math"),
        new CompletionItem("pow",     "x^y",          "math"),
        new CompletionItem("log",     "Natural log",  "math"),
        new CompletionItem("pi",      "π constant",   "math"),
        new CompletionItem("euler",   "e constant",   "math"),
        // File / OS
        new CompletionItem("file.read",   "Read file",      "os"),
        new CompletionItem("file.write",  "Write file",     "os"),
        new CompletionItem("file.append", "Append to file", "os"),
        new CompletionItem("file.delete", "Delete file",    "os"),
        new CompletionItem("file.exists", "File exists?",   "os"),
        new CompletionItem("dir.create",  "Make directory", "os"),
        new CompletionItem("env.get",     "Read env var",   "os"),
        new CompletionItem("os.cwd",      "Current dir",    "os"),
        new CompletionItem("process.exec","Run program",    "os"),
        new CompletionItem("process.shell","Shell command", "os"),
        new CompletionItem("time.now",    "Unix ms",        "os"),
        new CompletionItem("time.sleep",  "Sleep ms",       "os"),
        // Engine API
        new CompletionItem("Time.DeltaTime",     "Frame Δt",        "engine"),
        new CompletionItem("Transform.Rotate",   "Rotate by Vec",   "engine"),
        new CompletionItem("Vec.V",              "Vec.V(x,y,z)",    "engine"),
    };
}
