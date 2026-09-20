using System.Collections.Immutable;

namespace Transpiler.Core;

public sealed record StreamExportShape(string ReturnType, string SourceType, string Kind, bool Erased, string[] Elements)
{
    public string BridgeType(string element) => StreamContracts.Factory + "<" + SourceType + "," + element + ">";
    public string OpenMethod => Kind switch { "task" => "OpenTask", "value-task" => "OpenValueTask", _ => "Open" };
}

/// <summary>Versioned, closed-world host stream discovery and translated ownership entry points.</summary>
public static class StreamContracts
{
    public const string Policy = "managed-stream-v2";
    public const string Enumerable = "System.Collections.Generic.IAsyncEnumerable`1";
    public const string Cursor = "[Transpiler.Bcl]Transpiler.Bcl.Interop.StreamCursor`1";
    public const string Factory = "[Transpiler.Bcl]Transpiler.Bcl.Interop.StreamFactory`2";
    public static ImmutableArray<string> Members { get; } = [
        "Open", "get_FactoryPending", "get_FactoryCompleted", "FinishFactory",
        "StartMove", "get_MovePending", "get_MoveCompleted", "FinishMove", "get_Current",
        "Cancel", "StartDispose", "get_DisposePending", "get_DisposeCompleted", "FinishDispose", "get_IsClosed"];

    public static string? Element(string type)
    {
        var (definition, arguments) = GenericSpecializer.Split(type);
        return definition == Enumerable && arguments.Length == 1 ? arguments[0] : null;
    }

    /// <summary>Walk closed interfaces and bases, not method names or runtime duck typing.</summary>
    public static string[] Elements(string type, Func<string, TypeDefinitionModel?> findType)
    {
        ArgumentNullException.ThrowIfNull(findType);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var elements = new SortedSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>();
        pending.Push(type);
        while (pending.TryPop(out var current))
        {
            if (!seen.Add(current)) continue;
            if (seen.Count > 4096)
                throw new CompilationException(new Diagnostic("TR2221", "Stream interface graph exceeds the 4096-node budget."));
            if (Element(current) is { } element) elements.Add(element);
            if (findType(current) is not { } definition) continue;
            foreach (var contract in definition.Interfaces) pending.Push(contract);
            if (definition.BaseType is { } parent) pending.Push(parent);
        }
        return elements.ToArray();
    }

    /// <summary>
    /// One optional Task/ValueTask wrapper; erased Object results require explicit host element selection.
    /// Only already-closed, linked contracts are candidates. No type synthesis from host input occurs.
    /// </summary>
    public static StreamExportShape Discover(string returnType, Func<string, TypeDefinitionModel?> findType,
        IEnumerable<string> closedTypes)
    {
        var (definition, arguments) = GenericSpecializer.Split(returnType);
        var kind = arguments.Length == 1 ? definition switch
        {
            "System.Threading.Tasks.Task`1" => "task",
            "System.Threading.Tasks.ValueTask`1" => "value-task",
            _ => "value"
        } : "value";
        var source = kind == "value" ? returnType : arguments[0];
        var erased = source == "System.Object";
        var elements = erased
            ? closedTypes.SelectMany(t => Elements(t, findType)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : Elements(source, findType);
        return new(returnType, source, kind, erased, elements);
    }
}
