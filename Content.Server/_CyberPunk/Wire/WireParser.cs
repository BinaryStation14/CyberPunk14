namespace Content.Server._CyberPunk.Wire;

/// <summary>
/// Parses Wire's tokens into statements and expressions. Ported from Switchboard's
/// <c>wasm/wire/src/parser.rs</c>.
/// </summary>
public sealed class WireParser
{
    private static readonly HashSet<string> Keywords = new()
    {
        "def", "if", "elif", "else", "while", "for", "in", "not", "and", "or", "return", "break",
        "continue", "pass", "True", "False", "None",
    };

    private static readonly (string Sym, BinOp Op)[] AugOps =
    {
        ("+=", BinOp.Add),
        ("-=", BinOp.Sub),
        ("*=", BinOp.Mul),
        ("/=", BinOp.Div),
        ("//=", BinOp.Div),
        ("%=", BinOp.Mod),
    };

    private readonly List<Token> _tokens;
    private int _pos;

    private WireParser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    /// <summary>
    /// Parses a whole program.
    /// </summary>
    /// <exception cref="WireException">The program has a mistake.</exception>
    public static List<Stmt> Parse(string source)
    {
        var p = new WireParser(WireLexer.Lex(source));
        var body = new List<Stmt>();
        while (p.Peek.Kind != TokenKind.Eof)
        {
            if (p.Peek.Kind == TokenKind.Indent)
                throw p.Error("this line is indented, but nothing above it opens a block");

            body.Add(p.Statement());
        }

        return body;
    }

    private static string Describe(Token t)
    {
        return t.Kind switch
        {
            TokenKind.Name => $"\"{t.Text}\"",
            TokenKind.Int => t.Value.ToString(),
            TokenKind.Str => "some text",
            TokenKind.Sym => $"\"{t.Text}\"",
            TokenKind.Newline => "the end of the line",
            TokenKind.Indent => "an indented line",
            TokenKind.Dedent => "the end of the block",
            _ => "the end of the file",
        };
    }

    private Token Peek => _tokens[_pos];

    private Token Next()
    {
        var t = _tokens[_pos];
        if (_pos + 1 < _tokens.Count)
            _pos++;

        return t;
    }

    private WireException Error(string message)
    {
        return new WireException(Peek.Line, Peek.Column, message);
    }

    private bool IsSym(string s) => Peek.IsSym(s);

    private bool IsKw(string k) => Peek.IsName(k);

    private bool EatSym(string s)
    {
        if (!IsSym(s))
            return false;

        Next();
        return true;
    }

    private bool EatKw(string k)
    {
        if (!IsKw(k))
            return false;

        Next();
        return true;
    }

    private void ExpectSym(string s, string what)
    {
        if (!EatSym(s))
            throw Error($"expected \"{s}\" {what}, found {Describe(Peek)}");
    }

    private string Name(string what)
    {
        var t = Peek;
        if (t.Kind != TokenKind.Name || Keywords.Contains(t.Text))
            throw Error($"expected {what}, found {Describe(t)}");

        Next();
        return t.Text;
    }

    private void EndOfStatement()
    {
        switch (Peek.Kind)
        {
            case TokenKind.Newline:
                Next();
                return;
            case TokenKind.Eof:
            case TokenKind.Dedent:
                return;
            default:
                throw Error($"expected the end of the line, found {Describe(Peek)}");
        }
    }

    private Stmt Statement()
    {
        var (line, col) = (Peek.Line, Peek.Column);
        if (EatKw("def"))
        {
            var name = Name("a function name");
            ExpectSym("(", "after the function's name");
            var parameters = new List<string>();
            if (!EatSym(")"))
            {
                while (true)
                {
                    var param = Name("a parameter name");
                    if (parameters.Contains(param))
                        throw Error($"{param} is already a parameter");

                    parameters.Add(param);
                    if (EatSym(")"))
                        break;

                    ExpectSym(",", "between parameters");
                }
            }

            return new DefStmt(name, parameters, Block(), line, col);
        }

        if (EatKw("if"))
        {
            var arms = new List<(Expr, List<Stmt>)> { (Expr(), Block()) };
            var otherwise = new List<Stmt>();
            while (true)
            {
                if (EatKw("elif"))
                {
                    arms.Add((Expr(), Block()));
                }
                else
                {
                    if (EatKw("else"))
                        otherwise = Block();

                    break;
                }
            }

            return new IfStmt(arms, otherwise, line, col);
        }

        if (EatKw("while"))
        {
            var condition = Expr();
            return new WhileStmt(condition, Block(), line, col);
        }

        if (EatKw("for"))
        {
            var variable = Name("a variable name after for");
            if (!EatKw("in"))
                throw Error("expected `in` after the for loop's variable");

            var over = Expr();
            return new ForStmt(variable, over, Block(), line, col);
        }

        if (IsKw("elif") || IsKw("else"))
            throw Error("this has no `if` before it at the same indentation");

        if (IsKw("import") || IsKw("from"))
            throw Error("there is nothing to import: term, fs, net, sys, door, camera, ice and deck are always there");

        if (IsKw("class") || IsKw("lambda") || IsKw("try") || IsKw("with"))
            throw Error($"Wire doesn't have {Describe(Peek)} (see `man wire`)");

        var s = Simple();
        EndOfStatement();
        return s;
    }

