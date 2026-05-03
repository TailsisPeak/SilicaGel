using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SilicaGel;

/// <summary>
/// Standard library + OS bindings registered into the interpreter's global env.
/// All functions are exposed under both flat names ("print") and dotted paths
/// ("file.read") so both Gel and Silica syntaxes can call them naturally.
/// </summary>
public static class Stdlib
{
    public static void Register(Env g, TextWriter output)
    {
        Func<object?[], object?> Fn(Func<object?[], object?> f) => f;

        // ── Console / output ───────────────────────────────────────────────────
        g.DeclareLocal("print",       Fn(a => { output.WriteLine(string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("print.log",   Fn(a => { File.AppendAllText("silica.log", string.Join(" ", a.Select(Interpreter.Stringify)) + Environment.NewLine); return null; }));
        g.DeclareLocal("print.console", Fn(a => { output.WriteLine(string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("read.line",   Fn(_ => Console.ReadLine()));
        g.DeclareLocal("typeof",      Fn(a => TypeNameOf(a.Length == 0 ? null : a[0])));
        g.DeclareLocal("convert",     Fn(a => Convert(a)));
        g.DeclareLocal("quit",        Fn(a => throw new QuitSignal(a.Length > 0 ? Interpreter.Stringify(a[0]) : null)));

        // ── Math ───────────────────────────────────────────────────────────────
        g.DeclareLocal("sqrt",   Fn(a => Math.Sqrt(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("abs",    Fn(a => Math.Abs(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("floor",  Fn(a => Math.Floor(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("ceil",   Fn(a => Math.Ceiling(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("round",  Fn(a => Math.Round(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("clamp",  Fn(a => Math.Clamp(Interpreter.ToDouble(a[0]), Interpreter.ToDouble(a[1]), Interpreter.ToDouble(a[2]))));
        g.DeclareLocal("sin",    Fn(a => Math.Sin(Interpreter.ToDouble(a[0]) * Math.PI / 180)));
        g.DeclareLocal("cos",    Fn(a => Math.Cos(Interpreter.ToDouble(a[0]) * Math.PI / 180)));
        g.DeclareLocal("tan",    Fn(a => Math.Tan(Interpreter.ToDouble(a[0]) * Math.PI / 180)));
        g.DeclareLocal("arcsin", Fn(a => Math.Asin(Interpreter.ToDouble(a[0])) * 180 / Math.PI));
        g.DeclareLocal("arccos", Fn(a => Math.Acos(Interpreter.ToDouble(a[0])) * 180 / Math.PI));
        g.DeclareLocal("arctan", Fn(a => Math.Atan(Interpreter.ToDouble(a[0])) * 180 / Math.PI));
        g.DeclareLocal("arctan2",Fn(a => Math.Atan2(Interpreter.ToDouble(a[0]), Interpreter.ToDouble(a[1])) * 180 / Math.PI));
        g.DeclareLocal("log",    Fn(a => Math.Log(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("log.base10", Fn(a => Math.Log10(Interpreter.ToDouble(a[0]))));
        g.DeclareLocal("pow",    Fn(a => Math.Pow(Interpreter.ToDouble(a[0]), Interpreter.ToDouble(a[1]))));
        g.DeclareLocal("rem",    Fn(a => Interpreter.ToDouble(a[0]) % Interpreter.ToDouble(a[1])));
        g.DeclareLocal("percent",Fn(a => Interpreter.ToDouble(a[0]) / 100.0 * Interpreter.ToDouble(a[1])));
        g.DeclareLocal("range",  Fn(a => Range(a)));
        g.DeclareLocal("pi",     Math.PI);
        g.DeclareLocal("euler",  Math.E);

        // ── File I/O ───────────────────────────────────────────────────────────
        g.DeclareLocal("file.read",   Fn(a => File.ReadAllText(Interpreter.Stringify(a[0]))));
        g.DeclareLocal("file.write",  Fn(a => { File.WriteAllText(Interpreter.Stringify(a[0]), Interpreter.Stringify(a[1])); return null; }));
        g.DeclareLocal("file.append", Fn(a => { File.AppendAllText(Interpreter.Stringify(a[0]), Interpreter.Stringify(a[1])); return null; }));
        g.DeclareLocal("file.delete", Fn(a => { File.Delete(Interpreter.Stringify(a[0])); return null; }));
        g.DeclareLocal("file.exists", Fn(a => File.Exists(Interpreter.Stringify(a[0]))));
        g.DeclareLocal("file.list",   Fn(a => Directory.GetFiles(Interpreter.Stringify(a[0])).Cast<object?>().ToList()));
        g.DeclareLocal("dir.create",  Fn(a => { Directory.CreateDirectory(Interpreter.Stringify(a[0])); return null; }));
        g.DeclareLocal("dir.exists",  Fn(a => Directory.Exists(Interpreter.Stringify(a[0]))));

        // ── Process / OS ───────────────────────────────────────────────────────
        g.DeclareLocal("env.get",     Fn(a => Environment.GetEnvironmentVariable(Interpreter.Stringify(a[0]))));
        g.DeclareLocal("env.set",     Fn(a => { Environment.SetEnvironmentVariable(Interpreter.Stringify(a[0]), Interpreter.Stringify(a[1])); return null; }));
        g.DeclareLocal("os.name",     Fn(_ => Environment.OSVersion.Platform.ToString()));
        g.DeclareLocal("os.user",     Fn(_ => Environment.UserName));
        g.DeclareLocal("os.cwd",      Fn(_ => Directory.GetCurrentDirectory()));
        g.DeclareLocal("os.args",     Fn(_ => Environment.GetCommandLineArgs().Cast<object?>().ToList()));
        g.DeclareLocal("process.exec", Fn(a =>
        {
            var psi = new ProcessStartInfo { FileName = Interpreter.Stringify(a[0]), RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            if (a.Length > 1) psi.Arguments = Interpreter.Stringify(a[1]);
            using var p = Process.Start(psi);
            return p?.StandardOutput.ReadToEnd() ?? "";
        }));
        g.DeclareLocal("process.shell", Fn(a =>
        {
            var cmd = Interpreter.Stringify(a[0]);
            var psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo("cmd.exe", "/c " + cmd) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true }
                : new ProcessStartInfo("/bin/sh", "-c \"" + cmd.Replace("\"","\\\"") + "\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            return p?.StandardOutput.ReadToEnd() ?? "";
        }));

        // ── Time ───────────────────────────────────────────────────────────────
        g.DeclareLocal("time.now",   Fn(_ => (double)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        g.DeclareLocal("time.sleep", Fn(a => { System.Threading.Thread.Sleep((int)Interpreter.ToDouble(a[0])); return null; }));
        g.DeclareLocal("time.iso",   Fn(_ => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));

        // ── txt operations ─────────────────────────────────────────────────────
        g.DeclareLocal("txt.length", Fn(a => (double)Interpreter.Stringify(a[0]).Length));
        g.DeclareLocal("txt.split",  Fn(a => Interpreter.Stringify(a[0]).Split(Interpreter.Stringify(a[1])).Cast<object?>().ToList()));
        g.DeclareLocal("txt.trim",   Fn(a => Interpreter.Stringify(a[0]).Trim()));
        g.DeclareLocal("txt.upper",  Fn(a => Interpreter.Stringify(a[0]).ToUpper()));
        g.DeclareLocal("txt.lower",  Fn(a => Interpreter.Stringify(a[0]).ToLower()));
        g.DeclareLocal("txt.replace",Fn(a => Interpreter.Stringify(a[0]).Replace(Interpreter.Stringify(a[1]), Interpreter.Stringify(a[2]))));

        // ── Engine API stubs (so engine-targeting scripts at least don't crash) ─
        g.DeclareLocal("Time.DeltaTime", 0.016);
        g.DeclareLocal("Time.ElapsedTime", 0.0);
        g.DeclareLocal("Log.Info",  Fn(a => { output.WriteLine("[info] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("Log.Warn",  Fn(a => { output.WriteLine("[warn] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("Log.Error", Fn(a => { output.WriteLine("[error] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
    }

    private static string TypeNameOf(object? v) => v switch
    {
        null => "none",
        bool => "bool",
        double d => d == Math.Floor(d) ? "int" : "decimal",
        string => "txt",
        List<object?> => "array",
        Dictionary<string, object?> => "group",
        FuncValue => "func",
        _ => v.GetType().Name
    };

    private static object? Convert(object?[] a)
    {
        if (a.Length < 2) return null;
        var v = a[0]; var to = Interpreter.Stringify(a[1]);
        return to switch
        {
            "int" or "int8" or "int16" or "int32" => (double)(long)Interpreter.ToDouble(v),
            "decimal" or "decimal32" or "decimal128" or "exact" => Interpreter.ToDouble(v),
            "txt" or "string" => Interpreter.Stringify(v),
            "bool" => Interpreter.Truthy(v),
            _ => v
        };
    }

    private static object? Range(object?[] a)
    {
        double s = a.Length > 1 ? Interpreter.ToDouble(a[0]) : 0;
        double e = a.Length > 1 ? Interpreter.ToDouble(a[1]) : Interpreter.ToDouble(a[0]);
        var list = new List<object?>();
        if (s <= e) for (double i = s; i < e; i++) list.Add(i);
        else for (double i = s; i > e; i--) list.Add(i);
        return list;
    }

}
