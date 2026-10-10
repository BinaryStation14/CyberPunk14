using System.Linq;
using System.Text;

namespace Content.Shared._CyberPunk.Machines;

/// <summary>
/// Turns keys into lines while the terminal isn't in raw mode, as a real terminal does: it echoes what's typed,
/// Backspace rubs out, Up and Down step through earlier lines, and Enter hands the line over.
/// </summary>
public sealed class LineEditor
{
    /// <summary>How many earlier lines Up can bring back.</summary>
    public const int HistoryLimit = 32;

    private readonly StringBuilder _screen;
    private readonly StringBuilder _line = new();
    private readonly List<string> _history = new();

    /// <summary>The history entry on the line, or <c>_history.Count</c> for a new line.</summary>
    private int _recalled;

    /// <param name="screen">Where the echo goes.</param>
    public LineEditor(StringBuilder screen)
    {
        _screen = screen;
    }

    /// <summary>
    /// Handles a key, and returns the line when it's Enter.
    /// </summary>
    public string? Key(int key)
    {
        switch (key)
        {
            case TerminalKeys.Enter:
                return Submit();
            case TerminalKeys.Backspace:
                if (_line.Length > 0)
                {
                    _line.Length -= char.IsLowSurrogate(_line[^1]) ? 2 : 1;
                    _screen.Append(TerminalText.Backspace);
                }

                return null;
            case TerminalKeys.Up:
                Recall(_recalled - 1);
                return null;
            case TerminalKeys.Down:
                Recall(_recalled + 1);
                return null;
        }

        if (key is >= 0x20 and <= 0x10FFFF and not TerminalKeys.Delete
            && TerminalKeys.Valid(key)
            && _line.Length < TerminalText.MaxLine)
        {
            Type(char.ConvertFromUtf32(key));
        }

        return null;
    }

    /// <summary>
    /// Types text onto the end of the line.
    /// </summary>
    public void Type(string text)
    {
        _line.Append(text);
        _screen.Append(text);
    }

    /// <summary>
    /// Ends the line, as Enter does, and returns it.
    /// </summary>
    public string Submit()
    {
        var line = _line.ToString();
        _line.Clear();
        _screen.Append('\n');

        if (line != "" && (_history.Count == 0 || _history[^1] != line))
        {
            _history.Add(line);
            if (_history.Count > HistoryLimit)
                _history.RemoveAt(0);
        }

        _recalled = _history.Count;
        return line;
    }

    /// <summary>
    /// Drops the line being typed, leaving its echo on the screen.
    /// </summary>
    public void Clear()
    {
        _line.Clear();
        _recalled = _history.Count;
    }

    private void Recall(int index)
    {
        if (index < 0 || index > _history.Count)
            return;

        _screen.Append(TerminalText.Backspace, _line.ToString().EnumerateRunes().Count());
        Clear();
        _recalled = index;
        if (index < _history.Count)
            Type(_history[index]);
    }
}
