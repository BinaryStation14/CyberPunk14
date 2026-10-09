using System.Globalization;
using System.Linq;
using System.Text;
using Content.Shared._CyberPunk.Machines;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Why a program's UI text isn't one. The message is for players.
/// </summary>
public sealed class ProgramUiException(string message) : Exception(message);

/// <summary>
/// Reads the UI a program describes as text, and checks it is within bounds, so clients only ever get a tree
/// they can draw. The text is S-expressions, one per widget:
/// <code>
/// (column
///   (label "Door control")
///   (row (button open "Open") (button close "Close"))
///   (input name "")
///   (list people "Ana" "Bo")
///   (progress 3 10)
///   (canvas map 200 100 (rect 0 0 10 10 "#ff0000") (line 0 0 50 50 "green") (text 4 4 "hi" "white")))
/// </code>
/// </summary>
public static class ProgramUiParser
{
    /// <summary>The longest UI text, in bytes.</summary>
    public const int MaxBytes = 32 * 1024;

    /// <summary>Widgets in one UI, at most.</summary>
    public const int MaxNodes = 256;

    /// <summary>How deep widgets can nest.</summary>
    public const int MaxDepth = 16;

    /// <summary>Canvas operations in one UI, at most, over all its canvases.</summary>
    public const int MaxOps = 512;

    /// <summary>Items in one list, at most.</summary>
    public const int MaxItems = 256;

    /// <summary>The longest text a widget or item can show, in characters.</summary>
    public const int MaxText = 1024;

    /// <summary>The biggest canvas, in pixels.</summary>
    public const int MaxCanvasWidth = 640;

    public const int MaxCanvasHeight = 400;

    /// <summary>How far outside a canvas its drawing may reach.</summary>
    private const int MaxCoordinate = 4096;

    public const int MaxIdLength = 32;

    private const string Kinds = "column, row, label, button, input, list, progress or canvas";

    /// <summary>
    /// Whether a widget id is allowed: 1 to 32 of a-z, A-Z, 0-9, _ and -.
    /// </summary>
    public static bool ValidId(string id)
    {
        return id.Length is > 0 and <= MaxIdLength && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>
    /// Reads a UI.
    /// </summary>
    /// <exception cref="ProgramUiException">It isn't one, and why.</exception>
    public static ProgramUiNode Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxBytes)
            throw new ProgramUiException($"too long ({MaxBytes / 1024} KiB at most)");

        var reader = new Reader(text);
        var expr = reader.Read(0) ?? throw new ProgramUiException("it's empty: describe a widget, like (label \"hi\")");
        if (reader.Read(0) != null)
            throw new ProgramUiException("one widget at the top, please: put them in a (column ...)");

