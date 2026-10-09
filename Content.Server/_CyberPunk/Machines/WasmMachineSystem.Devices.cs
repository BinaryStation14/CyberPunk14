using System.Collections;
using System.Linq;
using System.Reflection;
using Content.Server._CyberPunk.Network;
using Content.Server._CyberPunk.Wasm;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.UserInterface;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._CyberPunk.Machines;

/// <summary>
/// Machines with a UI of their own, like vending machines and air alarms, on the network: one standing on data
/// cable gets an address from the router like a computer does, and programs on computers reach it with
/// <c>dev_request</c>. It answers what it is and what it takes (<c>info</c>), what its UI shows
/// (<c>state</c>), and does what its UI's buttons do (<c>call</c>), as if the computer had pressed them.
/// </summary>
/// <remarks>
/// <para>
/// A call is the UI message the machine's own window would send, built from what the program gave, and raised
/// on the machine with the computer as the one who sent it. So the machine's own checks still apply, and a
/// machine whose UI needs access refuses a computer, which carries none. Machines without power don't answer
/// calls.
/// </para>
/// <para>
/// Which messages a machine takes is read from the event bus: the UI messages that its components subscribe
/// to.
/// </para>
/// </remarks>
public sealed partial class WasmMachineSystem
{
    [Dependency] private AccessReaderSystem _access = default!;

    /// <summary>The machines with a UI on a working network this time round.</summary>
    private readonly HashSet<EntityUid> _devices = new();

    private readonly List<EntityUid> _anchored = new();

    /// <summary>The UI messages each component type takes, read from the event bus when first needed.</summary>
    private Dictionary<Type, List<Type>>? _uiMessages;

