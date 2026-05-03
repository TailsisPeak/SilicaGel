using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Axion.Scripting;

namespace SilicaGel;

// ───────────────────────────────────────────────────────────────────────────────
// Silica V2 — indent-based parser implementing the 2026 Silica spec.
//   • No braces; blocks are introduced by a colon-less header followed by an
//     INDENT line. Equivalent to Python-style indentation.
//   • Variable decls require a type:  int x = 0
//   • `is`, `is not`, `not`, `&`, `or`, `xor`  →  ==, !=, !, &&, ||, xor
//   • match / for-3-clause / for-in / while / loop with break/continue
//   • func name(args) -> RetType   then indented body
//   • group Name(field, field)  declares a group type
//   • global keyword promotes a variable
//   • errors.verbose top-of-file flag captured into ProgramFlags
// Produces the same shared AST so the interpreter, validator, and converter all
// understand it without changes.
// ───────────────────────────────────────────────────────────────────────────────

public static class SilicaV2
{
    public sealed class ParseError : Exception
    {
        public int Line { get; }
        public int Col { get; }
        public ParseError(string m, int line, int col) : base(m) { Line = line; Col = col; }
    }

    private sealed record Tok(string Type, string Value, int Line, int Col);

    public static ProgramNode Parse(string source) => new Parser(Tokenize(source)).ParseProgram();

    private static readonly HashSet<string> TwoCharOps = new() { "<=", ">=", "!=", "==", "->" };
    private static readonly HashSet<string> SingleOps = new(){ "+","-","*","/","^","%","=","<",">","(",")","[","]",",",".","&",";",":" };

