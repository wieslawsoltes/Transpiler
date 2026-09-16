using System.Globalization;
using Transpiler.Core;

namespace Transpiler.Backends;

/// <summary>Target-neutral linkage and virtual-slot metadata. No executable IL is included in output metadata.</summary>
internal sealed class BackendMetadata
{
    private readonly CompilationAnalysis _analysis;
    private readonly Dictionary<int, string> _slots = [];
    public BackendMetadata(CompilationAnalysis analysis) => _analysis = analysis;
    public static string Id(int token) => token.ToString(CultureInfo.InvariantCulture);
    public string MethodId(MethodReference method) => Id(_analysis.Assembly.Resolve(method)?.Token ?? method.Token);

    private string Slot(MethodDefinitionModel method)
    {
        if (_slots.TryGetValue(method.Token, out var slot)) return slot;
        slot = method.Key + "->" + method.Reference.ReturnType;
        if (method.IsVirtual && !method.NewSlot)
        {
            var parent = _analysis.Assembly.FindType(method.Reference.Type)?.BaseType;
            var visited = new HashSet<string>();
            while (parent is not null && visited.Add(parent))
            {
                var declaration = _analysis.Assembly.Methods.FirstOrDefault(m => m.Reference.Type == parent && m.IsVirtual &&
                    m.Reference.Name == method.Reference.Name && m.Reference.Parameters.SequenceEqual(method.Reference.Parameters) &&
                    m.Reference.ReturnType == method.Reference.ReturnType);
                if (declaration is not null) { slot = Slot(declaration); break; }
                parent = _analysis.Assembly.FindType(parent)?.BaseType;
            }
        }
        _slots[method.Token] = slot;
        return slot;
    }

    public object Build()
    {
        var image = _analysis.Assembly;
        var linked = _analysis.Methods.Select(a => a.Method).ToDictionary(m => m.Token);
        var methods = new SortedDictionary<string, object>(StringComparer.Ordinal);
        object Describe(MethodReference reference, MethodDefinitionModel? definition) => new
        {
            type = reference.Type, name = reference.Name, @params = reference.Parameters, returns = reference.ReturnType,
            instance = reference.Instance, @virtual = definition?.IsVirtual ?? false,
            slot = definition is null ? reference.Key : Slot(definition),
            intrinsic = definition is null ? IntrinsicCatalog.Find(reference) : null
        };
        foreach (var method in linked.Values) methods[Id(method.Token)] = Describe(method.Reference, method);
        foreach (var reference in linked.Values.SelectMany(m => m.Instructions).Select(i => i.Operand).OfType<MethodReference>())
            if (image.Resolve(reference) is null) methods[Id(reference.Token)] = Describe(reference, null);
        var fields = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var f in image.Fields)
            fields[f.Reference.Key] = new { owner = f.Reference.Type, type = f.Reference.FieldType, @static = f.IsStatic };
        var tables = new Dictionary<string, SortedDictionary<string, string>>();
        SortedDictionary<string, string> Table(string name, HashSet<string> visiting)
        {
            if (tables.TryGetValue(name, out var cached)) return cached;
            if (!visiting.Add(name)) throw new CompilationException(new Diagnostic("TR2200", "Cyclic class inheritance metadata."));
            var table = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (image.FindType(name)?.BaseType is { } parent && image.FindType(parent) is not null)
                foreach (var pair in Table(parent, visiting)) table[pair.Key] = pair.Value;
            foreach (var method in linked.Values.Where(m => m.Reference.Type == name && m.IsVirtual)) table[Slot(method)] = Id(method.Token);
            visiting.Remove(name);
            return tables[name] = table;
        }
        var types = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var t in image.Types.Where(t => !t.IsInterface && !t.IsValueType && t.GenericArity == 0))
            types[t.Name] = new
            {
                @base = t.BaseType, before = t.BeforeFieldInit,
                fields = image.Fields.Where(f => f.Reference.Type == t.Name).Select(f => f.Reference.Key).ToArray(),
                cctor = linked.Values.FirstOrDefault(m => m.Reference.Type == t.Name && m.Reference.Name == ".cctor") is { } cctor ? Id(cctor.Token) : null,
                vtable = Table(t.Name, [])
            };
        var exports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in _analysis.Exports) exports[key] = Id(linked.Values.Single(m => m.Key == key).Token);
        return new { schema = AssemblyModel.SchemaVersion, profile = "portable-mvp", assembly = image.Name,
            types, fields, methods, exports, entry = image.EntryPoint == 0 ? null : Id(image.EntryPoint) };
    }
}
