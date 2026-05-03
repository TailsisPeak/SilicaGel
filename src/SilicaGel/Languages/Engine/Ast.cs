using System.Collections.Generic;

namespace Axion.Scripting;

// ───────────────────────────────────────────────────────────────────────────────
// Shared AST — all three Axion languages (Gel, Silica, Blocks) parse to this.
// Gel→AST, Silica→AST, Blocks→AST, then any of: AST→Gel, AST→Silica, AST→C#.
// This is what makes cross-language conversion free.
// ───────────────────────────────────────────────────────────────────────────────

public abstract class AstNode { public int Line; }

public sealed class ProgramNode : AstNode
{
    public List<Stmt> Body = new();
    public List<string> ProgramFlags = new();   // e.g. "errors.verbose" (Silica)
}

// ── Statements ────────────────────────────────────────────────────────────────
public abstract class Stmt    : AstNode { }
public abstract class Expr    : AstNode { }

public sealed class CommentStmt : Stmt { public string Text = ""; }
public sealed class ImportStmt  : Stmt { public string Path = ""; }
public sealed class VarDecl     : Stmt
{
    /// <summary>"int", "float", "string", "bool", "decimal", "exact", "byte", "bit", "txt", or "" for inferred.</summary>
    public string Type = "";
    public string Name = "";
    public Expr?  Value;
    public bool   IsGlobal;
}
public sealed class Assign      : Stmt { public Expr Target = null!; public Expr Value = null!; }
public sealed class ExprStmt    : Stmt { public Expr Expr = null!; }
public sealed class ReturnStmt  : Stmt { public List<Expr> Values = new(); }
public sealed class BreakStmt   : Stmt { }
public sealed class ContinueStmt: Stmt { }
public sealed class CrashStmt   : Stmt { public Expr? Reason; }   // gel: crash() / silica: quit()
public sealed class IfStmt : Stmt
{
    public Expr Cond = null!;
    public List<Stmt> Then = new();
    public List<(Expr Cond, List<Stmt> Body)> Elifs = new();
    public List<Stmt>? Else;
}
public sealed class WhileStmt : Stmt { public Expr Cond = null!; public List<Stmt> Body = new(); }
public sealed class LoopStmt  : Stmt { public List<Stmt> Body = new(); }   // infinite loop (silica's `loop`)
public sealed class ForStmt   : Stmt
{
    public Stmt? Init;
    public Expr? Cond;
    public Stmt? Step;
    public List<Stmt> Body = new();
}
public sealed class ForEachStmt : Stmt
{
    public string VarName = "";
    public Expr Iterable = null!;
    public List<Stmt> Body = new();
}
public sealed class FuncDecl : Stmt
{
    public string Name = "";
    public List<string> Params = new();
    public string ReturnType = "";   // empty if inferred / void
    public List<Stmt> Body = new();
}
public sealed class MatchStmt : Stmt
{
    public Expr Subject = null!;
    public List<(Expr Pattern, List<Stmt> Body)> Cases = new();
    public List<Stmt>? Else;
}

// ── Expressions ───────────────────────────────────────────────────────────────
public sealed class NumLit  : Expr { public string Value = "0"; }   // string preserves int vs float
public sealed class StrLit  : Expr { public string Value = "\"\""; }   // includes surrounding quotes
public sealed class BoolLit : Expr { public bool   Value; }
public sealed class NullLit : Expr { }
public sealed class VarRef  : Expr { public string Name = ""; }
public sealed class BinOp   : Expr
{
    /// <summary>Normalized: + - * / ^ % == != &lt; &gt; &lt;= &gt;= &amp;&amp; ||</summary>
    public string Op = "";
    public Expr Left  = null!;
    public Expr Right = null!;
}
public sealed class UnaryOp : Expr { public string Op = ""; public Expr Operand = null!; }
public sealed class CallExpr : Expr { public Expr Callee = null!; public List<Expr> Args = new(); }
public sealed class MemberExpr : Expr { public Expr Target = null!; public string Member = ""; }
public sealed class IndexExpr  : Expr { public Expr Target = null!; public Expr Index = null!; }
public sealed class ListLit    : Expr { public List<Expr> Items = new(); }
public sealed class RangeExpr  : Expr { public Expr Start = null!; public Expr End = null!; }
