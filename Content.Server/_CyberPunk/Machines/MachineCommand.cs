using System.Linq;
using System.Text;
using Content.Server.Administration;
using Content.Server._CyberPunk.Wasm;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._CyberPunk.Machines;

/// <summary>
/// Works a WASM machine from the console, for admins and tests: shows its screen and programs, types at it, and
/// writes files to its disk.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class MachineCommand : LocalizedEntityCommands
{
    [Dependency] private WasmMachineSystem _machines = default!;

    private static readonly string[] Actions = ["screen", "ps", "type", "write"];

    public override string Command => "machine";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2 || !Actions.Contains(args[1]))
        {
            shell.WriteError(Loc.GetString("cmd-machine-help"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var netEnt) ||
            !EntityManager.TryGetEntity(netEnt, out var uid) ||
            !EntityManager.TryGetComponent(uid, out WasmMachineComponent? machine))
        {
            shell.WriteError(Loc.GetString("cmd-machine-not-a-machine", ("uid", args[0])));
            return;
        }

        var ent = new Entity<WasmMachineComponent>(uid.Value, machine);
        switch (args[1])
        {
            case "screen":
                shell.WriteLine(machine.Screen);
                break;
            case "ps":
                var vm = machine.Vm;
                shell.WriteLine(Loc.GetString("cmd-machine-ps",
                    ("state", vm?.State.ToString() ?? "-"),
                    ("clock", vm?.ClockMs ?? 0),
                    ("programs", vm == null ? "" : string.Join(", ", vm.Processes)),
                    ("jobs", vm == null ? "" : string.Join(", ", vm.Jobs.Select(j => $"{j.Id}: {string.Join(" > ", j.Programs)}")))));
                break;
            case "type":
                _machines.TypeLine(ent, string.Join(' ', args[2..]));
                break;
            case "write":
                if (args.Length < 3)
                {
                    shell.WriteError(Loc.GetString("cmd-machine-help"));
                    return;
                }

                var text = string.Join(' ', args[3..]) + "\n";
                var error = _machines.WriteFile(ent, args[2], Encoding.UTF8.GetBytes(text));
                if (error != DiskError.None)
                    shell.WriteError(Loc.GetString("cmd-machine-write-failed", ("error", error.ToString())));
                break;
        }
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return args.Length switch
        {
            1 => CompletionResult.FromHintOptions(
                CompletionHelper.Components<WasmMachineComponent>(args[0], EntityManager),
                Loc.GetString("cmd-machine-hint-machine")),
            2 => CompletionResult.FromHintOptions(Actions, Loc.GetString("cmd-machine-hint-action")),
            _ => CompletionResult.Empty,
        };
    }
}
