using System.Diagnostics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Wasmtime;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Runs one of the <see cref="WasmSamples"/> as the firmware of a throwaway machine on the server's WASM
/// host, and reports what happened.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WasmTestCommand : LocalizedEntityCommands
{
    [Dependency] private WasmHostSystem _wasm = default!;

    /// <summary>Ticks the machine gets to finish in.</summary>
    private const int MaxTicks = 10;

    private static readonly Dictionary<string, string> Samples = new()
    {
        ["hello"] = WasmSamples.Hello,
        ["spin"] = WasmSamples.Spin,
        ["gc"] = WasmSamples.Gc,
        ["badpointer"] = WasmSamples.BadPointer,
        ["grow"] = WasmSamples.Grow,
    };

    public override string Command => "wasmtest";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !Samples.TryGetValue(args[0], out var wat))
        {
            shell.WriteError(Loc.GetString("cmd-wasmtest-usage", ("samples", string.Join(", ", Samples.Keys))));
            return;
        }

        var host = _wasm.Host;
        Module module;
        try
        {
            module = host.Load(Module.ConvertText(wat));
        }
        catch (Exception e) when (e is WasmtimeException or WasmLoadException)
        {
            shell.WriteError(Loc.GetString("cmd-wasmtest-failed", ("error", e.Message)));
            return;
        }

        var watch = Stopwatch.StartNew();
        using var vm = Vm.WithFirmware(args[0], module, DeviceKind.Computer);
        vm.PowerOn(host);

        ulong fuel = 0;
        var ticks = 0;
        while (ticks < MaxTicks && vm.State == VmState.Running)
        {
            fuel += vm.Tick(host, 33, WasmHost.FuelPerCall);
            ticks++;
        }

        var output = vm.TakeOutput().Trim('\n');
        if (output.Length > 0)
            shell.WriteLine(output);

        shell.WriteLine(Loc.GetString("cmd-wasmtest-result",
            ("state", vm.State.ToString()),
            ("ticks", ticks),
            ("fuel", fuel),
            ("ms", watch.Elapsed.TotalMilliseconds.ToString("0.0"))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(Samples.Keys, Loc.GetString("cmd-wasmtest-hint"))
            : CompletionResult.Empty;
    }
}
