using System.Diagnostics;
using Content.Server._CyberPunk.Wasm;
using NUnit.Framework;

namespace Content.Tests.Server._CyberPunk;

[TestFixture]
[TestOf(typeof(WasmHost))]
public sealed class WasmHostTest
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

    private WasmRunResult Run(string wat, ulong fuel = WasmHost.FuelPerCall)
    {
        using var module = _host.CompileText("test", wat);
        return _host.RunStart(module, fuel);
    }

    [Test]
    public void HelloPrints()
    {
        var result = Run(WasmSamples.Hello);

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.Finished), result.Error);
        Assert.That(result.Output, Is.EqualTo("Hello from WASM!\n"));
        Assert.That(result.FuelUsed, Is.GreaterThan(0));
    }

    [Test]
    public void EndlessLoopRunsOutOfFuel()
    {
        var watch = Stopwatch.StartNew();
        var result = Run(WasmSamples.Spin);

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.OutOfFuel), result.Error);
        Assert.That(result.FuelUsed, Is.EqualTo(WasmHost.FuelPerCall));
        // One call's budget must fit comfortably in a server tick.
        Assert.That(watch.Elapsed.TotalMilliseconds, Is.LessThan(100));
    }

    [Test]
    public void GcStructsWork()
    {
        var result = Run(WasmSamples.Gc);

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.Finished), result.Error);
        Assert.That(result.Output, Is.EqualTo("GC ok\n"));
    }

    [Test]
    public void BadPointerKillsOnlyTheProgram()
    {
        var result = Run(WasmSamples.BadPointer);

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.Trapped));
        Assert.That(result.Output, Is.Empty);

        // The host still works afterwards.
        Assert.That(Run(WasmSamples.Hello).Outcome, Is.EqualTo(WasmOutcome.Finished));
    }

    [Test]
    public void MemoryCapRefusesGrowth()
    {
        var result = Run(WasmSamples.Grow);

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.Finished), result.Error);
        Assert.That(result.Output, Is.EqualTo("refused\n"));
    }

    [Test]
    public void MissingStartIsInvalid()
    {
        var result = Run("(module)");

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.Invalid));
    }

    [Test]
    public void UnknownImportIsInvalid()
    {
        var result = Run("""
            (module
              (import "sb_v4" "no_such_function" (func))
              (func (export "start")))
            """);

        Assert.That(result.Outcome, Is.EqualTo(WasmOutcome.Invalid));
    }
}
