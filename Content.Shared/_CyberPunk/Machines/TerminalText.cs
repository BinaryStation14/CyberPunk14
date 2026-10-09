namespace Content.Shared._CyberPunk.Machines;

/// <summary>
/// The text a machine's terminal shows, and the control characters a program's output can carry. The server
/// keeps every terminal's screen this way, and clients keep their copy the same way.
/// </summary>
public static class TerminalText
{
    /// <summary>Rubs out the last character on the line.</summary>
    public const char Backspace = '\b';

    /// <summary>Clears the screen.</summary>
    public const char Clear = '\x0c';

    /// <summary>How much output a terminal keeps for people who open it later, in characters.</summary>
    public const int ScrollbackLimit = 16 * 1024;

    /// <summary>The longest line a player can type into a terminal, in characters.</summary>
    public const int MaxLine = 200;

    /// <summary>
    /// Adds output to a screen: a clear wipes what came before it, a backspace rubs out the character before
    /// it on its line, and the rest is kept up to <see cref="ScrollbackLimit"/>, dropping whole old lines.
    /// </summary>
    public static string Apply(string screen, string text)
    {
        var builder = new System.Text.StringBuilder(screen, screen.Length + text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case Clear:
                    builder.Clear();
                    break;
                case Backspace:
                    if (builder.Length > 0 && builder[^1] != '\n')
                    {
                        var width = builder.Length > 1 && char.IsLowSurrogate(builder[^1]) ? 2 : 1;
                        builder.Remove(builder.Length - width, width);
                    }
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        if (builder.Length <= ScrollbackLimit)
            return builder.ToString();

        var result = builder.ToString();
        var cut = result.Length - ScrollbackLimit;
        var newline = result.IndexOf('\n', cut);
        if (newline >= 0)
            cut = newline + 1;
        else if (char.IsLowSurrogate(result[cut]))
            cut++;

        return result[cut..];
    }
}

/// <summary>
/// The codes keys have in raw mode: a character's own code, or one of these.
/// </summary>
public static class TerminalKeys
{
    public const int Backspace = 8;
    public const int Tab = 9;
    public const int Enter = 10;
    public const int Delete = 127;
    public const int Up = 0x11_0001;
    public const int Down = 0x11_0002;
    public const int Left = 0x11_0003;
    public const int Right = 0x11_0004;
    public const int Home = 0x11_0005;
    public const int End = 0x11_0006;
    public const int PageUp = 0x11_0007;
    public const int PageDown = 0x11_0008;

    /// <summary>
    /// Ctrl and a letter: 1 for Ctrl+A to 26 for Ctrl+Z.
    /// </summary>
    public static int Ctrl(char letter) => char.ToLowerInvariant(letter) - 'a' + 1;

    /// <summary>
    /// Whether a key code is one a program can be sent: Ctrl+letter, Backspace, Tab, Enter, Delete, the
    /// movement keys, or a printable character.
    /// </summary>
    public static bool Valid(int key)
    {
        if (key is >= 1 and <= 26 or Delete or >= Up and <= PageDown)
            return true;

        // A character that isn't a control character (those are all below U+00A0) or half a surrogate pair.
        return key is >= 0x20 and < 0x7F or >= 0xA0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF);
    }
}