        var counts = new Counts();
        return Widget(expr, 0, counts);
    }

    private sealed class Counts
    {
        public int Nodes;
        public int Ops;
    }

    /// <summary>
    /// A parsed S-expression: an atom (a word, or quoted text) or a list.
    /// </summary>
    private sealed record Expr(string? Atom, bool Quoted, List<Expr>? Items)
    {
        public bool IsList => Items != null;

        public override string ToString()
        {
            if (Items == null)
                return Quoted ? $"\"{Atom}\"" : Atom!;

            return Items.Count == 0 ? "()" : $"({Items[0]} ...)";
        }
    }

    private sealed class Reader(string text)
    {
        private int _at;

        /// <summary>
        /// The next expression, or null at the end.
        /// </summary>
        public Expr? Read(int depth)
        {
            SkipSpace();
            if (_at >= text.Length)
                return null;

            var c = text[_at];
            if (c == ')')
                throw new ProgramUiException("a ) with no ( before it");

            if (c == '(')
            {
                if (depth > MaxDepth + 1)
                    throw new ProgramUiException($"nested too deep ({MaxDepth} at most)");

                _at++;
                var items = new List<Expr>();
                while (true)
                {
                    SkipSpace();
                    if (_at >= text.Length)
                        throw new ProgramUiException("a ( is never closed");

                    if (text[_at] == ')')
                    {
                        _at++;
                        return new Expr(null, false, items);
                    }

                    items.Add(Read(depth + 1)!);
                }
            }

            if (c == '"')
                return new Expr(QuotedText(), true, null);

            var start = _at;
            while (_at < text.Length && !char.IsWhiteSpace(text[_at]) && text[_at] is not ('(' or ')' or '"'))
            {
                _at++;
            }

            return new Expr(text[start.._at], false, null);
        }

        private string QuotedText()
        {
            _at++;
            var result = new StringBuilder();
            while (true)
            {
                if (_at >= text.Length)
                    throw new ProgramUiException("text in quotes is never closed");

                var c = text[_at++];
                if (c == '"')
                    return result.ToString();

                if (c == '\\' && _at < text.Length)
                {
                    var next = text[_at++];
                    result.Append(next == 'n' ? '\n' : next);
                    continue;
                }

                result.Append(c);
            }
        }

        private void SkipSpace()
        {
            while (_at < text.Length && char.IsWhiteSpace(text[_at]))
            {
                _at++;
            }
        }
    }

    private static ProgramUiNode Widget(Expr expr, int depth, Counts counts)
    {
        if (expr.Items is not { Count: > 0 } items || items[0] is not { Atom: { } kind, Quoted: false })
            throw new ProgramUiException($"expected a widget, like (label \"hi\"), but found {expr}");

        if (depth >= MaxDepth)
            throw new ProgramUiException($"widgets nested too deep ({MaxDepth} at most)");

        if (++counts.Nodes > MaxNodes)
            throw new ProgramUiException($"too many widgets ({MaxNodes} at most)");

        var args = items.Skip(1).ToList();
        switch (kind)
        {
            case "column":
            case "row":
                return new ProgramUiNode
                {
                    Kind = kind == "column" ? ProgramUiKind.Column : ProgramUiKind.Row,
                    Children = args.Select(a => Widget(a, depth + 1, counts)).ToArray(),
                };
            case "label":
                Arity(kind, args, 1, 1, "(label \"text\")");
                return new ProgramUiNode { Kind = ProgramUiKind.Label, Text = Text(args[0], kind) };
            case "button":
                Arity(kind, args, 2, 2, "(button ok \"OK\")");
                return new ProgramUiNode { Kind = ProgramUiKind.Button, Id = Id(args[0], kind), Text = Text(args[1], kind) };
            case "input":
                Arity(kind, args, 1, 2, "(input name \"text it starts with\")");
                return new ProgramUiNode
                {
                    Kind = ProgramUiKind.Input,
                    Id = Id(args[0], kind),
                    Text = args.Count > 1 ? Text(args[1], kind) : "",
                };
            case "list":
                Arity(kind, args, 1, MaxItems + 1, "(list files \"a.txt\" \"b.txt\")");
                return new ProgramUiNode
                {
                    Kind = ProgramUiKind.List,
                    Id = Id(args[0], kind),
                    Items = args.Skip(1).Select(a => Text(a, kind)).ToArray(),
                };
            case "progress":
            {
                Arity(kind, args, 2, 2, "(progress 3 10)");
                var max = Number(args[1], kind, 1, 1_000_000);
                return new ProgramUiNode
                {
                    Kind = ProgramUiKind.Progress,
                    Value = Math.Clamp(Number(args[0], kind, int.MinValue, int.MaxValue), 0, max),
                    Max = max,
                };
            }
            case "canvas":
            {
                Arity(kind, args, 3, int.MaxValue, "(canvas map 200 100 (rect 0 0 10 10 \"red\") ...)");
                var node = new ProgramUiNode
                {
                    Kind = ProgramUiKind.Canvas,
                    Id = Id(args[0], kind),
                    Width = Number(args[1], kind, 1, MaxCanvasWidth),
                    Height = Number(args[2], kind, 1, MaxCanvasHeight),
                };

                var ops = new List<CanvasOp>();
                foreach (var op in args.Skip(3))
                {
                    if (++counts.Ops > MaxOps)
                        throw new ProgramUiException($"too much drawing ({MaxOps} rect, line and text at most)");

                    ops.Add(Op(op));
                }

                node.Ops = ops.ToArray();
                return node;
            }
            default:
                throw new ProgramUiException($"there's no widget called {kind} ({Kinds})");
        }
    }

    private static CanvasOp Op(Expr expr)
    {
        if (expr.Items is not { Count: > 0 } items || items[0] is not { Atom: { } kind, Quoted: false })
            throw new ProgramUiException($"expected drawing, like (rect 0 0 10 10 \"red\"), but found {expr}");

        var args = items.Skip(1).ToList();
        switch (kind)
        {
            case "rect":
            case "line":
                Arity(kind, args, 5, 5, kind == "rect" ? "(rect X Y WIDTH HEIGHT \"color\")" : "(line X1 Y1 X2 Y2 \"color\")");
                return new CanvasOp
                {
                    Kind = kind == "rect" ? CanvasOpKind.Rect : CanvasOpKind.Line,
                    X = Coordinate(args[0], kind),
                    Y = Coordinate(args[1], kind),
                    A = Coordinate(args[2], kind),
                    B = Coordinate(args[3], kind),
                    Color = Colour(args[4], kind),
                };
            case "text":
                Arity(kind, args, 4, 4, "(text X Y \"words\" \"color\")");
                return new CanvasOp
                {
                    Kind = CanvasOpKind.Text,
                    X = Coordinate(args[0], kind),
                    Y = Coordinate(args[1], kind),
                    Text = Text(args[2], kind),
                    Color = Colour(args[3], kind),
                };
            default:
                throw new ProgramUiException($"a canvas can't draw {kind} (rect, line or text)");
        }
    }

    private static void Arity(string kind, List<Expr> args, int min, int max, string example)
    {
        if (args.Count < min || args.Count > max)
            throw new ProgramUiException($"{kind} is written {example}");
    }

    private static string Atom(Expr expr, string kind)
    {
        return expr.Atom ?? throw new ProgramUiException($"{kind} takes text or a number here, not {expr}");
    }

    private static string Text(Expr expr, string kind)
    {
        var text = Atom(expr, kind);
        if (text.Length > MaxText)
            throw new ProgramUiException($"{kind}'s text is too long ({MaxText} characters at most)");

        return text;
    }

    private static string Id(Expr expr, string kind)
    {
        var id = Atom(expr, kind);
        if (!ValidId(id))
            throw new ProgramUiException($"{kind}'s id {expr} should be 1 to {MaxIdLength} of letters, digits, _ and -");

        return id;
    }

    private static int Number(Expr expr, string kind, int min, int max)
    {
        if (!int.TryParse(Atom(expr, kind), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n))
            throw new ProgramUiException($"{kind} needs a whole number, not {expr}");

        if (n < min || n > max)
            throw new ProgramUiException($"{kind} needs a number from {min} to {max}, not {n}");

        return n;
    }

    private static int Coordinate(Expr expr, string kind)
    {
        return Number(expr, kind, -MaxCoordinate, MaxCoordinate);
    }

    private static Color Colour(Expr expr, string kind)
    {
        var name = Atom(expr, kind);
        if (name.StartsWith('#') && Color.TryFromHex(name, out var hex))
            return hex;

        if (Color.TryFromName(name, out var named))
            return named;

        throw new ProgramUiException($"{kind} needs a color, like \"#ff8000\" or \"red\", not {expr}");
    }
}