    /// <summary>
    /// A one-line statement.
    /// </summary>
    private Stmt Simple()
    {
        var (line, col) = (Peek.Line, Peek.Column);
        if (EatKw("pass"))
            return new PassStmt(line, col);

        if (EatKw("break"))
            return new BreakStmt(line, col);

        if (EatKw("continue"))
            return new ContinueStmt(line, col);

        if (EatKw("return"))
        {
            var value = Peek.Kind is TokenKind.Newline or TokenKind.Eof or TokenKind.Dedent ? null : Expr();
            return new ReturnStmt(value, line, col);
        }

        var target = Expr();
        if (EatSym("="))
        {
            CheckTarget(target);
            var value = Expr();
            if (IsSym("="))
                throw Error("Wire assigns one thing at a time");

            return new AssignStmt(target, value, line, col);
        }

        foreach (var (sym, op) in AugOps)
        {
            if (!EatSym(sym))
                continue;

            CheckTarget(target);
            return new AugAssignStmt(target, op, Expr(), line, col);
        }

        if (IsSym(","))
            throw Error("Wire assigns one thing at a time (no `a, b = ...`)");

        return new ExprStmt(target, line, col);
    }

    /// <summary>
    /// After a <c>:</c>, an indented block, or one statement on the same line.
    /// </summary>
    private List<Stmt> Block()
    {
        ExpectSym(":", "to start the block");
        if (Peek.Kind != TokenKind.Newline)
        {
            var s = Simple();
            EndOfStatement();
            return new List<Stmt> { s };
        }

        Next();
        if (Peek.Kind != TokenKind.Indent)
            throw Error("expected an indented block after the `:`");

        Next();
        var body = new List<Stmt>();
        while (Peek.Kind is not (TokenKind.Dedent or TokenKind.Eof))
        {
            body.Add(Statement());
        }

        if (Peek.Kind == TokenKind.Dedent)
            Next();

        return body;
    }

    private Expr Expr() => Or();

    private Expr Or()
    {
        var left = And();
        while (IsKw("or"))
        {
            var (line, col) = (Peek.Line, Peek.Column);
            Next();
            left = new OrExpr(left, And(), line, col);
        }

        return left;
    }

    private Expr And()
    {
        var left = Not();
        while (IsKw("and"))
        {
            var (line, col) = (Peek.Line, Peek.Column);
            Next();
            left = new AndExpr(left, Not(), line, col);
        }

        return left;
    }

    private Expr Not()
    {
        if (!IsKw("not"))
            return Comparison();

        var (line, col) = (Peek.Line, Peek.Column);
        Next();
        return new NotExpr(Not(), line, col);
    }

    private CmpOp? CmpOperator()
    {
        CmpOp op;
        var t = Peek;
        if (t.Kind == TokenKind.Sym)
        {
            switch (t.Text)
            {
                case "==": op = CmpOp.Eq; break;
                case "!=": op = CmpOp.Ne; break;
                case "<": op = CmpOp.Lt; break;
                case "<=": op = CmpOp.Le; break;
                case ">": op = CmpOp.Gt; break;
                case ">=": op = CmpOp.Ge; break;
                default: return null;
            }
        }
        else if (t.IsName("in"))
        {
            op = CmpOp.In;
        }
        else if (t.IsName("not") && _pos + 1 < _tokens.Count && _tokens[_pos + 1].IsName("in"))
        {
            Next();
            op = CmpOp.NotIn;
        }
        else
        {
            return null;
        }

        Next();
        return op;
    }

    private Expr Comparison()
    {
        var left = Sum();
        var (line, col) = (Peek.Line, Peek.Column);
        if (CmpOperator() is not { } op)
            return left;

        var right = Sum();
        if (CmpOperator() != null)
            throw new WireException(line, col, "Wire compares two things at a time: write `a < b and b < c`");

        return new CompareExpr(op, left, right, line, col);
    }

