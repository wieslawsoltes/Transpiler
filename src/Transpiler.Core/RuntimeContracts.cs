namespace Transpiler.Core;

/// <summary>Host-GC service contracts, kept separate from algorithms implemented in managed library IL.</summary>
public static class RuntimeContracts
{
    public static bool IsWeakReference(string type)
    { var (definition, args) = GenericSpecializer.Split(type); return definition == "System.WeakReference`1" && args.Length == 1; }
    public static string? Find(MethodReference method)
    {
        var clock = HostClockContracts.Find(method);
        if (clock is not null) return clock;
        if (!AssemblyLinker.IsFramework(method.Assembly)) return null;
        var numeric = NumericContracts.Find(method);
        if (numeric is not null) return numeric;
        if (!method.Instance && method.ReturnType == "System.Void" && method.Type == "System.GC" && method.Name == "KeepAlive" &&
            method.Parameters.SequenceEqual(new[] { "System.Object" })) return "gc.keep-alive";
        if (!method.Instance && method.ReturnType == "System.Int32" && method.Type == "System.Runtime.CompilerServices.RuntimeHelpers" &&
            method.Name == "GetHashCode" && method.Parameters.SequenceEqual(new[] { "System.Object" })) return "object.identity-hash";
        if (!method.Instance || !IsWeakReference(method.Type)) return null;
        var argument = GenericSpecializer.Split(method.Type).Arguments[0];
        if (method.Name == ".ctor" && method.ReturnType == "System.Void" && method.Parameters.SequenceEqual(new[] { argument })) return "weak.ctor";
        if (method.Name == "SetTarget" && method.ReturnType == "System.Void" && method.Parameters.SequenceEqual(new[] { argument })) return "weak.set";
        if (method.Name == "TryGetTarget" && method.ReturnType == "System.Boolean" && method.Parameters.SequenceEqual(new[] { argument + "&" })) return "weak.get";
        return null; // Collect, finalizer waits, resurrection, pinning and heap statistics are NOT no-op fallbacks.
    }
}
