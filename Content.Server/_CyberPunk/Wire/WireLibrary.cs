using System.Linq;

namespace Content.Server._CyberPunk.Wire;

/// <summary>
/// A function a Wire program calls by name.
/// </summary>
/// <param name="Name"><c>print</c>, or <c>module.name</c>.</param>
/// <param name="Usage">How it is written in the manual.</param>
/// <param name="Device">Null on every machine; else the kind of machine that has it.</param>
public sealed record WireFunction(string Name, string Usage, int MinArgs, int MaxArgs, string? Device, string Doc);

/// <summary>
/// What Wire programs can call: built-in functions, the machine's modules (with which kind of machine has each),
/// constants, and the methods of lists, text and dicts. The compiler checks calls against these tables, and the
/// manual is written from them. Ported from Switchboard's <c>wasm/wire/src/stdlib.rs</c>.
/// </summary>
public static class WireLibrary
{
    /// <summary>A function that takes any number of arguments.</summary>
    public const int Any = int.MaxValue;

    public static readonly IReadOnlyList<WireFunction> Builtins = new WireFunction[]
    {
        new("print", "print(a, b, ...)", 0, Any, null,
            "Writes its arguments to the terminal, separated by spaces, and starts a new line."),
        new("len", "len(x)", 1, 1, null,
            "How many items a list or dict has, or characters a text has."),
        new("str", "str(x)", 1, 1, null,
            "x as text."),
        new("int", "int(x)", 1, 1, null,
            "Text or True/False as a whole number; None if the text isn't one."),
        new("range", "range(stop) / range(start, stop) / range(start, stop, step)", 1, 3, null,
            "The numbers from start (0) up to, not including, stop: for i in range(10)."),
        new("abs", "abs(n)", 1, 1, null,
            "n without its sign."),
        new("min", "min(a, b, ...) / min(list)", 1, Any, null,
            "The smallest."),
        new("max", "max(a, b, ...) / max(list)", 1, Any, null,
            "The largest."),
        new("sorted", "sorted(list)", 1, 1, null,
            "A sorted copy of a list."),
        new("chr", "chr(n)", 1, 1, null,
            "The character with code n."),
        new("ord", "ord(text)", 1, 1, null,
            "The code of a text's first character."),
    };

    public static readonly IReadOnlyList<string> Modules = new[]
    {
        "term", "fs", "net", "sys", "door", "camera", "ice", "deck", "body",
    };

