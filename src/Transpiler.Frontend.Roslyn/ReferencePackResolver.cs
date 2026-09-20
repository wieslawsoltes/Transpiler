using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Transpiler.Core;

namespace Transpiler.Frontend.Roslyn;

public sealed record ReferencePack(string Directory, string Version, string[] Assemblies, AssemblyInput[] Inputs);

/// <summary>Resolves SDK reference assemblies, never runtime implementation assemblies, for C# binding.</summary>
public static class ReferencePackResolver
{
    public static ReferencePack Resolve(string? explicitDirectory = null, CompilationInputSession? inputs = null)
    {
        inputs ??= new();
        inputs.CancellationToken.ThrowIfCancellationRequested();
        string directory;
        if (explicitDirectory is not null) directory = Path.GetFullPath(explicitDirectory);
        else
        {
            var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
            var root = Environment.GetEnvironmentVariable("DOTNET_ROOT") ?? runtime.Parent?.Parent?.Parent?.FullName;
            if (root is null) throw new CompilationException(new Diagnostic("TR0102", "Cannot locate the SDK root. Set DOTNET_ROOT or supply a reference-pack directory."));
            var packs = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (!System.IO.Directory.Exists(packs)) throw new CompilationException(new Diagnostic("TR0102", "No .NET reference packs found in " + packs));
            directory = System.IO.Directory.GetDirectories(packs).Select(p => new { Path = p, Version = Version.TryParse(Path.GetFileName(p), out var v) ? v : null })
                .Where(p => p.Version?.Major == 10).OrderByDescending(p => p.Version)
                .Select(p => Path.Combine(p.Path, "ref", "net10.0")).FirstOrDefault(System.IO.Directory.Exists)
                ?? throw new CompilationException(new Diagnostic("TR0102", "A .NET 10 SDK reference pack is required."));
        }
        var files = System.IO.Directory.GetFiles(directory, "*.dll").Order(StringComparer.Ordinal).ToArray();
        if (!files.Any(p => Path.GetFileName(p) == "System.Runtime.dll"))
            throw new CompilationException(new Diagnostic("TR0103", "Reference-pack directory does not contain System.Runtime.dll: " + directory));
        return new(directory, new DirectoryInfo(directory).Parent?.Parent?.Name ?? "explicit", files,
            files.Select(p => inputs.Read(p).Fingerprint(Path.GetFileName(p))).ToArray());
    }
}
