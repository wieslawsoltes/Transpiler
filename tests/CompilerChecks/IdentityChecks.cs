using Transpiler.Core;

internal static class IdentityChecks
{
    public static void Run()
    {
        var checks = 0;
        void Assert(bool value) { if (!value) throw new InvalidOperationException("Identity assertion " + checks); checks++; }
        void Reject(string code, Action action)
        {
            try { action(); }
            catch (CompilationException e) when (e.Diagnostics.Any(d => d.Code == code)) { checks++; return; }
            throw new InvalidOperationException("Expected " + code);
        }
        string text = "[App]Container`1+<Iterate>d__7`1<[Library]Node`1<System.Int32[]>,System.String[rank=2]>&";
        var syntax = CliTypeIdentity.Parse(text);
        Assert(syntax.CanonicalName == text && syntax.Form == CliTypeForm.ByReference);
        Assert(ReferenceEquals(syntax, syntax.Rewrite(n => n)));
        Assert(syntax.Rewrite(n => n.AssemblyScope == "Library" ? n with { AssemblyScope = "Implementation" } : n).CanonicalName.Contains("[Implementation]Node`1<System.Int32[]>"));
        Assert(!CliTypeIdentity.Parse("[A]T").Equals(CliTypeIdentity.Parse("[B]T")));
        Assert(!CliTypeIdentity.Parse("System.Int32[]").Equals(CliTypeIdentity.Parse("System.Int32[rank=1]")));
        Assert(CliTypeIdentity.Parse("!!0001").Equals(CliTypeIdentity.Parse("!!1")));
        Assert(CliTypeIdentity.Parse("[A]G`1<!01[rank=02]>").CanonicalName == "[A]G`1<!1[rank=2]>");
        Assert(CliTypeIdentity.Parse("modreq([A]Marker) System.Int32&").Rewrite(n => n.AssemblyScope == "A" ? new("B", n.MetadataName) : n).CanonicalName == "modreq([B]Marker) System.Int32&");
        Reject("TR3020", () => CliTypeIdentity.Parse("[Broken"));
        Reject("TR3020", () => CliTypeIdentity.Parse(new string('a', 4097)));
        Reject("TR3020", () => CliTypeIdentity.Parse("System.Int32" + string.Concat(Enumerable.Repeat("[]", 66))));

        static AssemblyModel Module(string name, params string[] definitions) => new(name, 0,
            definitions.Select(t => new TypeDefinitionModel(AssemblyLinker.IsFramework(name) ? t : "[" + name + "]" + t,
                "System.Object", false, false, true, 0, [])).ToArray(), [], []);
        var c = Module("C", "Api.Type", "Api.Type+Nested");
        var b = Module("B") with { Forwarders = [new("Api.Type", c.Identity)] };
        var a = Module("A") with { Forwarders = [new("Api.Type", b.Identity)] };
        static Dictionary<string, AssemblyModel> Graph(params AssemblyModel[] modules) => modules.ToDictionary(m => m.Name, StringComparer.Ordinal);
        var resolver = new TypeForwardingResolver(Graph(a, b, c));
        Assert(resolver.Resolve(new("A", "Api.Type")) == new NamedTypeIdentity("C", "Api.Type"));
        Assert(resolver.Resolve(new("A", "Api.Type+Nested")) == new NamedTypeIdentity("C", "Api.Type+Nested"));
        Assert(resolver.Bindings.Length == 2);
        Assert(resolver.Resolve(new(null, "Api.Type")) == new NamedTypeIdentity(null, "Api.Type"));
        Reject("TR3012", () => new TypeForwardingResolver(Graph(a)).Resolve(new("A", "Api.Type")));
        Reject("TR3011", () => new TypeForwardingResolver(Graph(a, b with { Forwarders = [new("Api.Type", a.Identity)] })).Resolve(new("A", "Api.Type")));
        Reject("TR3013", () => new TypeForwardingResolver(Graph(a, b, Module("C"))).Resolve(new("A", "Api.Type")));
        Reject("TR3014", () => new TypeForwardingResolver(Graph(a, b with { Identity = b.Identity with { Version = "2.0.0.0" } }, c)).Resolve(new("A", "Api.Type")));
        Reject("TR3010", () => new TypeForwardingResolver(Graph(a with { Forwarders = [a.Forwarders[0], a.Forwarders[0]] })));
        Reject("TR3010", () => new TypeForwardingResolver(Graph(Module("A", "Api.Type") with { Forwarders = a.Forwarders })));

        var core = Module("System.Private.CoreLib", "Api.Type");
        var facade = Module("A") with { Forwarders = [new("Api.Type", core.Identity)] };
        var member = new MethodReference(1, "A", "[A]Api.Type", "M", [], "System.Void", false, 0);
        var body = new MethodDefinitionModel(new(2, "User", "[User]Program", "Main", [], "System.Void", false, 0),
            true, false, false, false, false, true, 2, [], [new(0, 1, "call", member), new(1, 2, "ret", null)], []);
        var user = Module("User", "Program") with { Methods = [body] };
        var rewritten = new TypeForwardingResolver(Graph(facade, core, user)).Rewrite(user);
        var moved = (MethodReference)rewritten.Methods[0].Instructions[0].Operand!;
        Assert(moved.Assembly == core.Name && moved.Type == "Api.Type");

        const string task = "[Transpiler.Bcl]Transpiler.Bcl.Tasks.Task";
        var input = user with { Methods = [body with { Reference = body.Reference with { Parameters = [task, task + "Unrelated", task + "+Nested", "[Other]Transpiler.Bcl.Tasks.Task"] },
            Instructions = [new(0, 1, "ldstr", task)] }] };
        var substituted = LibrarySubstitution.Apply(input).Methods[0];
        Assert(substituted.Reference.Parameters.SequenceEqual(new[] { "System.Threading.Tasks.Task", task + "Unrelated", "System.Threading.Tasks.Task+Nested", "[Other]Transpiler.Bcl.Tasks.Task" }));
        Assert((string)substituted.Instructions[0].Operand! == task);
        Assert((user with { Methods = [body] }).Resolve(body.Reference with { Instance = true }) is null);
        Console.WriteLine($"Identity checks: {checks} passed");
    }
}
