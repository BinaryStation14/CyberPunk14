using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Content.Server._CyberPunk.Wasm;
using Content.Shared._CyberPunk.Machines;
using NUnit.Framework;
using Wasmtime;

#nullable enable

namespace Content.Tests.Server._CyberPunk;

/// <summary>
/// A machine on its own: the default OS's shell, programs, booting its own OS, and the sandbox's limits. A misbehaving program must
/// never hang or crash the host. Ported from Switchboard's <c>sb_wasm/tests/vm.rs</c>.
/// </summary>
[TestFixture]
[TestOf(typeof(Vm))]
public sealed class VmTest
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

    /// <summary>
    /// A booted machine and everything it has printed so far.
    /// </summary>
    private sealed class Machine : IDisposable
    {
        public readonly WasmHost Host;
        public readonly Vm Vm;
        public string Screen = "";

        public Machine(WasmHost host, Vm vm)
        {
            Host = host;
            Vm = vm;
        }

        public static Machine Boot(WasmHost host)
        {
            var machine = new Machine(host, new Vm());
            machine.Vm.PowerOn(host);
            machine.RunUntil("$ ");
            return machine;
        }

        public TimeSpan Tick()
        {
            var watch = Stopwatch.StartNew();
            Vm.Tick(Host, 33, WasmHost.FuelPerCall);
            var took = watch.Elapsed;
            Screen += Vm.TakeOutput();
            return took;
        }

        /// <summary>
        /// Ticks until <paramref name="text"/> appears on the screen.
        /// </summary>
        public void RunUntil(string text)
        {
            for (var i = 0; i < 200; i++)
            {
                if (Screen.Contains(text))
                    return;

                Tick();
            }

            Assert.Fail($"Never saw \"{text}\". Screen:\n{Screen}");
        }

        /// <summary>
        /// Types a command and runs until the shell prompts again, returning what it printed.
        /// </summary>
        public string Command(string line)
        {
            Screen = "";
            Vm.TypeLine(line);
            RunUntil($"{line}\n");
            for (var i = 0; i < 200 && !Screen.EndsWith("$ "); i++)
            {
                Tick();
            }

            return Screen;
        }

        public string? Upload(string name, string wat)
        {
            return Vm.Upload(Host, name, Module.ConvertText(wat));
        }

        public void Dispose()
        {
            Vm.Dispose();
        }
    }

    [Test]
    public void TheOsBootsToAShell()
    {
        using var m = Machine.Boot(_host);

        Assert.That(m.Screen, Does.Contain(DefaultOs.Name));
        Assert.That(m.Screen, Does.Contain($"kernel v{Kernel.ApiVersion}"));
        Assert.That(m.Vm.State, Is.EqualTo(VmState.Running));
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { "os" }));
    }

    [Test]
    public void TheShellReadsAndWritesFiles()
    {
        using var m = Machine.Boot(_host);

        Assert.That(m.Command("ls"), Does.Contain("(no files)"));
        Assert.That(m.Command("write notes.txt remember the milk"), Does.Contain("wrote 18 bytes to notes.txt"));
        Assert.That(m.Command("append notes.txt and eggs"), Does.Contain("wrote 27 bytes"));
        Assert.That(m.Command("cat notes.txt"), Does.Contain("remember the milk\nand eggs\n$ "));
        Assert.That(m.Command("ls"), Does.Contain("notes.txt"));
        m.Command("rm notes.txt");
        Assert.That(m.Command("cat notes.txt"), Does.Contain("cat: notes.txt: no such file"));
        Assert.That(m.Command("rm notes.txt"), Does.Contain("rm: notes.txt: no such file"));
        Assert.That(m.Command("echo hi there"), Does.Contain("hi there\n"));
        Assert.That(m.Command("write ../evil x"), Does.Contain("write: bad file name"));
        Assert.That(m.Command("frobnicate"), Does.Contain("frobnicate: unknown command"));
        Assert.That(m.Command("ver"), Does.Contain($"{DefaultOs.Name}, kernel v{Kernel.ApiVersion}"));
        Assert.That(m.Command("uptime"), Does.Match(@"up \d+ s"));
        Assert.That(m.Command("help"), Does.Contain("build FILE"));
    }

    [Test]
    public void LongOutputGoesOutOverSeveralTicks()
    {
        using var m = Machine.Boot(_host);

        // Far more than a tick's 4 KiB of terminal output.
        var text = string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i:000} of a long file\n"));
        m.Vm.SeedFile("long.txt", System.Text.Encoding.UTF8.GetBytes(text));

        var shown = m.Command("cat long.txt");
        Assert.That(shown, Does.Contain(text));
        Assert.That(shown, Does.Not.Contain("[output truncated]"));

        // The manual too.
        var kernel = m.Command("man kernel");
        Assert.That(kernel, Does.Contain("fs_read"));
        Assert.That(m.Command("man computer"), Does.Contain("term_write"));
        Assert.That(kernel, Does.Contain("job_kill"));
        Assert.That(m.Command("man nonsense"), Does.Contain("man: no page on nonsense"));
    }

    [Test]
    public void AProgramRunsAndPrints()
    {
        using var m = Machine.Boot(_host);
        Assert.That(m.Upload("hello.bin", WasmSamples.Hello), Is.Null);

        var output = m.Command("run hello.bin");
        Assert.That(output, Does.Contain("Hello from WASM!"));

        // And control comes back to the shell.
        Assert.That(output, Does.EndWith("$ "));
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { "os" }));
    }

    [Test]
    public void ProgramsGetTheirArguments()
    {
        using var m = Machine.Boot(_host);
        m.Upload("args.bin", """
            (module
              (import "sb_v1" "args" (func $args (param i32 i32) (result i32)))
              (import "sb_v0" "term_write" (func $write (param i32 i32)))
              (memory (export "memory") 1)
              (func (export "start")
                (call $write (i32.const 0) (call $args (i32.const 0) (i32.const 256)))))
            """);

        Assert.That(m.Command("run args.bin one two  three"), Does.Contain("one two  three"));
        Assert.That(m.Command("run"), Does.Contain("usage: run"));
        Assert.That(m.Command("run missing.bin"), Does.Contain("run: missing.bin: no such file"));
        m.Command("write notes.txt not a program");
        Assert.That(m.Command("run notes.txt"), Does.Contain("run: notes.txt: not a program"));
    }

    [Test]
    public void AnEndlessLoopIsKilledQuicklyAndTheShellCarriesOn()
    {
        using var m = Machine.Boot(_host);
        m.Upload("spin.bin", WasmSamples.Spin);
        m.Screen = "";
        m.Vm.TypeLine("run spin.bin");

        var slowest = TimeSpan.Zero;
        for (var i = 0; i < 10 && !m.Screen.Contains("time budget"); i++)
        {
            var took = m.Tick();
            if (took > slowest)
                slowest = took;
        }

        Assert.That(m.Screen, Does.Contain("[spin.bin killed: it used up its time budget]"));

        // One call's budget, well under a tick in a release build; generous for debug builds and busy machines.
        Assert.That(slowest, Is.LessThan(TimeSpan.FromMilliseconds(250)));
        Assert.That(m.Command("echo still here"), Does.Contain("still here"));
    }

    [Test]
    public void MemoryIsCapped()
    {
        using var m = Machine.Boot(_host);

        // Grows its memory until refused, then says so.
        m.Upload("hog.bin", """
            (module
              (import "sb_v0" "term_write" (func $write (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "capped")
              (func (export "start")
                (block $done
                  (loop $grow
                    (br_if $done (i32.eq (memory.grow (i32.const 16)) (i32.const -1)))
                    (br $grow)))
                (call $write (i32.const 0) (i32.const 6))))
            """);
        Assert.That(m.Command("run hog.bin"), Does.Contain("capped"));

        // One that asks for too much up front doesn't start at all.
        Assert.That(2000 * 65536L, Is.GreaterThan(WasmHost.MemoryLimit));
        m.Upload("huge.bin", """(module (memory (export "memory") 2000) (func (export "start")))""");
        Assert.That(m.Command("run huge.bin"), Does.Contain("could not run huge.bin"));
    }

    [Test]
    public void CrashingProgramsAreReportedAndCleanedUp()
    {
        using var m = Machine.Boot(_host);
        m.Upload("recurse.bin", """
            (module
              (memory (export "memory") 1)
              (func $deeper (call $deeper))
              (func (export "start") (call $deeper)))
            """);
        // Players see why it trapped, not the start of a backtrace.
        Assert.That(m.Command("run recurse.bin"), Does.Contain("[recurse.bin crashed: wasm trap: call stack exhausted]"));

        // Pointers outside its memory trap instead of reading the host's.
        m.Upload("snoop.bin", WasmSamples.BadPointer);
        Assert.That(m.Command("run snoop.bin"), Does.Contain("[snoop.bin crashed"));
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { "os" }));
    }

    [Test]
    public void UploadsAreChecked()
    {
        using var m = Machine.Boot(_host);

        Assert.That(m.Vm.Upload(_host, "junk.bin", "not wasm at all"u8.ToArray()), Does.Contain("not a valid program"));
        Assert.That(m.Upload("future.bin",
                """(module (import "sb_v9" "teleport" (func)) (memory (export "memory") 1) (func (export "start")))"""),
            Does.Contain("needs host API v9"));
        Assert.That(m.Upload("other.bin",
                """(module (import "env" "f" (func)) (memory (export "memory") 1) (func (export "start")))"""),
            Does.Contain("imports \"env\""));
        Assert.That(m.Upload("lib.bin", """(module (memory (export "memory") 1))"""), Does.Contain("start"));
        Assert.That(m.Upload("../evil", WasmSamples.Hello), Is.Not.Null);
        Assert.That(m.Command("run junk.bin"), Does.Contain("no such file"));
    }

    [Test]
    public void KernelFunctionsAreOnlyInTheVersionsThatHaveThem()
    {
        using var m = Machine.Boot(_host);

        // random arrived in v4, so a program importing it from sb_v0 doesn't link.
        m.Upload("old.bin", """
            (module
              (import "sb_v0" "random" (func (result i32)))
              (memory (export "memory") 1)
              (func (export "start")))
            """);
        Assert.That(m.Command("run old.bin"), Does.Contain("could not run old.bin"));

        // And a program built for an older kernel still links against it.
        m.Upload("v0.bin", WasmSamples.Hello.Replace("sb_v4", "sb_v0"));
        Assert.That(m.Command("run v0.bin"), Does.Contain("Hello from WASM!"));
    }

    [Test]
    public void PowerCutsStopEverythingAndFilesSurvive()
    {
        using var m = Machine.Boot(_host);
        m.Command("write keep.txt survives the blackout");

        m.Vm.PowerOff();
        m.Screen += m.Vm.TakeOutput();
        Assert.That(m.Screen, Does.Contain("[power lost]"));
        Assert.That(m.Vm.State, Is.EqualTo(VmState.Off));
        Assert.That(m.Vm.Processes, Is.Empty);

        // Typing into a dead machine does nothing.
        m.Vm.TypeLine("echo ghost");
        m.Tick();

        m.Screen = "";
        m.Vm.PowerOn(_host);
        m.RunUntil("$ ");
        Assert.That(m.Screen, Does.Contain("[power restored: rebooting]"));
        Assert.That(m.Screen, Does.Not.Contain("ghost"));
        Assert.That(m.Command("cat keep.txt"), Does.Contain("survives the blackout"));
    }

    [Test]
    public void ACrashedOsRebootsItself()
    {
        // An "OS" that ends at once: the machine halts, then reboots.
        using var host = new WasmHost(Module.ConvertText("""(module (memory (export "memory") 1) (func (export "start")))"""));
        using var vm = new Vm();
        vm.PowerOn(host);
        vm.Tick(host, 33, WasmHost.FuelPerCall);
        Assert.That(vm.State, Is.EqualTo(VmState.Halted));

        var screen = vm.TakeOutput();
        for (var i = 0; i < 100; i++)
        {
            vm.Tick(host, 33, WasmHost.FuelPerCall);
            screen += vm.TakeOutput();
        }

        Assert.That(screen, Does.Contain("[system halted]"));
        Assert.That(screen, Does.Contain("[rebooting]"));
    }

    [Test]
    public void WatBuildsOnTheMachine()
    {
        using var m = Machine.Boot(_host);
        m.Vm.SeedFile("hi.wat", """
            (module
              (import "sb_v5" "term_write" (func $write (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "built here\n")
              (func (export "start") (call $write (i32.const 0) (i32.const 11))))
            """u8.ToArray());

        Assert.That(m.Command("build hi.wat"), Does.Match(@"built hi\.bin \(\d+ bytes\)"));
        Assert.That(m.Command("run hi.bin"), Does.Contain("built here"));
        Assert.That(m.Command("build hi.wat other.bin"), Does.Contain("built other.bin"));

        m.Vm.SeedFile("bad.wat", "(module (func (export \"start\") (i32.add)))"u8.ToArray());
        var bad = m.Command("build bad.wat");
        Assert.That(bad, Does.Contain("build: bad.wat: "));
        Assert.That(bad.Length, Is.GreaterThan("build: bad.wat: \n$ ".Length + "build bad.wat\n".Length));

        Assert.That(m.Command("build nothing.wat"), Does.Contain("build: nothing.wat: no such file"));
    }

    private const string Loop = """
        (module
          (memory (export "memory") 1)
          (func (export "start"))
          (func (export "tick")))
        """;

    [Test]
    public void BackgroundJobsRunAlongsideTheShell()
    {
        using var m = Machine.Boot(_host);
        m.Upload("once.bin", """
            (module
              (import "sb_v0" "term_write" (func $write (param i32 i32)))
              (import "sb_v0" "exit" (func $exit (param i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "job ran\n")
              (func (export "start"))
              (func (export "tick")
                (call $write (i32.const 0) (i32.const 8))
                (call $exit (i32.const 0))))
            """);
        m.Upload("loop.bin", Loop);

        Assert.That(m.Command("run once.bin &"), Does.Contain("[1] once.bin"));
        m.RunUntil("job ran");

        Assert.That(m.Command("run loop.bin &"), Does.Contain("[2] loop.bin"));
        Assert.That(m.Command("jobs"), Does.Contain("[2] loop.bin"));
        Assert.That(m.Vm.Jobs.Select(j => j.Id), Is.EqualTo(new uint[] { 2 }));

        // The shell still answers while it runs.
        Assert.That(m.Command("echo alongside"), Does.Contain("alongside"));

        Assert.That(m.Command("kill 2"), Does.Contain("stopping job 2"));
        m.RunUntil("[job 2: loop.bin killed]");
        Assert.That(m.Vm.Jobs, Is.Empty);
        Assert.That(m.Command("jobs"), Does.Contain("(no background jobs)"));
        Assert.That(m.Command("kill 9"), Does.Contain("kill: no job 9"));
        Assert.That(m.Command("kill x"), Does.Contain("usage: kill N"));
    }

    [Test]
    public void ProgramsStackOnlySoDeep()
    {
        using var m = Machine.Boot(_host);

        // Runs itself, so each copy stacks another on top until the kernel refuses.
        m.Upload("nest.bin", """
            (module
              (import "sb_v0" "exec" (func $exec (param i32 i32) (result i32)))
              (import "sb_v0" "term_write" (func $write (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "nest.bin")
              (data (i32.const 16) "too deep\n")
              (func (export "start")
                (if (i32.eq (call $exec (i32.const 0) (i32.const 9)) (i32.const -3))
                  (then (call $write (i32.const 16) (i32.const 9)))))
              (func (export "tick")))
            """);

        m.Screen = "";
        m.Vm.TypeLine("run nest.bin");
        m.RunUntil("too deep");
        Assert.That(m.Vm.Processes, Has.Count.EqualTo(WasmHost.MaxDepth));
    }

    [Test]
    public void RawModeSendsKeysOneByOne()
    {
        using var m = Machine.Boot(_host);

        // Prints the first key it gets, as a character, and ends.
        m.Upload("key.bin", """
            (module
              (import "sb_v2" "term_raw" (func $raw (param i32)))
              (import "sb_v2" "term_key" (func $key (result i32)))
              (import "sb_v0" "term_write" (func $write (param i32 i32)))
              (import "sb_v0" "exit" (func $exit (param i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "got ")
              (func (export "start") (call $raw (i32.const 1)))
              (func (export "tick")
                (local $k i32)
                (local.set $k (call $key))
                (if (i32.ge_s (local.get $k) (i32.const 0))
                  (then
                    (i32.store8 (i32.const 4) (local.get $k))
                    (call $write (i32.const 0) (i32.const 5))
                    (call $exit (i32.const 0))))))
            """);

        m.Screen = "";
        m.Vm.TypeLine("run key.bin");
        for (var i = 0; i < 10 && !m.Vm.IsRaw; i++)
        {
            m.Tick();
        }

        Assert.That(m.Vm.IsRaw);

        // Typed lines are ignored in raw mode, and so are invalid keys.
        m.Vm.TypeLine("ignored");
        m.Vm.TypeKey(0);
        m.Vm.TypeKey('x');
        m.RunUntil("got x");
        m.RunUntil("$ ");
        Assert.That(m.Vm.IsRaw, Is.False);
        Assert.That(m.Screen, Does.Not.Contain("ignored"));
    }

    [Test]
    public void KeysEditTheLineOutsideRawMode()
    {
        using var m = Machine.Boot(_host);

        void Type(string text)
        {
            foreach (var c in text)
            {
                m.Vm.TypeKey(c);
            }
        }

        // What's typed shows as it's typed, Backspace rubs out, and Enter runs the line.
        m.Screen = "";
        Type("echo helo");
        m.Vm.TypeKey(TerminalKeys.Backspace);
        Type("lo");
        m.Vm.TypeKey(TerminalKeys.Enter);
        m.RunUntil("$ ");
        var screen = TerminalText.Apply("", m.Screen);
        Assert.That(screen, Does.StartWith("echo hello\nhello\n"));

        // Up brings the last line back, and Down goes back to a new one.
        m.Screen = "";
        m.Vm.TypeKey(TerminalKeys.Up);
        m.Vm.TypeKey(TerminalKeys.Down);
        Type("echo two");
        m.Vm.TypeKey(TerminalKeys.Up);
        m.Vm.TypeKey(TerminalKeys.Enter);
        m.RunUntil("$ ");
        screen = TerminalText.Apply("", m.Screen);
        Assert.That(screen, Does.StartWith("echo hello\nhello\n"));
    }

    [Test]
    public void TheSamplesRunAsFirmware()
    {
        string Run(string wat)
        {
            using var vm = Vm.WithFirmware("sample", _host.Load(Module.ConvertText(wat)), DeviceKind.Computer);
            vm.PowerOn(_host);
            for (var i = 0; i < 3 && vm.State == VmState.Running; i++)
            {
                vm.Tick(_host, 33, WasmHost.FuelPerCall);
            }

            return vm.TakeOutput();
        }

        Assert.That(Run(WasmSamples.Hello), Does.StartWith("Hello from WASM!\n"));
        Assert.That(Run(WasmSamples.Gc), Does.StartWith("GC ok\n"));
        Assert.That(Run(WasmSamples.Grow), Does.StartWith("refused\n"));
        Assert.That(Run(WasmSamples.Spin), Does.Contain("[sample killed: it used up its time budget]"));
        Assert.That(Run(WasmSamples.BadPointer), Does.Contain("[sample crashed"));
    }

    private const string DoorFirmware = """
        (module
          (import "sb_v2" "door_open" (func $open (result i32)))
          (import "sb_v2" "door_status" (func $status (result i32)))
          (import "sb_v2" "request_name" (func $name (param i32 i32) (result i32)))
          (import "sb_v0" "term_write" (func $write (param i32 i32)))
          (memory (export "memory") 1)
          (data (i32.const 0) "Ana")
          (func (export "start")
            (drop (call $open)))
          (func (export "tick"))
          ;; Lets in Ana only.
          (func (export "on_door_request") (result i32)
            (if (i32.ne (call $name (i32.const 16) (i32.const 32)) (i32.const 3))
              (then (return (i32.const 0))))
            (i32.eq (i32.load (i32.const 16)) (i32.load (i32.const 0)))))
        """;

    [Test]
    public void DoorFirmwareWorksTheDoorAndVetsWhoOpensIt()
    {
        using var vm = Vm.WithFirmware("door", _host.Load(Module.ConvertText(DoorFirmware)), DeviceKind.DoorController);
        vm.SetDevice(new DoorDevice(Open: false, Blocked: false, Bolted: false));
        vm.PowerOn(_host);

        // It hasn't started yet, so it guards nothing.
        Assert.That(vm.DoorRequest(_host, new Requester("Ana", "", []), WasmHost.FuelPerCall / 4), Is.Null);

        vm.Tick(_host, 33, WasmHost.FuelPerCall);
        Assert.That(vm.TakeDeviceCommands(), Is.EqualTo(new[] { DeviceCommand.OpenDoor }));
        Assert.That(vm.GuardsDoor);
        Assert.That(vm.DoorRequest(_host, new Requester("Ana", "", []), WasmHost.FuelPerCall / 4), Is.True);
        Assert.That(vm.DoorRequest(_host, new Requester("Bob", "crowbar", []), WasmHost.FuelPerCall / 4), Is.False);
    }

    [Test]
    public void DoorFunctionsOnlyWorkOnDoorControllers()
    {
        // The same firmware on a computer: door_open returns -1 and nothing reaches the world.
        using var vm = Vm.WithFirmware("door", _host.Load(Module.ConvertText(DoorFirmware)), DeviceKind.Computer);
        vm.SetDevice(new DoorDevice(false, false, false));
        vm.PowerOn(_host);
        vm.Tick(_host, 33, WasmHost.FuelPerCall);

        Assert.That(vm.TakeDeviceCommands(), Is.Empty);
    }

    [Test]
    public void ABoltedDoorWontOpen()
    {
        using var vm = Vm.WithFirmware("door", _host.Load(Module.ConvertText(DoorFirmware)), DeviceKind.DoorController);
        vm.SetDevice(new DoorDevice(Open: false, Blocked: false, Bolted: true));
        vm.PowerOn(_host);
        vm.Tick(_host, 33, WasmHost.FuelPerCall);

        Assert.That(vm.TakeDeviceCommands(), Is.Empty);
    }

    [Test]
    public void ProgramsOnlyReachTheNetworkTheyAreOn()
    {
        // Sends one packet to 10.0.0.2 on port 7 at start, and prints what came of it.
        const string sender = """
            (module
              (import "sb_v1" "net_send" (func $send (param i32 i32 i32 i32) (result i32)))
              (import "sb_v0" "term_write" (func $write (param i32 i32)))
              (memory (export "memory") 1)
              (data (i32.const 0) "ping")
              (func (export "start")
                (i32.store8 (i32.const 16) (i32.add (i32.const 50) (call $send (i32.const 0x0A000002) (i32.const 7) (i32.const 0) (i32.const 4))))
                (call $write (i32.const 16) (i32.const 1))))
            """;

        using var vm = Vm.WithFirmware("sender", _host.Load(Module.ConvertText(sender)), DeviceKind.Computer);

        // Not connected: -1, so "1".
        vm.PowerOn(_host);
        vm.Tick(_host, 33, WasmHost.FuelPerCall);
        Assert.That(vm.TakeOutput(), Does.StartWith("1"));

        vm.PowerOff();
        vm.SetNetwork(0x0A000001, new HashSet<uint> { 0x0A000002 }, [0x0A000002], new Dictionary<string, uint>());
        vm.PowerOn(_host);
        vm.TakeOutput();
        vm.Tick(_host, 33, WasmHost.FuelPerCall);
        Assert.That(vm.TakeOutput(), Does.StartWith("2"));
        var sent = vm.TakeOutbox();
        Assert.That(sent, Has.Count.EqualTo(1));
        Assert.That(sent[0].From, Is.EqualTo(0x0A000001));
        Assert.That(sent[0].To, Is.EqualTo(0x0A000002));
        Assert.That(sent[0].Port, Is.EqualTo(7));
        Assert.That(sent[0].Data, Is.EqualTo("ping"u8.ToArray()));
    }

    [Test]
    public void TheShellNamesTheMachine()
    {
        var m = Machine.Boot(_host);
        Assert.That(m.Command("ip"), Does.Contain("ip: no address"));
        Assert.That(m.Command("hostname"), Does.Contain("(no hostname"));
        Assert.That(m.Command("hosts"), Does.Contain("(no hostnames known"));

        Assert.That(m.Command("hostname Not Valid"), Does.Contain("hostname: a name is"));
        m.Command("hostname lab-1");
        Assert.That(m.Vm.TakeHostnameChanged());
        Assert.That(m.Vm.Hostname, Is.EqualTo("lab-1"));
        Assert.That(m.Command("hostname"), Does.Contain("lab-1\n"));

        m.Vm.SetNetwork(0x0A010102,
            new HashSet<uint> { 0x0A010102, 0x0A010203 },
            [],
            new Dictionary<string, uint> { ["lab-1"] = 0x0A010102, ["door"] = 0x0A010203 });
        Assert.That(m.Command("ip"), Does.Contain("10.1.1.2\n"));
        Assert.That(m.Command("hosts"), Does.Contain("door 10.1.2.3\nlab-1 10.1.1.2\n"));

        // The name lasts through a power cut.
        m.Vm.PowerOff();
        m.Vm.PowerOn(_host);
        m.RunUntil("$ ");
        Assert.That(m.Vm.Hostname, Is.EqualTo("lab-1"));
    }

    [Test]
    public void TheShellCopiesFilesAndListsFolders()
    {
        using var m = Machine.Boot(_host);
        m.Vm.SeedFile("examples/hello.wire", "print('hi')\n"u8.ToArray());
        m.Vm.SeedFile("examples/spin.wire", "x\n"u8.ToArray());

        var all = m.Command("ls");
        Assert.That(all, Does.Contain("examples/"));
        Assert.That(all, Does.Contain("2 files"));
        Assert.That(m.Command("ls examples"), Does.Match(@"hello\.wire +12 bytes"));
        Assert.That(m.Command("ls nowhere"), Does.Contain("ls: nowhere: no such folder"));

        Assert.That(m.Command("cp examples/hello.wire ."), Does.Contain("copied examples/hello.wire to hello.wire"));
        Assert.That(m.Command("cat hello.wire"), Does.Contain("print('hi')"));
        Assert.That(m.Command("cp hello.wire examples"), Does.Contain("to examples/hello.wire"));
        Assert.That(m.Command("cp missing.txt x"), Does.Contain("cp: missing.txt: no such file"));
    }

    [Test]
    public void AutorunRunsCommandsAtBoot()
    {
        using var m = new Machine(_host, new Vm());
        Assert.That(m.Upload("hello.bin", WasmSamples.Hello), Is.Null);
        m.Vm.SeedFile("autorun", "echo first\n\nrun hello.bin\necho never\n"u8.ToArray());
        m.Vm.PowerOn(_host);
        m.RunUntil("Hello from WASM!");
        m.RunUntil("$ ");

        Assert.That(m.Screen, Does.Contain("autorun: echo first\nfirst\n"));
        Assert.That(m.Screen, Does.Contain("autorun: run hello.bin"));
        Assert.That(m.Screen, Does.Not.Contain("never"));
    }

    /// <summary>
    /// An OS of a player's own: prints a line and keeps running.
    /// </summary>
    private const string OwnOs = """
        (module
          (import "sb_v0" "term_write" (func $write (param i32 i32)))
          (memory (export "memory") 1)
          (data (i32.const 0) "my own OS\n")
          (func (export "start") (call $write (i32.const 0) (i32.const 10)))
          (func (export "tick")))
        """;

    [Test]
    public void ABootFileReplacesTheOs()
    {
        using var m = new Machine(_host, new Vm());
        Assert.That(m.Upload(Vm.BootFile, OwnOs), Is.Null);
        m.Vm.PowerOn(_host);
        m.RunUntil("my own OS");

        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { Vm.BootFile }));
        Assert.That(m.Screen, Does.Not.Contain(DefaultOs.Name));
    }

    [Test]
    public void ABootFileThatWontLoadFallsBackToTheDefaultOs()
    {
        using var m = new Machine(_host, new Vm());
        m.Vm.SeedFile(Vm.BootFile, "not a program"u8.ToArray());
        m.Vm.PowerOn(_host);
        m.RunUntil("$ ");

        Assert.That(m.Screen, Does.Contain($"[{Vm.BootFile} won't boot: not a valid program"));
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { "os" }));
    }

    [Test]
    public void ABootFileThatStopsFallsBackUntilARestart()
    {
        // An OS that ends at once: the machine boots the default OS after it, instead of it again.
        using var m = new Machine(_host, new Vm());
        Assert.That(m.Upload(Vm.BootFile, """(module (memory (export "memory") 1) (func (export "start")))"""), Is.Null);
        m.Vm.PowerOn(_host);
        m.RunUntil($"[{Vm.BootFile} stopped: starting the default OS]");
        m.RunUntil("$ ");
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { "os" }));

        // A fixed one boots on reboot.
        Assert.That(m.Upload(Vm.BootFile, OwnOs), Is.Null);
        m.Screen = "";
        m.Vm.TypeLine("reboot");
        m.RunUntil("my own OS");
        Assert.That(m.Screen, Does.Contain("[rebooting]"));
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { Vm.BootFile }));
    }

    [Test]
    public void RebootRestartsTheMachine()
    {
        using var m = Machine.Boot(_host);
        Assert.That(m.Upload("loop.bin", Loop), Is.Null);
        m.Command("run loop.bin &");
        Assert.That(m.Vm.Jobs, Has.Count.EqualTo(1));

        var screen = m.Command("reboot");
        Assert.That(screen, Does.Contain("[rebooting]"));
        Assert.That(screen, Does.Contain(DefaultOs.Name));
        Assert.That(m.Vm.Jobs, Is.Empty);
        Assert.That(m.Vm.Processes, Is.EqualTo(new[] { "os" }));
    }
}
