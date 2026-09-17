using Transpiler.Core;

internal static class SafetyChecks
{
    public static void Run()
    {
        int checks = 0;
        void Check(string name, string? reject, Action action)
        {
            try { action(); }
            catch (CompilationException e) when (reject is not null && e.Diagnostics.Any(d => d.Code == reject)) { checks++; return; }
            if (reject is not null) throw new InvalidOperationException("Incorrectly accepted " + name);
            checks++;
        }
        static MethodDefinitionModel M(string name, string result, string[] arguments, string[] locals, params (string Op, object? Value)[] body) => new(
            new(name == "Helper" ? 2 : 1, "Checks", "[Checks]Program", name, arguments, result, false, 0),
            true, false, false, false, false, true, 8, locals, body.Select((p, i) => new Instruction(i, i+1, p.Op, p.Value)).ToArray(), []);
        static AssemblyModel Image(MethodDefinitionModel m, FieldDefinitionModel[]? fields = null, params MethodDefinitionModel[] others) => new("Checks", 0,
            [new("[Checks]Program", "System.Object", false, false, true, 0, [])], [m, ..others], fields ?? []);
        const string i4 = "System.Int32";
        var field = new FieldReference(4, "Checks", "[Checks]Program", "Storage", i4);
        var def = new FieldDefinitionModel(field, true, false, null);
        void Analyze(string name, string? reject, MethodDefinitionModel m, params MethodDefinitionModel[] others) =>
            Check(name, reject, () => CompilerAnalysis.Analyze(Image(m, [def], others)));
        Analyze("caller byref", null, M("M", i4+"&", [i4+"&"], [], ("ldarg", 0), ("ret", null)));
        Analyze("local address", "TR2120", M("M", i4+"&", [], [i4], ("ldloca", 0), ("ret", null)));
        Analyze("argument slot", "TR2120", M("M", i4+"&", [i4], [], ("ldarga", 0), ("ret", null)));
        Analyze("static address", null, M("M", i4+"&", [], [], ("ldsflda", field), ("ret", null)));
        Analyze("array address", null, M("M", i4+"&", [i4+"[]"], [], ("ldarg", 0), ("ldc.i4", 0), ("ldelema", i4), ("ret", null)));
        Analyze("local alias", "TR2120", M("M", i4+"&", [], [i4,i4+"&"], ("ldloca", 0), ("stloc", 1), ("ldloc", 1), ("ret", null)));
        Analyze("uninitialized ref", "TR2120", M("M", i4+"&", [], [i4+"&"], ("ldloc", 0), ("ret", null)));
        Analyze("mixed join", "TR2120", M("M", i4+"&", ["System.Boolean"], [i4], ("ldarg",0),("brtrue",4),("ldsflda",field),("br",5),("ldloca",0),("ret",null)));
        var helper = M("Helper", i4+"&", [i4+"&"], [], ("ldarg",0),("ret",null));
        Analyze("indirect frame escape", "TR2120", M("M", i4+"&", [], [i4], ("ldloca",0),("call",helper.Reference),("ret",null)), helper);
        Analyze("caller forwarding", null, M("M", i4+"&", [i4+"&"], [], ("ldarg",0),("call",helper.Reference),("ret",null)), helper);
        Analyze("matching storage", null, M("M",i4,[i4+"&"],[],("ldarg",0),("ldind.i4",null),("ret",null)));
        Analyze("wrong indirect width","TR2121",M("M","System.Int64",[i4+"&"],[],("ldarg",0),("ldind.i8",null),("ret",null)));
        Analyze("wrong object token","TR2121",M("M","System.Int64",[i4+"&"],[],("ldarg",0),("ldobj","System.Int64"),("ret",null)));
        Analyze("wrong store width","TR2121",M("M","System.Void",[i4+"&"],[],("ldarg",0),("ldc.i8",1L),("stind.i8",null),("ret",null)));
        Check("byref field", "TR2120", () => ByReferenceSafety.Validate(Image(helper, [def with { Reference = field with { FieldType = i4+"&" } }]), [new(helper, new Dictionary<int,string[]>())]));
        var initialized = M("M",i4,[],[i4],("ldc.i4",3),("stloc",0),("nop",null),("leave",6),("pop",null),("leave",6),("ldloc",0),("ret",null)) with
        {InitLocals=false, Exceptions=[new("Catch",2,4,4,6,"System.Exception",0)]};
        Check("exceptional initialized", null, () => CompilerAnalysis.Analyze(Image(initialized)));
        var missing = initialized with {Instructions=initialized.Instructions.Select(i=>i.Offset==1?i with {Op="pop",Operand=null}:i).ToArray()};
        Check("exceptional missing initialization","TR2006",()=>CompilerAnalysis.Analyze(Image(missing)));
        var inCatch = M("M",i4,[],[i4],("ldnull",null),("throw",null),("pop",null),("ldc.i4",9),("stloc",0),("leave",6),("ldloc",0),("ret",null)) with
        {InitLocals=false, Exceptions=[new("Catch",0,2,2,6,"System.Exception",0)]};
        Check("catch initializes", null, () => CompilerAnalysis.Analyze(Image(inCatch)));
        var bypass = M("M",i4,[],[i4],("leave",4),("ldc.i4",9),("stloc",0),("endfinally",null),("ldloc",0),("ret",null)) with
        {InitLocals=false, Exceptions=[new("Finally",0,1,1,4,null,0)]};
        Check("finally alone not assumed", "TR2006", () => CompilerAnalysis.Analyze(Image(bypass)));
        var filter = M("M", "System.Void", [], [], ("nop",null),("leave",7),("pop",null),("ldc.i4",1),("endfilter",null),("pop",null),("leave",7),("ret",null)) with
        { Exceptions = [new("Filter",0,2,5,7,null,2)] };
        Check("valid filter layout", null, () => CilControlFlow.Build(filter));
        Check("embedded filter try", "TR2110", () => CilControlFlow.Build(filter with
            { Exceptions = [..filter.Exceptions, new("Catch",2,3,3,4,"System.Exception",0)] }));
        Check("missing final endfilter", "TR2110", () => CilControlFlow.Build(filter with
            { Instructions = filter.Instructions.Select(i => i.Offset == 4 ? i with {Op="nop"} : i).ToArray() }));
        Console.WriteLine($"Safety checks: {checks} passed");
    }
}
