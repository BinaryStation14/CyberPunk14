using System;
using System.Linq;
using System.Text;
using Content.Server._CyberPunk.Wasm;
using Content.Server._CyberPunk.Wire;
using Content.Shared._CyberPunk.Machines;
using NUnit.Framework;
using Robust.Shared.Maths;

#nullable enable

namespace Content.Tests.Server._CyberPunk;

/// <summary>
/// Programs showing a UI at a computer's terminal: the text they describe it in, and the events that come back.
/// </summary>
[TestFixture]
[TestOf(typeof(ProgramUiParser))]
public sealed class ProgramUiTest
{
    private WasmHost _host = default!;

    [OneTimeSetUp]
    public void Setup()
    {
        _host = new WasmHost();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _host.Dispose();
    }

    [Test]
    public void TheParserReadsEveryWidget()
    {
        var root = ProgramUiParser.Parse("""
            (column
              (label "Door \"control\"")
              (row (button open "Open") (button shut "Shut"))
              (input name)
              (input note "hi")
              (list people "Ana" "Bo")
              (progress 30 10)
              (canvas map 200 100 (rect 0 0 10 10 "#ff0000") (line -5 0 50 50 green) (text 4 4 "hi" white)))
            """);

        Assert.That(root.Kind, Is.EqualTo(ProgramUiKind.Column));
        Assert.That(root.Children.Select(c => c.Kind), Is.EqualTo(new[]
        {
            ProgramUiKind.Label, ProgramUiKind.Row, ProgramUiKind.Input, ProgramUiKind.Input, ProgramUiKind.List,
            ProgramUiKind.Progress, ProgramUiKind.Canvas,
        }));
        Assert.That(root.Children[0].Text, Is.EqualTo("Door \"control\""));
        Assert.That(root.Children[1].Children.Select(b => (b.Id, b.Text)), Is.EqualTo(new[] { ("open", "Open"), ("shut", "Shut") }));
        Assert.That((root.Children[2].Id, root.Children[2].Text), Is.EqualTo(("name", "")));
        Assert.That(root.Children[3].Text, Is.EqualTo("hi"));
        Assert.That(root.Children[4].Items, Is.EqualTo(new[] { "Ana", "Bo" }));

        // A bar can't be fuller than full.
        Assert.That((root.Children[5].Value, root.Children[5].Max), Is.EqualTo((10, 10)));

        var canvas = root.Children[6];
        Assert.That((canvas.Id, canvas.Width, canvas.Height), Is.EqualTo(("map", 200, 100)));
        Assert.That(canvas.Ops.Select(o => o.Kind), Is.EqualTo(new[] { CanvasOpKind.Rect, CanvasOpKind.Line, CanvasOpKind.Text }));
        Assert.That(canvas.Ops[0].Color, Is.EqualTo(Color.FromHex("#ff0000")));
        Assert.That((canvas.Ops[1].X, canvas.Ops[1].B), Is.EqualTo((-5, 50)));
        Assert.That(canvas.Ops[2].Text, Is.EqualTo("hi"));
    }

    [TestCase("", "it's empty")]
    [TestCase("(label \"a\") (label \"b\")", "one widget at the top")]
    [TestCase("(label \"a\"", "never closed")]
    [TestCase(")", "no ( before it")]
    [TestCase("(label \"a)", "never closed")]
    [TestCase("(slider x)", "no widget called slider")]
    [TestCase("label", "expected a widget")]
    [TestCase("(button \"Go\")", "button is written")]
    [TestCase("(button \"bad id\" \"Go\")", "should be 1 to 32")]
    [TestCase("(progress x 10)", "whole number")]
    [TestCase("(progress 1 0)", "from 1 to 1000000")]
    [TestCase("(canvas c 641 10)", "from 1 to 640")]
    [TestCase("(canvas c 10 10 (circle 1 1 1 red))", "can't draw circle")]
    [TestCase("(canvas c 10 10 (rect 1 1 1 1 notacolor))", "needs a color")]
    [TestCase("(canvas c 10 10 (rect 1 1 1 5000 red))", "from -4096 to 4096")]
    [TestCase("(column (row))", "")]
    public void TheParserSaysWhatsWrong(string text, string why)
    {
        if (why == "")
        {
            Assert.DoesNotThrow(() => ProgramUiParser.Parse(text));
            return;
        }

        var e = Assert.Throws<ProgramUiException>(() => ProgramUiParser.Parse(text))!;
        Assert.That(e.Message, Does.Contain(why));
    }

