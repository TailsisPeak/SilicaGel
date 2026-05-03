using System;
using System.Collections.Generic;
using System.Text;

namespace Axion.Scripting;

// ───────────────────────────────────────────────────────────────────────────────
// Silica front-end: source → tokens → AST, and AST → source.
// Implements the Silica Language Specification (see /attached_assets/silica-language-spec*.md).
// ───────────────────────────────────────────────────────────────────────────────

public static class Silica
{
    private static readonly HashSet<string> Keywords = new()
    {
        "if","else","match","while","for","loop","in","do","break","continue",
        "func","return","import","global","quit","convert","range",
        "true","false","none","is","not","or","and",
        "int","decimal","exact","bool","string","txt","bit","byte",
    };

    public sealed record Token(string Type, string Value, int Line);

    public static List<Token> Lex(string source)
    {
        var t = new List<Token>(); int i = 0, line = 1;
        while (i < source.Length)
        {
            char ch = source[i];
            if (ch == '\n') { t.Add(new("NL", "\n", line)); i++; line++; continue; }
            if (ch == ' ' || ch == '\t' || ch == '\r') { i++; continue; }
            if (ch == '#') { var sb = new StringBuilder(); while (i < source.Length && source[i] != '\n') sb.Append(source[i++]); t.Add(new("COMMENT", sb.ToString(), line)); continue; }
            if (ch == '"') { var sb = new StringBuilder(); sb.Append('"'); i++; while (i < source.Length && source[i] != '"') { if (source[i] == '\\') sb.Append(source[i++]); sb.Append(source[i++]); } sb.Append('"'); i++; t.Add(new("STR", sb.ToString(), line)); continue; }
            if (char.IsDigit(ch)) { var sb = new StringBuilder(); while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) sb.Append(source[i++]); t.Add(new("NUM", sb.ToString(), line)); continue; }
            if (char.IsLetter(ch) || ch == '_') { var sb = new StringBuilder(); while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '.')) sb.Append(source[i++]); var v = sb.ToString(); t.Add(new(Keywords.Contains(v) ? "KW" : "ID", v, line)); continue; }
            if (i + 1 < source.Length)
            {
                var two = source.Substring(i, 2);
                if (two is "->" or "<=" or ">=" or "&&" or "||") { t.Add(new("OP", two, line)); i += 2; continue; }
            }
            if ("+-*/^<>=!&()[]{},.;:".IndexOf(ch) >= 0) { t.Add(new("OP", ch.ToString(), line)); i++; continue; }
            i++;
        }
        t.Add(new("EOF", "", line));
        return t;
    }

    public static ProgramNode Parse(string source) => new SilicaParser(Lex(source)).ParseProgram();

    private sealed class SilicaParser
    {
        private readonly List<Token> _t;
        private int _p;
        public SilicaParser(List<Token> tokens) { _t = tokens.FindAll(x => x.Type != "NL"); }
        private Token Peek(int o = 0) => _p + o < _t.Count ? _t[_p + o] : _t[^1];
        private Token Eat() => _t[_p++];
        private bool At(string v) => Peek().Value == v;
        private bool IsEOF() => Peek().Type == "EOF";
        private void Expect(string v) { if (Peek().Value == v) Eat(); }

        public ProgramNode ParseProgram()
        {
            var p = new ProgramNode();
            // Program flags (e.g. "errors.verbose")
            while (Peek().Type == "ID" && Peek().Value.Contains('.') && Peek().Value.StartsWith("errors."))
            { p.ProgramFlags.Add(Eat().Value); }
            while (!IsEOF()) { var s = ParseStmt(); if (s != null) p.Body.Add(s); }
            return p;
        }

        private Stmt? ParseStmt()
        {
            var tk = Peek();
            if (tk.Type == "COMMENT") { Eat(); return new CommentStmt { Text = tk.Value.TrimStart('#', ' '), Line = tk.Line }; }
            if (tk.Value == "import") { Eat(); return new ImportStmt { Path = Eat().Value, Line = tk.Line }; }
            if (tk.Value == "if")     return ParseIf();
            if (tk.Value == "while")  return ParseWhile();
            if (tk.Value == "loop")   { int line = Eat().Line; return new LoopStmt { Body = ParseBlock(), Line = line }; }
            if (tk.Value == "for")    return ParseFor();
            if (tk.Value == "match")  return ParseMatch();
            if (tk.Value == "func")   return ParseFunc();
            if (tk.Value == "return") return ParseReturn();
            if (tk.Value == "break")  { Eat(); return new BreakStmt { Line = tk.Line }; }
            if (tk.Value == "continue"){ Eat(); return new ContinueStmt { Line = tk.Line }; }
            if (tk.Value == "quit")   return ParseQuit();
            bool isGlobal = false;
            if (tk.Value == "global") { Eat(); isGlobal = true; tk = Peek(); }
            if (tk.Value is "int" or "decimal" or "exact" or "bool" or "string" or "txt" or "bit" or "byte")
                return ParseTypedDecl(isGlobal);
            // Plain expression / assignment
            var e = ParseExpr();
            if (At("=") && Peek(1).Value != "=") { Eat(); return new Assign { Target = e, Value = ParseExpr(), Line = tk.Line }; }
            return new ExprStmt { Expr = e, Line = tk.Line };
        }

        // Silica uses indentation in its spec, but to keep parsing consistent across IDE/runtime we
        // accept BOTH `{ ... }` blocks and an indentation-style with a single trailing line.
        // This implementation uses braces if present, else a single statement.
        private List<Stmt> ParseBlock()
        {
            var body = new List<Stmt>();
            if (At("{")) { Eat(); while (!IsEOF() && !At("}")) { var s = ParseStmt(); if (s != null) body.Add(s); } if (At("}")) Eat(); }
            else { var s = ParseStmt(); if (s != null) body.Add(s); }
            return body;
        }

        private Stmt ParseIf()
        {
            int line = Peek().Line; Expect("if");
            var cond = ParseExpr();
            // Silica syntax: `if x is 5` (no braces in spec). We accept both.
            var then = ParseBlock();
            var elifs = new List<(Expr, List<Stmt>)>();
            List<Stmt>? els = null;
            if (At("else")) { Eat();
                if (At("if")) { Eat(); var ec = ParseExpr(); var eb = ParseBlock(); elifs.Add((ec, eb)); }
                else els = ParseBlock();
            }
            return new IfStmt { Cond = cond, Then = then, Elifs = elifs, Else = els, Line = line };
        }

        private Stmt ParseWhile()
        {
            int line = Peek().Line; Expect("while");
            var c = ParseExpr();
            return new WhileStmt { Cond = c, Body = ParseBlock(), Line = line };
        }

        private Stmt ParseFor()
        {
            int line = Peek().Line; Expect("for");
            // for i = 0; i < 10; i = i + 1
            if (Peek().Type == "ID" && Peek(1).Value == "=")
            {
                var name = Eat().Value; Eat();
                var start = ParseExpr();
                if (At(";")) Eat();
                var cond = ParseExpr();
                if (At(";")) Eat();
                // step: identifier = expr
                Stmt? step = null;
                if (Peek().Type == "ID" && Peek(1).Value == "=") { var n = Eat().Value; Eat(); step = new VarDecl { Name = n, Value = ParseExpr() }; }
                var init = new VarDecl { Name = name, Value = start };
                return new ForStmt { Init = init, Cond = cond, Step = step, Body = ParseBlock(), Line = line };
            }
            // for item in list
            if (Peek().Type == "ID")
            {
                var v = Eat().Value;
                if (At("in")) Eat();
                var it = ParseExpr();
                return new ForEachStmt { VarName = v, Iterable = it, Body = ParseBlock(), Line = line };
            }
            return new ExprStmt { Expr = new VarRef { Name = "for" }, Line = line };
        }

        private Stmt ParseMatch()
        {
            int line = Peek().Line; Expect("match");
            Expect("("); var subj = ParseExpr(); if (At(")")) Eat();
            var cases = new List<(Expr, List<Stmt>)>();
            List<Stmt>? els = null;
            if (At("{")) Eat();
            while (!IsEOF() && !At("}") && !At("else"))
            {
                var pat = ParseExpr();
                var body = ParseBlock();
                cases.Add((pat, body));
            }
            if (At("else")) { Eat(); els = ParseBlock(); }
            if (At("}")) Eat();
            return new MatchStmt { Subject = subj, Cases = cases, Else = els, Line = line };
        }

        private Stmt ParseFunc()
        {
            int line = Peek().Line; Expect("func");
            var name = Eat().Value;
            var ps = new List<string>(); Expect("(");
            while (!At(")") && !IsEOF()) { ps.Add(Eat().Value); if (At(",")) Eat(); }
            Expect(")");
            string ret = "";
            if (At("->")) { Eat(); ret = Eat().Value; }
            return new FuncDecl { Name = name, Params = ps, ReturnType = ret, Body = ParseBlock(), Line = line };
        }

        private Stmt ParseReturn()
        {
            int line = Peek().Line; Eat();
            var r = new ReturnStmt { Line = line };
            if (!IsEOF() && Peek().Type != "COMMENT" && !At("}"))
            {
                r.Values.Add(ParseExpr());
                while (At(",")) { Eat(); r.Values.Add(ParseExpr()); }
            }
            return r;
        }

        private Stmt ParseQuit()
        {
            int line = Peek().Line; Eat();
            Expr? reason = null;
            if (At("(")) { Eat(); if (!At(")")) reason = ParseExpr(); if (At(")")) Eat(); }
            return new CrashStmt { Reason = reason, Line = line };
        }

        private Stmt ParseTypedDecl(bool isGlobal)
        {
            int line = Peek().Line; var t = Eat().Value;
            var name = Eat().Value; Expect("=");
            var v = ParseExpr();
            return new VarDecl { Type = NormalizeType(t), Name = name, Value = v, IsGlobal = isGlobal, Line = line };
        }

        private static string NormalizeType(string t) => t switch
        {
            "int" => "int", "decimal" => "double", "exact" => "decimal",
            "bool" => "bool", "string" => "string", "txt" => "string",
            "bit" => "bool", "byte" => "byte",
            _ => t,
        };

        private Expr ParseExpr() => Or();
        private Expr Or() { var l = And(); while (At("||") || At("or")) { Eat(); l = new BinOp { Op = "||", Left = l, Right = And() }; } return l; }
        private Expr And() { var l = Not(); while (At("&&") || At("&") || At("and")) { Eat(); l = new BinOp { Op = "&&", Left = l, Right = Not() }; } return l; }
        private Expr Not() { if (At("!") || At("not")) { Eat(); return new UnaryOp { Op = "!", Operand = Not() }; } return Cmp(); }
        private Expr Cmp()
        {
            var l = Add();
            while (true)
            {
                if (At("is")) { Eat(); if (At("not")) { Eat(); l = new BinOp { Op = "!=", Left = l, Right = Add() }; }
                                else l = new BinOp { Op = "==", Left = l, Right = Add() }; }
                else if (At("<") || At(">") || At("<=") || At(">=") || At("!=") || At("==")) { var op = Eat().Value; l = new BinOp { Op = op, Left = l, Right = Add() }; }
                else break;
            }
            return l;
        }
        private Expr Add(){ var l = Mul(); while (At("+") || At("-")) { var op = Eat().Value; l = new BinOp { Op = op, Left = l, Right = Mul() }; } return l; }
        private Expr Mul(){ var l = Pow(); while (At("*") || At("/")) { var op = Eat().Value; l = new BinOp { Op = op, Left = l, Right = Pow() }; } return l; }
        private Expr Pow(){ var l = Unary(); if (At("^")) { Eat(); return new BinOp { Op = "^", Left = l, Right = Unary() }; } return l; }
        private Expr Unary(){ if (At("-")) { Eat(); return new UnaryOp { Op = "-", Operand = Primary() }; } return Primary(); }
        private Expr Primary()
        {
            var tk = Peek(); Expr e;
            if (tk.Type == "NUM") { Eat(); e = new NumLit { Value = tk.Value }; }
            else if (tk.Type == "STR") { Eat(); e = new StrLit { Value = tk.Value }; }
            else if (tk.Value is "true" or "false") { Eat(); e = new BoolLit { Value = tk.Value == "true" }; }
            else if (tk.Value == "none") { Eat(); e = new NullLit(); }
            else if (tk.Value == "[") { Eat(); var items = new List<Expr>();
                while (!At("]") && !IsEOF()) { items.Add(ParseExpr()); if (At(",")) Eat(); }
                if (At("]")) Eat(); e = new ListLit { Items = items }; }
            else if (tk.Value == "(") { Eat(); e = ParseExpr(); if (At(")")) Eat(); }
            else if (tk.Value == "range") { Eat(); Expect("("); var s = ParseExpr(); if (At(",")) Eat(); var en = ParseExpr(); if (At(")")) Eat(); e = new RangeExpr { Start = s, End = en }; }
            else if (tk.Type is "ID" or "KW") { Eat(); e = new VarRef { Name = tk.Value }; }
            else { Eat(); e = new VarRef { Name = tk.Value }; }
            return Postfix(e);
        }
        private Expr Postfix(Expr e)
        {
            while (true)
            {
                if (At("(")) { Eat(); var args = new List<Expr>();
                    while (!At(")") && !IsEOF()) { args.Add(ParseExpr()); if (At(",")) Eat(); }
                    if (At(")")) Eat(); e = new CallExpr { Callee = e, Args = args }; }
                else if (At("[")) { Eat(); var idx = ParseExpr(); if (At("]")) Eat(); e = new IndexExpr { Target = e, Index = idx }; }
                else if (At(".")) { Eat(); var prop = Eat().Value;
                    if (At("(")) { Eat(); var args = new List<Expr>();
                        while (!At(")") && !IsEOF()) { args.Add(ParseExpr()); if (At(",")) Eat(); }
                        if (At(")")) Eat();
                        e = new CallExpr { Callee = new MemberExpr { Target = e, Member = prop }, Args = args }; }
                    else e = new MemberExpr { Target = e, Member = prop }; }
                else break;
            }
            return e;
        }
    }

    // ── Emitter (AST → Silica source) ─────────────────────────────────────────
    public static string Emit(ProgramNode p)
    {
        var sb = new StringBuilder();
        foreach (var f in p.ProgramFlags) sb.AppendLine(f);
        if (p.ProgramFlags.Count > 0) sb.AppendLine();
        foreach (var s in p.Body) EmitStmt(s, 0, sb);
        return sb.ToString();
    }

    private static string Ind(int n) => new(' ', n * 4);

    private static void EmitStmt(Stmt s, int lvl, StringBuilder sb)
    {
        var ind = Ind(lvl);
        switch (s)
        {
            case CommentStmt c: sb.AppendLine($"{ind}# {c.Text}"); break;
            case ImportStmt i:  sb.AppendLine($"{ind}import {i.Path}"); break;
            case VarDecl v:
            {
                var ty = TypeToSil(v.Type);
                var prefix = v.IsGlobal ? "global " : "";
                if (!string.IsNullOrEmpty(ty)) sb.AppendLine($"{ind}{prefix}{ty} {v.Name} = {EmitExpr(v.Value!)}");
                else sb.AppendLine($"{ind}{prefix}{v.Name} = {EmitExpr(v.Value!)}");
                break;
            }
            case Assign a:      sb.AppendLine($"{ind}{EmitExpr(a.Target)} = {EmitExpr(a.Value)}"); break;
            case ExprStmt es:   sb.AppendLine($"{ind}{EmitExpr(es.Expr)}"); break;
            case ReturnStmt r:  sb.AppendLine($"{ind}return{(r.Values.Count > 0 ? " " + string.Join(", ", r.Values.ConvertAll(EmitExpr)) : "")}"); break;
            case BreakStmt:     sb.AppendLine($"{ind}break"); break;
            case ContinueStmt:  sb.AppendLine($"{ind}continue"); break;
            case CrashStmt c:   sb.AppendLine($"{ind}quit({(c.Reason != null ? EmitExpr(c.Reason) : "")})"); break;
            case IfStmt iff:
                sb.AppendLine($"{ind}if {EmitExpr(iff.Cond)}");
                foreach (var t in iff.Then) EmitStmt(t, lvl + 1, sb);
                foreach (var (ec, eb) in iff.Elifs) { sb.AppendLine($"{ind}else if {EmitExpr(ec)}"); foreach (var t in eb) EmitStmt(t, lvl + 1, sb); }
                if (iff.Else != null) { sb.AppendLine($"{ind}else"); foreach (var t in iff.Else) EmitStmt(t, lvl + 1, sb); }
                break;
            case WhileStmt w:
                sb.AppendLine($"{ind}while {EmitExpr(w.Cond)}");
                foreach (var t in w.Body) EmitStmt(t, lvl + 1, sb); break;
            case LoopStmt lp:
                sb.AppendLine($"{ind}loop");
                foreach (var t in lp.Body) EmitStmt(t, lvl + 1, sb); break;
            case ForStmt f:
                sb.Append($"{ind}for ");
                if (f.Init is VarDecl iv) sb.Append($"{iv.Name} = {EmitExpr(iv.Value!)}; ");
                sb.Append($"{(f.Cond != null ? EmitExpr(f.Cond) : "true")}; ");
                if (f.Step is VarDecl sv) sb.AppendLine($"{sv.Name} = {EmitExpr(sv.Value!)}");
                else sb.AppendLine();
                foreach (var t in f.Body) EmitStmt(t, lvl + 1, sb); break;
            case ForEachStmt fe:
                sb.AppendLine($"{ind}for {fe.VarName} in {EmitExpr(fe.Iterable)}");
                foreach (var t in fe.Body) EmitStmt(t, lvl + 1, sb); break;
            case FuncDecl fd:
                sb.Append($"{ind}func {fd.Name}({string.Join(", ", fd.Params)})");
                if (!string.IsNullOrEmpty(fd.ReturnType)) sb.Append($" -> {fd.ReturnType}");
                sb.AppendLine();
                foreach (var t in fd.Body) EmitStmt(t, lvl + 1, sb); break;
            case MatchStmt m:
                sb.AppendLine($"{ind}match({EmitExpr(m.Subject)})");
                foreach (var (p, b) in m.Cases) { sb.AppendLine($"{Ind(lvl + 1)}{EmitExpr(p)}"); foreach (var t in b) EmitStmt(t, lvl + 2, sb); }
                if (m.Else != null) { sb.AppendLine($"{ind}else"); foreach (var t in m.Else) EmitStmt(t, lvl + 1, sb); }
                break;
        }
    }

    private static string EmitExpr(Expr e) => e switch
    {
        NumLit n => n.Value,
        StrLit s => s.Value,
        BoolLit b => b.Value ? "true" : "false",
        NullLit => "none",
        VarRef v => v.Name,
        BinOp b when b.Op == "==" => $"({EmitExpr(b.Left)} is {EmitExpr(b.Right)})",
        BinOp b when b.Op == "!=" => $"({EmitExpr(b.Left)} is not {EmitExpr(b.Right)})",
        BinOp b when b.Op == "&&" => $"({EmitExpr(b.Left)} & {EmitExpr(b.Right)})",
        BinOp b when b.Op == "||" => $"({EmitExpr(b.Left)} or {EmitExpr(b.Right)})",
        BinOp b => $"({EmitExpr(b.Left)} {b.Op} {EmitExpr(b.Right)})",
        UnaryOp u => $"{(u.Op == "!" ? "not " : u.Op)}{EmitExpr(u.Operand)}",
        CallExpr c => $"{EmitExpr(c.Callee)}({string.Join(", ", c.Args.ConvertAll(EmitExpr))})",
        MemberExpr m => $"{EmitExpr(m.Target)}.{m.Member}",
        IndexExpr ix => $"{EmitExpr(ix.Target)}[{EmitExpr(ix.Index)}]",
        ListLit l => $"[{string.Join(", ", l.Items.ConvertAll(EmitExpr))}]",
        RangeExpr r => $"range({EmitExpr(r.Start)}, {EmitExpr(r.End)})",
        _ => "/*expr*/",
    };

    private static string TypeToSil(string t) => t switch
    {
        "int" => "int", "float" or "double" => "decimal", "decimal" => "exact",
        "string" => "string", "bool" => "bool", "byte" => "byte",
        _ => "",
    };
}
