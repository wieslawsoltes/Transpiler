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
        PortableProfileGuard.Validate(_analysis);
        var image = _analysis.Assembly;
        var linked = _analysis.Methods.Select(a => a.Method).ToDictionary(m => m.Token);
        var methods = new SortedDictionary<string, object>(StringComparer.Ordinal);
        object Describe(MethodReference reference, MethodDefinitionModel? definition) => new
        {
            type = reference.Type, name = reference.Name, @params = reference.Parameters, returns = reference.ReturnType,
            arguments = reference.GenericArguments, instance = reference.Instance, @virtual = definition?.IsVirtual ?? false,
            slot = definition is null ? reference.Key : Slot(definition),
            intrinsic = definition is null || definition.Instructions.Length == 0 ? IntrinsicCatalog.Find(reference, image) : null
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
            foreach (var contract in image.Types.Where(t => t.IsInterface && CompilerAnalysis.IsAssignable(image, name, t.Name)))
                foreach (var declaration in linked.Values.Where(m => m.Reference.Type == contract.Name))
                {
                    MethodDefinitionModel? implementation = null;
                    var current = name; var search = new HashSet<string>();
                    while (search.Add(current) && image.FindType(current) is { } owner)
                    {
                        bool Reference(string t) => CliTypes.StackKind(t) == "o" && image.FindType(t)?.IsValueType != true;
                        bool Compatible(MethodReference implementation, MethodReference contractMethod) => implementation.Name == contractMethod.Name &&
                            implementation.Parameters.Length == contractMethod.Parameters.Length &&
                            implementation.Parameters.Zip(contractMethod.Parameters).All(p => p.First == p.Second || Reference(p.First) && Reference(p.Second) && CompilerAnalysis.IsAssignable(image, p.Second, p.First)) &&
                            (implementation.ReturnType == contractMethod.ReturnType || Reference(implementation.ReturnType) && Reference(contractMethod.ReturnType) && CompilerAnalysis.IsAssignable(image, implementation.ReturnType, contractMethod.ReturnType));
                        var explicitMap = owner.Overrides.FirstOrDefault(o => o.Declaration.Key == declaration.Key) ?? owner.Overrides.FirstOrDefault(o =>
                            CompilerAnalysis.IsAssignable(image, o.Declaration.Type, contract.Name) && Compatible(o.Declaration, declaration.Reference));
                        implementation = explicitMap is not null ? image.Resolve(explicitMap.Body) : linked.Values.OrderBy(m => m.Reference.Parameters.SequenceEqual(declaration.Reference.Parameters) && m.Reference.ReturnType == declaration.Reference.ReturnType ? 0 : 1).FirstOrDefault(m =>
                            m.Reference.Type == current && m.Reference.Instance && m.Reference.Name == declaration.Reference.Name &&
                            Compatible(m.Reference, declaration.Reference) &&
                            m.Reference.GenericArguments.SequenceEqual(declaration.Reference.GenericArguments));
                        if (implementation is not null || owner.BaseType is null) break;
                        current = owner.BaseType;
                    }
                    if (implementation is not null) table[Slot(declaration)] = Id(implementation.Token);
                    else if (!declaration.IsAbstract) table[Slot(declaration)] = Id(declaration.Token);
                }
            foreach (var map in image.FindType(name)?.Overrides ?? [])
                if (image.Resolve(map.Declaration) is { } declaration && image.Resolve(map.Body) is { } body)
                    table[Slot(declaration)] = Id(body.Token);
            visiting.Remove(name);
            return tables[name] = table;
        }
        Dictionary<string, string> ObjectSlots(string name)
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<string>();
            while (seen.Add(name) && image.FindType(name) is { } type)
            {
                foreach (var m in linked.Values.Where(m => m.Reference.Type == name && ValueSemanticsContracts.IsObjectOverride(m)))
                    slots.TryAdd(m.Reference.Name, Id(m.Token));
                if (type.BaseType is null) break;
                name = type.BaseType;
            }
            return slots;
        }
        var types = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var t in image.Types.Where(t => t.GenericArity == 0))
            types[t.Name] = new
            {
                definition = GenericSpecializer.Split(t.Name).Definition, arguments = GenericSpecializer.Split(t.Name).Arguments, variance = t.GenericVariance,
                @base = t.BaseType, before = t.BeforeFieldInit, valueType = t.IsValueType, enumType = t.EnumUnderlyingType, enumFlags = t.EnumFlags, enumValues = t.EnumValues, interfaces = t.Interfaces,
                fields = image.Fields.Where(f => f.Reference.Type == t.Name).Select(f => f.Reference.Key).ToArray(),
                cctor = linked.Values.FirstOrDefault(m => m.Reference.Type == t.Name && m.Reference.Name == ".cctor") is { } cctor ? Id(cctor.Token) : null,
                objectSlots = ObjectSlots(t.Name),
                vtable = Table(t.Name, [])
            };
        var exports = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in _analysis.Exports) exports[AssemblyLinker.DisplayType(key)] = Id(linked.Values.Single(m => m.Key == key).Token);
        var arrayEnumerators = linked.Values.Where(m => m.Reference.Name == ".ctor" &&
            m.Reference.Type.StartsWith("[Transpiler.Bcl]Transpiler.Bcl.ArrayEnumerator`1<", StringComparison.Ordinal))
            .ToDictionary(m => m.Reference.Parameters.Single(), m => Id(m.Token), StringComparer.Ordinal);
        var asyncBindings = new SortedDictionary<string, object>(StringComparer.Ordinal);
        const string tasks = "System.Threading.Tasks.";
        foreach (var method in linked.Values.Where(m => m.Reference.Name == "GetAwaiter" &&
            (m.Reference.Type == tasks + "Task" || m.Reference.Type.StartsWith(tasks + "Task`1<", StringComparison.Ordinal))))
        {
            var result = linked.Values.FirstOrDefault(m => m.Reference.Type == method.Reference.ReturnType && m.Reference.Name == "GetResult");
            var completed = linked.Values.FirstOrDefault(m => m.Reference.Type == tasks + "Task" && m.Reference.Name == "get_IsCompleted");
            var pump = linked.Values.FirstOrDefault(m => m.Reference.Type == "[Transpiler.Bcl]Transpiler.Bcl.Tasks.Scheduler" && m.Reference.Name == "RunOne");
            if (result is not null && completed is not null && pump is not null)
                asyncBindings[method.Reference.Type] = new { getAwaiter = Id(method.Token), getResult = Id(result.Token),
                    completed = Id(completed.Token), pump = Id(pump.Token), awaiterType = method.Reference.ReturnType, resultType = result.Reference.ReturnType };
        }
        return new { schema = AssemblyModel.SchemaVersion, profile = "portable-mvp", assembly = image.Name,
            types, fields, methods, exports, asyncBindings, arrayEnumerators, entry = image.EntryPoint == 0 ? null : Id(image.EntryPoint) };
    }
}