    [Test]
    public void TheParserHasLimits()
    {
        Assert.That(Error(new string(' ', ProgramUiParser.MaxBytes + 1)), Does.Contain("too long"));
        Assert.That(Error($"(label \"{new string('a', ProgramUiParser.MaxText + 1)}\")"), Does.Contain("too long"));

        var labels = string.Concat(Enumerable.Repeat("(label \"a\")", ProgramUiParser.MaxNodes));
        Assert.That(Error($"(column {labels})"), Does.Contain("too many widgets"));

        var deep = string.Concat(Enumerable.Repeat("(column ", ProgramUiParser.MaxDepth + 1)) + new string(')', ProgramUiParser.MaxDepth + 1);
        Assert.That(Error(deep), Does.Contain("too deep"));

        // Far deeper than any UI is turned away before it's read, rather than overflowing the stack.
        var abyss = new string('(', 30_000);
        Assert.That(Error(abyss), Does.Contain("too deep"));

        var rects = string.Concat(Enumerable.Repeat("(rect 0 0 1 1 red)", ProgramUiParser.MaxOps + 1));
        Assert.That(Error($"(canvas c 10 10 {rects})"), Does.Contain("too much drawing"));

        var items = string.Concat(Enumerable.Repeat(" \"a\"", ProgramUiParser.MaxItems + 1));
        Assert.That(Error($"(list l{items})"), Does.Contain("list is written"));
    }

    private static string Error(string text)
    {
        return Assert.Throws<ProgramUiException>(() => ProgramUiParser.Parse(text))!.Message;
    }

    private const string Panel = """
        ui.show(ui.column([
            ui.label("hi"),
            ui.button("go", "Go"),
            ui.input("name", "x"),
            ui.list("pick", ["a", "b"]),
            ui.progress(3, 10),
            ui.canvas("map", 20, 10, [ui.rect(0, 0, 5, 5, "red"), ui.line(0, 0, 9, 9, "#00ff00"), ui.text(1, 1, "t", "white")]),
        ]))
        def tick():
            for e in ui.events():
                print(e.kind, e.id, "[" + e.value + "]")
                if e.id == "go":
                    ui.clear()
                if e.value == "quit":
                    sys.exit()
        """;

    private Vm Boot(string source, DeviceKind kind = DeviceKind.Computer)
    {
        var vm = Vm.WithFirmware("prog", _host.Load(WireCompiler.Compile(source)), kind);
        vm.PowerOn(_host);
        return vm;
    }

    private string Tick(Vm vm, int ticks = 1)
    {
        var output = new StringBuilder();
        for (var i = 0; i < ticks; i++)
        {
            vm.Tick(_host, 33, WasmHost.FuelPerCall);
            output.Append(vm.TakeOutput());
        }

        return output.ToString();
    }

    [Test]
    public void AWireProgramShowsAUiAndHearsWhatIsDoneToIt()
    {
        using var vm = Boot(Panel);
        Tick(vm);

        Assert.That(vm.Ui, Is.Not.Null);
        var root = vm.Ui!;
        Assert.That(root.Children.Select(c => c.Kind), Is.EqualTo(new[]
        {
            ProgramUiKind.Label, ProgramUiKind.Button, ProgramUiKind.Input, ProgramUiKind.List, ProgramUiKind.Progress,
            ProgramUiKind.Canvas,
        }));
        Assert.That(root.Children[5].Ops.Select(o => o.Kind), Is.EqualTo(new[] { CanvasOpKind.Rect, CanvasOpKind.Line, CanvasOpKind.Text }));

        // Showing the same UI again keeps the same tree, so nothing is sent to the terminal again.
        Tick(vm, 3);
        Assert.That(vm.Ui, Is.SameAs(root));

        Assert.That(vm.UiEvent("name", ProgramUiEventKind.Submit, "Ana"), Is.True);
        Assert.That(vm.UiEvent("pick", ProgramUiEventKind.Select, "1"), Is.True);
        Assert.That(vm.UiEvent("map", ProgramUiEventKind.Click, "19 0"), Is.True);
        Assert.That(Tick(vm), Is.EqualTo("submit name [Ana]\nselect pick [1]\nclick map [19 0]\n"));

        Assert.That(vm.UiEvent("go", ProgramUiEventKind.Click, ""), Is.True);
        Assert.That(Tick(vm), Is.EqualTo("click go []\n"));
        Assert.That(vm.Ui, Is.Null);
    }

