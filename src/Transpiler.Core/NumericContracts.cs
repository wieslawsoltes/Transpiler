namespace Transpiler.Core;

/// <summary>Exact scalar/storage intrinsics. Runtime handles are opaque and do not enable general reflection.</summary>
public static class NumericContracts
{
    public static string? Find(MethodReference m)
    {
        if (!AssemblyLinker.IsFramework(m.Assembly) || m.Instance || m.GenericArity != 0) return null;
        bool Match(string type, string name, string result, params string[] args) =>
            m.Type == type && m.Name == name && m.ReturnType == result && m.Parameters.SequenceEqual(args);
        if (Match("System.Runtime.CompilerServices.RuntimeHelpers", "InitializeArray", "System.Void", "System.Array", "System.RuntimeFieldHandle")) return "array.initialize-data";
        if (Match("System.BitConverter", "SingleToInt32Bits", "System.Int32", "System.Single")) return "bits.single-i4";
        if (Match("System.BitConverter", "Int32BitsToSingle", "System.Single", "System.Int32")) return "bits.i4-single";
        if (Match("System.BitConverter", "DoubleToInt64Bits", "System.Int64", "System.Double")) return "bits.double-i8";
        if (Match("System.BitConverter", "Int64BitsToDouble", "System.Double", "System.Int64")) return "bits.i8-double";
        foreach (var type in new[] { "System.Single", "System.Double" })
            foreach (var name in new[] { "IsFinite", "IsNaN", "IsInfinity", "IsPositiveInfinity", "IsNegativeInfinity", "IsNegative" })
                if (Match(type, name, "System.Boolean", type)) return "float." + name;
        foreach (var name in new[] { "Abs", "Sqrt", "Floor", "Ceiling", "Truncate" })
            if (Match("System.MathF", name, "System.Single", "System.Single")) return "mathf." + name.ToLowerInvariant();
        foreach (var name in new[] { "Min", "Max", "CopySign" })
            if (Match("System.MathF", name, "System.Single", "System.Single", "System.Single")) return "mathf." + name.ToLowerInvariant();
        return null;
    }
}
