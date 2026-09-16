using System.Reflection.Emit;

namespace Transpiler.Core;

/// <summary>Explicit, versioned type substitutions. Algorithms stay in managed IL; unknown members never fall back.</summary>
public static class LibrarySubstitution
{
    public const string Policy = "portable-bcl-v1";
    public static IReadOnlyDictionary<string, string> Types { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["[Transpiler.Bcl]Transpiler.Bcl.List`1"] = "System.Collections.Generic.List`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Queue`1"] = "System.Collections.Generic.Queue`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Stack`1"] = "System.Collections.Generic.Stack`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Enumerable"] = "System.Linq.Enumerable"
    };

    public static AssemblyModel Apply(AssemblyModel input)
    {
        string Type(string value)
        {
            // Application type names are assembly-qualified: a user-defined System.* name cannot enter this policy.
            foreach (var entry in Types) value = value.Replace(entry.Key, entry.Value, StringComparison.Ordinal);
            return value;
        }
        bool Replaced(string owner) => Types.Values.Any(t => owner == t || owner.StartsWith(t + "<", StringComparison.Ordinal) || owner.StartsWith(t + "+", StringComparison.Ordinal));
        MethodReference Method(MethodReference method)
        {
            var type = Type(method.Type);
            return method with { Type = type, Assembly = Replaced(type) ? "Transpiler.Bcl" : method.Assembly,
                Parameters = method.Parameters.Select(Type).ToArray(), ReturnType = Type(method.ReturnType),
                GenericArguments = method.GenericArguments.Select(Type).ToArray() };
        }
        FieldReference Field(FieldReference field)
        {
            var type = Type(field.Type);
            return field with { Type = type, Assembly = Replaced(type) ? "Transpiler.Bcl" : field.Assembly, FieldType = Type(field.FieldType) };
        }
        return input with
        {
            Types = input.Types.Select(t => t with { Name = Type(t.Name), BaseType = t.BaseType is null ? null : Type(t.BaseType),
                Interfaces = t.Interfaces.Select(Type).ToArray(), Overrides = t.Overrides.Select(o => new MethodOverride(Method(o.Body), Method(o.Declaration))).ToArray() }).ToArray(),
            Fields = input.Fields.Select(f => f with { Reference = Field(f.Reference) }).ToArray(),
            Methods = input.Methods.Select(m => m with { Reference = Method(m.Reference), Locals = m.Locals.Select(Type).ToArray(),
                Exceptions = m.Exceptions.Select(e => e with { CatchType = e.CatchType is null ? null : Type(e.CatchType) }).ToArray(),
                Instructions = m.Instructions.Select(i => i with { Operand = i.Operand switch
                { MethodReference x => Method(x), FieldReference x => Field(x),
                  string x when i.Code.OperandType == OperandType.InlineType => Type(x), _ => i.Operand } }).ToArray() }).ToArray()
        };
    }
}
