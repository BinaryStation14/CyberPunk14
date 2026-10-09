using System.Diagnostics;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Runs one of the <see cref="WasmSamples"/> on the server's WASM host and reports what happened.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class WasmTestCommand : LocalizedEntityCommands
{
    [Dependency] private WasmHostSystem _wasm = default!;

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

        var watch = Stopwatch.StartNew();

        WasmRunResult result;
        try
        {
            using var module = _wasm.Host.CompileText(args[0], wat);
            result = _wasm.Host.RunStart(module);
        }
        catch (Exception e)
        {
            shell.WriteError(Loc.GetString("cmd-wasmtest-failed", ("error", e.Message)));
            return;
        }

        if (result.Output.Length > 0)
            shell.WriteLine(result.Output.TrimEnd('\n'));

        shell.WriteLine(Loc.GetString("cmd-wasmtest-result",
            ("outcome", result.Outcome.ToString()),
            ("fuel", result.FuelUsed),
            ("ms", watch.Elapsed.TotalMilliseconds.ToString("0.0"))));

        if (result.Error != null)
            shell.WriteLine(result.Error);
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length == 1
            ? CompletionResult.FromHintOptions(Samples.Keys, Loc.GetString("cmd-wasmtest-hint"))
            : CompletionResult.Empty;
    }
}
