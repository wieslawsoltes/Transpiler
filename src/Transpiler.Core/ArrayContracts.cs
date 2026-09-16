using System.Globalization;
namespace Transpiler.Core;

/// <summary>CLI rectangular array shape and exact runtime-array/type-handle operations.</summary>
public static class ArrayContracts
{
    public static bool TryShape(string type, out string element, out int rank)
    {
        element = ""; rank = 0;
        var at = type.LastIndexOf("[rank=", StringComparison.Ordinal);
        if (at < 0 || !type.EndsWith(']') || !int.TryParse(type.AsSpan(at + 6, type.Length - at - 7), NumberStyles.None,
            CultureInfo.InvariantCulture, out rank) || rank is < 1 or > 32) return false;
        element = type[..at]; return element.Length != 0;
    }
    public static string? Find(MethodReference m)
    {
        if (TryShape(m.Type, out var element, out var rank) && m.Instance && m.GenericArity == 0)
        {
            var indices = Enumerable.Repeat("System.Int32", rank).ToArray();
            if (m.Name == ".ctor" && m.ReturnType == "System.Void" &&
                (m.Parameters.SequenceEqual(indices) || m.Parameters.SequenceEqual(indices.Concat(indices)))) return "rect.new";
            if (m.Name == "Get" && m.ReturnType == element && m.Parameters.SequenceEqual(indices)) return "rect.get";
            if (m.Name == "Address" && m.ReturnType == element + "&" && m.Parameters.SequenceEqual(indices)) return "rect.address";
            if (m.Name == "Set" && m.ReturnType == "System.Void" && m.Parameters.SequenceEqual(indices.Append(element))) return "rect.set";
        }
        if (!AssemblyLinker.IsFramework(m.Assembly) || m.GenericArity != 0) return null;
        bool Is(string type, string name, string result, bool instance, params string[] parameters) =>
            m.Type == type && m.Name == name && m.ReturnType == result && m.Instance == instance && m.Parameters.SequenceEqual(parameters);
        if (Is("System.Type", "GetTypeFromHandle", "System.Type", false, "System.RuntimeTypeHandle")) return "type.from-handle";
        if (Is("System.Object", "GetType", "System.Type", true) || Is("System.Exception", "GetType", "System.Type", true)) return "type.of-object";
        if (Is("System.Type", "op_Equality", "System.Boolean", false, "System.Type", "System.Type")) return "type.equals";
        if (Is("System.Type", "op_Inequality", "System.Boolean", false, "System.Type", "System.Type")) return "type.not-equals";
        if (Is("System.Type", "GetElementType", "System.Type", true)) return "type.element";
        if (Is("System.Type", "get_IsArray", "System.Boolean", true)) return "type.is-array";
        if (Is("System.Type", "GetArrayRank", "System.Int32", true)) return "type.rank";
        if (m.Type != "System.Array") return null;
        if (!m.Instance && m.Name == "CreateInstance" && m.ReturnType == "System.Array")
        {
            if (m.Parameters.SequenceEqual(new[] { "System.Type", "System.Int32[]", "System.Int32[]" })) return "array.create-bounds";
            if (m.Parameters.SequenceEqual(new[] { "System.Type", "System.Int32[]" })) return "array.create-lengths";
            for (var r = 1; r <= 3; r++)
                if (m.Parameters.SequenceEqual(new[] { "System.Type" }.Concat(Enumerable.Repeat("System.Int32", r)))) return "array.create";
        }
        if (Is("System.Array", "Clear", "System.Void", false, "System.Array")) return "array.clear-all";
        if (Is("System.Array", "Clear", "System.Void", false, "System.Array", "System.Int32", "System.Int32")) return "array.clear";
        if (!m.Instance) return null;
        if (m.Parameters.Length == 0)
        {
            if (m.Name is "get_Length" or "get_Rank" && m.ReturnType == "System.Int32") return "array." + m.Name[4..];
            if (m.Name == "get_LongLength" && m.ReturnType == "System.Int64") return "array.LongLength";
            if (m.Name == "Clone" && m.ReturnType == "System.Object") return "array.clone";
            if (m.Name == "GetEnumerator" && m.ReturnType == "System.Collections.IEnumerator") return "array.enumerate";
        }
        if (m.Parameters.SequenceEqual(new[] { "System.Int32" }))
        {
            if (m.Name is "GetLength" or "GetLowerBound" or "GetUpperBound" && m.ReturnType == "System.Int32") return "array." + m.Name;
            if (m.Name == "GetLongLength" && m.ReturnType == "System.Int64") return "array.GetLongLength";
        }
        foreach (var indexType in new[] { "System.Int32", "System.Int64" })
            for (var r = 0; r <= 3; r++)
            {
                var indices = r == 0 ? new[] { indexType + "[]" } : Enumerable.Repeat(indexType, r).ToArray();
                if (m.Name == "GetValue" && m.ReturnType == "System.Object" && m.Parameters.SequenceEqual(indices)) return "array.get-value";
                if (m.Name == "SetValue" && m.ReturnType == "System.Void" && m.Parameters.SequenceEqual(new[] { "System.Object" }.Concat(indices))) return "array.set-value";
            }
        return null;
    }
}
