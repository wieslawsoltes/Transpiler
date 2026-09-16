using System.Globalization;

namespace Transpiler.Core;

/// <summary>Deterministic, explicit-input, single-load-context linker. Never probes the network or executes assemblies.</summary>
public static class AssemblyLinker
{
    public static bool IsFramework(string name) => name is "System.Private.CoreLib" or "System.Runtime" or "System.Console"
        or "mscorlib" or "netstandard" or "System.Collections" or "System.Linq" or "System.Threading" or "System.Threading.Tasks"
        or "System.Runtime.Extensions" or "System.Memory" or "System.Runtime.InteropServices";

    public static AssemblyModel Link(AssemblyModel root, IEnumerable<AssemblyModel>? dependencies = null)
    {
        var supplied = new[] { root }.Concat(dependencies ?? []).OrderBy(a => a.Identity.ToString(), StringComparer.Ordinal).ToArray();
        if (supplied.Length > 256) Fail("TR3000", "Assembly count exceeds the 256-module compilation budget.");
        var graph = new Dictionary<string, AssemblyModel>(StringComparer.Ordinal);
        foreach (var module in supplied)
        {
            if (module.IsReferenceAssembly && !module.ContractsOnly) Fail("TR3001", $"'{module.Identity}' is a reference assembly, not executable implementation IL.");
            if (graph.TryGetValue(module.Name, out var existing))
            {
                if (existing.Identity != module.Identity || !existing.Inputs.SequenceEqual(module.Inputs))
                    Fail("TR3002", $"Conflicting identity/content for assembly '{module.Name}'. This profile permits one version per name.");
            }
            else graph[module.Name] = module;
        }
        foreach (var module in graph.Values)
            foreach (var reference in module.References)
                if (graph.TryGetValue(reference.Name, out var dependency) && reference != dependency.Identity)
                    Fail("TR3003", $"'{module.Identity}' requests '{reference}', but the supplied implementation is '{dependency.Identity}'.");
        var methods = new List<MethodDefinitionModel>(); var fields = new List<FieldDefinitionModel>(); var types = new List<TypeDefinitionModel>();
        var next = 1; var entry = 0;
        foreach (var module in graph.Values.OrderBy(a => a.Identity.ToString(), StringComparer.Ordinal))
        {
            var tokens = new SortedSet<int>(module.Methods.Select(m => m.Token));
            foreach (var reference in module.Methods.SelectMany(m => m.Instructions).Select(i => i.Operand).OfType<MethodReference>()) tokens.Add(reference.Token);
            foreach (var map in module.Types.SelectMany(t => t.Overrides)) { tokens.Add(map.Body.Token); tokens.Add(map.Declaration.Token); }
            var ids = tokens.ToDictionary(t => t, _ => next++);
            MethodReference Map(MethodReference method) => method with { Token = ids[method.Token] };
            foreach (var method in module.Methods)
                methods.Add(method with
                {
                    Reference = Map(method.Reference),
                    Instructions = method.Instructions.Select(i => i.Operand is MethodReference reference ? i with { Operand = Map(reference) } : i).ToArray()
                });
            types.AddRange(module.Types.Select(t => t with { Overrides = t.Overrides.Select(m => new MethodOverride(Map(m.Body), Map(m.Declaration))).ToArray() }));
            fields.AddRange(module.Fields);
            if (module.Name == root.Name && module.EntryPoint != 0) entry = ids[module.EntryPoint];
        }
        var duplicate = types.Where(t => !t.Name.EndsWith("<Module>", StringComparison.Ordinal)).GroupBy(t => t.Name).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) Fail("TR3004", $"Multiple definitions of '{duplicate.Key}'. Type forwarding requires an explicit resolver policy.");
        return new(root.Name, entry, types.ToArray(), methods.ToArray(), fields.ToArray())
        {
            RootAssembly = root.Name, Identity = root.Identity, ExportRoots = root.ExportRoots,
            Inputs = graph.Values.SelectMany(m => m.Inputs).OrderBy(i => i.Identity, StringComparer.Ordinal).ToArray()
        };
    }

    public static string DisplayType(string type)
    {
        if (!type.StartsWith('[')) return type;
        var end = type.IndexOf(']');
        return end < 0 ? type : type[(end + 1)..];
    }
    private static void Fail(string code, string message) => throw new CompilationException(new Diagnostic(code, message));
}
