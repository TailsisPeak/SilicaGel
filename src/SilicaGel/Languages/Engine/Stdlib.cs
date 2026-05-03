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

        // ── Gel input.* family ────────────────────────────────────────────────
        // Mirrors the standard Gel input system from gel-lang so the same .gel
        // script runs both standalone and inside the IDE.  The IDE host can
        // override these by re-declaring them on the global env after Register().
        g.DeclareLocal("input",         Fn(a => { if (a.Length > 0) Console.Write(Interpreter.Stringify(a[0])); return Console.ReadLine() ?? ""; }));
        g.DeclareLocal("input.line",    Fn(a => { if (a.Length > 0) Console.Write(Interpreter.Stringify(a[0])); return Console.ReadLine() ?? ""; }));
        g.DeclareLocal("input.number",  Fn(a => { if (a.Length > 0) Console.Write(Interpreter.Stringify(a[0])); var s = Console.ReadLine() ?? ""; return double.TryParse(s.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : double.NaN; }));
        g.DeclareLocal("input.int",     Fn(a => { if (a.Length > 0) Console.Write(Interpreter.Stringify(a[0])); var s = Console.ReadLine() ?? ""; return long.TryParse(s.Trim(), out var v) ? (double)v : 0d; }));
        g.DeclareLocal("input.bool",    Fn(a => { if (a.Length > 0) Console.Write(Interpreter.Stringify(a[0])); var s = (Console.ReadLine() ?? "").Trim().ToLowerInvariant(); return s is "y" or "yes" or "true" or "1" or "on"; }));
        g.DeclareLocal("input.confirm", Fn(a => { Console.Write((a.Length > 0 ? Interpreter.Stringify(a[0]) : "") + " (y/n) "); var s = (Console.ReadLine() ?? "").Trim().ToLowerInvariant(); return s is "y" or "yes" or "true" or "1" or "on"; }));
        g.DeclareLocal("input.key",     Fn(a => { var k = Console.ReadKey(intercept: a.Length > 0 && Interpreter.Truthy(a[0])); return k.KeyChar != '\0' ? k.KeyChar.ToString().ToUpperInvariant() : k.Key.ToString(); }));
        // The polling-style entries are stubs in the IDE host (no frame pump);
        // a host can override them after Register().
        g.DeclareLocal("input.keyDown",     Fn(_ => false));
        g.DeclareLocal("input.keyPressed",  Fn(_ => false));
        g.DeclareLocal("input.keyReleased", Fn(_ => false));
        g.DeclareLocal("input.axis",        Fn(_ => 0d));
        g.DeclareLocal("input.pointer.x",   Fn(_ => 0d));
        g.DeclareLocal("input.pointer.y",   Fn(_ => 0d));
        g.DeclareLocal("input.pointer.dx",  Fn(_ => 0d));
        g.DeclareLocal("input.pointer.dy",  Fn(_ => 0d));
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
        // The IDE has no engine running, so these are inert defaults.  Hosts that
        // embed the interpreter inside Axion can override any of them after Register().
        g.DeclareLocal("Time.DeltaTime", 0.016);
        g.DeclareLocal("Time.ElapsedTime", 0.0);
        g.DeclareLocal("Log.Info",  Fn(a => { output.WriteLine("[info] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("Log.Warn",  Fn(a => { output.WriteLine("[warn] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("Log.Error", Fn(a => { output.WriteLine("[error] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));

        // ── Gel game-engine surface (mirrors Axion's CSharpEmitter) ────────────
        // Mirrors keep the same .gel scripts authorable inside the IDE without an
        // engine attached.  All return inert defaults — runtime behavior comes from
        // the engine emitter when the script is compiled into a Behavior subclass.
        // log.*
        g.DeclareLocal("log.info",  Fn(a => { output.WriteLine("[info] "  + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("log.warn",  Fn(a => { output.WriteLine("[warn] "  + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        g.DeclareLocal("log.error", Fn(a => { output.WriteLine("[error] " + string.Join(" ", a.Select(Interpreter.Stringify))); return null; }));
        // time.*
        g.DeclareLocal("time.delta",        0.016);
        g.DeclareLocal("time.deltaTime",    0.016);
        g.DeclareLocal("time.elapsed",      0.0);
        g.DeclareLocal("time.elapsedTime",  0.0);
        g.DeclareLocal("time.frame",        0.0);
        g.DeclareLocal("time.frames",       0.0);
        g.DeclareLocal("time.scale",        1.0);
        g.DeclareLocal("time.fixedDelta",   1.0 / 60.0);
        // random.*
        var rng = new Random();
        g.DeclareLocal("random.value", Fn(_   => rng.NextDouble()));
        g.DeclareLocal("random.range", Fn(a   => rng.NextDouble() * (Interpreter.ToDouble(a[1]) - Interpreter.ToDouble(a[0])) + Interpreter.ToDouble(a[0])));
        g.DeclareLocal("random.int",   Fn(a   => (double)rng.Next((int)Interpreter.ToDouble(a[0]), (int)Interpreter.ToDouble(a[1]))));
        g.DeclareLocal("random.sign",  Fn(_   => rng.Next(2) == 0 ? -1d : 1d));
        // vec3 / vec2 / find / destroy / instantiate — IDE returns plain dictionaries
        // so the script stays well-typed under the interpreter; the engine emitter
        // replaces these with real System.Numerics.Vector / Scene calls.
        Func<double, double, double, Dictionary<string, object?>> mkV3 = (x, y, z) =>
            new Dictionary<string, object?> { ["x"] = x, ["y"] = y, ["z"] = z };
        g.DeclareLocal("vec3", Fn(a => (object)mkV3(
            a.Length > 0 ? Interpreter.ToDouble(a[0]) : 0,
            a.Length > 1 ? Interpreter.ToDouble(a[1]) : 0,
            a.Length > 2 ? Interpreter.ToDouble(a[2]) : 0)));
        g.DeclareLocal("vec2", Fn(a => (object)new Dictionary<string, object?>
        {
            ["x"] = a.Length > 0 ? Interpreter.ToDouble(a[0]) : 0,
            ["y"] = a.Length > 1 ? Interpreter.ToDouble(a[1]) : 0,
        }));
        g.DeclareLocal("find",        Fn(_ => null));
        g.DeclareLocal("destroy",     Fn(_ => null));
        g.DeclareLocal("instantiate", Fn(a => new Dictionary<string, object?>
        {
            ["name"] = a.Length > 0 ? Interpreter.Stringify(a[0]) : "GameObject",
            ["transform"] = mkV3(0, 0, 0),
        }));
        // transform / gameObject / scene — give a no-op surface so scripts targeting
        // the engine still parse + run inside the IDE for syntax/preview purposes.
        var inertTransform = new Dictionary<string, object?>
        {
            ["position"]      = mkV3(0, 0, 0),
            ["rotation"]      = mkV3(0, 0, 0),
            ["scale"]         = mkV3(1, 1, 1),
            ["eulerAngles"]   = mkV3(0, 0, 0),
            ["forward"]       = mkV3(0, 0, -1),
            ["right"]         = mkV3(1, 0, 0),
            ["up"]            = mkV3(0, 1, 0),
            ["translate"]     = (Func<object?[], object?>)(_ => null),
            ["rotate"]        = (Func<object?[], object?>)(_ => null),
            ["lookAt"]        = (Func<object?[], object?>)(_ => null),
            ["setPosition"]   = (Func<object?[], object?>)(_ => null),
            ["setScale"]      = (Func<object?[], object?>)(_ => null),
        };
        g.DeclareLocal("transform",  inertTransform);
        g.DeclareLocal("gameObject", new Dictionary<string, object?>
        {
            ["name"]      = "(preview)",
            ["active"]    = true,
            ["transform"] = inertTransform,
        });
        g.DeclareLocal("self",  new Dictionary<string, object?>());
        g.DeclareLocal("scene", new Dictionary<string, object?>
        {
            ["find"]   = (Func<object?[], object?>)(_ => null),
            ["create"] = (Func<object?[], object?>)(a => new Dictionary<string, object?> { ["name"] = a.Length > 0 ? Interpreter.Stringify(a[0]) : "GameObject" }),
            ["remove"] = (Func<object?[], object?>)(_ => null),
        });
        g.DeclareLocal("mouse", new Dictionary<string, object?>
        {
            ["position"] = mkV3(0, 0, 0),
            ["delta"]    = mkV3(0, 0, 0),
            ["scroll"]   = mkV3(0, 0, 0),
        });
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
