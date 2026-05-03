using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Axion.Scripting;

namespace SilicaGel;

// ───────────────────────────────────────────────────────────────────────────────
// Standalone tree-walking interpreter for Gel and Silica AST.
// Runs scripts directly — no C# transpile, no external compiler needed.
// ───────────────────────────────────────────────────────────────────────────────

public sealed class InterpreterRuntimeException : Exception
{
    public int Line { get; }
    public InterpreterRuntimeException(string msg, int line) : base(msg) { Line = line; }
}

public sealed class Env
{
    public readonly Env? Parent;
    public readonly Dictionary<string, object?> Vars = new();
    public Env(Env? parent = null) { Parent = parent; }

    public object? Get(string name)
    {
        for (var e = this; e != null; e = e.Parent)
            if (e.Vars.TryGetValue(name, out var v)) return v;
        throw new InterpreterRuntimeException($"undefined: {name}", 0);
    }
    public bool TryGet(string name, out object? val)
    {
        for (var e = this; e != null; e = e.Parent)
            if (e.Vars.TryGetValue(name, out val)) return true;
        val = null; return false;
    }
    public void DeclareLocal(string name, object? value) => Vars[name] = value;
    public bool AssignExisting(string name, object? value)
    {
        for (var e = this; e != null; e = e.Parent)
            if (e.Vars.ContainsKey(name)) { e.Vars[name] = value; return true; }
        return false;
    }
}

public sealed class FuncValue
{
    public List<string> Params = new();
    public List<Stmt> Body = new();
    public Env Closure = null!;
    public string Name = "";
}

internal sealed class ReturnSignal : Exception { public object? Value; public ReturnSignal(object? v) { Value = v; } }
internal sealed class BreakSignal : Exception { }
internal sealed class ContinueSignal : Exception { }
public  sealed class QuitSignal : Exception { public string? Reason; public QuitSignal(string? r) { Reason = r; } }

public sealed class Interpreter
{
    private readonly TextWriter _out;
    private readonly Env _global = new();

    public Interpreter(TextWriter output)
    {
        _out = output;
        Stdlib.Register(_global, _out);
    }

    public void Run(ProgramNode prog)
    {
        // First pass: hoist function declarations so order doesn't matter
        foreach (var s in prog.Body)
            if (s is FuncDecl fd)
                _global.DeclareLocal(fd.Name, new FuncValue { Name = fd.Name, Params = fd.Params, Body = fd.Body, Closure = _global });

        try
        {
            foreach (var s in prog.Body)
                if (s is not FuncDecl) Exec(s, _global);

            // Auto-invoke main entry points if they exist
            foreach (var entry in new[] { "main", "start", "Main", "Start" })
                if (_global.TryGet(entry, out var v) && v is FuncValue f)
                {
                    CallFunction(f, Array.Empty<object?>(), 0);
                    break;
                }
        }
        catch (QuitSignal q)
        {
            if (!string.IsNullOrEmpty(q.Reason)) _out.WriteLine($"[quit] {q.Reason}");
        }
    }

    // ── Statements ─────────────────────────────────────────────────────────────
    private void Exec(Stmt s, Env env)
    {
        switch (s)
        {
            case CommentStmt: return;
            case ImportStmt: return; // imports not supported in interpreter (yet)
            case VarDecl v:
                {
                    var val = v.Value != null ? Eval(v.Value, env) : null;
                    if (v.IsGlobal) _global.DeclareLocal(v.Name, val);
                    else env.DeclareLocal(v.Name, val);
                    return;
                }
            case Assign a:
                {
                    var val = Eval(a.Value, env);
                    AssignTo(a.Target, val, env);
                    return;
                }
            case ExprStmt es: Eval(es.Expr, env); return;
            case ReturnStmt r:
                {
                    object? v = r.Values.Count == 0 ? null : Eval(r.Values[0], env);
                    throw new ReturnSignal(v);
                }
            case BreakStmt: throw new BreakSignal();
            case ContinueStmt: throw new ContinueSignal();
            case CrashStmt c: throw new QuitSignal(c.Reason != null ? Stringify(Eval(c.Reason, env)) : null);
            case IfStmt i:
                {
                    if (Truthy(Eval(i.Cond, env))) ExecBlock(i.Then, new Env(env));
                    else
                    {
                        bool matched = false;
                        foreach (var (ec, eb) in i.Elifs)
                            if (Truthy(Eval(ec, env))) { ExecBlock(eb, new Env(env)); matched = true; break; }
                        if (!matched && i.Else != null) ExecBlock(i.Else, new Env(env));
                    }
                    return;
                }
            case WhileStmt w:
                while (Truthy(Eval(w.Cond, env)))
                {
                    try { ExecBlock(w.Body, new Env(env)); }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { continue; }
                }
                return;
            case LoopStmt lp:
                while (true)
                {
                    try { ExecBlock(lp.Body, new Env(env)); }
                    catch (BreakSignal) { break; }
                    catch (ContinueSignal) { continue; }
                }
                return;
            case ForStmt f:
                {
                    var loopEnv = new Env(env);
                    if (f.Init != null) Exec(f.Init, loopEnv);
                    while (f.Cond == null || Truthy(Eval(f.Cond, loopEnv)))
                    {
                        try { ExecBlock(f.Body, new Env(loopEnv)); }
                        catch (BreakSignal) { goto endFor; }
                        catch (ContinueSignal) { /* fallthrough */ }
                        if (f.Step != null) Exec(f.Step, loopEnv);
                    }
                    endFor: return;
                }
            case ForEachStmt fe:
                {
                    var iter = Eval(fe.Iterable, env);
                    foreach (var item in ToEnumerable(iter, fe.Line))
                    {
                        var inner = new Env(env);
                        inner.DeclareLocal(fe.VarName, item);
                        try { ExecBlock(fe.Body, inner); }
                        catch (BreakSignal) { break; }
                        catch (ContinueSignal) { continue; }
                    }
                    return;
                }
            case FuncDecl fd:
                env.DeclareLocal(fd.Name, new FuncValue { Name = fd.Name, Params = fd.Params, Body = fd.Body, Closure = env });
                return;
            case MatchStmt m:
                {
                    var subj = Eval(m.Subject, env);
                    foreach (var (pat, body) in m.Cases)
                    {
                        var pv = Eval(pat, env);
                        if (Equal(subj, pv)) { ExecBlock(body, new Env(env)); return; }
                    }
                    if (m.Else != null) ExecBlock(m.Else, new Env(env));
                    return;
                }
        }
    }

