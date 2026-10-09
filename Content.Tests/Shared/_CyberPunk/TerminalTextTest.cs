using Content.Shared._CyberPunk.Machines;
using NUnit.Framework;

namespace Content.Tests.Shared._CyberPunk;

[TestFixture]
[TestOf(typeof(TerminalText))]
public sealed class TerminalTextTest
{
    [Test]
    public void AppendsText()
    {
        Assert.That(TerminalText.Apply("$ ", "ls\nhello\n"), Is.EqualTo("$ ls\nhello\n"));
    }

    [Test]
    public void ClearWipesWhatCameBefore()
    {
        var screen = TerminalText.Apply("old\n", $"gone{TerminalText.Clear}new");
        Assert.That(screen, Is.EqualTo("new"));
    }

    [Test]
    public void BackspaceRubsOutWithinTheLine()
    {
        Assert.That(TerminalText.Apply("$ ", $"lx{TerminalText.Backspace}s"), Is.EqualTo("$ ls"));
        Assert.That(TerminalText.Apply("a\n", $"{TerminalText.Backspace}b"), Is.EqualTo("a\nb"));
        Assert.That(TerminalText.Apply("a\U0001F600", $"{TerminalText.Backspace}"), Is.EqualTo("a"));
    }

    [Test]
    public void ScrollbackDropsWholeOldLines()
    {
        var line = new string('x', 99) + "\n";
        var screen = "";
        for (var i = 0; i < 400; i++)
        {
            screen = TerminalText.Apply(screen, line);
        }

        Assert.That(screen.Length, Is.LessThanOrEqualTo(TerminalText.ScrollbackLimit));
        Assert.That(screen, Does.StartWith(line));
        Assert.That(screen, Does.EndWith(line));
    }

    [Test]
    public void ValidKeys()
    {
        Assert.That(TerminalKeys.Ctrl('C'), Is.EqualTo(3));
        Assert.That(TerminalKeys.Valid(TerminalKeys.Ctrl('z')));
        Assert.That(TerminalKeys.Valid(TerminalKeys.Enter));
        Assert.That(TerminalKeys.Valid(TerminalKeys.Delete));
        Assert.That(TerminalKeys.Valid(TerminalKeys.PageDown));
        Assert.That(TerminalKeys.Valid('a'));
        Assert.That(TerminalKeys.Valid('é'));
        Assert.That(TerminalKeys.Valid(0x1F600));

        Assert.That(TerminalKeys.Valid(0), Is.False);
        Assert.That(TerminalKeys.Valid(27), Is.False);
        Assert.That(TerminalKeys.Valid(0x85), Is.False);
        Assert.That(TerminalKeys.Valid(0xD800), Is.False);
        Assert.That(TerminalKeys.Valid(TerminalKeys.PageDown + 1), Is.False);
        Assert.That(TerminalKeys.Valid(-1), Is.False);
    }
}
