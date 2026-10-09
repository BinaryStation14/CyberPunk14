using System;
using System.Linq;
using System.Text;
using Content.Server._CyberPunk.Wasm;
using Content.Shared._CyberPunk.Machines;
using NUnit.Framework;

#nullable enable

namespace Content.Tests.Server._CyberPunk;

/// <summary>
/// nano, the editor that comes with the OS, written in Wire. Ported from Switchboard's nano tests.
/// </summary>
[TestFixture]
[TestOf(typeof(SystemPrograms))]
public sealed class NanoTest
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

    /// <summary>
    /// A computer with nano open, and its screen as the terminal shows it.
    /// </summary>
    private sealed class Editor(WasmHost host, Vm vm) : IDisposable
    {
        public readonly Vm Vm = vm;
        private string _screen = "";

        /// <summary>What the terminal shows now: everything since the last clear.</summary>
        public string Screen => _screen;

        public void Tick()
        {
            Vm.Tick(host, 33, WasmHost.FuelPerCall);
            _screen = TerminalText.Apply(_screen, Vm.TakeOutput());
        }

        public void RunUntil(string text)
        {
            for (var i = 0; i < 200 && !_screen.Contains(text); i++)
            {
                Tick();
            }

            Assert.That(_screen, Does.Contain(text));
        }

        public void Keys(params int[] keys)
        {
            foreach (var key in keys)
            {
                Vm.TypeKey(key);
            }

            Tick();
            Tick();
        }

        public void Type(string text)
        {
            Keys(text.Select(c => (int) c).ToArray());
        }

        public string File(string name)
        {
            return Vm.Disk.Read(name) is { } bytes ? Encoding.UTF8.GetString(bytes) : "(none)";
        }

        public void Dispose()
        {
            Vm.Dispose();
        }
    }

    private Editor Open(string command, string? file = null, string? text = null)
    {
        var vm = new Vm();
        if (file != null)
            vm.SeedFile(file, Encoding.UTF8.GetBytes(text!));

        vm.PowerOn(_host);
        var editor = new Editor(_host, vm);
        editor.RunUntil("$ ");
        vm.TypeLine(command);
        editor.RunUntil("CP nano 1.0");
        return editor;
    }

    [Test]
    public void ItWritesANewFile()
    {
        using var nano = Open("nano notes.txt");
        Assert.That(nano.Screen, Does.Contain("File: notes.txt"));
        Assert.That(nano.Screen, Does.Contain("[ New file ]"));

        nano.Type("hello");
        nano.Keys(TerminalKeys.Enter);
        nano.Type("world");
        Assert.That(nano.Screen, Does.Contain("Modified"));
        Assert.That(nano.Screen, Does.Contain("hello\nworld" + TerminalText.Cursor + " "));
        Assert.That(nano.Screen, Does.Contain("line 2/2, col 6"));

        nano.Keys(TerminalKeys.Ctrl('o'));
        Assert.That(nano.Screen, Does.Contain("[ Wrote 2 lines ]"));
        Assert.That(nano.File("notes.txt"), Is.EqualTo("hello\nworld\n"));

        // Nothing changed since, so it exits at once, back to the shell.
        nano.Keys(TerminalKeys.Ctrl('x'));
        nano.RunUntil("$ ");
        nano.Vm.TypeLine("cat notes.txt");
        nano.RunUntil("world\n$ ");
    }

    [Test]
    public void ItEditsAFileAndAsksBeforeLosingChanges()
    {
        using var nano = Open("nano todo.txt", "todo.txt", "one\nthree\n");
        Assert.That(nano.Screen, Does.Contain("[ Read 2 lines ]"));

        // Into the middle: a new line under "one".
        nano.Keys(TerminalKeys.End, TerminalKeys.Enter);
        nano.Type("two");
        nano.Keys(TerminalKeys.Down, TerminalKeys.Home, TerminalKeys.Delete, TerminalKeys.Delete);
        nano.Type("TH");
        Assert.That(nano.Screen, Does.Contain("one\ntwo\nTH" + TerminalText.Cursor + "ree"));

        nano.Keys(TerminalKeys.Ctrl('x'));
        Assert.That(nano.Screen, Does.Contain("Save modified buffer?"));

        // Ctrl+C goes back to the file; Y saves and exits.
        nano.Keys(TerminalKeys.Ctrl('c'));
        Assert.That(nano.Screen, Does.Contain("[ Cancelled ]"));
        nano.Keys(TerminalKeys.Ctrl('x'), 'y');
        nano.RunUntil("$ ");
        Assert.That(nano.File("todo.txt"), Is.EqualTo("one\ntwo\nTHree\n"));
    }

    [Test]
    public void NoThrowsTheChangesAway()
    {
        using var nano = Open("nano keep.txt", "keep.txt", "as it was\n");
        nano.Type("junk");
        nano.Keys(TerminalKeys.Ctrl('x'), 'n');
        nano.RunUntil("$ ");
        Assert.That(nano.File("keep.txt"), Is.EqualTo("as it was\n"));
    }

    [Test]
    public void EnterKeepsTheIndentAndIndentsAfterAColon()
    {
        using var nano = Open("nano prog.wire");
        nano.Type("def tick():");
        nano.Keys(TerminalKeys.Enter);
        nano.Type("if x:");
        nano.Keys(TerminalKeys.Enter);
        nano.Type("pass");
        nano.Keys(TerminalKeys.Enter, TerminalKeys.Tab, TerminalKeys.Backspace);
        nano.Type("y");
        nano.Keys(TerminalKeys.Ctrl('o'));
        Assert.That(nano.File("prog.wire"), Is.EqualTo("def tick():\n    if x:\n        pass\n" + new string(' ', 11) + "y\n"));
    }

    [Test]
    public void CutAndPasteLines()
    {
        using var nano = Open("nano list.txt", "list.txt", "a\nb\nc\nd\n");

        // Two cuts in a row cut both lines, and they paste wherever, as often as wanted.
        nano.Keys(TerminalKeys.Ctrl('k'), TerminalKeys.Ctrl('k'), TerminalKeys.Down);
        nano.Keys(TerminalKeys.Ctrl('u'), TerminalKeys.Ctrl('u'), TerminalKeys.Ctrl('o'));
        Assert.That(nano.File("list.txt"), Is.EqualTo("c\na\nb\na\nb\nd\n"));

        // Backspace at the start of a line joins it to the one before.
        nano.Keys(TerminalKeys.Backspace, TerminalKeys.Ctrl('o'));
        Assert.That(nano.File("list.txt"), Is.EqualTo("c\na\nb\na\nbd\n"));
    }

    [Test]
    public void SavingANewBufferAsksForAName()
    {
        using var nano = Open("nano");
        Assert.That(nano.Screen, Does.Contain("File: New Buffer"));
        nano.Type("text");
        nano.Keys(TerminalKeys.Ctrl('o'));
        Assert.That(nano.Screen, Does.Contain("File Name to Write: " + TerminalText.Cursor));
        nano.Type("namd");
        nano.Keys(TerminalKeys.Backspace);
        nano.Type("ed.txt");
        nano.Keys(TerminalKeys.Enter);
        Assert.That(nano.Screen, Does.Contain("File: named.txt"));
        Assert.That(nano.File("named.txt"), Is.EqualTo("text\n"));
    }

    [Test]
    public void TheScreenScrollsWithTheCursorAndHelpComesAndGoes()
    {
        var text = string.Concat(Enumerable.Range(1, 50).Select(i => $"line {i}\n")) + new string('x', 120) + "\n";
        using var nano = Open("nano long.txt", "long.txt", text);
        Assert.That(nano.Screen, Does.Contain("line 20\n"));
        Assert.That(nano.Screen, Does.Not.Contain("line 21\n"));

        nano.Keys(TerminalKeys.PageDown, TerminalKeys.PageDown);
        Assert.That(nano.Screen, Does.Contain("line 41"));
        Assert.That(nano.Screen, Does.Not.Contain("line 20\n"));

        // A long line scrolls sideways to keep the cursor on screen, and every line fits the terminal.
        nano.Keys(TerminalKeys.PageDown, TerminalKeys.End);
        Assert.That(nano.Screen, Does.Contain("col 121"));
        Assert.That(nano.Screen.Split('\n').Select(l => l.Replace(TerminalText.Cursor.ToString(), "").Length),
            Has.All.LessThanOrEqualTo(80));
        Assert.That(nano.Screen.Split('\n'), Has.Length.EqualTo(24));

        nano.Keys(TerminalKeys.Ctrl('g'));
        Assert.That(nano.Screen, Does.Contain("CP nano 1.0 help"));
        nano.Keys('q');
        Assert.That(nano.Screen, Does.Contain("File: long.txt"));
        Assert.That(nano.File("long.txt"), Is.EqualTo(text));
    }

    [Test]
    public void ItWontEditAProgram()
    {
        var vm = new Vm();
        vm.SeedFile("prog.wasm", [0, (byte) 'a', (byte) 's', (byte) 'm', 1, 0, 0, 0]);
        vm.PowerOn(_host);
        using var editor = new Editor(_host, vm);
        editor.RunUntil("$ ");
        vm.TypeLine("nano prog.wasm");
        editor.RunUntil("nano: prog.wasm is a program, not text");
        editor.RunUntil("$ ");
    }

    [Test]
    public void ItsSourceCanBeCopiedAndBuilt()
    {
        Assert.That(Kernel.Scaffold("nano"), Is.EqualTo(SystemPrograms.Source("nano")));
        Assert.That(SystemPrograms.Source("nope"), Is.Null);

        // A player's own copy builds and runs as nano does.
        var vm = new Vm();
        vm.PowerOn(_host);
        using var editor = new Editor(_host, vm);
        editor.RunUntil("$ ");
        vm.TypeLine("new mynano nano");
        editor.RunUntil("wrote mynano.wire, nano's source");
        vm.TypeLine("build mynano.wire");
        editor.RunUntil("built mynano.wasm");
        vm.TypeLine("run mynano.wasm notes.txt");
        editor.RunUntil("File: notes.txt");
    }
}
