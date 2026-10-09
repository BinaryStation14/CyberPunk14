using System.Linq;
using System.Security.Cryptography;
using Content.Server._CyberPunk.Wire;
using Wasmtime;
using WasmStore = Wasmtime.Store;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Why a program can't be loaded: it isn't valid WebAssembly, needs something this kernel doesn't provide, or
/// has no <c>start</c>. The message is for players.
/// </summary>
public sealed class WasmLoadException(string message) : Exception(message);

/// <summary>
/// The WASM runtime every in-game machine runs on: one Wasmtime engine, the kernel functions linked once for
/// every machine, the operating system they boot, the programs that come with it, and the programs compiled
/// so far. Ported from <c>Host</c> in Switchboard's <c>sb_wasm/src/vm.rs</c>.
/// </summary>
/// <remarks>
/// Programs are untrusted. Every call into one gets a fuel budget (roughly one unit per instruction) and is
/// stopped when it runs out; memory and tables are capped per program; kernel functions check every pointer.
/// A program that traps, runs out of fuel or blows its memory is ended and its machine carries on. See
/// <see cref="Vm"/> for a machine, and <see cref="KernelApi"/> for the kernel functions.
/// </remarks>
public sealed class WasmHost : IDisposable
{
    /// <summary>Fuel a single call into a program (its <c>start</c>, one <c>tick</c> or one hook) may burn.</summary>
    public const ulong FuelPerCall = 2_000_000;

    /// <summary>The most memory a program can have.</summary>
    public const long MemoryLimit = 8 * 1024 * 1024;

    /// <summary>How many programs can be stacked on one machine, the OS included (and in each background job).</summary>
    public const int MaxDepth = 4;

    /// <summary>Background jobs a machine can run at once.</summary>
    public const int MaxJobs = 8;

    /// <summary>Terminal output a machine can produce in one tick; the rest is dropped.</summary>
    public const int OutputPerTick = 4 * 1024;

    /// <summary>Typed input waiting to be read, at most.</summary>
    public const int InputLimit = 4 * 1024;

    /// <summary>Keys waiting to be read in raw mode, at most; more are dropped.</summary>
    public const int KeyLimit = 256;

    /// <summary>How long a halted or crashed OS takes to restart, in milliseconds.</summary>
    public const ulong RebootDelayMs = 3000;

    /// <summary>UI events waiting to be read, at most; more are dropped.</summary>
    public const int UiEventLimit = 64;

    /// <summary>The biggest packet, in bytes.</summary>
    public const int MaxPacket = 1024;

    /// <summary>Packets a machine can send in one tick; more are refused.</summary>
    public const int OutboxPerTick = 16;

    /// <summary>Packets waiting to be read, at most; more are dropped.</summary>
    public const int InboxLimit = 64;

    /// <summary>The longest argument string a program can be started with.</summary>
    public const int MaxArgs = 256;

    /// <summary>The biggest source file <c>build</c> takes, in bytes.</summary>
    public const int MaxSource = 64 * 1024;

    /// <summary>The terminal's screen.</summary>
    public const int TermRows = 24;

    public const int TermCols = 80;

    /// <summary>Compiled programs kept for reuse, at most.</summary>
    private const int ModuleCache = 256;

    /// <summary>Deep recursion traps instead of overflowing the server's stack.</summary>
    private const int MaxStackSize = 512 * 1024;

    private readonly Engine _engine;
    private readonly Linker _linker;
    private readonly HashSet<string> _kernelModules = new();
    private readonly Dictionary<string, Module> _system = new();
    private readonly Dictionary<string, Module> _modules = new();

    /// <summary>
    /// The operating system machines boot, unless they have firmware of their own.
    /// </summary>
    public Module Os { get; }

    /// <summary>
    /// The names of the kernel functions linked, which are those in <see cref="Kernel.Functions"/>.
    /// </summary>
    public IReadOnlySet<string> KernelFunctions { get; }