    public static readonly IReadOnlyList<WireFunction> ModuleFunctions = new WireFunction[]
    {
        new("sys.clock", "sys.clock()", 0, 0, null,
            "Milliseconds since the machine booted."),
        new("sys.args", "sys.args()", 0, 0, null,
            "The text the program was started with: `run prog.wasm these words`."),
        new("sys.exit", "sys.exit() / sys.exit(code)", 0, 1, null,
            "Ends the program once the current hook returns."),
        new("sys.run", "sys.run(file) / sys.run(file, args)", 1, 2, null,
            "Starts another program when this hook returns; it takes over until it ends. False if it can't."),
        new("sys.random", "sys.random(n)", 1, 1, null,
            "A random whole number from 0 up to, not including, n."),
        new("sys.start", "sys.start(file) / sys.start(file, args)", 1, 2, null,
            "Starts a program as a background job, from the next tick: it runs alongside this one, with no terminal input of its own (8 jobs at most). Its job id, or None if it can't."),
        new("sys.jobs", "sys.jobs()", 0, 0, null,
            "The background jobs running, as a list. Each has .id and .name (the program in front)."),
        new("sys.kill", "sys.kill(id)", 1, 1, null,
            "Stops a background job, at the end of this tick. False if there's no such job."),
        new("sys.device", "sys.device()", 0, 0, null,
            "What this machine is: \"computer\", \"door\", \"camera\" or \"deck\"."),
        new("fs.read", "fs.read(name)", 1, 1, null,
            "A file's contents as text, or None if there's no such file."),
        new("fs.write", "fs.write(name, text)", 2, 2, null,
            "Creates or replaces a file. False if the name is bad or the disk is full."),
        new("fs.append", "fs.append(name, text)", 2, 2, null,
            "Adds text to the end of a file (creating it). False if it can't."),
        new("fs.delete", "fs.delete(name)", 1, 1, null,
            "Deletes a file. False if there was none."),
        new("fs.files", "fs.files()", 0, 0, null,
            "The names of every file on the disk."),
        new("net.address", "net.address()", 0, 0, null,
            "This machine's network address, like \"10.2.1.1\", or None."),
        new("net.send", "net.send(address, port, text)", 3, 3, null,
            "Sends text (up to 1024 bytes) to the machine at an address or hostname, on a port (0-65535). It arrives next tick. False if it can't be sent."),
        new("net.receive", "net.receive()", 0, 0, null,
            "Every packet that has arrived since last time, as a list. Each has .sender (an address), .port and .text."),
        new("net.neighbours", "net.neighbours()", 0, 0, null,
            "The addresses of the other machines on this building's network."),
        new("net.hostname", "net.hostname()", 0, 0, null,
            "This machine's hostname, or None if it has none."),
        new("net.set_hostname", "net.set_hostname(name)", 1, 1, null,
            "Sets this machine's hostname (1 to 32 of a-z, 0-9 and -), which its router tells the rest of the network from the next tick; \"\" clears it. It's kept through reboots. False if the name can't be used."),
        new("net.resolve", "net.resolve(name)", 1, 1, null,
            "The address of the machine with this hostname, as the routers know it, or None. An address comes back as it is."),
        new("net.hosts", "net.hosts()", 0, 0, null,
            "Every hostname the routers know, as a dict from name to address."),
        new("term.read_line", "term.read_line()", 0, 0, "computer",
            "The next line typed at the terminal, or None if nothing has been typed yet."),
        new("term.raw", "term.raw(on)", 1, 1, "computer",
            "Raw mode on (True) or off: every key comes to term.key() as it is pressed, and nothing typed is shown. It ends with the program."),
        new("term.key", "term.key()", 0, 0, "computer",
            "The next key pressed in raw mode, or None: a character's code (chr() turns it into text), or term.ENTER, term.UP and so on."),
        new("term.clear", "term.clear()", 0, 0, "computer",
            "Clears the screen (24 lines of 80 characters)."),
        new("door.open", "door.open()", 0, 0, "door",
            "Opens the door. False if it is bolted."),
        new("door.close", "door.close()", 0, 0, "door",
            "Closes the door. False if it is bolted or someone is in the doorway."),
        new("door.bolt", "door.bolt()", 0, 0, "door",
            "Bolts the door where it is: it won't move, by hand or by program, until unbolted."),
        new("door.unbolt", "door.unbolt()", 0, 0, "door",
            "Unbolts the door."),
        new("door.status", "door.status()", 0, 0, "door",
            "The door as it stands: .open, .bolted, and .blocked (someone is in the doorway), each True or False."),
        new("camera.count", "camera.count()", 0, 0, "camera",
            "How many people the camera sees (within 6 tiles)."),
        new("camera.names", "camera.names()", 0, 0, "camera",
            "The names of the people it sees, as a list."),
        new("ice.start", "ice.start()", 0, 0, "ice",
            "Puts ICE in cyberspace on this computer's pad, guarding its network, run by this program: the other ice functions direct it from the next tick. It goes when the program ends; if a runner beats it, the program is halted. False if it can't."),
        new("ice.integrity", "ice.integrity()", 0, 0, "ice",
            "The ICE's integrity (100 when it starts; at none it derezzes), or None if this program runs no ICE."),
        new("ice.here", "ice.here()", 0, 0, "ice",
            "The node the ICE stands at (a number), or None if it isn't at one."),
        new("ice.nodes", "ice.nodes()", 0, 0, "ice",
            "Every node of the network it guards, as a list of numbers."),
        new("ice.neighbours", "ice.neighbours()", 0, 0, "ice",
            "The nodes linked to the one it stands at (empty if it isn't at one)."),
        new("ice.go", "ice.go(node)", 1, 1, "ice",
            "Walks to a node of its network along the paths, the shortest way. False if it isn't one."),
        new("ice.chase", "ice.chase(runner)", 1, 1, "ice",
            "Chases a runner it can see (by .id) along the paths, for as long as they stay in its network. False if it can't see them."),
        new("ice.runners", "ice.runners()", 0, 0, "ice",
            "The netrunners in its network that it can see (along a clear path, within 9 tiles), as a list. Each has .id, .node (the node they are nearest), .name, .authorized (True if they carry an ID card of this network's organization), .in_reach (close enough to strike), and .x and .y (the tile they stand on)."),
        new("ice.attack", "ice.attack(runner)", 1, 1, "ice",
            "Strikes a runner in reach (by .id): a quarter of their integrity (an eighth through a ward), once a second at most. At none left they are thrown out. False if they aren't in reach."),
        new("ice.position", "ice.position()", 0, 0, "ice",
            "The tile it stands on, as [x, y]."),
        new("ice.alert", "ice.alert()", 0, 0, "ice",
            "Where a completed trace says an intruder is, as [x, y], on the one tick after the trace completes; else None. Any runner who steps up to one of the network's computers without the organization's ID card is traced, and when the trace completes every ICE on the network is alerted."),
        new("ice.go_to", "ice.go_to(x, y)", 2, 2, "ice",
            "Walks to a tile of its network along the paths, the shortest way; it stops if there's no way there."),
        new("ice.mode", "ice.mode(name)", 1, 1, "ice",
            "Shows how alert it is, in its colour, for runners to read: \"patrolling\", \"searching\" or \"engaging\". Only for show."),
        new("deck.integrity", "deck.integrity()", 0, 0, "deck",
            "The runner's integrity: 100 when they jack in, and at none they are thrown out."),
        new("deck.status", "deck.status()", 0, 0, "deck",
            "The deck as it stands: .warded (a ward is up), .can_strike and .can_ward (ready now), each True or False."),
        new("deck.targets", "deck.targets()", 0, 0, "deck",
            "The ICE and runners the deck can see (along a clear path, within 9 tiles), nearest first. Each has .id, .ice (True for ICE), .name, .integrity, .in_reach and .distance (in tiles)."),
        new("deck.strike", "deck.strike(target)", 1, 1, "deck",
            "Strikes ICE or a runner in reach (by .id) for a fifth of its integrity; at none ICE derezzes and a runner is thrown out. Every 0.8 seconds at most. False if out of reach or not ready."),
        new("deck.ward", "deck.ward()", 0, 0, "deck",
            "Raises a ward: strikes on the runner are halved for 2 seconds. Every 6 seconds at most. False if not ready."),
        new("deck.hold", "deck.hold(file)", 1, 1, "deck",
            "Puts a program (a file, or blade or ward) in a free hand of the runner's virtual body, ready to use. False if it isn't a program."),
        new("deck.push", "deck.push(address, file)", 2, 2, "deck",
            "Copies a file to the computer at address (one the deck reaches over the network), or with address None onto the computer whose pad the runner stands at. The runner must be allowed: it is nobody's, they carry its owner's card, or they stand at it with its lock breached. It lands under its own name, out of any folder; the result shows on the terminal. False if there's no such file."),
        new("body.vitals", "body.vitals()", 0, 0, "implant",
            "How the implant's owner is: .state (\"ok\", \"critical\" or \"dead\"), .damage (critical at 100, dead at 200), .brute, .burn, .oxy, .blood (percent), .bleeding, .doses (a trauma pump's) and .ready (a reflex booster can boost). None if the implant isn't fitted."),
        new("body.alert", "body.alert(text)", 1, 1, "implant",
            "Shows a line to the implant's owner, wherever they are. Once a second at most: False if too soon."),
        new("body.inject", "body.inject()", 0, 0, "implant",
            "A trauma pump's dose: stops the bleeding, puts some blood back and holds off dying for a minute. The doses left, or False if it is empty or not a trauma pump."),
        new("body.boost", "body.boost()", 0, 0, "implant",
            "A reflex booster's boost: five seconds of speed, then thirty to recover. False if it isn't ready or isn't a reflex booster."),
    };

