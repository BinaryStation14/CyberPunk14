using System.Globalization;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._CyberPunk.Machines;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

namespace Content.Client._CyberPunk.Machines;

/// <summary>
/// Draws the UI a program shows (a tree of <see cref="ProgramUiNode"/>) with the game's own controls, and
/// reports what is done to it with <see cref="OnEvent"/>.
/// </summary>
public sealed class ProgramUiView : BoxContainer
{
    /// <summary>
    /// Someone used a widget: its id, what they did, and the value the program gets.
    /// </summary>
    public event Action<string, ProgramUiEventKind, string>? OnEvent;

    /// <summary>
    /// The inputs on show, by id, with the text the program gave each, so text being typed survives the program
    /// showing its UI again.
    /// </summary>
    private Dictionary<string, (string Given, LineEdit Edit)> _inputs = new();

    public ProgramUiView()
    {
        Orientation = LayoutOrientation.Vertical;
        Margin = new Thickness(6);
    }

    /// <summary>
    /// Shows a program's UI, or nothing.
    /// </summary>
    public void SetUi(ProgramUiNode? root)
    {
        var old = _inputs;
        _inputs = new Dictionary<string, (string, LineEdit)>();
        RemoveAllChildren();

        if (root != null)
            AddChild(Build(root, old));
    }

    private Control Build(ProgramUiNode node, Dictionary<string, (string Given, LineEdit Edit)> old)
    {
        switch (node.Kind)
        {
            case ProgramUiKind.Column:
            case ProgramUiKind.Row:
            {
                var box = new BoxContainer
                {
                    Orientation = node.Kind == ProgramUiKind.Column
                        ? LayoutOrientation.Vertical
                        : LayoutOrientation.Horizontal,
                    SeparationOverride = 4,
                };

                foreach (var child in node.Children)
                {
                    box.AddChild(Build(child, old));
                }

                return box;
            }
            case ProgramUiKind.Label:
                return new Label { Text = node.Text };
            case ProgramUiKind.Button:
            {
                var button = new Button { Text = node.Text };
                var id = node.Id;
                button.OnPressed += _ => OnEvent?.Invoke(id, ProgramUiEventKind.Click, "");
                return button;
            }
            case ProgramUiKind.Input:
            {
                // The same input with the same text from the program keeps whatever has been typed into it.
                var text = old.TryGetValue(node.Id, out var was) && was.Given == node.Text ? was.Edit.Text : node.Text;
                var edit = new LineEdit { Text = text, MinWidth = 160, HorizontalExpand = true };
                var id = node.Id;
                edit.OnTextEntered += args => OnEvent?.Invoke(id, ProgramUiEventKind.Submit, args.Text);
                _inputs[id] = (node.Text, edit);
                return edit;
            }
            case ProgramUiKind.List:
            {
                var list = new ItemList { MinHeight = 80, MinWidth = 160, SelectMode = ItemList.ItemListSelectMode.Button };
                foreach (var item in node.Items)
                {
                    list.AddItem(item);
                }

                var id = node.Id;
                list.OnItemSelected += args =>
                    OnEvent?.Invoke(id, ProgramUiEventKind.Select, args.ItemIndex.ToString(CultureInfo.InvariantCulture));
                return list;
            }
            case ProgramUiKind.Progress:
                return new ProgressBar
                {
                    MinValue = 0,
                    MaxValue = node.Max,
                    Value = node.Value,
                    MinHeight = 16,
                    HorizontalExpand = true,
                };
            case ProgramUiKind.Canvas:
            {
                var canvas = new ProgramCanvas(node);
                var id = node.Id;
                canvas.OnClick += (x, y) => OnEvent?.Invoke(id, ProgramUiEventKind.Click, $"{x} {y}");
                return canvas;
            }
            default:
                return new Control();
        }
    }
}

/// <summary>
/// A program's drawing: rectangles, lines and text on a black background, clipped to its size.
/// </summary>
public sealed partial class ProgramCanvas : Control
{
    [Dependency] private IResourceCache _cache = default!;

    private const int FontSize = 12;

    private readonly ProgramUiNode _node;
    private readonly Font _font;

    /// <summary>
    /// The canvas was clicked here, in its own pixels.
    /// </summary>
    public event Action<int, int>? OnClick;

    public ProgramCanvas(ProgramUiNode node)
    {
        IoCManager.InjectDependencies(this);
        _node = node;
        _font = _cache.GetFont("/Fonts/RobotoMono/RobotoMono-Regular.ttf", FontSize);

        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        HorizontalAlignment = HAlignment.Left;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return new Vector2(_node.Width, _node.Height);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var scale = UIScale;
        handle.DrawRect(UIBox2.FromDimensions(0, 0, _node.Width * scale, _node.Height * scale), Color.Black);
        foreach (var op in _node.Ops)
        {
            var at = new Vector2(op.X, op.Y) * scale;
            switch (op.Kind)
            {
                case CanvasOpKind.Rect:
                    handle.DrawRect(UIBox2.FromDimensions(at, new Vector2(op.A, op.B) * scale), op.Color);
                    break;
                case CanvasOpKind.Line:
                    handle.DrawLine(at, new Vector2(op.A, op.B) * scale, op.Color);
                    break;
                case CanvasOpKind.Text:
                    handle.DrawString(_font, at, op.Text, scale, op.Color);
                    break;
            }
        }
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);

        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        var x = (int) Math.Floor(args.RelativePosition.X);
        var y = (int) Math.Floor(args.RelativePosition.Y);
        if (x < 0 || y < 0 || x >= _node.Width || y >= _node.Height)
            return;

        args.Handle();
        OnClick?.Invoke(x, y);
    }
}
