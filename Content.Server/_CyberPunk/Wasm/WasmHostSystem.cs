namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Owns the server's single <see cref="WasmHost"/>. It's created the first time something needs it, so
/// servers that never run a guest program never load Wasmtime.
/// </summary>
public sealed class WasmHostSystem : EntitySystem
{
    private WasmHost? _host;

    public WasmHost Host => _host ??= new WasmHost();

    public override void Shutdown()
    {
        base.Shutdown();

        _host?.Dispose();
        _host = null;
    }
}
