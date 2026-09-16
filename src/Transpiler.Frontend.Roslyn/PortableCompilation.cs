using System.Runtime.InteropServices;
using Transpiler.Core;

namespace Transpiler.Frontend.Roslyn;

public sealed record LinkOptions(bool PortableBcl = false, string? ReferencePackDirectory = null, string? CoreLibrary = null);

/// <summary>Orchestrates explicit implementation inputs, declaration-only contracts, and optional portable libraries.</summary>
public static class PortableCompilation
{
    private static readonly HashSet<string> Contracts = new(StringComparer.Ordinal)
    {
        "System.IDisposable", "System.Collections.IEnumerable", "System.Collections.IEnumerator",
        "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IEnumerator`1",
        "System.Runtime.CompilerServices.IAsyncStateMachine", "System.Runtime.CompilerServices.INotifyCompletion",
        "System.Runtime.CompilerServices.ICriticalNotifyCompletion"
    };

    public static AssemblyModel Link(AssemblyModel root, IEnumerable<string>? implementationPaths = null, LinkOptions? options = null)
    {
        options ??= new();
        var dependencies = (implementationPaths ?? []).Select(p => AssemblyImporter.Read(File.ReadAllBytes(p))).ToList();
        if (options.PortableBcl)
        {
            var pack = ReferencePackResolver.Resolve(options.ReferencePackDirectory);
            var declarations = AssemblyImporter.Read(File.ReadAllBytes(Path.Combine(pack.Directory, "System.Runtime.dll")), Contracts.Contains);
            // Reference assemblies supply abstract interface metadata only, never executable stubs.
            declarations = declarations with { ContractsOnly = true, Methods = declarations.Methods.Where(m => m.IsAbstract).ToArray() };
            dependencies.Add(declarations);
            dependencies.Add(AssemblyImporter.Read(File.ReadAllBytes(typeof(Transpiler.Bcl.Enumerable).Assembly.Location)));
            var corePath = options.CoreLibrary ?? Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "System.Private.CoreLib.dll");
            var original = AssemblyImporter.Read(File.ReadAllBytes(corePath), t => t == "System.Math",
                m => m.Type == "System.Math" && m.Name == "BigMul" && m.Parameters.SequenceEqual(new[] { "System.Int32", "System.Int32" }) && m.ReturnType == "System.Int64");
            if (original.Methods.Length != 1 || original.Methods[0].Instructions.Length == 0)
                throw new CompilationException(new Diagnostic("TR3200", "The selected CoreLib does not contain the supported Math.BigMul(Int32,Int32) managed body."));
            dependencies.Add(original);
        }
        var linked = AssemblyLinker.Link(root, dependencies);
        return options.PortableBcl ? LibrarySubstitution.Apply(linked) : linked;
    }
}