    private Expr Sum()
    {
        var left = Term();
        while (true)
        {
            BinOp op;
            if (IsSym("+"))
                op = BinOp.Add;
            else if (IsSym("-"))
                op = BinOp.Sub;
            else
                return left;

            var (line, col) = (Peek.Line, Peek.Column);
            Next();
            left = new BinaryExpr(op, left, Term(), line, col);
        }
    }

    private Expr Term()
    {
        var left = Unary();
        while (true)
        {
            BinOp op;
            if (IsSym("*"))
                op = BinOp.Mul;
            else if (IsSym("/") || IsSym("//"))
                op = BinOp.Div;
            else if (IsSym("%"))
                op = BinOp.Mod;
            else
                return left;

            var (line, col) = (Peek.Line, Peek.Column);
            Next();
            left = new BinaryExpr(op, left, Unary(), line, col);
        }
    }

    private Expr Unary()
    {
        if (!IsSym("-"))
            return Postfix();

        var (line, col) = (Peek.Line, Peek.Column);
        Next();
        var inner = Unary();
        return inner is IntExpr i
            ? new IntExpr(-i.Value, line, col)
            : new NegExpr(inner, line, col);
    }

    private Expr Postfix()
    {
        var e = Atom();

        // Calls, items and fields are placed where the expression starts, so mistakes point at the name.
        var (line, col) = (e.Line, e.Column);
        while (true)
        {
            if (EatSym("("))
            {
                e = new CallExpr(e, ListOf(")", "arguments"), line, col);
            }
            else if (EatSym("["))
            {
                var index = Expr();
                if (IsSym(":"))
                    throw Error("Wire has no slices; use text methods or a loop");

                ExpectSym("]", "to end the index");
                e = new IndexExpr(e, index, line, col);
            }
            else if (EatSym("."))
            {
                e = new AttrExpr(e, Name("a name after the dot"), line, col);
            }
            else
            {
                return e;
            }
        }
    }

    /// <summary>
    /// Comma-separated expressions up to <paramref name="close"/>, allowing a trailing comma.
    /// </summary>
    private List<Expr> ListOf(string close, string what)
    {
        var items = new List<Expr>();
        while (true)
        {
            if (EatSym(close))
                return items;

            items.Add(Expr());
            if (EatSym(close))
                return items;

            ExpectSym(",", $"between {what}");
        }
    }

    private Expr Atom()
    {
        var (line, col) = (Peek.Line, Peek.Column);
        var t = Next();
        switch (t.Kind)
        {
            case TokenKind.Int:
                return new IntExpr(t.Value, line, col);
            case TokenKind.Str:
                // Neighbouring texts join, as in Python.
                var s = t.Text;
                while (Peek.Kind == TokenKind.Str)
                {
                    s += Next().Text;
                }

                return new StrExpr(s, line, col);
            case TokenKind.Name:
                return t.Text switch
                {
                    "True" => new BoolExpr(true, line, col),
                    "False" => new BoolExpr(false, line, col),
                    "None" => new NoneExpr(line, col),
                    _ when Keywords.Contains(t.Text) =>
                        throw new WireException(line, col, $"\"{t.Text}\" can't be used here"),
                    _ => new NameExpr(t.Text, line, col),
                };
            case TokenKind.Sym when t.Text == "(":
                var e = Expr();
                if (IsSym(","))
                    throw Error("Wire has no tuples; use a list: [a, b]");

                ExpectSym(")", "to close the bracket");
                return e;
            case TokenKind.Sym when t.Text == "[":
                return new ListExpr(ListOf("]", "list items"), line, col);
            case TokenKind.Sym when t.Text == "{":
                var pairs = new List<(Expr, Expr)>();
                while (!EatSym("}"))
                {
                    var key = Expr();
                    ExpectSym(":", "between a key and its value");
                    pairs.Add((key, Expr()));
                    if (EatSym("}"))
                        break;

                    ExpectSym(",", "between dict entries");
                }

                return new DictExpr(pairs, line, col);
            default:
                throw new WireException(line, col, $"expected a value, found {Describe(t)}");
        }
    }

    private static void CheckTarget(Expr e)
    {
        switch (e)
        {
            case NameExpr or IndexExpr:
                return;
            case AttrExpr:
                throw new WireException(e.Line, e.Column, "you can't set that");
            default:
                throw new WireException(e.Line, e.Column,
                    "you can only assign to a name or an item, like x = 1 or things[0] = 1");
        }
    }
}
