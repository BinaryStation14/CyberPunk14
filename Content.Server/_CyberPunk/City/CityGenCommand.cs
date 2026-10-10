using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._CyberPunk.City;

[AdminCommand(AdminFlags.Mapping)]
public sealed partial class CityGenCommand : LocalizedEntityCommands
{
    [Dependency] private CitySystem _city = default!;

    public override string Command => "citygen";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        ulong? seed = null;
        if (args.Length > 0)
        {
            if (!ulong.TryParse(args[0], out var parsed))
            {
                shell.WriteError(Loc.GetString("cmd-citygen-bad-seed"));
                return;
            }

            seed = parsed;
        }

        shell.WriteLine(Loc.GetString("cmd-citygen-done", ("seed", _city.Generate(seed))));
    }
}
