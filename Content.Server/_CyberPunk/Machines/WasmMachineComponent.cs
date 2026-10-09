using Content.Server._CyberPunk.Wasm;

namespace Content.Server._CyberPunk.Machines;

/// <summary>
/// A machine that runs WASM programs: it boots its OS (or firmware) when it gets power and stops dead when it
/// loses it. Its disk lives here, so its files survive power cuts and go wherever the machine goes.
/// </summary>
[RegisterComponent, Access(typeof(WasmMachineSystem))]
public sealed partial class WasmMachineComponent : Component
{
    /// <summary>
    /// What kind of machine it is, which decides the kernel functions that work on it.
    /// </summary>
    [DataField]
    public DeviceKind Kind = DeviceKind.Computer;

    /// <summary>
    /// Text files put on its disk when it's made, by name.
    /// </summary>
    [DataField]
    public Dictionary<string, string> Files = new();

    /// <summary>
    /// The name it goes by on the network, which its router publishes. Its programs can change it.
    /// </summary>
    [DataField]
    public string Hostname = "";

    /// <summary>
    /// Its data port in its node container, where data cable joins it to a network.
    /// </summary>
    [DataField]
    public string DataNode = "data";

    /// <summary>
    /// The running machine. Created at map init.
    /// </summary>
    [ViewVariables]
    public Vm? Vm;

    /// <summary>
    /// What its terminal shows, newest last.
    /// </summary>
    [ViewVariables]
    public string Screen = "";

    /// <summary>
    /// Whether its terminal is in raw mode.
    /// </summary>
    [ViewVariables]
    public bool Raw;

    /// <summary>
    /// The machine tick it last ran in.
    /// </summary>
    [ViewVariables]
    public ulong LastRun;
}
