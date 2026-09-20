using Transpiler.Core;

namespace Transpiler.Frontend.Roslyn;

public sealed record AssemblyClosureResult(AssemblyModel[] Dependencies, DependencyBinding[] Bindings);

/// <summary>
/// Resolves only explicit directory candidates named &lt;simple-name&gt;.dll/.exe. No implicit working,
/// root, SDK, NuGet or network search. Exact identity and content win, never path ordering.
/// </summary>
public static class AssemblyClosure
{
    public const int MaxAssemblies = 256;
    public const int MaxReferences = 16384;
    public const string Policy = "explicit-directory-closure-v1";

    public static AssemblyClosureResult Resolve(AssemblyModel root, IEnumerable<string> implementationPaths,
        IEnumerable<string> referenceDirectories, CompilationInputSession inputs)
    {
        ArgumentNullException.ThrowIfNull(root); ArgumentNullException.ThrowIfNull(inputs);
        var token = inputs.CancellationToken;
        var directories = referenceDirectories.Select(Path.GetFullPath).Distinct(CompilationInputSession.PathComparer)
            .Take(257).Order(StringComparer.Ordinal).ToArray();
        if (directories.Length > 256) Fail("TR3034", "Reference directory count exceeds 256.");
        token.ThrowIfCancellationRequested();
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(directory);
        }
        if (root.IsReferenceAssembly) Fail("TR3001", $"'{root.Identity}' is a reference assembly, not executable implementation IL.");
        var modules = new Dictionary<string, (AssemblyHeader Header, CompilationFile? File)>(StringComparer.Ordinal)
        { [root.Name] = (new(root.Identity, root.References, root.IsReferenceAssembly), null) };
        var pending = new Queue<string>(); pending.Enqueue(root.Name);
        var models = new List<AssemblyModel>();
        var bindings = new List<DependencyBinding>();
        string Hash(string name) => modules[name].File?.Sha256 ?? root.Inputs.Single().Sha256;
        void Add(CompilationFile file, AssemblyHeader header)
        {
            if (header.IsReferenceAssembly) Fail("TR3001", $"'{file.Path}' is a reference assembly, not executable implementation IL.");
            if (modules.TryGetValue(header.Identity.Name, out var previous))
            {
                if (header.Identity != previous.Header.Identity || file.Sha256 != Hash(header.Identity.Name))
                    Fail("TR3002", $"Conflicting identity/content for '{header.Identity.Name}'.");
                return;
            }
            if (modules.Count == MaxAssemblies) Fail("TR3000", $"Dependency closure exceeds {MaxAssemblies} assemblies.");
            modules.Add(header.Identity.Name, (header, file)); pending.Enqueue(header.Identity.Name);
        }
        foreach (var path in implementationPaths.Select(Path.GetFullPath).Distinct(CompilationInputSession.PathComparer).Order(StringComparer.Ordinal))
        {
            var file = inputs.Read(path); Add(file, AssemblyHeader.Read(file, token));
        }
        while (pending.TryDequeue(out var name))
        {
            token.ThrowIfCancellationRequested();
            var requester = modules[name].Header;
            foreach (var reference in requester.References.Distinct().OrderBy(r => r.ToString(), StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                if (bindings.Count == MaxReferences) Fail("TR3034", $"Dependency closure exceeds {MaxReferences} reference edges.");
                if (modules.TryGetValue(reference.Name, out var known))
                {
                    if (known.Header.Identity != reference)
                        Fail("TR3003", $"'{requester.Identity}' requests '{reference}', but the supplied identity is '{known.Header.Identity}'.");
                    bindings.Add(new(requester.Identity.ToString(), reference.ToString(), "implementation", Hash(reference.Name))); continue;
                }
                if (IsFrameworkContract(reference))
                {
                    bindings.Add(new(requester.Identity.ToString(), reference.ToString(), "framework-contract", null)); continue;
                }
                if (!SafeSimpleName(reference.Name)) Fail("TR3033", $"Unsafe dependency simple name '{reference.Name}'; directory traversal is not a resolution policy.");
                var candidates = new List<(CompilationFile File, AssemblyHeader Header)>();
                var observed = new List<string>();
                foreach (var directory in directories)
                    foreach (var extension in new[] { ".dll", ".exe" })
                    {
                        token.ThrowIfCancellationRequested();
                        var path = Path.Combine(directory, reference.Name + extension);
                        if (!File.Exists(path)) continue;
                        var file = inputs.Read(path); var header = AssemblyHeader.Read(file, token);
                        observed.Add($"{header.Identity}{(header.IsReferenceAssembly ? " [reference-only]" : "")}");
                        if (header.Identity == reference && !header.IsReferenceAssembly) candidates.Add((file, header));
                    }
                if (candidates.Count == 0)
                    Fail("TR3031", $"'{requester.Identity}' requires '{reference}'; no exact implementation exists in the explicit reference directories." +
                        (observed.Count == 0 ? "" : " Candidates: " + string.Join("; ", observed.Distinct().Order(StringComparer.Ordinal))));
                if (candidates.Select(c => c.File.Sha256).Distinct(StringComparer.Ordinal).Skip(1).Any())
                    Fail("TR3032", $"Multiple byte-distinct implementations match '{reference}'; refusing search-path precedence.");
                var selected = candidates.OrderBy(c => c.File.Path, StringComparer.Ordinal).First();
                Add(selected.File, selected.Header);
                bindings.Add(new(requester.Identity.ToString(), reference.ToString(), "implementation", selected.File.Sha256));
            }
        }
        foreach (var module in modules.Values.Where(m => m.File is not null).OrderBy(m => m.Header.Identity.ToString(), StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            models.Add(AssemblyImporter.Read(module.File!.Content.ToArray(), cancellationToken: token));
        }
        return new(models.ToArray(), bindings.OrderBy(b => b.Requester, StringComparer.Ordinal).ThenBy(b => b.Requested, StringComparer.Ordinal).ToArray());
    }

    public static bool IsFrameworkContract(AssemblyIdentity identity) => AssemblyLinker.IsFramework(identity.Name) &&
        identity.Culture == "neutral" && identity.PublicKeyToken is "b03f5f7f11d50a3a" or "b77a5c561934e089" or "7cec85d7bea7798e" or "cc7b13ffcd2ddd51";

    private static bool SafeSimpleName(string name) => name.Length is > 0 and <= 200 && name is not ("." or "..") &&
        !name.EndsWith('.') && !name.EndsWith(' ') && !name.Any(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c));
    private static void Fail(string code, string message) => throw new CompilationException(new Diagnostic(code, message));
}
