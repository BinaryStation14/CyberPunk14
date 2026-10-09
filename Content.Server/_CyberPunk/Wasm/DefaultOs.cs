using System.IO;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// The operating system computers boot unless their disk has a <see cref="Vm.BootFile"/>: a shell written in
/// Wire, in <c>os.wire</c> next to this file, built when the server starts. Ported from Switchboard's
/// <c>wasm/os_default</c>.
/// </summary>
/// <remarks>
/// Players read its source with <c>new NAME os</c>, and can build their own from it.
/// </remarks>
public static class DefaultOs
{
    /// <summary>
    /// The OS's name and version, as its banner and <c>ver</c> show it. It must match <c>NAME</c> in the source.
    /// </summary>
    public const string Name = "Gridline OS 4.2";

    private static string? _source;

    /// <summary>
    /// The OS's Wire source.
    /// </summary>
    public static string Source => _source ??= Load();

    private static string Load()
    {
        using var stream = typeof(DefaultOs).Assembly.GetManifestResourceStream("CyberPunk14.os.wire")
                           ?? throw new InvalidOperationException("os.wire isn't embedded in Content.Server.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
