using Transpiler.Core;

internal static class StreamContractChecks
{
    public static void Run()
    {
        const string integer = "System.Int32", text = "System.String";
        static string Enumerable(string t) => StreamContracts.Enumerable + "<" + t + ">";
        static TypeDefinitionModel Type(string name, string? parent = null, params string[] interfaces)
            => new(name, parent, false, false, true, 0, interfaces);
        var types = new Dictionary<string, TypeDefinitionModel>(StringComparer.Ordinal)
        {
            ["[Library]Base`1<System.Int32>"] = Type("[Library]Base`1<System.Int32>", "System.Object", Enumerable(integer)),
            ["[Library]Derived"] = Type("[Library]Derived", "[Library]Base`1<System.Int32>"),
            ["[Library]Value"] = Type("[Library]Value", "System.ValueType", Enumerable(integer)) with { IsValueType = true },
            ["[Library]IInherited"] = Type("[Library]IInherited", null, Enumerable(text)) with { IsInterface = true },
            ["[Library]Impl"] = Type("[Library]Impl", null, "[Library]IInherited"),
            ["[Library]Multi"] = Type("[Library]Multi", null, Enumerable(text), Enumerable(integer), Enumerable(text)),
            ["[Library]CycleA"] = Type("[Library]CycleA", "[Library]CycleB", Enumerable(integer)),
            ["[Library]CycleB"] = Type("[Library]CycleB", "[Library]CycleA"),
            ["[Other]Derived"] = Type("[Other]Derived"),
            ["[Library]Duck"] = Type("[Library]Duck")
        };
        var count = 0;
        void Check(bool pass) { if (!pass) throw new InvalidOperationException("Stream contract assertion " + count); count++; }
        StreamExportShape Discover(string type) => StreamContracts.Discover(type, t => types.GetValueOrDefault(t), types.Keys);
        Check(Discover(Enumerable(integer)).Elements.SequenceEqual([integer]));
        Check(Discover("[Library]Derived").Elements.SequenceEqual([integer]));
        Check(Discover("[Library]Value").Elements.SequenceEqual([integer]));
        Check(Discover("[Library]Impl").Elements.SequenceEqual([text]));
        Check(Discover("[Library]Multi").Elements.SequenceEqual([integer, text]));
        Check(Discover("[Library]CycleA").Elements.SequenceEqual([integer]));
        Check(Discover("[Other]Derived").Elements.Length == 0);
        Check(Discover("[Library]Duck").Elements.Length == 0);
        Check(Discover("Fake.IAsyncEnumerable`1<System.Int32>").Elements.Length == 0);
        Check(Discover("System.Int32[]").Elements.Length == 0);
        Check(Discover("System.Object").Erased);
        Check(Discover("System.Object").Elements.SequenceEqual([integer, text]));
        foreach (var (definition, kind, open) in new[] {
            ("System.Threading.Tasks.Task`1", "task", "OpenTask"),
            ("System.Threading.Tasks.ValueTask`1", "value-task", "OpenValueTask") })
        {
            var shape = Discover(definition + "<[Library]Derived>");
            Check(shape.Kind == kind && shape.OpenMethod == open);
            Check(shape.SourceType == "[Library]Derived" && !shape.Erased);
            Check(shape.Elements.SequenceEqual([integer]));
            Check(shape.BridgeType(integer) == StreamContracts.Factory + "<[Library]Derived,System.Int32>");
            Check(Discover(definition + "<System.Object>").Erased);
            Check(Discover(definition + "<System.Object>").Elements.SequenceEqual([integer, text]));
        }
        Check(Discover("System.Threading.Tasks.Task`1<System.Threading.Tasks.Task`1<" + Enumerable(integer) + ">>").Elements.Length == 0);
        Check(Discover("System.Threading.Tasks.Task").Elements.Length == 0);
        Check(Discover("System.Threading.Tasks.ValueTask").Elements.Length == 0);
        Check(StreamContracts.Members.Distinct().Count() == StreamContracts.Members.Length);
        Check(StreamContracts.Policy == "managed-stream-v2");
        // Bound hostile/cyclic graph inspection independently from specialization's constructed-type budget.
        var large = EnumerableRange(4100).ToDictionary(i => "[Huge]N" + i, i => Type("[Huge]N" + i, i == 4099 ? null : "[Huge]N" + (i + 1)));
        try { StreamContracts.Elements("[Huge]N0", t => large.GetValueOrDefault(t)); Check(false); }
        catch (CompilationException error) { Check(error.Diagnostics.Single().Code == "TR2221"); }
        Console.WriteLine($"Stream contracts: {count} passed");
    }
    private static IEnumerable<int> EnumerableRange(int length) => System.Linq.Enumerable.Range(0, length);
}
