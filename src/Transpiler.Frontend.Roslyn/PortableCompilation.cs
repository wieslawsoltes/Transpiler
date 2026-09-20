using System.Runtime.InteropServices;
using Transpiler.Core;

namespace Transpiler.Frontend.Roslyn;

public sealed record LinkOptions(bool PortableBcl = false, string? ReferencePackDirectory = null, string? CoreLibrary = null)
{
    public IReadOnlyList<string> ReferenceDirectories { get; init; } = [];
}

/// <summary>Orchestrates explicit implementation inputs, declaration-only contracts, and optional portable libraries.</summary>
public static class PortableCompilation
{
    private static readonly HashSet<string> Contracts = new(StringComparer.Ordinal)
    {
        "System.IDisposable", "System.IEquatable`1", "System.IComparable`1", "System.IComparable",
        "System.Collections.Generic.IEqualityComparer`1", "System.Collections.Generic.IComparer`1",
        "System.Collections.Generic.ICollection`1", "System.Collections.Generic.IList`1",
        "System.Collections.Generic.IReadOnlyCollection`1", "System.Collections.Generic.IReadOnlyList`1",
        "System.Collections.Generic.IDictionary`2", "System.Collections.Generic.IReadOnlyDictionary`2",
        "System.Collections.Generic.ISet`1", "System.Collections.Generic.IReadOnlySet`1", "System.Collections.IEnumerable", "System.Collections.IEnumerator",
        "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IEnumerator`1",
        "System.Runtime.CompilerServices.IAsyncStateMachine", "System.Runtime.CompilerServices.INotifyCompletion",
        "System.Runtime.CompilerServices.ICriticalNotifyCompletion"
    };

    public static AssemblyModel Link(AssemblyModel root, IEnumerable<string>? implementationPaths = null, LinkOptions? options = null, CompilationInputSession? inputs = null)
    {
        options ??= new();
        inputs ??= new();
        var token = inputs.CancellationToken;
        token.ThrowIfCancellationRequested();
        var closure = options.ReferenceDirectories.Count == 0 ? null :
            AssemblyClosure.Resolve(root, implementationPaths ?? [], options.ReferenceDirectories, inputs);
        var dependencies = closure is not null ? closure.Dependencies.ToList() :
            (implementationPaths ?? []).Select(p => AssemblyImporter.Read(inputs.Read(p).Content.ToArray(), cancellationToken: token)).ToList();
        if (options.PortableBcl)
        {
            var pack = ReferencePackResolver.Resolve(options.ReferencePackDirectory, inputs);
            var declarations = AssemblyImporter.Read(inputs.Read(Path.Combine(pack.Directory, "System.Runtime.dll")).Content.ToArray(), Contracts.Contains, cancellationToken: token);
            // Reference assemblies supply abstract interface metadata only, never executable stubs.
            declarations = declarations with { ContractsOnly = true, Methods = declarations.Methods.Where(m => m.IsAbstract).ToArray() };
            dependencies.Add(declarations);
            dependencies.Add(AssemblyImporter.Read(inputs.Read(typeof(Transpiler.Bcl.Enumerable).Assembly.Location).Content.ToArray(), cancellationToken: token));
            var corePath = options.CoreLibrary ?? Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Private.CoreLib.dll");
            var original = UpstreamBclCatalog.Import(inputs.Read(corePath).Content.ToArray());
            dependencies.Add(original);
        }
        token.ThrowIfCancellationRequested();
        var linked = AssemblyLinker.Link(root, dependencies) with { DependencyBindings = closure?.Bindings ?? [] };
        return options.PortableBcl ? LibrarySubstitution.Apply(linked) : linked;
    }
}