    [TestCase("nope", ProgramUiEventKind.Click, "")]
    [TestCase("go", ProgramUiEventKind.Click, "1")]
    [TestCase("go", ProgramUiEventKind.Submit, "")]
    [TestCase("name", ProgramUiEventKind.Submit, "two\nlines")]
    [TestCase("pick", ProgramUiEventKind.Select, "2")]
    [TestCase("pick", ProgramUiEventKind.Select, "-1")]
    [TestCase("pick", ProgramUiEventKind.Select, "01")]
    [TestCase("map", ProgramUiEventKind.Click, "20 0")]
    [TestCase("map", ProgramUiEventKind.Click, "1 10")]
    [TestCase("map", ProgramUiEventKind.Click, "1")]
    [TestCase("", ProgramUiEventKind.Click, "")]
    public void EventsTheUiCantSendAreDropped(string id, ProgramUiEventKind kind, string value)
    {
        using var vm = Boot(Panel);
        Tick(vm);

        Assert.That(vm.UiEvent(id, kind, value), Is.False);
        Assert.That(Tick(vm), Is.Empty);
    }

    [Test]
    public void EventsQueueOnlySoFar()
    {
        using var vm = Boot(Panel);
        Tick(vm);

        for (var i = 0; i < WasmHost.UiEventLimit; i++)
        {
            Assert.That(vm.UiEvent("name", ProgramUiEventKind.Submit, "x"), Is.True);
        }

        Assert.That(vm.UiEvent("name", ProgramUiEventKind.Submit, "x"), Is.False);
        Assert.That(Tick(vm).Split('\n', StringSplitOptions.RemoveEmptyEntries), Has.Length.EqualTo(WasmHost.UiEventLimit));
    }

    [Test]
    public void AMistakeInTheUiStopsTheProgramAndSaysWhy()
    {
        using var vm = Boot("ui.show(\"(slider x)\")");
        var output = Tick(vm);

        Assert.That(output, Does.Contain("ui.show: there's no widget called slider"));
        Assert.That(vm.Ui, Is.Null);
    }

    [Test]
    public void OnlyComputersShowUis()
    {
        using var vm = Boot("ui.show(ui.label(\"hi\"))", DeviceKind.DoorController);
        Assert.That(Tick(vm), Does.Contain("ui.show only works on computers"));
        Assert.That(vm.Ui, Is.Null);
    }

    [Test]
    public void TheUiIsTheFrontProgramsAndJobsHaveNone()
    {
        using var vm = new Vm();
        vm.PowerOn(_host);
        vm.SeedFile("panel.wire", Encoding.UTF8.GetBytes(Panel));
        vm.SeedFile("job.wire", "print(ui.show(ui.label(\"from a job\")))\n"u8.ToArray());

        var screen = new StringBuilder();
        void RunUntil(string text)
        {
            for (var i = 0; i < 200 && !screen.ToString().Contains(text); i++)
            {
                screen.Append(Tick(vm));
            }

            Assert.That(screen.ToString(), Does.Contain(text));
        }

        RunUntil("$ ");
        vm.TypeLine("build job.wire");
        RunUntil("built job.bin");
        vm.TypeLine("run job.bin &");
        RunUntil("False");
        Assert.That(vm.Ui, Is.Null);

        vm.TypeLine("build panel.wire");
        RunUntil("built panel.bin");
        vm.TypeLine("run panel.bin");
        for (var i = 0; i < 20 && vm.Ui == null; i++)
        {
            Tick(vm);
        }

        Assert.That(vm.Ui, Is.Not.Null);

        // When the program ends, its UI goes and the shell's text is back.
        Assert.That(vm.UiEvent("name", ProgramUiEventKind.Submit, "quit"), Is.True);
        for (var i = 0; i < 20 && vm.Ui != null; i++)
        {
            Tick(vm);
        }

        Assert.That(vm.Ui, Is.Null);
    }
}
