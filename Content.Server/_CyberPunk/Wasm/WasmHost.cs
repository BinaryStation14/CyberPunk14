using System.Text;
using Wasmtime;
using WasmStore = Wasmtime.Store;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// How a call into a guest program ended.
/// </summary>
public enum WasmOutcome : byte
{
    /// <summary>The call returned normally.</summary>
    Finished,

    /// <summary>The call used up its fuel and was stopped.</summary>
    OutOfFuel,

    /// <summary>The guest trapped: a bad memory access, unreachable, a host function refusing it, and so on.</summary>
    Trapped,

    /// <summary>The module couldn't be loaded or linked, or has no <c>start</c> export.</summary>
    Invalid,
}

/// <summary>
/// The result of running a guest program's <c>start</c>.
/// </summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="FuelUsed">How much fuel it burned, roughly one per instruction.</param>
/// <param name="Output">Everything it wrote to the terminal.</param>
/// <param name="Error">Why it didn't finish, when it didn't.</param>
public readonly record struct WasmRunResult(WasmOutcome Outcome, ulong FuelUsed, string Output, string? Error);

/// <summary>
/// The WASM runtime every in-game machine runs on: one Wasmtime engine, with fuel metering, a memory cap
/// and the GC proposal turned on.
/// </summary>
/// <remarks>
/// Guests are untrusted. Each call gets a fuel budget and a guest that traps, runs out of fuel or exceeds
/// its memory only kills itself. Host functions use Switchboard's kernel v4 ABI (the <c>sb_v4</c> import
/// module) so the same programs and manual pages work. This is milestone 1: only <c>term_write</c> is
/// linked so far.
/// </remarks>
public sealed class WasmHost : IDisposable
{
    /// <summary>
    /// The import module kernel functions live in.
    /// </summary>
    public const string KernelModule = "sb_v4";

    /// <summary>
    /// Fuel for one call into a guest: about 2 million instructions, as in Switchboard.
    /// </summary>
    public const ulong FuelPerCall = 2_000_000;

    /// <summary>
    /// The most memory a guest can grow to.
    /// </summary>
    public const long MemoryLimit = 8 * 1024 * 1024;

    /// <summary>
    /// The most a guest can write to its terminal in one call.
    /// </summary>
    public const int OutputPerCall = 4096;

    private const int MaxStackSize = 512 * 1024;

    private readonly Engine _engine;

    public WasmHost()
    {
        using var config = new Config()
            .WithFuelConsumption(true)
            .WithReferenceTypes(true)
            .WithFunctionReferences(true)
            .WithGc(true)
            .WithMaximumStackSize(MaxStackSize);

        _engine = new Engine(config);
    }

    /// <summary>
    /// Compiles a module from WAT text.
    /// </summary>
    public Module CompileText(string name, string wat)
    {
        return Module.FromText(_engine, name, wat);
    }

    /// <summary>
    /// Compiles a module from a WASM binary.
    /// </summary>
    public Module CompileBinary(string name, ReadOnlySpan<byte> wasm)
    {
        return Module.FromBytes(_engine, name, wasm);
    }

    /// <summary>
    /// Instantiates a module in a fresh store and calls its <c>start</c> export with at most
    /// <paramref name="fuel"/>.
    /// </summary>
    public WasmRunResult RunStart(Module module, ulong fuel = FuelPerCall)
    {
        var output = new StringBuilder();

        using var store = new WasmStore(_engine);
        store.SetLimits(memorySize: MemoryLimit);
        store.Fuel = fuel;

        using var linker = new Linker(_engine);
        DefineKernel(linker, output);

        WasmRunResult Done(WasmOutcome outcome, string? error = null)
        {
            return new WasmRunResult(outcome, fuel - store.Fuel, output.ToString(), error);
        }

        Instance instance;
        try
        {
            instance = linker.Instantiate(store, module);
        }
        catch (TrapException e)
        {
            // The module's own start section ran and failed.
            return Done(Failure(e), e.Message);
        }
        catch (WasmtimeException e)
        {
            // Couldn't link it: an import the kernel doesn't have, or one with the wrong signature.
            return Done(WasmOutcome.Invalid, e.Message);
        }

        var start = instance.GetAction("start");
        if (start == null)
            return Done(WasmOutcome.Invalid, "the module has no start export");

        try
        {
            start();
        }
        catch (WasmtimeException e)
        {
            // Everything that goes wrong once the guest runs is its own fault, including a kernel
            // function refusing it.
            return Done(Failure(e), e.Message);
        }

        return Done(WasmOutcome.Finished);
    }

    private static WasmOutcome Failure(WasmtimeException e)
    {
        return e is TrapException { Type: TrapCode.OutOfFuel } ? WasmOutcome.OutOfFuel : WasmOutcome.Trapped;
    }

    private static void DefineKernel(Linker linker, StringBuilder output)
    {
        linker.DefineFunction(KernelModule,
            "term_write",
            (Caller caller, int ptr, int len) =>
            {
                var memory = caller.GetMemory("memory") ?? throw new TrapException("the program exports no memory");

                // A pointer outside the guest's memory kills it; text past the output cap is dropped.
                if (ptr < 0 || len < 0 || (long) ptr + len > memory.GetLength())
                    throw new TrapException("term_write was handed memory outside the program");

                var allowed = Math.Max(0, Math.Min(len, OutputPerCall - output.Length));
                if (allowed > 0)
                    output.Append(memory.ReadString(ptr, allowed, Encoding.UTF8));
            });
    }

    public void Dispose()
    {
        _engine.Dispose();
    }
}
