using System;
using System.Collections.Generic;
using Axion.Scripting;

namespace SilicaGel;

/// <summary>
/// Validator for Gel and Silica producing diagnostics for the editor.
///   • brace / paren / bracket balance with line tracking
///   • unterminated string detection
///   • parser dry-run (catches exceptions and bad-AST)
///   • semantic pass: undefined identifier detection (catches typos like
///     `}asdasdasdasdasd` because the bare identifier isn't declared anywhere)
/// </summary>
public static class ScriptValidator
{
    private static readonly HashSet<string> Builtins = new()
    {
        // Stdlib functions
        "print","print.log","print.console","read.line","typeof","convert","quit",
        "sqrt","abs","floor","ceil","round","clamp","sin","cos","tan",
        "arcsin","arccos","arctan","arctan2","log","log.base10","pow","rem","percent","range",
        "pi","euler",
        "file.read","file.write","file.append","file.delete","file.exists","file.list",
        "dir.create","dir.exists",
        "env.get","env.set","os.name","os.user","os.cwd","os.args","process.exec","process.shell",
        "time.now","time.sleep","time.iso",
        "txt.length","txt.split","txt.trim","txt.upper","txt.lower","txt.replace",
        // Engine API
        "Time","Time.DeltaTime","Time.ElapsedTime",
        "Input","Input.GetKey","Input.GetAxis",
        "Transform","Transform.Position","Transform.Rotate","GameObject","Scene",
        "Vec","Vec.V","Mathf","Mathf.Sin","Mathf.Cos",
        "Log","Log.Info","Log.Warn","Log.Error",
        // Reserved identifiers / built-in values
        "true","false","none","null",
    };

    public static IReadOnlyList<Diagnostic> Validate(string source, string language)
    {
        var diags = new List<Diagnostic>();
        if (string.IsNullOrEmpty(source)) return diags;

        BalanceCheck(source, diags);

        ProgramNode? prog = null;
        try
        {
            prog = language switch
            {
                "Gel"    => Gel.Parse(source),
                "Silica" => SilicaV2.Parse(source),
                _ => null,
            };
        }
        catch (SilicaV2.ParseError pe)
        {
            diags.Add(new Diagnostic(pe.Line, pe.Col, 1, $"Silica: {pe.Message}", DiagnosticSeverity.Error));
        }
        catch (Exception ex)
        {
            diags.Add(new Diagnostic(1, 1, 1, $"Parse: {ex.Message}", DiagnosticSeverity.Error));
        }

        if (prog != null) SemanticCheck(prog, diags);

        return diags;
    }

