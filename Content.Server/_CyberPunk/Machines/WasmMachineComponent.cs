using Content.Server._CyberPunk.Wasm;
using Content.Shared._CyberPunk.Machines;
using Robust.Shared.Serialization.Manager.Attributes;

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
    /// Text files put on its disk when it's made, by name. A prototype that inherits from another adds its
    /// files to the parent's.
    /// </summary>
    [DataField, AlwaysPushInheritance]
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
    /// The program UI its terminal shows, as last sent to the people with it open; null for text.
    /// </summary>
    [ViewVariables]
    public ProgramUiNode? ShownUi;

    /// <summary>
    /// The title its programs give the terminal window, as last sent to the people with it open; null for none.
    /// </summary>
    [ViewVariables]
    public string? ShownTitle;

    /// <summary>
    /// Whether its terminal echoes what's typed, as last sent to the people with it open.
    /// </summary>
    [ViewVariables]
    public bool ShownEcho;

    /// <summary>
    /// The machine tick it last ran in.
    /// </summary>
    [ViewVariables]
    public ulong LastRun;
}
