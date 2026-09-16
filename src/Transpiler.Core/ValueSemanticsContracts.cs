namespace Transpiler.Core;

/// <summary>Exact runtime hooks for typed equality/order and the external Object virtual-slot bridge.</summary>
public static class ValueSemanticsContracts
{
    public const string Helper = "[Transpiler.Bcl]Transpiler.Bcl.RuntimeValue";
    public static bool IsObjectOverride(MethodDefinitionModel method) => method.IsVirtual && !method.NewSlot && IsObjectShape(method.Reference);
    public static bool IsObjectShape(MethodReference m) => m.Instance && m.GenericArity == 0 && m.GenericArguments.Length == 0 &&
        ((m.Name == "ToString" && m.ReturnType == "System.String" && m.Parameters.Length == 0) ||
         (m.Name == "GetHashCode" && m.ReturnType == "System.Int32" && m.Parameters.Length == 0) ||
         (m.Name == "Equals" && m.ReturnType == "System.Boolean" && m.Parameters.SequenceEqual(new[] { "System.Object" })));

    public static string? Find(MethodReference m)
    {
        if (m.Assembly == "Transpiler.Bcl" && m.Type == Helper && !m.Instance && m.GenericArguments.Length == 1)
        {
            var type = m.GenericArguments[0];
            if (m.Name == "Equal" && m.ReturnType == "System.Boolean" && m.Parameters.SequenceEqual(new[] { type, type })) return "value.equal";
            if (m.Name == "Hash" && m.ReturnType == "System.Int32" && m.Parameters.SequenceEqual(new[] { type })) return "value.hash";
            if (m.Name == "Compare" && m.ReturnType == "System.Int32" && m.Parameters.SequenceEqual(new[] { type, type })) return "value.compare";
        }
        if (!AssemblyLinker.IsFramework(m.Assembly)) return null;
        if (m.Type == "System.Object" && !m.Instance && m.Name == "Equals" && m.ReturnType == "System.Boolean" &&
            m.Parameters.SequenceEqual(new[] { "System.Object", "System.Object" })) return "object.equals-static";
        if (m.Instance && m.Parameters.Length == 0 && m.Name == "GetHashCode" && m.ReturnType == "System.Int32" &&
            (CliTypes.IsPrimitive(m.Type) || m.Type is "System.Object" or "System.ValueType" or "System.Enum" or "System.String")) return "object.hash";
        if (m.Instance && m.Name == "Equals" && m.ReturnType == "System.Boolean" && m.Parameters.Length == 1 &&
            (m.Parameters[0] == m.Type || m.Parameters[0] == "System.Object") &&
            (CliTypes.IsPrimitive(m.Type) || m.Type is "System.Object" or "System.ValueType" or "System.Enum" or "System.String")) return "object.equals";
        if (m.Instance && m.Name == "CompareTo" && m.ReturnType == "System.Int32" && m.Parameters.SequenceEqual(new[] { m.Type }) && CliTypes.IsPrimitive(m.Type)) return "value.compare-instance";
        if (m.Type == "System.String" && !m.Instance && m.Name == "CompareOrdinal" && m.ReturnType == "System.Int32" &&
            m.Parameters.SequenceEqual(new[] { "System.String", "System.String" })) return "string.compare-ordinal";
        if (m.Type is "System.ValueType" or "System.Enum" && IsObjectShape(m) && m.Name == "ToString") return "object.string";
        return null;
    }
}