    // ── Brace balance + unterminated strings ──────────────────────────────────
    private static void BalanceCheck(string source, List<Diagnostic> diags)
    {
        var stack = new Stack<(char ch, int line, int col)>();
        int line = 1, col = 1;
        bool inStr = false; char strQuote = '\0'; int strLine = 1, strCol = 1;
        bool inLineComment = false;

        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\n')
            {
                if (inStr) { diags.Add(new Diagnostic(strLine, strCol, 1, "Unterminated string literal.", DiagnosticSeverity.Error)); inStr = false; }
                inLineComment = false; line++; col = 1; continue;
            }
            if (inLineComment) { col++; continue; }
            if (inStr)
            {
                if (c == '\\' && i + 1 < source.Length) { i++; col += 2; continue; }
                if (c == strQuote) inStr = false;
                col++; continue;
            }
            if (c == '#' || (c == '/' && i + 1 < source.Length && source[i+1] == '/')) { inLineComment = true; col++; continue; }
            if (c == '"' || c == '\'') { inStr = true; strQuote = c; strLine = line; strCol = col; col++; continue; }
            if (c == '(' || c == '[' || c == '{') stack.Push((c, line, col));
            else if (c == ')' || c == ']' || c == '}')
            {
                if (stack.Count == 0) diags.Add(new Diagnostic(line, col, 1, $"Unmatched '{c}'.", DiagnosticSeverity.Error));
                else
                {
                    var top = stack.Pop();
                    char want = top.ch switch { '(' => ')', '[' => ']', '{' => '}', _ => ' ' };
                    if (c != want) diags.Add(new Diagnostic(line, col, 1, $"Mismatched bracket: expected '{want}' for '{top.ch}' at line {top.line}.", DiagnosticSeverity.Error));
                }
            }
            col++;
        }
        if (inStr) diags.Add(new Diagnostic(strLine, strCol, 1, "Unterminated string literal at end of file.", DiagnosticSeverity.Error));
        foreach (var (ch, ln, cl) in stack)
            diags.Add(new Diagnostic(ln, cl, 1, $"Unclosed '{ch}'.", DiagnosticSeverity.Error));
    }

    // ── Semantic pass: undefined identifier detection ─────────────────────────
    private static void SemanticCheck(ProgramNode prog, List<Diagnostic> diags)
    {
        var declared = new HashSet<string>(Builtins);
        // First pass: collect all top-level decls
        foreach (var s in prog.Body) Collect(s, declared);

        // Second pass: scan bodies for unknown VarRefs
        foreach (var s in prog.Body) Walk(s, declared, diags, new HashSet<string>(declared));
    }

    private static void Collect(Stmt s, HashSet<string> set)
    {
        switch (s)
        {
            case FuncDecl f: set.Add(f.Name); break;
            case VarDecl v: set.Add(v.Name); break;
        }
    }

    private static void Walk(Stmt s, HashSet<string> globals, List<Diagnostic> diags, HashSet<string> scope)
    {
        switch (s)
        {
            case VarDecl v:
                if (v.Value != null) WalkExpr(v.Value, scope, diags);
                scope.Add(v.Name); break;
            case Assign a:
                WalkExpr(a.Value, scope, diags);
                if (a.Target is VarRef vr) scope.Add(vr.Name);
                else WalkExpr(a.Target, scope, diags);
                break;
            case ExprStmt es: WalkExpr(es.Expr, scope, diags); break;
            case ReturnStmt r: foreach (var x in r.Values) WalkExpr(x, scope, diags); break;
            case CrashStmt c: if (c.Reason != null) WalkExpr(c.Reason, scope, diags); break;
            case IfStmt i:
                WalkExpr(i.Cond, scope, diags);
                foreach (var st in i.Then) Walk(st, globals, diags, new HashSet<string>(scope));
                foreach (var (ec, eb) in i.Elifs) { WalkExpr(ec, scope, diags); foreach (var st in eb) Walk(st, globals, diags, new HashSet<string>(scope)); }
                if (i.Else != null) foreach (var st in i.Else) Walk(st, globals, diags, new HashSet<string>(scope));
                break;
            case WhileStmt w:
                WalkExpr(w.Cond, scope, diags);
                foreach (var st in w.Body) Walk(st, globals, diags, new HashSet<string>(scope));
                break;
            case LoopStmt lp:
                foreach (var st in lp.Body) Walk(st, globals, diags, new HashSet<string>(scope));
                break;
            case ForStmt f:
                {
                    var inner = new HashSet<string>(scope);
                    if (f.Init != null) Walk(f.Init, globals, diags, inner);
                    if (f.Cond != null) WalkExpr(f.Cond, inner, diags);
                    if (f.Step != null) Walk(f.Step, globals, diags, inner);
                    foreach (var st in f.Body) Walk(st, globals, diags, inner);
                    break;
                }
            case ForEachStmt fe:
                WalkExpr(fe.Iterable, scope, diags);
                {
                    var inner = new HashSet<string>(scope);
                    inner.Add(fe.VarName);
                    foreach (var st in fe.Body) Walk(st, globals, diags, inner);
                    break;
                }
            case FuncDecl fd:
                {
                    var inner = new HashSet<string>(scope);
                    foreach (var p in fd.Params) inner.Add(p);
                    inner.Add(fd.Name);
                    foreach (var st in fd.Body) Walk(st, globals, diags, inner);
                    break;
                }
            case MatchStmt m:
                WalkExpr(m.Subject, scope, diags);
                foreach (var (pat, body) in m.Cases) { WalkExpr(pat, scope, diags); foreach (var st in body) Walk(st, globals, diags, new HashSet<string>(scope)); }
                if (m.Else != null) foreach (var st in m.Else) Walk(st, globals, diags, new HashSet<string>(scope));
                break;
        }
    }

    private static void WalkExpr(Expr e, HashSet<string> scope, List<Diagnostic> diags)
    {
        switch (e)
        {
            case VarRef v:
                if (!scope.Contains(v.Name) && !Builtins.Contains(v.Name) && !IsLikelyBuiltinPrefix(v.Name))
                    diags.Add(new Diagnostic(Math.Max(1, v.Line), 1, 1, $"Unknown identifier '{v.Name}'.", DiagnosticSeverity.Error));
                break;
            case BinOp b: WalkExpr(b.Left, scope, diags); WalkExpr(b.Right, scope, diags); break;
            case UnaryOp u: WalkExpr(u.Operand, scope, diags); break;
            case CallExpr c:
                // Allow direct member calls like file.read(...) — Callee will be a MemberExpr or VarRef
                if (c.Callee is MemberExpr) { /* member root checked below */ WalkExpr(((MemberExpr)c.Callee).Target, scope, diags); }
                else WalkExpr(c.Callee, scope, diags);
                foreach (var a in c.Args) WalkExpr(a, scope, diags);
                break;
            case MemberExpr m: WalkExpr(m.Target, scope, diags); break;
            case IndexExpr ix: WalkExpr(ix.Target, scope, diags); WalkExpr(ix.Index, scope, diags); break;
            case ListLit ll: foreach (var x in ll.Items) WalkExpr(x, scope, diags); break;
            case RangeExpr r: WalkExpr(r.Start, scope, diags); WalkExpr(r.End, scope, diags); break;
        }
    }

    private static bool IsLikelyBuiltinPrefix(string name)
    {
        // Allow root names of dotted built-ins (file, dir, env, os, time, txt, process, print, Time, Input, etc.)
        foreach (var b in Builtins)
        {
            int dot = b.IndexOf('.');
            if (dot > 0 && b.Substring(0, dot) == name) return true;
        }
        return false;
    }
}
