using System.Text.RegularExpressions;

namespace Transpiler.Core;

/// <summary>Bounded closed-world monomorphization. Preserves constructed identities and per-instantiation statics.</summary>
public static partial class GenericSpecializer
{
    [GeneratedRegex(@"!!(\d+)|!(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterPattern();
    [GeneratedRegex(@"`\d+<", RegexOptions.CultureInvariant)]
    private static partial Regex ConstructionPattern();

    public static (string Definition, string[] Arguments) Split(string type)
    {
        if (!type.EndsWith('>') || type.IndexOf('`') < 0) return (type, []);
        var start = -1;
        for (var candidate = type.IndexOf('<', type.IndexOf('`')); candidate >= 0; candidate = type.IndexOf('<', candidate + 1))
        {
            var nesting = 1; var end = candidate + 1;
            for (; end < type.Length && nesting != 0; end++) { if (type[end] == '<') nesting++; else if (type[end] == '>') nesting--; }
            if (nesting == 0 && end == type.Length) { start = candidate; break; }
        }
        if (start < 0) return (type, []);
        var args = new List<string>(); var depth = 0; var begin = start + 1;
        for (var i = begin; i < type.Length - 1; i++)
        {
            if (type[i] == '<') depth++;
            else if (type[i] == '>') depth--;
            else if (type[i] == ',' && depth == 0) { args.Add(type[begin..i]); begin = i + 1; }
        }
        args.Add(type[begin..^1]);
        return (type[..start], args.ToArray());
    }

    private static string Substitute(string text, string[] types, string[] methods) => ParameterPattern().Replace(text, m =>
    {
        var method = m.Groups[1].Success;
        var index = int.Parse(m.Groups[method ? 1 : 2].Value, System.Globalization.CultureInfo.InvariantCulture);
        var arguments = method ? methods : types;
        return index < arguments.Length ? arguments[index] : m.Value;
    });

    public static AssemblyModel Expand(AssemblyModel input, int maximumMethods = 16384, int maximumTypes = 4096)
    {
        var types = new Dictionary<string, TypeDefinitionModel>(StringComparer.Ordinal);
        var fields = new Dictionary<string, FieldDefinitionModel>(StringComparer.Ordinal);
        var methods = new Dictionary<string, MethodDefinitionModel>(StringComparer.Ordinal);
        var pending = new Queue<(MethodDefinitionModel Template, MethodReference Closed, string[] Types, string[] Methods)>();
        var next = 1;
        var external = new Dictionary<string, MethodReference>(StringComparer.Ordinal);
        var virtualCalls = new Dictionary<string, MethodReference>(StringComparer.Ordinal);
        var instantiatedVirtuals = new HashSet<string>(StringComparer.Ordinal);
        var typeTemplates = input.Types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var methodTemplates = input.Methods.GroupBy(m => (m.Reference.Assembly, m.Reference.Type))
            .ToDictionary(g => g.Key, g => g.ToArray());
        string Key(MethodReference r) => r.Assembly + ":" + r.Key + "->" + r.ReturnType;
        void Budget(bool condition, string text) { if (condition) throw new CompilationException(new Diagnostic("TR3100", text)); }

        string CloseType(string name)
        {
            Budget(name.Length > 4096, "Generic type identity exceeds the 4096-character expansion budget.");
            if (name.EndsWith('&') || name.EndsWith('*')) return CloseType(name[..^1]) + name[^1];
            if (name.EndsWith("[]", StringComparison.Ordinal)) return CloseType(name[..^2]) + "[]";
            var (definition, arguments) = Split(name);
            if (arguments.Length > 0)
            {
                arguments = arguments.Select(CloseType).ToArray();
                name = definition + "<" + string.Join(",", arguments) + ">";
            }
            if (ParameterPattern().IsMatch(name)) return name; // Open signatures are templates, never concrete runtime identities.
            if (types.ContainsKey(name) || !typeTemplates.TryGetValue(definition, out var template)) return name;
            if (template.GenericArity != arguments.Length) return name; // Still-open type; rejected if it reaches emission.
            Budget(types.Count >= maximumTypes, "Constructed type budget exceeded.");
            types[name] = template with { Name = name, GenericArity = 0 }; // Publish before recursively closing fields.
            string T(string value) => value == definition && arguments.Length > 0 ? name : CloseType(Substitute(value, arguments, []));
            var closed = types[name] with
            {
                BaseType = template.BaseType is null ? null : T(template.BaseType),
                Interfaces = template.Interfaces.Select(T).ToArray(), Overrides = [],
                EnumUnderlyingType = template.EnumUnderlyingType is null ? null : T(template.EnumUnderlyingType)
            };
            types[name] = closed;
            foreach (var f in input.Fields.Where(f => f.Reference.Type == definition))
            {
                var reference = f.Reference with { Type = name, FieldType = T(f.Reference.FieldType) };
                fields[reference.Key] = f with { Reference = reference };
            }
            return name;
        }

        MethodReference Bind(MethodReference reference)
        {
            var owner = CloseType(reference.Type);
            var (definition, typeArguments) = Split(owner);
            var methodArguments = reference.GenericArguments.Select(CloseType).ToArray();
            string T(string value) => CloseType(Substitute(value, typeArguments, methodArguments));
            var closed = reference with
            {
                Type = owner, Parameters = reference.Parameters.Select(T).ToArray(), ReturnType = T(reference.ReturnType),
                GenericArguments = methodArguments,
                GenericArity = reference.GenericArity == methodArguments.Length ? 0 : reference.GenericArity
            };
            if (AssemblyLinker.IsFramework(closed.Assembly))
            {
                var ownerAssemblies = input.Methods.Where(m => m.Reference.Type == definition && AssemblyLinker.IsFramework(m.Reference.Assembly))
                    .Select(m => m.Reference.Assembly).Distinct().ToArray();
                if (ownerAssemblies.Length == 1) closed = closed with { Assembly = ownerAssemblies[0] };
            }
            var key = Key(closed);
            if (methods.TryGetValue(key, out var existing)) return existing.Reference;
            if (external.TryGetValue(key, out var intrinsic)) return intrinsic;
            MethodDefinitionModel? template = null;
            methodTemplates.TryGetValue((closed.Assembly, definition), out var candidates);
            if (candidates is null && AssemblyLinker.IsFramework(reference.Assembly))
                candidates = input.Methods.Where(m => m.Reference.Type == definition && AssemblyLinker.IsFramework(m.Reference.Assembly)).ToArray();
            if (candidates is not null)
                template = candidates.FirstOrDefault(m => m.Reference.Name == reference.Name && m.Reference.Instance == reference.Instance &&
                    m.Reference.GenericArity == methodArguments.Length &&
                    m.Reference.Parameters.Select(T).SequenceEqual(closed.Parameters) && T(m.Reference.ReturnType) == closed.ReturnType);
            if (template is null)
            {
                closed = closed with { Token = next++ };
                external[key] = closed;
                return closed;
            }
            Budget(methods.Count >= maximumMethods, "Closed-method expansion budget exceeded.");
            if (closed.GenericArity != 0 || closed.Parameters.Append(closed.ReturnType).Any(x => ParameterPattern().IsMatch(x)))
                throw new CompilationException(new Diagnostic("TR3101", "An open generic method cannot be emitted without explicit closed arguments.", closed.Key));
            closed = closed with { Token = next++, Assembly = template.Reference.Assembly };
            methods[key] = template with { Reference = closed, Instructions = [], Locals = [], Exceptions = [] };
            pending.Enqueue((template, closed, typeArguments, methodArguments));
            return closed;
        }

        var roots = input.EntryPoint != 0 ? input.Methods.Where(m => m.Token == input.EntryPoint).ToArray()
            : input.Methods.Where(m => m.Reference.Assembly == input.RootAssembly && m.IsPublic && m.IsStatic &&
                m.Reference.Name != ".cctor" && m.Reference.GenericArity == 0 && input.FindType(m.Reference.Type)?.GenericArity == 0).ToArray();
        var exports = roots.Select(m => Bind(m.Reference)).ToArray();
        var changed = true;
        while (changed)
        {
            changed = false;
            while (pending.TryDequeue(out var work))
            {
                changed = true;
                string T(string value) => value == work.Template.Reference.Type ? work.Closed.Type : CloseType(Substitute(value, work.Types, work.Methods));
                MethodReference M(MethodReference method) => Bind(method with
                {
                    Type = T(method.Type),
                    GenericArguments = method.GenericArguments.Select(g => T(g)).ToArray()
                });
                FieldReference F(FieldReference field)
                {
                    var owner = T(field.Type); var (_, arguments) = Split(owner);
                    return field with { Type = owner, FieldType = CloseType(Substitute(field.FieldType, arguments, [])) };
                }
                var instructions = work.Template.Instructions.Select(i => i with
                {
                    Operand = i.Operand switch
                    {
                        MethodReference m => M(m), FieldReference f => F(f),
                        string t when i.Code.OperandType == System.Reflection.Emit.OperandType.InlineType => T(t),
                        _ => i.Operand
                    }
                }).ToArray();
                methods[Key(work.Closed)] = work.Template with
                {
                    Reference = work.Closed, Locals = work.Template.Locals.Select(T).ToArray(), Instructions = instructions,
                    Exceptions = work.Template.Exceptions.Select(e => e with { CatchType = e.CatchType is null ? null : T(e.CatchType) }).ToArray()
                };
                foreach (var i in instructions.Where(i => i.Op is "callvirt" or "ldvirtftn"))
                {
                    var call = (MethodReference)i.Operand!;
                    virtualCalls[Key(call)] = call;
                }
            }
            foreach (var type in types.Values.ToArray())
            {
                var (definition, arguments) = Split(type.Name);
                if (!typeTemplates.TryGetValue(definition, out var template)) continue;
                string T(string value) => value == definition ? type.Name : CloseType(Substitute(value, arguments, []));
                foreach (var cctor in input.Methods.Where(m => m.Reference.Type == definition && m.Reference.Name == ".cctor"))
                    Bind(cctor.Reference with { Type = type.Name });
                if (template.Overrides.Length != 0 && type.Overrides.Length == 0)
                    types[type.Name] = type with { Overrides = template.Overrides.Select(o => new MethodOverride(
                        Bind(o.Body with { Type = T(o.Body.Type), Parameters = o.Body.Parameters.Select(T).ToArray(), ReturnType = T(o.Body.ReturnType) }),
                        Bind(o.Declaration with { Type = T(o.Declaration.Type), Parameters = o.Declaration.Parameters.Select(T).ToArray(), ReturnType = T(o.Declaration.ReturnType) }))).ToArray() };
                foreach (var call in virtualCalls.Values.ToArray())
                {
                    // Conservative closed-world candidates; analysis later prunes irrelevant methods.
                    foreach (var candidate in input.Methods.Where(m => m.Reference.Type == definition && m.Reference.Instance &&
                        m.Reference.Name == call.Name && m.Reference.GenericArity == call.GenericArguments.Length))
                    {
                        var id = type.Name + "|" + candidate.Token + "|" + string.Join(",", call.GenericArguments);
                        if (!instantiatedVirtuals.Add(id)) continue;
                        Bind(candidate.Reference with { Type = type.Name, GenericArguments = call.GenericArguments });
                    }
                }
            }
            changed |= pending.Count != 0;
        }
        return input with
        {
            EntryPoint = input.EntryPoint == 0 ? 0 : exports.Single().Token,
            ExportRoots = exports.Select(e => e.Key).ToArray(),
            Types = types.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToArray(),
            Fields = fields.Values.OrderBy(f => f.Reference.Key, StringComparer.Ordinal).ToArray(),
            Methods = methods.Values.OrderBy(m => m.Token).ToArray()
        };
    }
}