    private void InitializeDevices()
    {
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnyAnchorChanged);
    }

    private void OnAnyAnchorChanged(ref AnchorStateChangedEvent args)
    {
        if (HasComp<ActivatableUIComponent>(args.Entity))
            _networkDirty = true;
    }

    /// <summary>
    /// Whether a machine joins the network as a device: it has a UI of its own that anyone can open, and it
    /// isn't a computer, which joins the network itself.
    /// </summary>
    public bool IsDevice(EntityUid uid)
    {
        return TryComp<ActivatableUIComponent>(uid, out var aui)
               && !aui.AdminOnly
               && !aui.InHandsOnly
               && !HasComp<WasmMachineComponent>(uid)
               && _ui.HasUi(uid, aui.Key);
    }

    /// <summary>
    /// Adds the devices on data cable to the networks they're on: anything anchored on a cable's tile.
    /// </summary>
    private void FindDevices(Dictionary<object, Entity<NetworkHubComponent>> served,
        Dictionary<object, List<EntityUid>> members)
    {
        _devices.Clear();
        var cables = EntityQueryEnumerator<DataCableComponent, TransformComponent>();
        while (cables.MoveNext(out var cableUid, out var cable, out var xform))
        {
            if (!xform.Anchored
                || xform.GridUid is not { } gridUid
                || !TryComp<MapGridComponent>(gridUid, out var grid)
                || NetworkOf(cableUid, cable.Node) is not { } network
                || !served.ContainsKey(network))
            {
                continue;
            }

            var tile = _map.TileIndicesFor((gridUid, grid), xform.Coordinates);
            _anchored.Clear();
            _map.GetAnchoredEntities((gridUid, grid), tile, _anchored);
            foreach (var uid in _anchored)
            {
                if (!IsDevice(uid) || !_devices.Add(uid))
                    continue;

                if (!members.TryGetValue(network, out var list))
                    members[network] = list = new List<EntityUid>();

                list.Add(uid);
            }
        }
    }

    /// <summary>
    /// The machine at an address answering a request from a computer's program, or null when there's no
    /// machine with a UI there. An answer is a value as <see cref="MachineLiteral"/> writes it, or <c>!</c> and
    /// why it wasn't done.
    /// </summary>
    public string? DeviceRequest(EntityUid computer, uint address, string request)
    {
        if (!_addresses.TryGetValue(address, out var at)
            || !_devices.Contains(at.Machine)
            || TerminatingOrDeleted(at.Machine)
            || !Transform(at.Machine).Anchored
            || !TryComp<ActivatableUIComponent>(at.Machine, out var aui))
        {
            return null;
        }

        var device = at.Machine;
        var words = request.Split(' ', 3);
        try
        {
            switch (words[0])
            {
                case "info":
                    return MachineLiteral.Write(Info(device), Number);
                case "state":
                    return Refusal(computer, device) ?? MachineLiteral.Write(State(device, aui.Key), Number);
                case "call" when words.Length >= 2:
                    return Refusal(computer, device) ?? Call(computer, device, aui.Key, words[1], words.Length > 2 ? words[2] : "{}");
                default:
                    return "!the request is info, state or call NAME ARGS";
            }
        }
        catch (Exception e)
        {
            Log.Error($"{ToPrettyString(device)} failed a request from {ToPrettyString(computer)} ({request}): {e}");
            return "!it went wrong inside the machine";
        }
    }

    /// <summary>
    /// Why the machine won't do what a computer asks, or null if it will.
    /// </summary>
    private string? Refusal(EntityUid computer, EntityUid device)
    {
        if (!_power.IsPowered(device))
            return "!it has no power";

        if (HasComp<ActivatableUIRequiresAccessComponent>(device) && !_access.IsAllowed(computer, device))
            return "!access denied";

        return null;
    }

    private int Number(EntityUid uid)
    {
        return GetNetEntity(uid).Id;
    }

    private NetEntity? Entity(int number)
    {
        var net = new NetEntity(number);
        return TryGetEntity(net, out _) ? net : null;
    }

    /// <summary>
    /// What a machine is, and the calls it takes with what each needs.
    /// </summary>
    private Dictionary<string, object?> Info(EntityUid device)
    {
        var calls = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, type) in CallsOf(device, out _))
        {
            var needs = new Dictionary<string, string>();
            foreach (var (param, paramType, _) in Parameters(type))
            {
                needs[param] = MachineLiteral.Describe(paramType);
            }

            calls[name] = needs;
        }

        return new Dictionary<string, object?>
        {
            ["name"] = Name(device),
            ["kind"] = MetaData(device).EntityPrototype?.ID,
            ["calls"] = calls,
        };
    }

    /// <summary>
    /// What a machine's UI shows: the state its window was last sent (under <c>ui</c>), and the networked state
    /// of each component that takes its UI's messages, by the component's name.
    /// </summary>
    private Dictionary<string, object?> State(EntityUid device, Enum key)
    {
        var state = new Dictionary<string, object?>();
        state["ui"] = _ui.TryGetUiState<BoundUserInterfaceState>(device, key, out var shown) ? shown : null;

        CallsOf(device, out var handlers);
        foreach (var comp in handlers)
        {
            if (Factory.GetRegistration(comp.GetType()).NetID == null)
                continue;

            state[Factory.GetComponentName(comp.GetType())] =
                EntityManager.GetComponentState(EntityManager.EventBus, comp, null, GameTick.Zero);
        }

        return state;
    }

    /// <summary>
    /// Does a call: builds the UI message it names from the program's arguments, and raises it on the machine as
    /// if the computer had sent it.
    /// </summary>
    private string Call(EntityUid computer, EntityUid device, Enum key, string name, string args)
    {
        var calls = CallsOf(device, out _);
        var type = calls.FirstOrDefault(c => MachineLiteral.Same(name, c.Key)).Value;
        if (type == null)
            return $"!it takes no call {name} (its calls: {string.Join(", ", calls.Keys)})";

        BoundUserInterfaceMessage message;
        try
        {
            if (MachineLiteral.Parse(args) is not MachineLiteral.Dict given)
                return "!the arguments are a dict";

            message = BuildMessage(type, given);
        }
        catch (FormatException e)
        {
            return $"!{name}: {e.Message}";
        }

        message.Actor = computer;
        message.Entity = GetNetEntity(device);
        message.UiKey = key;
        RaiseLocalEvent(device, (object) message, true);
        return "None";
    }

    /// <summary>
    /// The calls a machine takes, by name, and the components on it that take them.
    /// </summary>
    private SortedDictionary<string, Type> CallsOf(EntityUid device, out List<IComponent> handlers)
    {
        var messages = UiMessages();
        var calls = new SortedDictionary<string, Type>(StringComparer.Ordinal);
        handlers = new List<IComponent>();
        foreach (var comp in AllComps(device))
        {
            if (!messages.TryGetValue(comp.GetType(), out var types))
                continue;

            handlers.Add(comp);
            foreach (var type in types)
            {
                calls.TryAdd(CallName(type), type);
            }
        }

        return calls;
    }

    /// <summary>
    /// A UI message's name for programs: its type's, less the <c>Message</c> on the end.
    /// </summary>
    private static string CallName(Type type)
    {
        var name = type.Name;
        foreach (var suffix in new[] { "BoundUserInterfaceMessage", "BuiMessage", "UiMessage", "Message" })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                return name[..^suffix.Length];
        }

        return name;
    }

    /// <summary>
    /// The UI messages each component type subscribes to, from the event bus's table of who subscribes to
    /// what. Opening and closing a window, and the wire panels every machine has, aren't calls.
    /// </summary>
    private Dictionary<Type, List<Type>> UiMessages()
    {
        if (_uiMessages != null)
            return _uiMessages;

        _uiMessages = new Dictionary<Type, List<Type>>();
        var bus = EntityManager.EventBus;
        var field = bus.GetType().GetField("_eventSubsInv", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(bus) is not IDictionary subscriptions)
        {
            Log.Error("Can't read the event bus's subscriptions, so machines on the network take no calls");
            return _uiMessages;
        }

        foreach (DictionaryEntry entry in subscriptions)
        {
            if (entry.Key is not Type message
                || message.IsAbstract
                || !typeof(BoundUserInterfaceMessage).IsAssignableFrom(message)
                || message.Namespace is not { } space
                || space.StartsWith("Robust.", StringComparison.Ordinal)
                || space.Contains(".Wires", StringComparison.Ordinal)
                || entry.Value is not IEnumerable components)
            {
                continue;
            }

            foreach (var idx in components)
            {
                var comp = Factory.IdxToType((CompIdx) idx);
                if (comp == typeof(UserInterfaceComponent) || comp == typeof(ActivatableUIComponent))
                    continue;

                if (!_uiMessages.TryGetValue(comp, out var list))
                    _uiMessages[comp] = list = new List<Type>();

                list.Add(message);
            }
        }

        return _uiMessages;
    }

    /// <summary>
    /// What a UI message is built from: its biggest public constructor's parameters (required unless they have
    /// a default), and then its public fields and properties that can be set.
    /// </summary>
    private static List<(string Name, Type Type, bool Required)> Parameters(Type type)
    {
        var list = new List<(string, Type, bool)>();
        if (Constructor(type) is { } ctor)
        {
            foreach (var p in ctor.GetParameters())
            {
                list.Add((p.Name ?? "", p.ParameterType, !p.HasDefaultValue));
            }
        }

        foreach (var (name, memberType, _) in Settable(type))
        {
            if (!list.Any(p => MachineLiteral.Same(p.Item1, name)))
                list.Add((name, memberType, false));
        }

        return list;
    }

    private static ConstructorInfo? Constructor(Type type)
    {
        return type.GetConstructors().MaxBy(c => c.GetParameters().Length);
    }

    private static IEnumerable<(string Name, Type Type, Action<object, object?> Set)> Settable(Type type)
    {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!field.IsInitOnly && Callable(field.DeclaringType))
                yield return (field.Name, field.FieldType, field.SetValue);
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is { IsPublic: true } && property.GetIndexParameters().Length == 0
                && Callable(property.DeclaringType))
            {
                yield return (property.Name, property.PropertyType, property.SetValue);
            }
        }
    }

    /// <summary>
    /// Whether members declared on a type are the message's own, rather than what every UI message has.
    /// </summary>
    private static bool Callable(Type? declaring)
    {
        return declaring != null
               && declaring != typeof(BoundUserInterfaceMessage)
               && declaring != typeof(BaseBoundUserInterfaceEvent)
               && declaring != typeof(EntityEventArgs);
    }

    /// <summary>
    /// Builds a UI message from what a program gave: the constructor's parameters by name, then any public
    /// field or property.
    /// </summary>
    /// <exception cref="FormatException">Something is missing, unknown or the wrong kind.</exception>
    private BoundUserInterfaceMessage BuildMessage(Type type, MachineLiteral.Dict given)
    {
        var used = new HashSet<string>();
        foreach (var (key, _) in given)
        {
            if (key is not string)
                throw new FormatException("argument names are text");
        }

        object message;
        if (Constructor(type) is { } ctor)
        {
            var parameters = ctor.GetParameters();
            var values = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                var match = given.FirstOrDefault(g => MachineLiteral.Same((string) g.Key!, p.Name ?? ""));
                if (match.Key is string key)
                {
                    values[i] = Convert(p.Name, match.Value, p.ParameterType);
                    used.Add(key);
                }
                else if (p.HasDefaultValue)
                {
                    values[i] = p.DefaultValue;
                }
                else
                {
                    throw new FormatException($"needs {p.Name} ({MachineLiteral.Describe(p.ParameterType)})");
                }
            }

            message = ctor.Invoke(values);
        }
        else
        {
            message = Activator.CreateInstance(type)!;
        }

        var settable = Settable(type).ToList();
        foreach (var (key, value) in given)
        {
            var name = (string) key!;
            if (used.Contains(name))
                continue;

            var member = settable.FirstOrDefault(s => MachineLiteral.Same(name, s.Name));
            if (member.Set == null)
                throw new FormatException($"takes no {name}");

            member.Set(message, Convert(name, value, member.Type));
        }

        return (BoundUserInterfaceMessage) message;
    }

    private object? Convert(string? name, object? value, Type type)
    {
        try
        {
            return MachineLiteral.Convert(value, type, Entity);
        }
        catch (FormatException e)
        {
            throw new FormatException($"{name} {e.Message}");
        }
    }

    /// <summary>
    /// Requests from one computer's programs to the machines on its network.
    /// </summary>
    private sealed class DeviceLink(WasmMachineSystem system, EntityUid computer) : IMachineDevices
    {
        public string? Request(uint address, string request)
        {
            return system.DeviceRequest(computer, address, request);
        }
    }
}
