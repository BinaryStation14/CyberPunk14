namespace Content.Server._CyberPunk.Wire;

/// <summary>
/// A mistake in a Wire program, with where it is. The message is for players.
/// </summary>
public sealed class WireException(int line, int column, string problem) : Exception($"line {line}, column {column}: {problem}")
{
    public int Line { get; } = line;
    public int Column { get; } = column;

    /// <summary>What is wrong, without where.</summary>
    public string Problem { get; } = problem;
}

public enum TokenKind : byte
{
    Name,
    Int,
    Str,

    /// <summary>Punctuation and operators.</summary>
    Sym,
    Newline,
    Indent,
    Dedent,
    Eof,
}

public readonly record struct Token(TokenKind Kind, string Text, long Value, int Line, int Column)
{
    public bool IsSym(string sym) => Kind == TokenKind.Sym && Text == sym;

    public bool IsName(string name) => Kind == TokenKind.Name && Text == name;
}

public enum BinOp : byte
{
    Add,
    Sub,
    Mul,
    Div,
    Mod,
}

public enum CmpOp : byte
{
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    In,
    NotIn,
}

/// <summary>
/// An expression, placed at the line and column it starts on.
/// </summary>
public abstract record Expr(int Line, int Column);

public sealed record IntExpr(long Value, int Line, int Column) : Expr(Line, Column);

public sealed record StrExpr(string Value, int Line, int Column) : Expr(Line, Column);

public sealed record BoolExpr(bool Value, int Line, int Column) : Expr(Line, Column);

public sealed record NoneExpr(int Line, int Column) : Expr(Line, Column);

public sealed record NameExpr(string Name, int Line, int Column) : Expr(Line, Column);

public sealed record ListExpr(List<Expr> Items, int Line, int Column) : Expr(Line, Column);

public sealed record DictExpr(List<(Expr Key, Expr Value)> Pairs, int Line, int Column) : Expr(Line, Column);

public sealed record NegExpr(Expr Inner, int Line, int Column) : Expr(Line, Column);

public sealed record NotExpr(Expr Inner, int Line, int Column) : Expr(Line, Column);

public sealed record BinaryExpr(BinOp Op, Expr Left, Expr Right, int Line, int Column) : Expr(Line, Column);

public sealed record CompareExpr(CmpOp Op, Expr Left, Expr Right, int Line, int Column) : Expr(Line, Column);

public sealed record AndExpr(Expr Left, Expr Right, int Line, int Column) : Expr(Line, Column);

public sealed record OrExpr(Expr Left, Expr Right, int Line, int Column) : Expr(Line, Column);

public sealed record CallExpr(Expr Callee, List<Expr> Args, int Line, int Column) : Expr(Line, Column);

public sealed record AttrExpr(Expr Target, string Name, int Line, int Column) : Expr(Line, Column);

public sealed record IndexExpr(Expr Target, Expr Index, int Line, int Column) : Expr(Line, Column);

/// <summary>
/// A statement, placed at the line and column it starts on.
/// </summary>
public abstract record Stmt(int Line, int Column);

public sealed record ExprStmt(Expr Value, int Line, int Column) : Stmt(Line, Column);

public sealed record AssignStmt(Expr Target, Expr Value, int Line, int Column) : Stmt(Line, Column);

public sealed record AugAssignStmt(Expr Target, BinOp Op, Expr Value, int Line, int Column) : Stmt(Line, Column);

public sealed record IfStmt(List<(Expr Condition, List<Stmt> Body)> Arms, List<Stmt> Else, int Line, int Column)
    : Stmt(Line, Column);

public sealed record WhileStmt(Expr Condition, List<Stmt> Body, int Line, int Column) : Stmt(Line, Column);

public sealed record ForStmt(string Variable, Expr Over, List<Stmt> Body, int Line, int Column) : Stmt(Line, Column);

public sealed record DefStmt(string Name, List<string> Params, List<Stmt> Body, int Line, int Column)
    : Stmt(Line, Column);

public sealed record ReturnStmt(Expr? Value, int Line, int Column) : Stmt(Line, Column);

public sealed record BreakStmt(int Line, int Column) : Stmt(Line, Column);

public sealed record ContinueStmt(int Line, int Column) : Stmt(Line, Column);

public sealed record PassStmt(int Line, int Column) : Stmt(Line, Column);