    private void ExecBlock(List<Stmt> body, Env env)
    {
        foreach (var st in body) Exec(st, env);
    }

    private void AssignTo(Expr target, object? val, Env env)
    {
        switch (target)
        {
            case VarRef vr:
                if (!env.AssignExisting(vr.Name, val)) env.DeclareLocal(vr.Name, val);
                return;
            case IndexExpr ix:
                {
                    var t = Eval(ix.Target, env);
                    var k = Eval(ix.Index, env);
                    if (t is List<object?> list && k is double dn) { int i = (int)dn; while (list.Count <= i) list.Add(null); list[i] = val; return; }
                    if (t is Dictionary<string, object?> dict) { dict[Stringify(k)] = val; return; }
                    throw new InterpreterRuntimeException("cannot index-assign to that target", target.Line);
                }
            case MemberExpr me:
                {
                    var t = Eval(me.Target, env);
                    if (t is Dictionary<string, object?> dict) { dict[me.Member] = val; return; }
                    throw new InterpreterRuntimeException($"cannot assign member .{me.Member}", target.Line);
                }
        }
        throw new InterpreterRuntimeException("invalid assignment target", target.Line);
    }

    // ── Expressions ────────────────────────────────────────────────────────────
    private object? Eval(Expr e, Env env)
    {
        switch (e)
        {
            case NumLit n:  return double.Parse(n.Value, CultureInfo.InvariantCulture);
            case StrLit s:  return UnquoteString(s.Value);
            case BoolLit b: return b.Value;
            case NullLit:   return null;
            case VarRef v:  return env.Get(v.Name);
            case ListLit ll: return ll.Items.Select(x => Eval(x, env)).ToList();
            case RangeExpr r:
                {
                    var a = ToDouble(Eval(r.Start, env)); var b = ToDouble(Eval(r.End, env));
                    var list = new List<object?>();
                    if (a <= b) for (double i = a; i < b; i++) list.Add(i);
                    else for (double i = a; i > b; i--) list.Add(i);
                    return list;
                }
            case UnaryOp u:
                {
                    var v = Eval(u.Operand, env);
                    return u.Op switch
                    {
                        "-" => -ToDouble(v),
                        "!" => !Truthy(v),
                        _ => throw new InterpreterRuntimeException($"bad unary {u.Op}", u.Line)
                    };
                }
            case BinOp b: return EvalBin(b, env);
            case CallExpr c: return EvalCall(c, env);
            case MemberExpr m: return EvalMember(m, env);
            case IndexExpr ix:
                {
                    var t = Eval(ix.Target, env); var k = Eval(ix.Index, env);
                    if (t is List<object?> list && k is double dn) { int i = (int)dn; return (i >= 0 && i < list.Count) ? list[i] : null; }
                    if (t is string str && k is double sn) { int i = (int)sn; return (i >= 0 && i < str.Length) ? str[i].ToString() : ""; }
                    if (t is Dictionary<string, object?> dict) return dict.TryGetValue(Stringify(k), out var v) ? v : null;
                    throw new InterpreterRuntimeException("cannot index value", ix.Line);
                }
        }
        throw new InterpreterRuntimeException($"unknown expr {e.GetType().Name}", e.Line);
    }