    /// <summary>
    /// A host whose machines boot <paramref name="os"/>, or the <see cref="DefaultOs"/>, built from its Wire
    /// source.
    /// </summary>
    public WasmHost(byte[]? os = null)
    {
        using var config = new Config()
            .WithFuelConsumption(true)
            .WithReferenceTypes(true)
            .WithFunctionReferences(true)
            .WithGc(true)
            .WithMaximumStackSize(MaxStackSize);

        _engine = new Engine(config);
        _linker = new Linker(_engine);
        KernelFunctions = KernelApi.Define(_linker);

        for (var version = 0; version <= Kernel.ApiVersion; version++)
        {
            _kernelModules.Add(Kernel.Module(version));
        }

        Os = Compile(os ?? WireCompiler.Compile(DefaultOs.Source));
        foreach (var name in SystemPrograms.Names)
        {
            AddSystemProgram(name, WireCompiler.Compile(SystemPrograms.Source(name)!));
        }
    }

    /// <summary>
    /// Adds a program that comes with the OS, run by name though it isn't on the disk.
    /// </summary>
    public void AddSystemProgram(string name, byte[] wasm)
    {
        _system[name] = Compile(wasm);
    }

    public bool IsSystemProgram(string name)
    {
        return _system.ContainsKey(name);
    }

    public Module? SystemProgram(string name)
    {
        return _system.GetValueOrDefault(name);
    }

    /// <summary>
    /// Compiles a program and checks it can run on a machine, or fetches it if it was compiled before.
    /// </summary>
    /// <exception cref="WasmLoadException">It can't, and why.</exception>
    public Module Load(byte[] wasm)
    {
        var hash = Convert.ToHexString(SHA256.HashData(wasm));
        if (_modules.TryGetValue(hash, out var cached))
            return cached;

        var module = Compile(wasm);

        // Programs still running keep their own reference to their code, so dropping the cache is safe.
        if (_modules.Count >= ModuleCache)
            _modules.Clear();

        _modules[hash] = module;
        return module;
    }

    /// <summary>
    /// Compiles a program and checks it can run on a machine: it only imports kernel functions, and has a
    /// <c>start</c>.
    /// </summary>
    private Module Compile(byte[] wasm)
    {
        Module module;
        try
        {
            module = Module.FromBytes(_engine, "program", wasm);
        }
        catch (WasmtimeException e)
        {
            throw new WasmLoadException($"not a valid program ({FirstLine(e.Message)})");
        }

        foreach (var import in module.Imports)
        {
            if (_kernelModules.Contains(import.ModuleName))
                continue;

            var wanted = import.ModuleName;
            module.Dispose();
            throw new WasmLoadException(wanted.StartsWith("sb_v")
                ? $"needs host API v{wanted[4..]}, this computer has v{Kernel.ApiVersion}"
                : $"imports \"{wanted}\", which this computer doesn't provide");
        }

        if (module.Exports.All(e => e.Name != "start"))
        {
            module.Dispose();
            throw new WasmLoadException("has no `start` entry point");
        }

        return module;
    }

    /// <summary>
    /// A store for one of a machine's programs to run in, carrying the machine's IO and capped as every
    /// program is.
    /// </summary>
    internal WasmStore NewStore(MachineIo io)
    {
        var store = new WasmStore(_engine, io);
        store.SetLimits(memorySize: MemoryLimit, instances: 1, tables: 4, memories: 1);
        return store;
    }

    internal Instance Instantiate(WasmStore store, Module module)
    {
        return _linker.Instantiate(store, module);
    }

    /// <summary>
    /// The first line of an error, which is what players see: Wasmtime adds a backtrace after it. A trap puts
    /// its cause after the backtrace, so that is used instead.
    /// </summary>
    internal static string FirstLine(string message)
    {
        var line = message.Trim();
        var cause = line.IndexOf("Caused by:", StringComparison.Ordinal);
        if (cause >= 0)
            line = line[(cause + "Caused by:".Length)..].Trim();

        var end = line.IndexOf('\n');
        return end < 0 ? line : line[..end].TrimEnd();
    }

    public void Dispose()
    {
        foreach (var module in _system.Values.Concat(_modules.Values).Distinct())
        {
            module.Dispose();
        }

        _system.Clear();
        _modules.Clear();
        Os.Dispose();
        _linker.Dispose();
        _engine.Dispose();
    }
}
