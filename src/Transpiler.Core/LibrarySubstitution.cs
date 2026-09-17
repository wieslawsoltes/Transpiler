using System.Reflection.Emit;

namespace Transpiler.Core;

/// <summary>Explicit, versioned type substitutions. Algorithms stay in managed IL; unknown members never fall back.</summary>
public static class LibrarySubstitution
{
    public const string Policy = "portable-bcl-v1";
    public static IReadOnlyDictionary<string, string> Types { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["[Transpiler.Bcl]Transpiler.Bcl.ExceptionDispatchInfo"] = "System.Runtime.ExceptionServices.ExceptionDispatchInfo",
        ["[Transpiler.Bcl]Transpiler.Bcl.Async.IAsyncDisposable"] = "System.IAsyncDisposable",
        ["[Transpiler.Bcl]Transpiler.Bcl.Async.IAsyncEnumerable`1"] = "System.Collections.Generic.IAsyncEnumerable`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Async.IAsyncEnumerator`1"] = "System.Collections.Generic.IAsyncEnumerator`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Async.ConfiguredCancelableAsyncEnumerable`1"] = "System.Runtime.CompilerServices.ConfiguredCancelableAsyncEnumerable`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Async.ConfiguredAsyncDisposable"] = "System.Runtime.CompilerServices.ConfiguredAsyncDisposable",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.AsyncIteratorMethodBuilder"] = "System.Runtime.CompilerServices.AsyncIteratorMethodBuilder",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.TaskAsyncEnumerableExtensions"] = "System.Threading.Tasks.TaskAsyncEnumerableExtensions",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Sources.IValueTaskSource"] = "System.Threading.Tasks.Sources.IValueTaskSource",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Sources.IValueTaskSource`1"] = "System.Threading.Tasks.Sources.IValueTaskSource`1",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Sources.ValueTaskSourceStatus"] = "System.Threading.Tasks.Sources.ValueTaskSourceStatus",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Sources.ValueTaskSourceOnCompletedFlags"] = "System.Threading.Tasks.Sources.ValueTaskSourceOnCompletedFlags",
        ["[Transpiler.Bcl]Transpiler.Bcl.Tasks.Sources.ManualResetValueTaskSourceCore`1"] = "System.Threading.Tasks.Sources.ManualResetValueTaskSourceCore`1",
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
        var mappings = Types.Select(kv => (From: CliTypeIdentity.Parse(kv.Key).Name!, To: CliTypeIdentity.Parse(kv.Value).Name!))
            .OrderByDescending(p => p.From.MetadataName.Length).ToArray();
        NamedTypeIdentity Rewrite(NamedTypeIdentity name)
        {
            foreach (var (from, to) in mappings)
                if (name.AssemblyScope == from.AssemblyScope && (name.MetadataName == from.MetadataName || name.MetadataName.StartsWith(from.MetadataName + "+", StringComparison.Ordinal)))
                    return to with { MetadataName = to.MetadataName + name.MetadataName[from.MetadataName.Length..] };
            return name;
        }
        string Assembly(string original, string type)
        {
            var owner = CliTypeIdentity.Parse(type);
            if (owner.Form != CliTypeForm.Named || owner.Name!.AssemblyScope is not null) return original;
            return mappings.Any(p => owner.Name == p.To || owner.Name.MetadataName.StartsWith(p.To.MetadataName + "+", StringComparison.Ordinal))
                ? "Transpiler.Bcl" : original;
        }
        return TypeRewriter.Rewrite(input, Rewrite, Assembly);
    }
}