    private object? EvalBin(BinOp b, Env env)
    {
        if (b.Op == "&&") return Truthy(Eval(b.Left, env)) && Truthy(Eval(b.Right, env));
        if (b.Op == "||") return Truthy(Eval(b.Left, env)) || Truthy(Eval(b.Right, env));

        var l = Eval(b.Left, env); var r = Eval(b.Right, env);
        switch (b.Op)
        {
            case "+":
                if (l is string ls || r is string rs) return Stringify(l) + Stringify(r);
                return ToDouble(l) + ToDouble(r);
            case "-": return ToDouble(l) - ToDouble(r);
            case "*": return ToDouble(l) * ToDouble(r);
            case "/": return ToDouble(l) / ToDouble(r);
            case "%": return ToDouble(l) % ToDouble(r);
            case "^": return Math.Pow(ToDouble(l), ToDouble(r));
            case "==": return Equal(l, r);
            case "!=": return !Equal(l, r);
            case "<":  return ToDouble(l) <  ToDouble(r);
            case ">":  return ToDouble(l) >  ToDouble(r);
            case "<=": return ToDouble(l) <= ToDouble(r);
            case ">=": return ToDouble(l) >= ToDouble(r);
        }
        throw new InterpreterRuntimeException($"bad op {b.Op}", b.Line);
    }

    private object? EvalCall(CallExpr c, Env env)
    {
        // Handle dotted built-in calls: e.g. file.read(...)  →  look up "file.read" first
        if (c.Callee is MemberExpr me && me.Target is VarRef tv)
        {
            var key = $"{tv.Name}.{me.Member}";
            if (env.TryGet(key, out var direct) && direct is Func<object?[], object?> bf)
                return bf(c.Args.Select(a => Eval(a, env)).ToArray());
            // Also try walking deeper (file.read.x)
        }
        var callee = Eval(c.Callee, env);
        var args = c.Args.Select(a => Eval(a, env)).ToArray();
        return CallValue(callee, args, c.Line);
    }

    private object? CallValue(object? callee, object?[] args, int line)
    {
        if (callee is Func<object?[], object?> fn) return fn(args);
        if (callee is FuncValue fv) return CallFunction(fv, args, line);
        throw new InterpreterRuntimeException("value is not callable", line);
    }

    public object? CallFunction(FuncValue fv, object?[] args, int line)
    {
        var inner = new Env(fv.Closure);
        for (int i = 0; i < fv.Params.Count; i++)
            inner.DeclareLocal(fv.Params[i], i < args.Length ? args[i] : null);
        try { ExecBlock(fv.Body, inner); }
        catch (ReturnSignal rs) { return rs.Value; }
        return null;
    }

    private object? EvalMember(MemberExpr me, Env env)
    {
        // dotted built-in lookup: try full path
        if (me.Target is VarRef tv)
        {
            var key = $"{tv.Name}.{me.Member}";
            if (env.TryGet(key, out var v)) return v;
        }
        var target = Eval(me.Target, env);
        if (target is Dictionary<string, object?> dict && dict.TryGetValue(me.Member, out var dv)) return dv;
        if (target is string s)
        {
            return me.Member switch
            {
                "length" => (double)s.Length,
                _ => throw new InterpreterRuntimeException($"unknown txt member .{me.Member}", me.Line)
            };
        }
        if (target is List<object?> list)
        {
            return me.Member switch
            {
                "length" => (double)list.Count,
                _ => throw new InterpreterRuntimeException($"unknown array member .{me.Member}", me.Line)
            };
        }
        throw new InterpreterRuntimeException($"cannot access .{me.Member}", me.Line);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────
    public static double ToDouble(object? v) => v switch
    {
        double d => d,
        int i => i,
        bool b => b ? 1 : 0,
        string s => double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var x) ? x : double.NaN,
        null => 0,
        _ => double.NaN
    };

    public static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true
    };

    public static bool Equal(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        if (a is double da && b is double db) return da == db;
        if (a is string sa && b is string sb) return sa == sb;
        return a.Equals(b);
    }

    public static string Stringify(object? v) => v switch
    {
        null => "none",
        bool b => b ? "true" : "false",
        double d => d == Math.Floor(d) && !double.IsInfinity(d) ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture),
        string s => s,
        List<object?> list => "[" + string.Join(", ", list.Select(Stringify)) + "]",
        _ => v.ToString() ?? ""
    };

    private static IEnumerable<object?> ToEnumerable(object? v, int line)
    {
        if (v is List<object?> list) return list;
        if (v is string s) return s.Select(c => (object?)c.ToString());
        throw new InterpreterRuntimeException("value is not iterable", line);
    }

    private static string UnquoteString(string raw)
    {
        if (raw.Length < 2) return raw;
        var inner = raw.Substring(1, raw.Length - 2);
        var sb = new StringBuilder();
        for (int i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                char c = inner[++i];
                sb.Append(c switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '\\' => '\\', '"' => '"', '\'' => '\'', _ => c });
            }
            else sb.Append(inner[i]);
        }
        return sb.ToString();
    }
}
