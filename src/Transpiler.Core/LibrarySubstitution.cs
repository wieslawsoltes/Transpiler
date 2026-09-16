using System.Reflection.Emit;

namespace Transpiler.Core;

/// <summary>Explicit, versioned type substitutions. Algorithms stay in managed IL; unknown members never fall back.</summary>
public static class LibrarySubstitution
{
    public const string Policy = "portable-bcl-v1";
    public static IReadOnlyDictionary<string, string> Types { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["[Transpiler.Bcl]Transpiler.Bcl.Threading.CancellationToken"] = "System.Threading.CancellationToken",
        ["[Transpiler.Bcl]Transpiler.Bcl.Threading.CancellationTokenRegistration"] = "System.Threading.CancellationTokenRegistration",
        ["[Transpiler.Bcl]Transpiler.Bcl.Threading.CancellationTokenSource"] = "System.Threading.CancellationTokenSource",
        ["[Transpiler.Bcl]Transpiler.Bcl.OperationCanceledException"] = "System.OperationCanceledException",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.TaskCanceledException"] = "System.Threading.Tasks.TaskCanceledException",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ValueTask"] = "System.Threading.Tasks.ValueTask",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ValueTask`1"] = "System.Threading.Tasks.ValueTask`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ValueTaskAwaiter"] = "System.Runtime.CompilerServices.ValueTaskAwaiter",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ValueTaskAwaiter`1"] = "System.Runtime.CompilerServices.ValueTaskAwaiter`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ConfiguredValueTaskAwaitable"] = "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ConfiguredValueTaskAwaitable`1"] = "System.Runtime.CompilerServices.ConfiguredValueTaskAwaitable`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.AsyncValueTaskMethodBuilder"] = "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.AsyncValueTaskMethodBuilder`1"] = "System.Runtime.CompilerServices.AsyncValueTaskMethodBuilder`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.AggregateException"] = "System.AggregateException",
        ["[Transpiler.Bcl]Transpiler.Bcl.ReadOnlyCollection`1"] = "System.Collections.ObjectModel.ReadOnlyCollection`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.KeyNotFoundException"] = "System.Collections.Generic.KeyNotFoundException",
        ["[Transpiler.Bcl]Transpiler.Bcl.Dictionary`2"] = "System.Collections.Generic.Dictionary`2",
        ["[Transpiler.Bcl]Transpiler.Bcl.KeyValuePair`2"] = "System.Collections.Generic.KeyValuePair`2",
        ["[Transpiler.Bcl]Transpiler.Bcl.HashSet`1"] = "System.Collections.Generic.HashSet`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.EqualityComparer`1"] = "System.Collections.Generic.EqualityComparer`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Comparer`1"] = "System.Collections.Generic.Comparer`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.StringComparer"] = "System.StringComparer",
        ["[Transpiler.Bcl]Transpiler.Bcl.Nullable`1"] = "System.Nullable`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Nullable"] = "System.Nullable",
        ["[Transpiler.Bcl]Transpiler.Bcl.List`1"] = "System.Collections.Generic.List`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Queue`1"] = "System.Collections.Generic.Queue`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Stack`1"] = "System.Collections.Generic.Stack`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Enumerable"] = "System.Linq.Enumerable",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Task"] = "System.Threading.Tasks.Task",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Task`1"] = "System.Threading.Tasks.Task`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.TaskCompletionSource`1"] = "System.Threading.Tasks.TaskCompletionSource`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.TaskAwaiter"] = "System.Runtime.CompilerServices.TaskAwaiter",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.TaskAwaiter`1"] = "System.Runtime.CompilerServices.TaskAwaiter`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.YieldAwaitable"] = "System.Runtime.CompilerServices.YieldAwaitable",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ConfiguredTaskAwaitable"] = "System.Runtime.CompilerServices.ConfiguredTaskAwaitable",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.ConfiguredTaskAwaitable`1"] = "System.Runtime.CompilerServices.ConfiguredTaskAwaitable`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.AsyncTaskMethodBuilder"] = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.AsyncTaskMethodBuilder`1"] = "System.Runtime.CompilerServices.AsyncTaskMethodBuilder`1"
    };

    public static AssemblyModel Apply(AssemblyModel input)
    {
        string Type(string value)
        {
            // Application type names are assembly-qualified: a user-defined System.* name cannot enter this policy.
            foreach (var entry in Types.OrderByDescending(e => e.Key.Length)) value = value.Replace(entry.Key, entry.Value, StringComparison.Ordinal);
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
                  string x when i.Code.OperandType is OperandType.InlineType or OperandType.InlineTok => Type(x), _ => i.Operand } }).ToArray() }).ToArray()
        };
    }
}
