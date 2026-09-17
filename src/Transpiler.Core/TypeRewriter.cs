using System.Reflection.Emit;

namespace Transpiler.Core;

/// <summary>One metadata traversal for explicit substitutions/forwarders. String literals are never rewritten.</summary>
public static class TypeRewriter
{
    public static AssemblyModel Rewrite(AssemblyModel input, Func<NamedTypeIdentity, NamedTypeIdentity> names,
        Func<string, string, string>? declaringAssembly = null)
    {
        var cache = new Dictionary<string, string>(StringComparer.Ordinal);
        string Type(string value)
        {
            if (!cache.TryGetValue(value, out var result)) cache[value] = result = CliTypeIdentity.Parse(value).Rewrite(names).CanonicalName;
            return result;
        }
        MethodReference Method(MethodReference method)
        {
            var type = Type(method.Type);
            return method with { Type = type, Assembly = declaringAssembly?.Invoke(method.Assembly, type) ?? method.Assembly,
                Parameters = method.Parameters.Select(Type).ToArray(), ReturnType = Type(method.ReturnType),
                GenericArguments = method.GenericArguments.Select(Type).ToArray() };
        }
        FieldReference Field(FieldReference field)
        {
            var type = Type(field.Type);
            return field with { Type = type, Assembly = declaringAssembly?.Invoke(field.Assembly, type) ?? field.Assembly, FieldType = Type(field.FieldType) };
        }
        return input with
        {
            Types = input.Types.Select(t => t with { Name = Type(t.Name), BaseType = t.BaseType is null ? null : Type(t.BaseType),
                EnumUnderlyingType = t.EnumUnderlyingType is null ? null : Type(t.EnumUnderlyingType),
                Interfaces = t.Interfaces.Select(Type).ToArray(),
                Overrides = t.Overrides.Select(o => new MethodOverride(Method(o.Body), Method(o.Declaration))).ToArray() }).ToArray(),
            Fields = input.Fields.Select(f => f with { Reference = Field(f.Reference) }).ToArray(),
            Methods = input.Methods.Select(m => m with { Reference = Method(m.Reference), Locals = m.Locals.Select(Type).ToArray(),
                Exceptions = m.Exceptions.Select(e => e with { CatchType = e.CatchType is null ? null : Type(e.CatchType) }).ToArray(),
                Instructions = m.Instructions.Select(i => i with { Operand = i.Operand switch
                {
                    MethodReference x => Method(x), FieldReference x => Field(x),
                    string x when i.Code.OperandType is OperandType.InlineType or OperandType.InlineTok => Type(x), _ => i.Operand
                } }).ToArray() }).ToArray()
        };
    }
}