    /// <summary>
    /// Constants a program reads from a module, and their values.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, long Value)> Constants = new[]
    {
        ("term.ENTER", 10L),
        ("term.BACKSPACE", 8L),
        ("term.TAB", 9L),
        ("term.DELETE", 127L),
        ("term.UP", 0x11_0001L),
        ("term.DOWN", 0x11_0002L),
        ("term.LEFT", 0x11_0003L),
        ("term.RIGHT", 0x11_0004L),
        ("term.HOME", 0x11_0005L),
        ("term.END", 0x11_0006L),
        ("term.PAGE_UP", 0x11_0007L),
        ("term.PAGE_DOWN", 0x11_0008L),
    };

    /// <summary>
    /// Methods, by the kind of value that has them.
    /// </summary>
    public static readonly IReadOnlyList<(string Kind, string Usage, string Doc)> Methods = new[]
    {
        ("list", "list.append(x)", "Adds x to the end."),
        ("list", "list.pop() / list.pop(i)", "Removes and returns the last item (or item i)."),
        ("list", "list.insert(i, x)", "Puts x at position i."),
        ("list", "list.remove(x)", "Removes the first item equal to x."),
        ("list", "list.index(x)", "Where x first is, or None."),
        ("list", "list.count(x)", "How many items equal x."),
        ("text", "text.upper() / text.lower()", "In capitals, or small letters."),
        ("text", "text.strip()", "Without spaces (and newlines) at either end."),
        ("text", "text.split() / text.split(sep)", "A list of the words (or the parts between sep)."),
        ("text", "text.startswith(t) / text.endswith(t)", "Whether it starts or ends with t."),
        ("text", "text.find(t)", "Where t first is in it, or -1."),
        ("text", "text.replace(old, new)", "With every old replaced by new."),
        ("text", "text.join(list)", "The list's texts joined with this text between them."),
        ("text", "text.count(t)", "How many times t is in it."),
        ("dict", "dict.keys() / dict.values()", "Its keys, or values, as a list."),
        ("dict", "dict.get(key) / dict.get(key, default)", "The value for key, or default (None) if it has none."),
        ("dict", "dict.pop(key)", "Removes key and returns its value (None if it had none)."),
    };

