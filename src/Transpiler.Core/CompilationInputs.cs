using System.Collections.Immutable;
using System.Security.Cryptography;

namespace Transpiler.Core;

/// <summary>Input-I/O quotas, not a security sandbox or a bound on compiler CPU/heap use.</summary>
public sealed record CompilationInputLimits(long MaxFileBytes = 128L * 1024 * 1024,
    long MaxTotalBytes = 512L * 1024 * 1024, int MaxFiles = 4096)
{
    internal void Validate()
    {
        if (MaxFileBytes < 1 || MaxFileBytes > int.MaxValue || MaxTotalBytes < 1 || MaxFiles < 1)
            throw new ArgumentOutOfRangeException(nameof(CompilationInputLimits), "Input quotas must be positive; a file must fit in an Int32-sized image.");
    }
}

/// <summary>One immutable image is used for hashing, identity inspection, source binding and importing.</summary>
public sealed record CompilationFile(string Path, ImmutableArray<byte> Content, string Sha256)
{
    public AssemblyInput Fingerprint(string identity) => new(identity, Sha256);
}

/// <summary>
/// Per-compilation, single-owner snapshot cache. Files are read once; callers must not share a session
/// across parallel compilations. Quotas count every distinct canonical path read, including candidates.
/// </summary>
public sealed class CompilationInputSession
{
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly Dictionary<string, CompilationFile> _files = new(PathComparer);
    public CompilationInputLimits Limits { get; }
    public CancellationToken CancellationToken { get; }
    public long BytesRead { get; private set; }
    public int FileCount => _files.Count;
    public bool ContainsPath(string path) => _files.ContainsKey(System.IO.Path.GetFullPath(path));

    public CompilationInputSession(CompilationInputLimits? limits = null, CancellationToken cancellationToken = default)
    {
        Limits = limits ?? new(); Limits.Validate(); CancellationToken = cancellationToken;
    }

    public CompilationFile Read(string path)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(path);
        if (_files.TryGetValue(full, out var cached)) return cached;
        if (_files.Count >= Limits.MaxFiles) Limit($"Input file count exceeds {Limits.MaxFiles}.");
        using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > Limits.MaxFileBytes || input.Length > Limits.MaxTotalBytes - BytesRead)
            Limit($"Input '{full}' exceeds the per-file or remaining total byte budget.");
        using var output = new MemoryStream((int)input.Length);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var count = input.Read(buffer);
            if (count == 0) break;
            // Check again: the length observed at open is not a promise that the file cannot grow.
            if (count > Limits.MaxFileBytes - output.Length || count > Limits.MaxTotalBytes - BytesRead)
                Limit($"Input '{full}' grew beyond the configured byte budget.");
            output.Write(buffer, 0, count); BytesRead += count;
        }
        CancellationToken.ThrowIfCancellationRequested();
        var content = output.ToArray().ToImmutableArray();
        var result = new CompilationFile(full, content, Convert.ToHexString(SHA256.HashData(content.AsSpan())).ToLowerInvariant());
        _files.Add(full, result);
        return result;
    }

    private static void Limit(string message) => throw new CompilationException(new Diagnostic("TR3300", message));
}