    // ── Lexer with INDENT / DEDENT / NEWLINE generation ───────────────────────
    private static List<Tok> Tokenize(string src)
    {
        var tokens = new List<Tok>();
        var indents = new Stack<int>();
        indents.Push(0);
        int line = 1;
        var lines = src.Replace("\r\n","\n").Replace("\r","\n").Split('\n');
        for (int li = 0; li < lines.Length; li++)
        {
            line = li + 1;
            string text = lines[li];
            // measure indent
            int indent = 0;
            int p = 0;
            while (p < text.Length && (text[p] == ' ' || text[p] == '\t'))
            { indent += text[p] == '\t' ? 4 : 1; p++; }

            // skip blank or comment-only line
            string rest = text.Substring(p);
            if (rest.Length == 0 || rest.TrimStart().StartsWith("#"))
            {
                if (rest.TrimStart().StartsWith("#"))
                    tokens.Add(new Tok("COMMENT", rest.TrimStart().TrimStart('#').Trim(), line, p+1));
                continue;
            }

            // emit INDENT / DEDENT
            int top = indents.Peek();
            if (indent > top) { indents.Push(indent); tokens.Add(new Tok("INDENT","", line, 1)); }
            while (indent < indents.Peek())
            {
                indents.Pop();
                tokens.Add(new Tok("DEDENT","", line, 1));
            }

            // tokenize the rest of the line
            int i = p;
            while (i < text.Length)
            {
                char ch = text[i];
                if (ch == ' ' || ch == '\t') { i++; continue; }
                if (ch == '#') break; // comment to EOL
                if (ch == '"' || ch == '\'')
                {
                    char q = ch; var sb = new StringBuilder(); sb.Append(q); i++;
                    while (i < text.Length && text[i] != q)
                    { if (text[i] == '\\' && i+1 < text.Length) { sb.Append(text[i]); sb.Append(text[i+1]); i+=2; continue; } sb.Append(text[i++]); }
                    if (i < text.Length) { sb.Append(q); i++; }
                    tokens.Add(new Tok("STR", sb.ToString(), line, i+1));
                    continue;
                }
                if (char.IsDigit(ch))
                {
                    var sb = new StringBuilder();
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) sb.Append(text[i++]);
                    tokens.Add(new Tok("NUM", sb.ToString(), line, i+1));
                    continue;
                }
                if (char.IsLetter(ch) || ch == '_')
                {
                    var sb = new StringBuilder();
                    while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) sb.Append(text[i++]);
                    tokens.Add(new Tok("ID", sb.ToString(), line, i+1));
                    continue;
                }
                if (i + 1 < text.Length)
                {
                    var two = text.Substring(i, 2);
                    if (TwoCharOps.Contains(two)) { tokens.Add(new Tok("OP", two, line, i+1)); i += 2; continue; }
                }
                if (SingleOps.Contains(ch.ToString()))
                {
                    tokens.Add(new Tok("OP", ch.ToString(), line, i+1)); i++; continue;
                }
                // unknown char → skip
                i++;
            }
            tokens.Add(new Tok("NL","", line, text.Length+1));
        }
        while (indents.Peek() > 0) { indents.Pop(); tokens.Add(new Tok("DEDENT","", line, 1)); }
        tokens.Add(new Tok("EOF","", line, 1));
        return tokens;
    }

    // ── Parser ────────────────────────────────────────────────────────────────
    private static readonly HashSet<string> TypeNames = new()
    {
        "int","int8","int16","int32",
        "decimal","decimal32","decimal128","exact",
        "bool","txt","bit","byte",
        "vector2","vector3","vector4",
        "matrix2","matrix3","matrix4",
        "memory.address",
    };

    private sealed class Parser
    {
        private readonly List<Tok> _t;
        private int _p;
        public Parser(List<Tok> t) { _t = t; }
        private Tok Peek(int o = 0) => _p + o < _t.Count ? _t[_p + o] : _t[^1];
        private Tok Eat() => _t[_p++];
        private bool At(string v) => Peek().Value == v;
        private bool AtType(string ty) => Peek().Type == ty;
        private void SkipNL() { while (AtType("NL")) Eat(); }

        public ProgramNode ParseProgram()
        {
            var p = new ProgramNode();
            SkipNL();
            // capture top-of-file flags like `errors.verbose`
            while (Peek().Type == "ID" && Peek().Value == "errors" && Peek(1).Value == "." && Peek(2).Value == "verbose")
            { p.ProgramFlags.Add("errors.verbose"); _p += 3; SkipNL(); }

            while (!AtType("EOF"))
            {
                if (AtType("NL") || AtType("INDENT") || AtType("DEDENT")) { Eat(); continue; }
                if (AtType("COMMENT")) { var c = Eat(); p.Body.Add(new CommentStmt { Text = c.Value, Line = c.Line }); continue; }
                p.Body.Add(ParseStmt());
            }
            return p;
        }

        private List<Stmt> ParseBlock()
        {
            // After a block-introducing header, expect INDENT … statements … DEDENT
            SkipNL();
            var body = new List<Stmt>();
            if (!AtType("INDENT"))
            {
                // single-line body fallback (rare in spec, allows `if x is 5: print(x)`-style on one line)
                if (!AtType("EOF") && !AtType("DEDENT")) body.Add(ParseStmt());
                return body;
            }
            Eat(); // INDENT
            while (!AtType("DEDENT") && !AtType("EOF"))
            {
                if (AtType("NL")) { Eat(); continue; }
                if (AtType("COMMENT")) { var c = Eat(); body.Add(new CommentStmt { Text = c.Value, Line = c.Line }); continue; }
                body.Add(ParseStmt());
            }
            if (AtType("DEDENT")) Eat();
            return body;
        }

        private Stmt ParseStmt()
        {
            var tk = Peek();
            // global keyword
            bool isGlobal = false;
            if (tk.Value == "global") { isGlobal = true; Eat(); tk = Peek(); }

            // group decl: `group Name(field, field)`
            if (tk.Value == "group" && Peek(1).Type == "ID")
            {
                Eat(); var name = Eat().Value; var fields = new List<string>();
                if (At("(")) { Eat(); while (!At(")") && !AtType("EOF")) { while (Peek().Type == "ID" && TypeNames.Contains(Peek().Value)) Eat(); if (Peek().Type == "ID") fields.Add(Eat().Value); if (At(",")) Eat(); } if (At(")")) Eat(); }
                SkipNL();
                // model groups as a special VarDecl placeholder so the converter can show them
                return new VarDecl { Name = name, Type = "group", Value = new StrLit { Value = "\"group:" + string.Join(",", fields) + "\"" }, Line = tk.Line };
            }

            // Type-led declaration: int x = 0   /   txt s = "hi"   /   vector3 v = vector3(0,0,0)
            if (tk.Type == "ID" && (TypeNames.Contains(tk.Value) || (tk.Value == "memory" && Peek(1).Value == "." && Peek(2).Value == "address")))
            {
                int startSave = _p;
                string tname = Eat().Value;
                // memory.address composite
                if (tname == "memory" && At(".")) { Eat(); tname = "memory." + Eat().Value; }
                // type may have suffix like .unsigned (e.g. int.unsigned)
                while (At(".") && Peek(1).Type == "ID" && Peek(1).Value != "")
                { Eat(); tname += "." + Eat().Value; }
                if (Peek().Type == "ID")
                {
                    string vname = Eat().Value;
                    Expr? val = null;
                    if (At("=")) { Eat(); val = ParseExpr(); }
                    SkipNL();
                    return new VarDecl { Name = vname, Type = tname, Value = val, IsGlobal = isGlobal, Line = tk.Line };
                }
                _p = startSave; // not a decl after all; rewind
            }

            // Control-flow keywords
            switch (tk.Value)
            {
                case "if":      return ParseIf();
                case "while":   return ParseWhile();
                case "loop":    return ParseLoop();
                case "for":     return ParseFor();
                case "func":    return ParseFunc();
                case "match":   return ParseMatch();
                case "return":  return ParseReturn();
                case "break":   Eat(); SkipNL(); return new BreakStmt { Line = tk.Line };
                case "continue":Eat(); SkipNL(); return new ContinueStmt { Line = tk.Line };
                case "import":  Eat(); var path = Peek().Type == "ID" || Peek().Type == "STR" ? Eat().Value : ""; SkipNL(); return new ImportStmt { Path = path, Line = tk.Line };
            }

            // Expression statement / assignment
            var lhs = ParseExpr();
            if (At("="))
            {
                Eat(); var rhs = ParseExpr(); SkipNL();
                if (lhs is VarRef vr) return new VarDecl { Name = vr.Name, Value = rhs, IsGlobal = isGlobal, Line = tk.Line };
                return new Assign { Target = lhs, Value = rhs, Line = tk.Line };
            }
            SkipNL();
            return new ExprStmt { Expr = lhs, Line = tk.Line };
        }

        private Stmt ParseIf()
        {
            int line = Peek().Line; Eat(); // if
            var cond = ParseExpr();
            var then = ParseBlock();
            List<Stmt>? els = null;
            SkipNL();
            if (Peek().Value == "else") { Eat(); els = ParseBlock(); }
            return new IfStmt { Cond = cond, Then = then, Else = els, Line = line };
        }

        private Stmt ParseWhile()
        {
            int line = Peek().Line; Eat(); var cond = ParseExpr();
            return new WhileStmt { Cond = cond, Body = ParseBlock(), Line = line };
        }

        private Stmt ParseLoop()
        {
            int line = Peek().Line; Eat();
            return new LoopStmt { Body = ParseBlock(), Line = line };
        }

        private Stmt ParseFor()
        {
            int line = Peek().Line; Eat(); // for
            // Could be: `for i is 0; i < 10; i = i + 1`  (counting)
            //       or: `for item in list`              (iter)
            int save = _p;
            // try to detect ` in `
            int depth = 0; bool sawIn = false;
            for (int k = _p; k < _t.Count; k++)
            {
                var tk = _t[k];
                if (tk.Type == "NL" || tk.Type == "INDENT") break;
                if (tk.Value == "(") depth++; else if (tk.Value == ")") depth--;
                if (depth == 0 && tk.Type == "ID" && tk.Value == "in") { sawIn = true; break; }
            }
            if (sawIn)
            {
                var v = Eat().Value; Eat(); // skip "in"
                var iter = ParseExpr();
                return new ForEachStmt { VarName = v, Iterable = iter, Body = ParseBlock(), Line = line };
            }
            // counting: i is 0 ; cond ; step
            // use ; as separator (we have to recognize it; not in current single-op set so we rely on user newlines being absent)
            // Approximate: parse until `\n` or `;`. To keep this simple, accept syntax: `for i = 0 ; i < 10 ; i = i + 1`
            Stmt init = ParseStmt();
            Expr? cond = null; Stmt? step = null;
            if (At(";")) { Eat(); cond = ParseExpr(); }
            if (At(";")) { Eat(); step = ParseStmt(); }
            return new ForStmt { Init = init, Cond = cond, Step = step, Body = ParseBlock(), Line = line };
        }

        private Stmt ParseFunc()
        {
            int line = Peek().Line; Eat(); // func
            var name = Peek().Type == "ID" ? Eat().Value : "(anon)";
            var ps = new List<string>();
            if (At("(")) { Eat(); while (!At(")") && !AtType("EOF")) { if (Peek().Type == "ID") ps.Add(Eat().Value); if (At(",")) Eat(); } if (At(")")) Eat(); }
            string ret = "";
            if (At("->")) { Eat(); if (Peek().Type == "ID") ret = Eat().Value; }
            return new FuncDecl { Name = name, Params = ps, ReturnType = ret, Body = ParseBlock(), Line = line };
        }

        private Stmt ParseMatch()
        {
            int line = Peek().Line; Eat();
            if (At("(")) Eat();
            var subj = ParseExpr();
            if (At(")")) Eat();
            SkipNL();
            var ms = new MatchStmt { Subject = subj, Line = line };
            if (AtType("INDENT")) Eat();
            while (!AtType("DEDENT") && !AtType("EOF"))
            {
                if (AtType("NL")) { Eat(); continue; }
                if (Peek().Value == "else") { Eat(); ms.Else = ParseBlock(); break; }
                var pat = ParseExpr();
                var body = ParseBlock();
                ms.Cases.Add((pat, body));
            }
            if (AtType("DEDENT")) Eat();
            return ms;
        }

        private Stmt ParseReturn()
        {
            int line = Peek().Line; Eat();
            var r = new ReturnStmt { Line = line };
            if (!AtType("NL") && !AtType("EOF") && !AtType("DEDENT"))
                r.Values.Add(ParseExpr());
            SkipNL();
            return r;
        }

        // ── Expressions ───────────────────────────────────────────────────────
        private Expr ParseExpr() => Or();
        private Expr Or() { var l = Xor(); while (At("or")) { Eat(); l = new BinOp { Op = "||", Left = l, Right = Xor() }; } return l; }
        private Expr Xor(){ var l = And(); while (At("xor")) { Eat(); l = new BinOp { Op = "!=", Left = l, Right = And() }; } return l; }
        private Expr And(){ var l = Not(); while (At("&") || At("and")) { Eat(); l = new BinOp { Op = "&&", Left = l, Right = Not() }; } return l; }
        private Expr Not()
        {
            if (At("not")) { Eat(); return new UnaryOp { Op = "!", Operand = Not() }; }
            return Cmp();
        }
        private Expr Cmp()
        {
            var l = Add();
            while (true)
            {
                if (At("is"))
                {
                    Eat();
                    if (At("not")) { Eat(); l = new BinOp { Op = "!=", Left = l, Right = Add() }; }
                    else l = new BinOp { Op = "==", Left = l, Right = Add() };
                }
                else if (At("<") || At(">") || At("<=") || At(">=") || At("==") || At("!="))
                { var op = Eat().Value; l = new BinOp { Op = op, Left = l, Right = Add() }; }
                else break;
            }
            return l;
        }
        private Expr Add(){ var l = Mul(); while (At("+") || At("-")) { var op = Eat().Value; l = new BinOp { Op = op, Left = l, Right = Mul() }; } return l; }
        private Expr Mul(){ var l = Pow(); while (At("*") || At("/") || At("%")) { var op = Eat().Value; l = new BinOp { Op = op, Left = l, Right = Pow() }; } return l; }
        private Expr Pow(){ var l = Unary(); if (At("^")) { Eat(); return new BinOp { Op = "^", Left = l, Right = Unary() }; } return l; }
        private Expr Unary(){ if (At("-")) { Eat(); return new UnaryOp { Op = "-", Operand = Primary() }; } return Primary(); }
        private Expr Primary()
        {
            var tk = Peek(); Expr e;
            if (tk.Type == "NUM") { Eat(); e = new NumLit { Value = tk.Value, Line = tk.Line }; }
            else if (tk.Type == "STR") { Eat(); e = new StrLit { Value = tk.Value, Line = tk.Line }; }
            else if (tk.Value == "true" || tk.Value == "false") { Eat(); e = new BoolLit { Value = tk.Value == "true", Line = tk.Line }; }
            else if (tk.Value == "none") { Eat(); e = new NullLit { Line = tk.Line }; }
            else if (tk.Value == "[")
            {
                Eat(); var items = new List<Expr>();
                while (!At("]") && !AtType("EOF"))
                {
                    // allow inline type tags inside mixed arrays: `bool true`, `txt "x"` — skip the type tag
                    if (Peek().Type == "ID" && TypeNames.Contains(Peek().Value)) Eat();
                    items.Add(ParseExpr());
                    if (At(",")) Eat();
                }
                if (At("]")) Eat();
                e = new ListLit { Items = items, Line = tk.Line };
            }
            else if (tk.Value == "(") { Eat(); e = ParseExpr(); if (At(")")) Eat(); }
            else if (tk.Type == "ID") { Eat(); e = new VarRef { Name = tk.Value, Line = tk.Line }; }
            else { Eat(); e = new VarRef { Name = tk.Value, Line = tk.Line }; }
            return Postfix(e);
        }
        private Expr Postfix(Expr e)
        {
            while (true)
            {
                if (At("("))
                {
                    Eat(); var args = new List<Expr>();
                    while (!At(")") && !AtType("EOF")) { args.Add(ParseExpr()); if (At(",")) Eat(); }
                    if (At(")")) Eat(); e = new CallExpr { Callee = e, Args = args, Line = e.Line };
                }
                else if (At("[")) { Eat(); var idx = ParseExpr(); if (At("]")) Eat(); e = new IndexExpr { Target = e, Index = idx, Line = e.Line }; }
                else if (At("."))
                {
                    Eat(); var prop = Eat().Value;
                    if (At("("))
                    {
                        Eat(); var args = new List<Expr>();
                        while (!At(")") && !AtType("EOF")) { args.Add(ParseExpr()); if (At(",")) Eat(); }
                        if (At(")")) Eat();
                        e = new CallExpr { Callee = new MemberExpr { Target = e, Member = prop, Line = e.Line }, Args = args, Line = e.Line };
                    }
                    else e = new MemberExpr { Target = e, Member = prop, Line = e.Line };
                }
                else break;
            }
            return e;
        }
    }
}
