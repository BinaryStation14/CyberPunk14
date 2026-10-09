using System.Linq;

namespace Content.Server._CyberPunk.Wasm;

/// <summary>
/// Why a disk write failed.
/// </summary>
public enum DiskError : byte
{
    None,
    BadName,
    Full,
}

/// <summary>
/// A machine's flat disk: up to <see cref="MaxFiles"/> files and <see cref="MaxBytes"/> bytes. File names
/// may contain folders (<c>examples/hello.wat</c>), but there are no folder entries.
/// </summary>
public sealed class MachineDisk
{
    public const int MaxBytes = 1024 * 1024;
    public const int MaxFiles = 64;

    private readonly SortedDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>
    /// The files, in name order.
    /// </summary>
    public IEnumerable<KeyValuePair<string, byte[]>> Files => _files;

    public int Count => _files.Count;

    public int Used => _files.Values.Sum(f => f.Length);

    public byte[]? Read(string name)
    {
        return _files.GetValueOrDefault(name);
    }

    public DiskError Write(string name, byte[] data)
    {
        if (!ValidFileName(name))
            return DiskError.BadName;

        var replaced = _files.TryGetValue(name, out var old) ? old.Length : 0;
        var newFile = old == null;
        if (Used - replaced + data.Length > MaxBytes || newFile && _files.Count >= MaxFiles)
            return DiskError.Full;

        _files[name] = data;
        return DiskError.None;
    }

    public bool Remove(string name)
    {
        return _files.Remove(name);
    }

    /// <summary>
    /// Whether a file name is allowed: up to 64 characters, made of parts separated by <c>/</c>, each 1 to 32
    /// letters, digits, dots, dashes or underscores and not only dots.
    /// </summary>
    public static bool ValidFileName(string name)
    {
        if (name.Length > 64)
            return false;

        foreach (var part in name.Split('/'))
        {
            if (part.Length is < 1 or > 32 || part.All(c => c == '.'))
                return false;

            if (!part.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_'))
                return false;
        }

        return true;
    }
}
