using System;
using System.Collections.Generic;
using System.Text;

namespace Axion.Scripting;

// ───────────────────────────────────────────────────────────────────────────────
// Gel front-end: source → tokens → AST, and AST → source.
// Mirrors the JS reference transpiler used by the web IDE/VS Code extension.
// ───────────────────────────────────────────────────────────────────────────────

public static class Gel
{
    private static readonly HashSet<string> Keywords = new()
    {
        "if","elif","else","while","for","each","in","do",
        "func","function","return","rtn","end","import","include",
        "var","int","float","double","str","txt","bool","boolean",
        "dec","exact","ext","true","false","null","is","not","or","and",
        "print","rem","percent","crash",
    };

    public sealed record Token(string Type, string Value, int Line);

    // ── Lexer ─────────────────────────────────────────────────────────────────
    public static List<Token> Lex(string source)
    {
        var t = new List<Token>(); int i = 0, line = 1;
        while (i < source.Length)
        {
            char ch = source[i];
            if (ch == '\n') { t.Add(new("NL", "\n", line)); i++; line++; continue; }
            if (ch == ' ' || ch == '\t' || ch == '\r') { i++; continue; }
            if (ch == '#') { var sb = new StringBuilder(); while (i < source.Length && source[i] != '\n') sb.Append(source[i++]); t.Add(new("COMMENT", sb.ToString(), line)); continue; }
            if (ch == '"' || ch == '\'') { char q = ch; var sb = new StringBuilder(); sb.Append(q); i++; while (i < source.Length && source[i] != q) { if (source[i] == '\\') sb.Append(source[i++]); sb.Append(source[i++]); } sb.Append(q); i++; t.Add(new("STR", sb.ToString(), line)); continue; }
            if (char.IsDigit(ch)) { var sb = new StringBuilder(); while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) sb.Append(source[i++]); t.Add(new("NUM", sb.ToString(), line)); continue; }
            if (char.IsLetter(ch) || ch == '_') { var sb = new StringBuilder(); while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) sb.Append(source[i++]); var v = sb.ToString(); t.Add(new(Keywords.Contains(v) ? "KW" : "ID", v, line)); continue; }
            if (i + 1 < source.Length)
            {
                var two = source.Substring(i, 2);
                if (two is "!=" or "<=" or ">=" or "=<" or "=>" or "&&" or "||") { t.Add(new("OP", two, line)); i += 2; continue; }
            }
            if ("+-*/^<>=!&|()[]{},.".IndexOf(ch) >= 0) { t.Add(new("OP", ch.ToString(), line)); i++; continue; }
            i++;
        }
        t.Add(new("EOF", "", line));
        return t;
    }

    // ── Parser ────────────────────────────────────────────────────────────────
    public static ProgramNode Parse(string source) => new GelParser(Lex(source)).ParseProgram();

    private sealed class GelParser
    {
        private readonly List<Token> _t;
        private int _p;
        public GelParser(List<Token> tokens) { _t = tokens.FindAll(x => x.Type != "NL"); }
        private Token Peek(int o = 0) => _p + o < _t.Count ? _t[_p + o] : _t[^1];
        private Token Eat() => _t[_p++];
        private bool At(string v) => Peek().Value == v;
        private bool AtType(string ty) => Peek().Type == ty;
        private bool IsEOF() => Peek().Type == "EOF";
        private void Expect(string v) { if (Peek().Value == v) Eat(); }

        public ProgramNode ParseProgram()
        {
            var p = new ProgramNode();
            while (!IsEOF()) { var s = ParseStmt(); if (s != null) p.Body.Add(s); }
            return p;
        }

        private Stmt? ParseStmt()
        {
            var tk = Peek();
            if (tk.Type == "COMMENT") { Eat(); return new CommentStmt { Text = tk.Value.TrimStart('#', ' '), Line = tk.Line }; }
            if (tk.Value is "if")     return ParseIf();
            if (tk.Value is "while")  return ParseWhile();
            if (tk.Value is "for")    return ParseForEach();
            if (tk.Value is "func" or "function") return ParseFunc();
            if (tk.Value is "import" or "include") { Eat(); return new ImportStmt { Path = Eat().Value, Line = tk.Line }; }
            if (tk.Value is "return" or "rtn" or "end") return ParseReturn();
            if (tk.Value is "crash")  return ParseCrash();
            if (tk.Value is "var")    return ParseVar();
            if (tk.Value is "int" or "float" or "double" or "str" or "txt" or "bool" or "boolean" or "dec" or "exact" or "ext")
                return ParseTypedDecl();
            if (tk.Type is "ID" or "KW")
            {
                var n = Peek(1);
                if ((n.Value == "=" || n.Value == "is") && Peek(2).Value != "=") return ParseAssignSimple();
                var e = ParseExpr();
                if (At("=") && Peek(1).Value != "=") { Eat(); var v = ParseExpr(); return new Assign { Target = e, Value = v, Line = tk.Line }; }
                return new ExprStmt { Expr = e, Line = tk.Line };
            }
            Eat(); return null;
        }

        private List<Stmt> ParseBlock()
        {
            var body = new List<Stmt>();
            if (At("{")) { Eat(); while (!IsEOF() && !At("}")) { var s = ParseStmt(); if (s != null) body.Add(s); } if (At("}")) Eat(); }
            else { var s = ParseStmt(); if (s != null) body.Add(s); }
            return body;
        }

        private Stmt ParseIf()
        {
            int line = Peek().Line; Expect("if"); var cond = ParseExpr(); var then = ParseBlock();
            var elifs = new List<(Expr, List<Stmt>)>();
            while (At("elif")) { Eat(); var ec = ParseExpr(); var eb = ParseBlock(); elifs.Add((ec, eb)); }
            List<Stmt>? els = null;
            if (At("else")) { Eat(); els = ParseBlock(); }
            return new IfStmt { Cond = cond, Then = then, Elifs = elifs, Else = els, Line = line };
        }
        private Stmt ParseWhile()
        {
            int line = Peek().Line; Expect("while"); var c = ParseExpr();
            if (At("do")) Eat();
            return new WhileStmt { Cond = c, Body = ParseBlock(), Line = line };
        }
        private Stmt ParseFunc()
        {
            int line = Peek().Line; Eat();
            var name = Eat().Value; var ps = new List<string>(); Expect("(");
            while (!At(")") && !IsEOF()) { ps.Add(Eat().Value); if (At(",")) Eat(); }
            Expect(")");
            return new FuncDecl { Name = name, Params = ps, Body = ParseBlock(), Line = line };
        }
        private Stmt ParseForEach()
        {
            int line = Peek().Line; Expect("for"); Expect("each");
            var v = Eat().Value;
            if (At("in") || At("do")) Eat();
            var it = ParseExpr();
            return new ForEachStmt { VarName = v, Iterable = it, Body = ParseBlock(), Line = line };
        }
        private Stmt ParseReturn()
        {
            int line = Peek().Line; Eat();
            var r = new ReturnStmt { Line = line };
            if (!IsEOF() && Peek().Type != "COMMENT") r.Values.Add(ParseExpr());
            return r;
        }
        private Stmt ParseCrash()
        {
            int line = Peek().Line; Expect("crash"); Expect("("); var rsn = ParseExpr(); if (At(")")) Eat();
            return new CrashStmt { Reason = rsn, Line = line };
        }
        private Stmt ParseVar()
        {
            int line = Peek().Line; Eat();
            if (At("(")) { Eat(); var n = Eat().Value; if (At(",")) Eat(); var v = ParseExpr(); if (At(")")) Eat();
                return new VarDecl { Name = n.Trim('"','\''), Value = v, Line = line }; }
            var name = Eat().Value; Expect("="); var val = ParseExpr();
            return new VarDecl { Name = name, Value = val, Line = line };
        }
        private Stmt ParseTypedDecl()
        {
            int line = Peek().Line; var t = Eat().Value; var name = Eat().Value; Expect("="); var v = ParseExpr();
            return new VarDecl { Type = NormalizeType(t), Name = name, Value = v, Line = line };
        }
        private Stmt ParseAssignSimple()
        {
            int line = Peek().Line; var name = Eat().Value; Eat();
            return new VarDecl { Name = name, Value = ParseExpr(), Line = line };
        }

        private static string NormalizeType(string t) => t switch
        {
            "int"     => "int",
            "float"   => "float",
            "double"  => "double",
            "str" or "txt" => "string",
            "bool" or "boolean" => "bool",
            "dec" or "exact" or "ext" => "decimal",
            _ => t,
        };

        // ── Expressions (precedence climbing) ─────────────────────────────────
        private Expr ParseExpr() => Or();
        private Expr Or() { var l = And(); while (At("||") || At("or")) { Eat(); l = new BinOp { Op = "||", Left = l, Right = And() }; } return l; }
        private Expr And(){ var l = Not(); while (At("&&") || At("&") || At("and")) { Eat(); l = new BinOp { Op = "&&", Left = l, Right = Not() }; } return l; }
        private Expr Not()
        {
            if (At("!") || At("not")) { var isFn = Peek().Value == "not"; Eat();
                if (isFn && At("(")) { Eat(); var ex = ParseExpr(); if (At(")")) Eat(); return new UnaryOp { Op = "!", Operand = ex }; }
                return new UnaryOp { Op = "!", Operand = Not() }; }
            return Cmp();
        }
        private Expr Cmp()
        {
            var l = Add();
            string[] ops = { "<", ">", "<=", ">=", "=<", "=>", "!=", "==", "is" };
            while (Array.IndexOf(ops, Peek().Value) >= 0) { var op = Eat().Value;
                var nrm = op switch { "is" => "==", "=<" => "<=", "=>" => ">=", _ => op };
                l = new BinOp { Op = nrm, Left = l, Right = Add() }; }
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
            else if (tk.Value == "null") { Eat(); e = new NullLit(); }
            else if (tk.Value == "[") { Eat(); var items = new List<Expr>();
                while (!At("]") && !IsEOF()) { items.Add(ParseExpr()); if (At(",")) Eat(); }
                if (At("]")) Eat(); e = new ListLit { Items = items }; }
            else if (tk.Value == "(") { Eat(); e = ParseExpr(); if (At(")")) Eat(); }
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

    // ── Emitter (AST → Gel source) ────────────────────────────────────────────
    public static string Emit(ProgramNode p)
    {
        var sb = new StringBuilder();
        foreach (var s in p.Body) EmitStmt(s, 0, sb);
        return sb.ToString();
    }

    private static string Ind(int n) => new(' ', n * 2);

    private static void EmitStmt(Stmt s, int lvl, StringBuilder sb)
    {
        var ind = Ind(lvl);
        switch (s)
        {
            case CommentStmt c:  sb.AppendLine($"{ind}# {c.Text}"); break;
            case ImportStmt i:   sb.AppendLine($"{ind}import {i.Path}"); break;
            case VarDecl v:
                if (!string.IsNullOrEmpty(v.Type)) sb.AppendLine($"{ind}{TypeToGel(v.Type)} {v.Name} = {EmitExpr(v.Value!)}");
                else sb.AppendLine($"{ind}{v.Name} = {EmitExpr(v.Value!)}");
                break;
            case Assign a:       sb.AppendLine($"{ind}{EmitExpr(a.Target)} = {EmitExpr(a.Value)}"); break;
            case ExprStmt es:    sb.AppendLine($"{ind}{EmitExpr(es.Expr)}"); break;
            case ReturnStmt r:   sb.AppendLine($"{ind}return{(r.Values.Count > 0 ? " " + EmitExpr(r.Values[0]) : "")}"); break;
            case BreakStmt:      sb.AppendLine($"{ind}break"); break;
            case ContinueStmt:   sb.AppendLine($"{ind}continue"); break;
            case CrashStmt c:    sb.AppendLine($"{ind}crash({(c.Reason != null ? EmitExpr(c.Reason) : "")})"); break;
            case IfStmt iff:
                sb.AppendLine($"{ind}if {EmitExpr(iff.Cond)} {{");
                foreach (var t in iff.Then) EmitStmt(t, lvl + 1, sb);
                sb.AppendLine($"{ind}}}");
                foreach (var (ec, eb) in iff.Elifs) { sb.AppendLine($"{ind}elif {EmitExpr(ec)} {{"); foreach (var t in eb) EmitStmt(t, lvl + 1, sb); sb.AppendLine($"{ind}}}"); }
                if (iff.Else != null) { sb.AppendLine($"{ind}else {{"); foreach (var t in iff.Else) EmitStmt(t, lvl + 1, sb); sb.AppendLine($"{ind}}}"); }
                break;
            case WhileStmt w:
                sb.AppendLine($"{ind}while {EmitExpr(w.Cond)} {{");
                foreach (var t in w.Body) EmitStmt(t, lvl + 1, sb);
                sb.AppendLine($"{ind}}}"); break;
            case LoopStmt lp:
                sb.AppendLine($"{ind}while true {{");
                foreach (var t in lp.Body) EmitStmt(t, lvl + 1, sb);
                sb.AppendLine($"{ind}}}"); break;
            case ForEachStmt fe:
                sb.AppendLine($"{ind}for each {fe.VarName} in {EmitExpr(fe.Iterable)} {{");
                foreach (var t in fe.Body) EmitStmt(t, lvl + 1, sb);
                sb.AppendLine($"{ind}}}"); break;
            case ForStmt f:
                sb.AppendLine($"{ind}# (counted for loop — translated)");
                if (f.Init != null) EmitStmt(f.Init, lvl, sb);
                sb.AppendLine($"{ind}while {EmitExpr(f.Cond ?? new BoolLit { Value = true })} {{");
                foreach (var t in f.Body) EmitStmt(t, lvl + 1, sb);
                if (f.Step != null) EmitStmt(f.Step, lvl + 1, sb);
                sb.AppendLine($"{ind}}}"); break;
            case FuncDecl fd:
                sb.AppendLine($"{ind}func {fd.Name}({string.Join(", ", fd.Params)}) {{");
                foreach (var t in fd.Body) EmitStmt(t, lvl + 1, sb);
                sb.AppendLine($"{ind}}}"); break;
            case MatchStmt m:
                // Gel has no match — emit as if/elif chain
                if (m.Cases.Count > 0)
                {
                    sb.AppendLine($"{ind}if {EmitExpr(m.Subject)} == {EmitExpr(m.Cases[0].Pattern)} {{");
                    foreach (var t in m.Cases[0].Body) EmitStmt(t, lvl + 1, sb);
                    sb.AppendLine($"{ind}}}");
                    for (int i = 1; i < m.Cases.Count; i++) { sb.AppendLine($"{ind}elif {EmitExpr(m.Subject)} == {EmitExpr(m.Cases[i].Pattern)} {{"); foreach (var t in m.Cases[i].Body) EmitStmt(t, lvl + 1, sb); sb.AppendLine($"{ind}}}"); }
                    if (m.Else != null) { sb.AppendLine($"{ind}else {{"); foreach (var t in m.Else) EmitStmt(t, lvl + 1, sb); sb.AppendLine($"{ind}}}"); }
                }
                break;
        }
    }

    private static string EmitExpr(Expr e) => e switch
    {
        NumLit n => n.Value,
        StrLit s => s.Value,
        BoolLit b => b.Value ? "true" : "false",
        NullLit => "null",
        VarRef v => v.Name,
        BinOp b => $"({EmitExpr(b.Left)} {b.Op} {EmitExpr(b.Right)})",
        UnaryOp u => $"{u.Op}{EmitExpr(u.Operand)}",
        CallExpr c => $"{EmitExpr(c.Callee)}({string.Join(", ", c.Args.ConvertAll(EmitExpr))})",
        MemberExpr m => $"{EmitExpr(m.Target)}.{m.Member}",
        IndexExpr ix => $"{EmitExpr(ix.Target)}[{EmitExpr(ix.Index)}]",
        ListLit l => $"[{string.Join(", ", l.Items.ConvertAll(EmitExpr))}]",
        RangeExpr r => $"range({EmitExpr(r.Start)}, {EmitExpr(r.End)})",
        _ => "/*expr*/",
    };

    private static string TypeToGel(string t) => t switch
    {
        "int" => "int", "float" => "float", "double" => "double",
        "string" => "str", "bool" => "bool", "decimal" => "dec",
        _ => "var",
    };
}
