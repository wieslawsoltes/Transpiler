namespace Transpiler.Core;

public sealed record TypeForwarder(string MetadataName, AssemblyIdentity Target);
public sealed record ForwardingBinding(string Source, string Target);

/// <summary>
/// Resolves explicit assembly-scoped ExportedType forwarding chains before specialization.
/// Framework facade normalization remains its separately declared policy. No name-only probing or downloads.
/// </summary>
public sealed class TypeForwardingResolver
{
    private readonly IReadOnlyDictionary<string, AssemblyModel> _modules;
    private readonly Dictionary<NamedTypeIdentity, NamedTypeIdentity> _resolved = [];
    private readonly HashSet<ForwardingBinding> _used = [];
    private readonly Dictionary<NamedTypeIdentity, string> _owners = [];
    public ForwardingBinding[] Bindings => _used.OrderBy(b => b.Source, StringComparer.Ordinal).ToArray();

    public TypeForwardingResolver(IReadOnlyDictionary<string, AssemblyModel> modules)
    {
        _modules = modules;
        foreach (var module in modules.Values)
        {
            var duplicates = module.Forwarders.GroupBy(f => f.MetadataName, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicates is not null) Fail("TR3010", "Duplicate type forwarder: " + module.Name + ":" + duplicates.Key);
            foreach (var entry in module.Forwarders)
                if (module.FindType(Qualify(module.Name, entry.MetadataName)) is not null)
                    Fail("TR3010", "A type cannot be both defined and forwarded: " + entry.MetadataName);
        }
    }

    public NamedTypeIdentity Resolve(NamedTypeIdentity type)
    {
        if (type.AssemblyScope is null) return type; // Explicit framework normalization is not arbitrary app-name binding.
        if (_resolved.TryGetValue(type, out var cached)) return cached;
        var visited = new HashSet<NamedTypeIdentity>();
        var current = type;
        for (var depth = 0; ; depth++)
        {
            if (depth > 256 || !visited.Add(current)) Fail("TR3011", "Cyclic or over-budget type forwarding: " + type.Text);
            if (!_modules.TryGetValue(current.AssemblyScope!, out var module))
            {
                if (depth == 0) return type; // Ordinary unresolved implementation is diagnosed by reachability.
                Fail("TR3012", "Missing forwarded implementation assembly: " + current.AssemblyScope);
            }
            var forwarder = module!.Forwarders.FirstOrDefault(f => f.MetadataName == current.MetadataName);
            // Nested types follow their declaring type's assembly, even without a separate nested export row.
            if (forwarder is null)
                for (var parent = current.MetadataName.LastIndexOf('+'); parent >= 0; parent = current.MetadataName.LastIndexOf('+', parent - 1))
                {
                    forwarder = module.Forwarders.FirstOrDefault(f => f.MetadataName == current.MetadataName[..parent]);
                    if (forwarder is not null || parent == 0) break;
                }
            if (forwarder is null)
            {
                if (depth > 0 && module.FindType(Qualify(module.Name, current.MetadataName)) is null)
                    Fail("TR3013", "Forwarded type has no final definition: " + current.Text);
                var result = AssemblyLinker.IsFramework(current.AssemblyScope!) ? current with { AssemblyScope = null } : current;
                _resolved[type] = result;
                _owners[type] = current.AssemblyScope!;
                if (depth > 0) _used.Add(new(type.Text, result.Text));
                return result;
            }
            if (!_modules.TryGetValue(forwarder.Target.Name, out var target)) Fail("TR3012", "Missing forwarded implementation: " + forwarder.Target);
            if (target!.Identity != forwarder.Target) Fail("TR3014", "Forwarded assembly identity mismatch: " + forwarder.Target);
            current = new(forwarder.Target.Name, current.MetadataName);
        }
    }

    public AssemblyModel Rewrite(AssemblyModel module) => TypeRewriter.Rewrite(module, Resolve, (original, type) =>
    {
        var name = CliTypeIdentity.Parse(type);
        while (name.Element is not null) name = name.Element;
        if (name.Name?.AssemblyScope is { } scope) return scope;
        // Forwarding into a framework implementation deliberately removes its textual facade scope.
        // Preserve the resolved owner for member linkage even though the target type name is normalized.
        return name.Name is { } named && _owners.TryGetValue(new(original, named.MetadataName), out var owner) ? owner : original;
    });
    private static string Qualify(string scope, string type) => AssemblyLinker.IsFramework(scope) ? type : "[" + scope + "]" + type;
    private static void Fail(string code, string message) => throw new CompilationException(new Diagnostic(code, message));
}
