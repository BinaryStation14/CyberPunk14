using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server._CyberPunk.Machines;
using Content.Shared.FixedPoint;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

#nullable enable

namespace Content.Tests.Server._CyberPunk;

/// <summary>
/// The values machines on the network answer programs with, and the arguments programs send them.
/// </summary>
[TestFixture]
[TestOf(typeof(MachineLiteral))]
public sealed class MachineLiteralTest
{
    private enum Flavour
    {
        Sweet,
        Sour,
    }

    private sealed class Order
    {
        public string Item = "";
        public int Count { get; set; }
        public Flavour Flavour;
    }

    private static string Write(object? value)
    {
        return MachineLiteral.Write(value, uid => uid.Id);
    }

    private static object? Convert(string text, Type type)
    {
        return MachineLiteral.Convert(MachineLiteral.Parse(text), type, n => new NetEntity(n));
    }

    [Test]
    public void ReadsWhatProgramsWrite()
    {
        Assert.That(MachineLiteral.Parse("None"), Is.Null);
        Assert.That(MachineLiteral.Parse(" True "), Is.EqualTo(true));
        Assert.That(MachineLiteral.Parse("-42"), Is.EqualTo(-42L));
        Assert.That(MachineLiteral.Parse("\"a\\\"b\\n\\\\\""), Is.EqualTo("a\"b\n\\"));
        Assert.That(MachineLiteral.Parse("[1, [], \"x\"]"), Is.EqualTo(new List<object?> { 1L, new List<object?>(), "x" }));

        var dict = (MachineLiteral.Dict) MachineLiteral.Parse("{\"item_id\": 3, 1: None}")!;
        Assert.That(dict, Has.Count.EqualTo(2));
        Assert.That(dict.TryGet("ItemId", out var id) && id is 3L);

        Assert.Throws<FormatException>(() => MachineLiteral.Parse("[1, 2"));
        Assert.Throws<FormatException>(() => MachineLiteral.Parse("1 2"));
        Assert.Throws<FormatException>(() => MachineLiteral.Parse("nope"));
    }

    [Test]
    public void WritesWhatProgramsRead()
    {
        Assert.That(Write(null), Is.EqualTo("None"));
        Assert.That(Write(false), Is.EqualTo("False"));
        Assert.That(Write("tab\there \"q\""), Is.EqualTo("\"tab\\there \\\"q\\\"\""));
        Assert.That(Write(2.6f), Is.EqualTo("3"));
        Assert.That(Write(FixedPoint2.New(7.4)), Is.EqualTo("7"));
        Assert.That(Write(Flavour.Sour), Is.EqualTo("\"Sour\""));
        Assert.That(Write(TimeSpan.FromSeconds(2)), Is.EqualTo("2000"));
        Assert.That(Write(new Vector2i(1, -2)), Is.EqualTo("[1, -2]"));
        Assert.That(Write(new Dictionary<string, int> { ["a"] = 1 }), Is.EqualTo("{\"a\": 1}"));
        Assert.That(Write(new Order { Item = "cola", Count = 2, Flavour = Flavour.Sour }),
            Does.Contain("\"Item\": \"cola\"").And.Contain("\"Count\": 2").And.Contain("\"Flavour\": \"Sour\""));

        // What it writes, it reads back.
        Assert.That(MachineLiteral.Parse(Write(new List<string> { "x\n", "y" })), Is.EqualTo(new List<object?> { "x\n", "y" }));
    }

    [Test]
    public void TurnsArgumentsIntoWhatTheMachineTakes()
    {
        Assert.That(Convert("5", typeof(int)), Is.EqualTo(5));
        Assert.That(Convert("5", typeof(float)), Is.EqualTo(5f));
        Assert.That(Convert("None", typeof(int?)), Is.Null);
        Assert.That(Convert("\"sour\"", typeof(Flavour)), Is.EqualTo(Flavour.Sour));
        Assert.That(Convert("1", typeof(Flavour)), Is.EqualTo(Flavour.Sour));
        Assert.That(Convert("[1, 2]", typeof(Vector2)), Is.EqualTo(new Vector2(1, 2)));
        Assert.That(Convert("[1, 2]", typeof(List<int>)), Is.EqualTo(new List<int> { 1, 2 }));
        Assert.That(Convert("7", typeof(NetEntity)), Is.EqualTo(new NetEntity(7)));
        Assert.That(Convert("\"#ff8800\"", typeof(Color)), Is.EqualTo(Color.FromHex("#ff8800")));
        Assert.That(Convert("{\"a\": 1}", typeof(Dictionary<string, int>)), Is.EqualTo(new Dictionary<string, int> { ["a"] = 1 }));

        Assert.Throws<FormatException>(() => Convert("\"x\"", typeof(int)));
        Assert.Throws<FormatException>(() => Convert("\"bitter\"", typeof(Flavour)));
    }

    [Test]
    public void NamesMatchLoosely()
    {
        Assert.That(MachineLiteral.Same("vending_machine_eject", "VendingMachineEject"));
        Assert.That(MachineLiteral.Same("id", "ID"));
        Assert.That(!MachineLiteral.Same("eject", "Ejected"));
        Assert.That(MachineLiteral.Describe(typeof(Flavour)), Is.EqualTo("one of \"Sweet\", \"Sour\""));
    }
}
