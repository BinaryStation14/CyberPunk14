using System.Linq;
using System.Numerics;
using System.Text;
using Content.Client.Resources;
using Content.Shared._CyberPunk.Machines;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// A machine's terminal screen: a grid of <see cref="Columns"/> by <see cref="Rows"/> monospace characters
/// showing the end of its output, which the mouse wheel scrolls back through. In raw mode it takes the keyboard
/// while it has focus and reports every key with <see cref="OnKey"/>.
/// </summary>
public sealed partial class TerminalScreen : Control
{
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IResourceCache _cache = default!;

    /// <summary>The terminal's size, as programs see it through <c>term_size</c>.</summary>
    public const int Columns = 80;
    public const int Rows = 24;

    private const int FontSize = 12;
    private const int Padding = 4;

    private static readonly Color Background = Color.FromHex("#0b0f0c");
    private static readonly Color Foreground = Color.FromHex("#7cfc9a");

    private readonly Font _font;
    private readonly List<string> _lines = new();
    private string _screen = "";
    private bool _raw;
    private int _scroll;
    private TimeSpan _blink;

    /// <summary>
    /// A key pressed in raw mode, as one of the codes in <see cref="TerminalKeys"/>.
    /// </summary>
    public event Action<int>? OnKey;

    public TerminalScreen()
    {
        IoCManager.InjectDependencies(this);
        _font = _cache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", FontSize);

        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
    }

    /// <summary>
    /// Whether the terminal is in raw mode.
    /// </summary>
    public bool Raw => _raw;

    /// <summary>
    /// Everything the terminal has kept of its output.
    /// </summary>
    public string Text => _screen;

    /// <summary>
    /// Replaces everything on the screen.
    /// </summary>
    public void SetScreen(string screen, bool raw)
    {
        _screen = screen;
        _raw = raw;
        Refresh();
    }

    /// <summary>
    /// Adds new output to the screen.
    /// </summary>
    public void AddOutput(string text)
    {
        _screen = TerminalText.Apply(_screen, ref _raw, text);
        Refresh();
    }

    private void Refresh()
    {
        _lines.Clear();
        WrapLines(_screen, Columns, _lines);
        _scroll = 0;
    }

    /// <summary>
    /// Splits text into the rows a terminal shows it on: one for each line, and more for lines longer than the
    /// terminal is wide.
    /// </summary>
    public static void WrapLines(string text, int columns, List<string> rows)
    {
        var row = new StringBuilder(columns);
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == '\n')
            {
                rows.Add(row.ToString());
                row.Clear();
                width = 0;
                continue;
            }

            if (rune.Value == '\r' || Rune.IsControl(rune))
                continue;

            if (width == columns)
            {
                rows.Add(row.ToString());
                row.Clear();
                width = 0;
            }

            row.Append(rune.ToString());
            width++;
        }

        rows.Add(row.ToString());
    }

    private float CellWidth(float scale)
    {
        return _font.TryGetCharMetrics(new Rune('M'), scale, out var metrics) ? metrics.Advance : FontSize * scale;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        var scale = UIScale;
        return new Vector2(CellWidth(scale) * Columns + Padding * 2 * scale,
            _font.GetLineHeight(scale) * Rows + Padding * 2 * scale) / scale;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        handle.DrawRect(PixelSizeBox, Background);

        var cell = CellWidth(scale);
        var lineHeight = _font.GetLineHeight(scale);
        var ascent = _font.GetAscent(scale);
        var padding = Padding * scale;

        var last = _lines.Count - 1 - _scroll;
        var first = Math.Max(0, last - Rows + 1);
        var y = padding;
        for (var i = first; i <= last; i++)
        {
            var x = padding;
            foreach (var rune in _lines[i].EnumerateRunes())
            {
                _font.DrawChar(handle, rune, new Vector2(x, y + ascent), scale, Foreground);
                x += cell;
            }

            y += lineHeight;
        }

        // A block cursor after the last character, blinking while the screen has the keyboard.
        if (_raw && _scroll == 0 && HasKeyboardFocus() && _blink.TotalSeconds % 1 < 0.5 && _lines.Count > 0)
        {
            var column = Math.Min(Columns - 1, _lines[^1].EnumerateRunes().Count());
            var top = padding + (last - first) * lineHeight;
            handle.DrawRect(UIBox2.FromDimensions(padding + column * cell, top, cell, lineHeight), Foreground.WithAlpha(0.6f));
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        _blink += TimeSpan.FromSeconds(args.DeltaSeconds);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);

        var max = Math.Max(0, _lines.Count - Rows);
        _scroll = Math.Clamp(_scroll + (int) Math.Round(args.Delta.Y) * 3, 0, max);
        args.Handle();
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();
        _input.FirstChanceOnKeyEvent += OnFirstChanceKey;
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();
        _input.FirstChanceOnKeyEvent -= OnFirstChanceKey;
    }

    /// <summary>
    /// In raw mode, takes the keys a program needs before the game or the UI can bind them to something else,
    /// such as Ctrl+C or the arrow keys. Printable characters come through <see cref="TextEntered"/>.
    /// </summary>
    private void OnFirstChanceKey(KeyEventArgs args, KeyEventType type)
    {
        if (type == KeyEventType.Up || !_raw || !HasKeyboardFocus())
            return;

        if (KeyCode(args) is not { } code)
            return;

        args.Handle();
        _scroll = 0;
        OnKey?.Invoke(code);
    }

    private static int? KeyCode(KeyEventArgs args)
    {
        switch (args.Key)
        {
            case Keyboard.Key.BackSpace: return TerminalKeys.Backspace;
            case Keyboard.Key.Tab: return TerminalKeys.Tab;
            case Keyboard.Key.Return:
            case Keyboard.Key.NumpadEnter: return TerminalKeys.Enter;
            case Keyboard.Key.Delete: return TerminalKeys.Delete;
            case Keyboard.Key.Up: return TerminalKeys.Up;
            case Keyboard.Key.Down: return TerminalKeys.Down;
            case Keyboard.Key.Left: return TerminalKeys.Left;
            case Keyboard.Key.Right: return TerminalKeys.Right;
            case Keyboard.Key.Home: return TerminalKeys.Home;
            case Keyboard.Key.End: return TerminalKeys.End;
            case Keyboard.Key.PageUp: return TerminalKeys.PageUp;
            case Keyboard.Key.PageDown: return TerminalKeys.PageDown;
        }

        // Ctrl and a letter, but not AltGr, which some layouts type characters with.
        if (args.Control && !args.Alt && !args.AltGr && args.Key is >= Keyboard.Key.A and <= Keyboard.Key.Z)
            return TerminalKeys.Ctrl((char) ('a' + (args.Key - Keyboard.Key.A)));

        return null;
    }

    protected override void TextEntered(GUITextEnteredEventArgs args)
    {
        base.TextEntered(args);

        if (!_raw)
            return;

        foreach (var rune in args.TextEnteredEvent.Text.EnumerateRunes())
        {
            if (TerminalKeys.Valid(rune.Value))
                OnKey?.Invoke(rune.Value);
        }

        _scroll = 0;
    }
}
