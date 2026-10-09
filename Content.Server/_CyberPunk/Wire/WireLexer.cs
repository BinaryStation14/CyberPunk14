using System.Globalization;
using System.Text;

namespace Content.Server._CyberPunk.Wire;

/// <summary>
/// Turns Wire source into tokens, Python style: indentation at the start of a line opens and closes blocks (as
/// <see cref="TokenKind.Indent"/> and <see cref="TokenKind.Dedent"/> tokens), and a line ends a statement unless a
/// bracket is still open. Ported from Switchboard's <c>wasm/wire/src/lexer.rs</c>.
/// </summary>
public static class WireLexer
{
    /// <summary>Longest first, so <c>//=</c> wins over <c>//</c> and <c>/</c>.</summary>
    private static readonly string[] Syms =
    {
        "//=", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "//", "+", "-", "*", "/", "%",
        "<", ">", "=", "(", ")", "[", "]", "{", "}", ",", ":", ".",
    };

    /// <exception cref="WireException">The source has a mistake.</exception>
    public static List<Token> Lex(string source)
    {
        var tokens = new List<Token>();
        var indents = new List<int> { 0 };
        var depth = 0;
        var lastLine = 0;
        var lines = source.Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = i + 1;
            lastLine = line;
            var text = lines[i].TrimEnd('\r');
            var pos = 0;

            if (depth == 0)
            {
                // Indentation: tabs count as four spaces.
                var width = 0;
                while (pos < text.Length && text[pos] is ' ' or '\t')
                {
                    width += text[pos] == '\t' ? 4 : 1;
                    pos++;
                }

                if (pos == text.Length || text[pos] == '#')
                    continue;

                if (width > indents[^1])
                {
                    indents.Add(width);
                    tokens.Add(new Token(TokenKind.Indent, "", 0, line, 1));
                }
                else
                {
                    while (width < indents[^1])
                    {
                        indents.RemoveAt(indents.Count - 1);
                        tokens.Add(new Token(TokenKind.Dedent, "", 0, line, 1));
                    }

                    if (width != indents[^1])
                        throw new WireException(line, 1, "this line's indentation doesn't match any block above it");
                }
            }

            var any = false;
            while (pos < text.Length)
            {
                var c = text[pos];
                var col = pos + 1;
                if (c is ' ' or '\t')
                {
                    pos++;
                    continue;
                }

                if (c == '#')
                    break;

                any = true;
                if (char.IsAsciiDigit(c))
                {
                    var start = pos;
                    while (pos < text.Length && (char.IsAsciiLetterOrDigit(text[pos]) || text[pos] == '_'))
                    {
                        pos++;
                    }

                    var digits = text[start..pos].Replace("_", "");
                    if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                        throw new WireException(line, col, $"\"{digits}\" isn't a number Wire understands (whole numbers only)");

                    tokens.Add(new Token(TokenKind.Int, digits, value, line, col));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    var start = pos;
                    while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_'))
                    {
                        pos++;
                    }

                    tokens.Add(new Token(TokenKind.Name, text[start..pos], 0, line, col));
                    continue;
                }

                if (c is '"' or '\'')
                {
                    pos++;
                    var s = new StringBuilder();
                    while (true)
                    {
                        if (pos >= text.Length)
                            throw new WireException(line, col, "this text has no closing quote");

                        var ch = text[pos++];
                        if (ch == c)
                            break;

                        if (ch != '\\')
                        {
                            s.Append(ch);
                            continue;
                        }

                        if (pos >= text.Length)
                            throw new WireException(line, col, "this text has no closing quote");

                        var esc = text[pos++];
                        s.Append(esc switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '\'' => '\'',
                            '"' => '"',
                            '0' => '\0',
                            _ => throw new WireException(line, pos,
                                $"\\{esc} isn't an escape Wire knows (use \\n, \\t, \\\\, \\' or \\\")"),
                        });
                    }

                    tokens.Add(new Token(TokenKind.Str, s.ToString(), 0, line, col));
                    continue;
                }

                var sym = Array.Find(Syms, s => string.CompareOrdinal(text, pos, s, 0, s.Length) == 0);
                if (sym == null)
                    throw new WireException(line, col, $"'{c}' doesn't belong here");

                switch (sym)
                {
                    case "(" or "[" or "{":
                        depth++;
                        break;
                    case ")" or "]" or "}":
                        if (depth == 0)
                            throw new WireException(line, col, $"this \"{sym}\" closes nothing");

                        depth--;
                        break;
                }

                pos += sym.Length;
                tokens.Add(new Token(TokenKind.Sym, sym, 0, line, col));
            }

            if (any && depth == 0)
                tokens.Add(new Token(TokenKind.Newline, "", 0, line, text.Length + 1));
        }

        if (depth > 0)
            throw new WireException(lastLine, 1, "a bracket is still open at the end of the file");

        if (tokens.Count > 0 && tokens[^1].Kind != TokenKind.Newline)
            tokens.Add(new Token(TokenKind.Newline, "", 0, lastLine, 1));

        while (indents.Count > 1)
        {
            indents.RemoveAt(indents.Count - 1);
            tokens.Add(new Token(TokenKind.Dedent, "", 0, lastLine, 1));
        }

        tokens.Add(new Token(TokenKind.Eof, "", 0, lastLine + 1, 1));
        return tokens;
    }
}