    /// <summary>
    /// The names of every method, which the compiler knows how to call.
    /// </summary>
    public static readonly IReadOnlySet<string> MethodNames = new HashSet<string>
    {
        "append", "pop", "insert", "remove", "index", "count", "upper", "lower", "strip", "split", "startswith",
        "endswith", "find", "replace", "join", "keys", "values", "get",
    };

    /// <summary>
    /// Hooks: functions a program defines for the machine to call.
    /// </summary>
    public static readonly IReadOnlyList<(string Hook, string? Device, string Doc)> Hooks = new[]
    {
        ("(top-level code)", (string?) null,
            "Everything not inside a def runs once, when the program starts."),
        ("def tick():", null,
            "Runs 30 times a second after that, until sys.exit(). Without it, the program ends once its top-level code has run."),
        ("def on_door_request(who):", "door",
            "Runs when someone tries to open the door by hand, before it opens: return True to let them in, False to keep it shut. who.name is their name, who.holding the kind of thing in their hand (\"crowbar\", \"wrench\", ...; \"\" if nothing) and who.cards the organization tags of the ID cards they carry."),
    };

    private static readonly Dictionary<string, WireFunction> BuiltinsByName = Builtins.ToDictionary(f => f.Name);
    private static readonly Dictionary<string, WireFunction> ModuleFunctionsByName = ModuleFunctions.ToDictionary(f => f.Name);

    public static WireFunction? Builtin(string name) => BuiltinsByName.GetValueOrDefault(name);

    public static WireFunction? ModuleFunction(string name) => ModuleFunctionsByName.GetValueOrDefault(name);

    public static bool IsModule(string name) => Modules.Contains(name);

    public static long? Constant(string name)
    {
        foreach (var (n, v) in Constants)
        {
            if (n == name)
                return v;
        }

        return null;
    }
}
