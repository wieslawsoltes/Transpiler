namespace Transpiler.Core;

/// <summary>Exact managed signatures accepted by the portable runtime. No name-only BCL fallback.</summary>
public static class IntrinsicCatalog
{
    public static readonly string[] ExceptionTypes = ["System.Exception", "System.SystemException", "System.ArithmeticException",
        "System.DivideByZeroException", "System.OverflowException", "System.NullReferenceException", "System.IndexOutOfRangeException",
        "System.ArrayTypeMismatchException", "System.InvalidCastException", "System.ArgumentException", "System.ArgumentOutOfRangeException",
        "System.OperationCanceledException", "System.Threading.Tasks.TaskCanceledException", "System.ArgumentNullException", "System.InvalidOperationException", "System.NotSupportedException", "System.TypeInitializationException"];
    private static readonly IReadOnlyDictionary<string, string> Entries = Build();
    public static string? Find(MethodReference method, AssemblyModel? image = null)
    {
        var value = ValueSemanticsContracts.Find(method);
        if (value is not null) return value;
        var service = RuntimeContracts.Find(method);
        if (service is not null) return service;
        var delegateIntrinsic = DelegateContracts.Find(method, image);
        if (delegateIntrinsic is not null && (AssemblyLinker.IsFramework(method.Assembly) || image?.FindType(method.Type)?.BaseType == "System.MulticastDelegate")) return delegateIntrinsic;
        if (method.Assembly is not ("System.Runtime" or "System.Console" or "System.Private.CoreLib" or "mscorlib" or "netstandard")) return null;
        return Entries.GetValueOrDefault(method.Key + "->" + method.ReturnType + (method.Instance ? ":instance" : ":static"));
    }
    public static IReadOnlyDictionary<string, string> All => Entries;
    private static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        void Add(string type, string name, string result, string[] parameters, string intrinsic, bool instance = false) =>
            d.Add($"{type}::{name}({string.Join(",", parameters)})->{result}" + (instance ? ":instance" : ":static"), intrinsic);
        foreach (var type in new[] { "System.Boolean", "System.Char", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double", "System.String", "System.Object" })
        {
            Add("System.Console", "WriteLine", "System.Void", [type], "console.line");
            Add("System.Console", "Write", "System.Void", [type], "console.write");
        }
        Add("System.Console", "WriteLine", "System.Void", [], "console.line");
        Add("System.Environment", "get_CurrentManagedThreadId", "System.Int32", [], "environment.thread");
        Add("System.Object", ".ctor", "System.Void", [], "object.ctor", true);
        Add("System.Object", "ToString", "System.String", [], "object.string", true);
        Add("System.Object", "ReferenceEquals", "System.Boolean", ["System.Object", "System.Object"], "reference.equals");
        foreach (var name in new[] { "op_Equality", "Equals", "op_Inequality" })
            Add("System.String", name, "System.Boolean", ["System.String", "System.String"], name == "op_Inequality" ? "string.not-equals" : "string.equals");
        Add("System.String", "Equals", "System.Boolean", ["System.String"], "string.equals", true);
        Add("System.String", "get_Length", "System.Int32", [], "string.length", true);
        Add("System.String", "get_Chars", "System.Char", ["System.Int32"], "string.char", true);
        Add("System.String", "Substring", "System.String", ["System.Int32"], "string.substring", true);
        Add("System.String", "Substring", "System.String", ["System.Int32", "System.Int32"], "string.substring", true);
        for (var n = 2; n <= 4; n++) Add("System.String", "Concat", "System.String", Enumerable.Repeat("System.String", n).ToArray(), "string.concat");
        for (var n = 1; n <= 3; n++) Add("System.String", "Concat", "System.String", Enumerable.Repeat("System.Object", n).ToArray(), "string.concat");
        foreach (var type in ExceptionTypes)
        {
            Add(type, ".ctor", "System.Void", [], "exception.ctor", true);
            Add(type, ".ctor", "System.Void", ["System.String"], "exception.ctor", true);
        }
        Add("System.Exception", "get_Message", "System.String", [], "exception.message", true);
        foreach (var type in new[] { "System.Boolean", "System.Char", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64", "System.Single", "System.Double" })
            Add(type, "ToString", "System.String", [], "primitive.string", true);
        foreach (var type in new[] { "System.Int32", "System.Int64", "System.Double" })
        {
            Add("System.Math", "Abs", type, [type], "math.abs");
            Add("System.Math", "Min", type, [type, type], "math.min");
            Add("System.Math", "Max", type, [type, type], "math.max");
        }
        foreach (var name in new[] { "Sqrt", "Floor", "Ceiling", "Truncate" })
            Add("System.Math", name, "System.Double", ["System.Double"], "math." + name.ToLowerInvariant());
        Add("System.Double", "IsNaN", "System.Boolean", ["System.Double"], "double.isnan");
        Add("System.Double", "IsInfinity", "System.Boolean", ["System.Double"], "double.isinf");
        return d;
    }
}
