namespace Transpiler.Core;

/// <summary>Closed delegate signatures. Runtime implementation is target-specific; targets are ordinary translated methods.</summary>
public static class DelegateContracts
{
    public static bool IsDelegate(string type, AssemblyModel? image = null)
    {
        if (image?.FindType(type)?.BaseType == "System.MulticastDelegate") return true;
        var (definition, args) = GenericSpecializer.Split(type);
        if (definition == "System.Action") return args.Length == 0;
        if (definition.StartsWith("System.Action`", StringComparison.Ordinal))
            return int.TryParse(definition[14..], out var n) && n is >= 1 and <= 16 && args.Length == n;
        if (definition.StartsWith("System.Func`", StringComparison.Ordinal))
            return int.TryParse(definition[12..], out var n) && n is >= 1 and <= 17 && args.Length == n;
        if (definition == "System.Converter`2") return args.Length == 2;
        return definition is "System.Predicate`1" or "System.Comparison`1" && args.Length == 1;
    }

    public static string? Find(MethodReference method, AssemblyModel? image = null)
    {
        if (IsDelegate(method.Type, image))
        {
            if (method.Name == ".ctor" && method.Instance && method.ReturnType == "System.Void" &&
                method.Parameters.SequenceEqual(new[] { "System.Object", "System.IntPtr" })) return "delegate.ctor";
            if (method.Name == "Invoke" && method.Instance)
            {
                if (image?.FindType(method.Type)?.BaseType == "System.MulticastDelegate") return "delegate.invoke";
                var (type, args) = GenericSpecializer.Split(method.Type);
                var parameters = (type.StartsWith("System.Func`", StringComparison.Ordinal) || type == "System.Converter`2") ? args[..^1]
                    : type == "System.Comparison`1" ? new[] { args[0], args[0] } : args;
                var result = (type.StartsWith("System.Func`", StringComparison.Ordinal) || type == "System.Converter`2") ? args[^1]
                    : type == "System.Predicate`1" ? "System.Boolean" : type == "System.Comparison`1" ? "System.Int32" : "System.Void";
                if (method.Parameters.SequenceEqual(parameters) && method.ReturnType == result) return "delegate.invoke";
            }
        }
        if (method.Type == "System.Delegate" && !method.Instance)
        {
            if (method.Parameters.SequenceEqual(new[] { "System.Delegate", "System.Delegate" }))
            {
                if (method.ReturnType == "System.Delegate" && method.Name == "Combine") return "delegate.combine";
                if (method.ReturnType == "System.Delegate" && method.Name == "Remove") return "delegate.remove";
                if (method.ReturnType == "System.Boolean" && method.Name == "op_Equality") return "delegate.equals";
                if (method.ReturnType == "System.Boolean" && method.Name == "op_Inequality") return "delegate.not-equals";
            }
        }
        return null;
    }
}
