using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Axion.Scripting;

// ───────────────────────────────────────────────────────────────────────────────
// Block-based visual scripting language. Saved as JSON; converts into the shared
// AST so it can be transpiled to Gel, Silica, or C#.
// ───────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A node in the block program. Compatible block kinds:
///   "var"       — fields: name, value (block ref)
///   "set"       — fields: target (var name), value (block)
///   "print"     — fields: value (block)
///   "if"        — fields: cond (block), then (children), else (children)
///   "while"     — fields: cond (block), body (children)
///   "loop"      — body (children)        // infinite
///   "forEach"   — varName, iterable (block), body (children)
///   "func"      — name, params (string[]), body (children)
///   "return"    — value (block, optional)
///   "binop"     — op (string), left (block), right (block)
///   "num"       — value (string)
///   "str"       — value (string)        // raw, no quotes
///   "bool"      — value (bool)
///   "var-ref"   — name (string)
///   "call"      — callee (string), args (block[])
/// </summary>
public sealed class Block
{
    public string Kind { get; set; } = "";
    public Dictionary<string, JsonNode?> Fields { get; set; } = new();
    public List<Block> Then { get; set; } = new();
    public List<Block> Else { get; set; } = new();
    public List<Block> Body { get; set; } = new();
    public List<Block> Args { get; set; } = new();
}

public sealed class BlockProgram
{
    public List<Block> Roots { get; set; } = new();

    public static BlockProgram Load(string path) => FromJson(File.ReadAllText(path));

    public static BlockProgram FromJson(string json)
    {
        var prog = new BlockProgram();
        var root = JsonNode.Parse(json)!;
        if (root is JsonArray arr)
            foreach (var n in arr) if (n != null) prog.Roots.Add(ReadBlock(n));
        return prog;
    }

    public string ToJson() => JsonSerializer.Serialize(WriteList(Roots), new JsonSerializerOptions { WriteIndented = true });
    public void Save(string path) => File.WriteAllText(path, ToJson());

    private static Block ReadBlock(JsonNode node)
    {
        var b = new Block();
        var o = node.AsObject();
        b.Kind = o["kind"]?.GetValue<string>() ?? "";
        if (o["fields"] is JsonObject f) foreach (var kv in f) b.Fields[kv.Key] = kv.Value?.DeepClone();
        if (o["then"] is JsonArray t) foreach (var n in t) if (n != null) b.Then.Add(ReadBlock(n));
        if (o["else"] is JsonArray e) foreach (var n in e) if (n != null) b.Else.Add(ReadBlock(n));
        if (o["body"] is JsonArray bd) foreach (var n in bd) if (n != null) b.Body.Add(ReadBlock(n));
        if (o["args"] is JsonArray ar) foreach (var n in ar) if (n != null) b.Args.Add(ReadBlock(n));
        return b;
    }

    private static JsonArray WriteList(List<Block> list)
    {
        var arr = new JsonArray();
        foreach (var b in list) arr.Add(WriteBlock(b));
        return arr;
    }

    private static JsonObject WriteBlock(Block b)
    {
        var o = new JsonObject { ["kind"] = b.Kind };
        if (b.Fields.Count > 0) { var f = new JsonObject(); foreach (var kv in b.Fields) f[kv.Key] = kv.Value?.DeepClone(); o["fields"] = f; }
        if (b.Then.Count > 0) o["then"] = WriteList(b.Then);
        if (b.Else.Count > 0) o["else"] = WriteList(b.Else);
        if (b.Body.Count > 0) o["body"] = WriteList(b.Body);
        if (b.Args.Count > 0) o["args"] = WriteList(b.Args);
        return o;
    }

    public ProgramNode ToAst()
    {
        var p = new ProgramNode();
        foreach (var b in Roots) { var s = ToStmt(b); if (s != null) p.Body.Add(s); }
        return p;
    }

    private static Stmt? ToStmt(Block b) => b.Kind switch
    {
        "var"     => new VarDecl { Name = b.Fields["name"]?.GetValue<string>() ?? "x", Value = ToExpr(b.Args.Count > 0 ? b.Args[0] : null) },
        "set"     => new Assign { Target = new VarRef { Name = b.Fields["target"]?.GetValue<string>() ?? "x" }, Value = ToExpr(b.Args.Count > 0 ? b.Args[0] : null) },
        "print"   => new ExprStmt { Expr = new CallExpr { Callee = new VarRef { Name = "print" }, Args = { ToExpr(b.Args.Count > 0 ? b.Args[0] : null) } } },
        "if"      => new IfStmt { Cond = ToExpr(b.Args.Count > 0 ? b.Args[0] : null), Then = b.Then.ConvertAll(ToStmtSafe), Else = b.Else.Count > 0 ? b.Else.ConvertAll(ToStmtSafe) : null },
        "while"   => new WhileStmt { Cond = ToExpr(b.Args.Count > 0 ? b.Args[0] : null), Body = b.Body.ConvertAll(ToStmtSafe) },
        "loop"    => new LoopStmt { Body = b.Body.ConvertAll(ToStmtSafe) },
        "forEach" => new ForEachStmt { VarName = b.Fields["varName"]?.GetValue<string>() ?? "i", Iterable = ToExpr(b.Args.Count > 0 ? b.Args[0] : null), Body = b.Body.ConvertAll(ToStmtSafe) },
        "func"    => new FuncDecl { Name = b.Fields["name"]?.GetValue<string>() ?? "f", Params = ReadParams(b.Fields["params"]), Body = b.Body.ConvertAll(ToStmtSafe) },
        "return"  => new ReturnStmt { Values = b.Args.Count > 0 ? new() { ToExpr(b.Args[0]) } : new() },
        "break"   => new BreakStmt(),
        "continue"=> new ContinueStmt(),
        "call"    => new ExprStmt { Expr = new CallExpr { Callee = new VarRef { Name = b.Fields["callee"]?.GetValue<string>() ?? "f" }, Args = b.Args.ConvertAll(ToExpr) } },
        _ => null,
    };

    private static Stmt ToStmtSafe(Block b) => ToStmt(b) ?? new ExprStmt { Expr = new NullLit() };

    private static Expr ToExpr(Block? b)
    {
        if (b == null) return new NullLit();
        return b.Kind switch
        {
            "num"     => new NumLit { Value = b.Fields["value"]?.GetValue<string>() ?? "0" },
            "str"     => new StrLit { Value = $"\"{b.Fields["value"]?.GetValue<string>() ?? ""}\"" },
            "bool"    => new BoolLit { Value = b.Fields["value"]?.GetValue<bool>() ?? false },
            "var-ref" => new VarRef { Name = b.Fields["name"]?.GetValue<string>() ?? "x" },
            "binop"   => new BinOp { Op = b.Fields["op"]?.GetValue<string>() ?? "+", Left = ToExpr(b.Args.Count > 0 ? b.Args[0] : null), Right = ToExpr(b.Args.Count > 1 ? b.Args[1] : null) },
            "call"    => new CallExpr { Callee = new VarRef { Name = b.Fields["callee"]?.GetValue<string>() ?? "f" }, Args = b.Args.ConvertAll(ToExpr) },
            _ => new NullLit(),
        };
    }

    private static List<string> ReadParams(JsonNode? n)
    {
        var result = new List<string>();
        if (n is JsonArray a) foreach (var v in a) if (v is JsonValue jv) result.Add(jv.GetValue<string>());
        return result;
    }
}
