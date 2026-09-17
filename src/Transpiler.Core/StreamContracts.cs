using System.Collections.Immutable;

namespace Transpiler.Core;

/// <summary>Versioned host entry points into a translated, closed managed stream cursor.</summary>
public static class StreamContracts
{
    public const string Policy = "managed-stream-v1";
    public const string Enumerable = "System.Collections.Generic.IAsyncEnumerable`1";
    public const string Cursor = "[Transpiler.Bcl]Transpiler.Bcl.Interop.StreamCursor`1";
    public static ImmutableArray<string> Members { get; } = [
        "Open", "StartMove", "get_MovePending", "get_MoveCompleted", "FinishMove", "get_Current",
        "Cancel", "StartDispose", "get_DisposePending", "get_DisposeCompleted", "FinishDispose", "get_IsClosed"];

    public static string? Element(string type)
    {
        var (definition, arguments) = GenericSpecializer.Split(type);
        return definition == Enumerable && arguments.Length == 1 ? arguments[0] : null;
    }
}
