using System;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Server._CyberPunk.Wasm;
using NUnit.Framework;

namespace Content.Tests.Server._CyberPunk;

/// <summary>
/// The kernel's table and manual: what a program can link is exactly what the manual documents, and every
/// page fits the screen.
/// </summary>
[TestFixture]
[TestOf(typeof(Kernel))]
public sealed class KernelTest
{
    private static readonly Regex ManLink = new(@"man (\w+)");

    /// <summary>
    /// The topics the index lists.
    /// </summary>
    private static string[] IndexTopics()
    {
        return ManLink.Matches(Kernel.Man("")!)
            .Select(m => m.Groups[1].Value)
            .ToArray();
    }

    [Test]
    public void EveryIndexTopicHasAPage()
    {
        var topics = IndexTopics();
        Assert.That(topics, Is.Not.Empty);
        foreach (var topic in topics)
        {
            Assert.That(Kernel.Man(topic), Is.Not.Null.And.Not.Empty, topic);
        }
    }

    [Test]
    public void UnknownTopicsHaveNoPage()
    {
        Assert.That(Kernel.Man("nonsense"), Is.Null);
    }

    [Test]
    public void PagesFitTheScreen()
    {
        foreach (var topic in IndexTopics().Append("").Append("shell"))
        {
            foreach (var line in Kernel.Man(topic)!.Split('\n'))
            {
                Assert.That(line.Length, Is.LessThanOrEqualTo(Kernel.PageWidth), $"man {topic}: {line}");
            }
        }
    }

    [Test]
    public void EveryFunctionIsInTheManual()
    {
        var manual = string.Join("\n", IndexTopics().Select(t => Kernel.Man(t)));
        foreach (var f in Kernel.Functions)
        {
            Assert.That(manual, Does.Match($@"\n  {f.Name}( |\n)"), f.Name);
        }

        foreach (var h in Kernel.Hooks)
        {
            Assert.That(Kernel.Man("hooks"), Does.Contain(h.Name), h.Name);
        }
    }

    [Test]
    public void FunctionNamesAreUnique()
    {
        Assert.That(Kernel.Functions.Select(f => f.Name), Is.Unique);
        Assert.That(Kernel.Functions.All(f => f.Since >= 0 && f.Since <= Kernel.ApiVersion));
    }

    [Test]
    public void HostLinksExactlyTheTable()
    {
        using var host = new WasmHost();
        Assert.That(host.KernelFunctions, Is.EquivalentTo(Kernel.Functions.Select(f => f.Name)));
    }

    [Test]
    public void WrapKeepsIndents()
    {
        var text = "    " + string.Join(' ', Enumerable.Repeat("word", 40));
        var lines = Kernel.Wrap(text).TrimEnd('\n').Split('\n');
        Assert.That(lines.Length, Is.GreaterThan(1));
        foreach (var line in lines)
        {
            Assert.That(line, Does.StartWith("    word"));
            Assert.That(line.Length, Is.LessThanOrEqualTo(Kernel.PageWidth));
        }
    }
}
