using System.Reflection.Emit;

namespace Transpiler.Core;

public sealed record MethodAnalysis(MethodDefinitionModel Method, IReadOnlyDictionary<int, string[]> StackBefore)
{
    public CilControlFlowGraph? ControlFlow { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public StackSsaGraph? Ssa { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SsaExclusion { get; init; }
}
public sealed record CompilationAnalysis(AssemblyModel Assembly, MethodAnalysis[] Methods, string[] Exports);

/// <summary>Closed-world reachability, explicit capabilities, and fixed-point evaluation-stack analysis; not a security verifier.</summary>
public static partial class CompilerAnalysis
{
    private static readonly HashSet<string> Supported = new(("nop ldarg ldarga starg ldloc ldloca stloc ldc.i4 ldc.i8 ldc.r8 ldc.r4 ldnull ldstr dup pop " +
        "add sub mul div div.un rem rem.un neg not and or xor shl shr shr.un add.ovf add.ovf.un sub.ovf sub.ovf.un mul.ovf mul.ovf.un " +
        "ceq cgt cgt.un clt clt.un br brtrue brfalse beq bne.un bge bge.un bgt bgt.un ble ble.un blt blt.un switch ret call callvirt newobj ldftn ldvirtftn ldtoken ckfinite " +
        "ldfld stfld ldflda ldsfld stsfld ldsflda newarr ldlen ldelema ldelem stelem " +
        "ldelem.i1 ldelem.u1 ldelem.i2 ldelem.u2 ldelem.i4 ldelem.u4 ldelem.i8 ldelem.r4 ldelem.r8 ldelem.ref " +
        "stelem.i1 stelem.i2 stelem.i4 stelem.i8 stelem.r4 stelem.r8 stelem.ref " +
        "ldind.i1 ldind.u1 ldind.i2 ldind.u2 ldind.i4 ldind.u4 ldind.i8 ldind.r4 ldind.r8 ldind.ref stind.i1 stind.i2 stind.i4 stind.i8 stind.r4 stind.r8 stind.ref " +
        "ldobj stobj cpobj initobj constrained. box unbox unbox.any castclass isinst throw rethrow leave endfinally endfilter").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static readonly HashSet<string> Conversions = new(BuildConversions());
    public static IReadOnlyCollection<string> SupportedOpcodes => Supported.Concat(Conversions).Order().ToArray();
    private static IEnumerable<string> BuildConversions()
    {
        foreach (var type in new[] { "i1", "u1", "i2", "u2", "i4", "u4", "i8", "u8" })
        {
            yield return "conv." + type; yield return "conv.ovf." + type; yield return "conv.ovf." + type + ".un";
        }
        yield return "conv.r4"; yield return "conv.r8"; yield return "conv.r.un";
    }

    public static CompilationAnalysis Analyze(AssemblyModel input)
    {
        input = GenericSpecializer.Expand(input);
        var image = input with { Methods = input.Methods.Select(m => m with { Instructions = m.Instructions.Select(Normalize).ToArray() }).ToArray() };
        var errors = new List<Diagnostic>(); var done = new HashSet<int>(); var queue = new Queue<MethodDefinitionModel>();
        var roots = image.Methods.Where(m => image.ExportRoots.Contains(m.Key)).ToArray();
        if (roots.Length == 0) throw new CompilationException(new Diagnostic("TR2000", "No managed entry point or public static library exports were found."));
        foreach (var root in roots.Concat(image.Methods.Where(m => image.HostRoots.Contains(m.Key)))) queue.Enqueue(root);
        var analyses = new List<MethodAnalysis>();
        void EnqueueType(string type)
        {
            var seen = new HashSet<string>();
            while (image.FindType(type) is { } t && seen.Add(type))
            {
                foreach (var cctor in image.Methods.Where(m => m.Reference.Type == type && m.Reference.Name == ".cctor")) queue.Enqueue(cctor);
                if (t.BaseType is null) break;
                type = t.BaseType;
            }
        }
        while (queue.TryDequeue(out var method))
        {
            if (!done.Add(method.Token)) continue;
            var before = errors.Count;
            var runtimeBody = method.Instructions.Length == 0 && IntrinsicCatalog.Find(method.Reference, image) is not null;
            void Error(string code, string message, int? offset = null) => errors.Add(new(code, message, method.Key, offset));
            void Type(string type, int? offset = null) { if (!SupportedType(image, type)) Error("TR2003", $"Type '{type}' needs a capability outside portable-mvp.", offset); }
            if (method.IsPInvoke || (!method.IsAbstract && !runtimeBody && method.Instructions.Length == 0)) Error("TR2004", "Reachable method has no supported managed body.");
            if (method.Reference.GenericArity != 0) Error("TR2005", "Generic methods require the planned reified/closed-generic lowering.");
            try { DefiniteLocalAssignment.Validate(method); }
            catch (CompilationException error) { errors.AddRange(error.Diagnostics); }
            Type(method.Reference.Type); Type(method.Reference.ReturnType);
            foreach (var type in method.Reference.Parameters.Concat(method.Locals)) Type(type);
            foreach (var field in image.Fields.Where(f => f.Reference.Type == method.Reference.Type)) Type(field.Reference.FieldType);
            // External virtual slots require a runtime/BCL override bridge, not just same-name dispatch.
            if (method.IsVirtual && !method.NewSlot && !ValueSemanticsContracts.IsObjectOverride(method) && image.FindType(method.Reference.Type)?.Overrides.Length == 0 && !image.Methods.Any(m => m.IsVirtual && m.Reference.Type != method.Reference.Type &&
                IsDerivedFrom(image, method.Reference.Type, m.Reference.Type) && m.Reference.Name == method.Reference.Name &&
                m.Reference.Parameters.SequenceEqual(method.Reference.Parameters)))
                Error("TR2012", "Overriding an external virtual slot is not implemented by portable-mvp.");
            EnqueueType(method.Reference.Type);
            foreach (var clause in method.Exceptions)
            {
                if (clause.Kind is not ("Catch" or "Finally" or "Fault" or "Filter")) Error("TR2010", $"Exception clause '{clause.Kind}' is outside the supported CLI handler kinds.", clause.FilterStart);
                if (clause.CatchType is not null) Type(clause.CatchType, clause.HandlerStart);
            }
            foreach (var i in method.Instructions)
            {
                if (i.Op == "ldtoken" && i.Operand is not (FieldReference or string)) Error("TR2013", "Method and unsupported member tokens require additional reflection metadata support.", i.Offset);
                if (!Supported.Contains(i.Op) && !Conversions.Contains(i.Op)) Error("TR2001", $"Opcode '{i.Op}' is not supported by portable-mvp.", i.Offset);
                if (i.Operand is MethodReference call)
                {
                    if (call.GenericArity != 0) Error("TR2005", "Generic method instantiations are not yet supported.", i.Offset);
                    foreach (var type in call.Parameters.Append(call.ReturnType)) Type(type, i.Offset);
                    if (image.Resolve(call) is { } target)
                    {
                        queue.Enqueue(target); EnqueueType(call.Type);
                        if (i.Op is "callvirt" or "ldvirtftn" && target.IsVirtual)
                            foreach (var candidate in image.Methods.Where(m => m.IsVirtual && m.Reference.Name == call.Name &&
                                (m.Reference.Parameters.SequenceEqual(call.Parameters) || image.FindType(call.Type)?.IsInterface == true) && IsAssignable(image, m.Reference.Type, call.Type))) queue.Enqueue(candidate);
                        if (i.Op == "callvirt")
                            foreach (var map in image.Types.SelectMany(t => t.Overrides).Where(o => o.Declaration.Key == call.Key || o.Declaration.Name == call.Name && IsAssignable(image, o.Declaration.Type, call.Type)))
                                if (image.Resolve(map.Body) is { } implementation) queue.Enqueue(implementation);
                        if (i.Op == "call" && target.IsAbstract) Error("TR2011", "Direct call to an abstract declaration.", i.Offset);
                    }
                    else if (IntrinsicCatalog.Find(call, image) is null) Error("TR2002", $"No intrinsic or linked implementation for '{call.Assembly}:{call.Key}->{call.ReturnType}'.", i.Offset);
                }
                if (i.Operand is FieldReference field)
                {
                    if (i.Op == "ldtoken")
                    {
                        if (image.Resolve(field) is not { IsStatic: true, InitialData: not null })
                            Error("TR2013", "Only initialized static field handles are supported by ldtoken.", i.Offset);
                        continue;
                    }
                    if (image.Resolve(field)?.InitialData is not null)
                        Error("TR2013", "RVA storage may only be consumed through a checked field handle.", i.Offset);
                    Type(field.FieldType, i.Offset); EnqueueType(field.Type);
                    if (image.Resolve(field) is not { } definition) Error("TR2007", $"External field '{field.Key}' is not linked.", i.Offset);
                    else if (definition.IsLiteral || definition.IsStatic != (i.Op is "ldsfld" or "stsfld" or "ldsflda"))
                        Error("TR2008", "Literal field access or field storage-kind mismatch.", i.Offset);
                }
                if ((i.Code.OperandType is OperandType.InlineType or OperandType.InlineTok) && i.Operand is string typeName) Type(typeName, i.Offset);
            }
            if (errors.Count == before)
            {
                try
                {
                    var flow = CilControlFlow.Build(method);
                    analyses.Add(new(method, method.IsAbstract || runtimeBody ? new Dictionary<int, string[]>() : Verify(image, method)) { ControlFlow = flow });
                }
                catch (CompilationException ex) { errors.AddRange(ex.Diagnostics); }
            }
        }
        if (errors.Count != 0) throw new CompilationException(errors.Distinct().ToArray());
        ByReferenceSafety.Validate(image, analyses);
        return new(image, analyses.OrderBy(a => a.Method.Token).ToArray(), roots.Select(m => m.Key).ToArray());
    }

    public static bool IsAssignable(AssemblyModel image, string type, string target)
    {
        var seen = new HashSet<string>(); var todo = new Stack<string>(); todo.Push(type);
        while (todo.TryPop(out var current))
        {
            if (current == target) return true;
            if (target == "System.Object" && current != "System.Void") return true;
            var (actualDefinition, actualArguments) = GenericSpecializer.Split(current);
            var (targetDefinition, targetArguments) = GenericSpecializer.Split(target);
            var variance = image.FindType(current)?.GenericVariance ?? [];
            if (actualDefinition == targetDefinition && actualArguments.Length > 0 && actualArguments.Length == targetArguments.Length && variance.Length == actualArguments.Length)
            {
                bool Reference(string name) => image.FindType(name)?.IsValueType != true && CliTypes.StackKind(name) == "o";
                bool Match(int index) => actualArguments[index] == targetArguments[index] ||
                    Reference(actualArguments[index]) && Reference(targetArguments[index]) && (variance[index] == 1
                        ? IsAssignable(image, actualArguments[index], targetArguments[index]) : variance[index] == 2 && IsAssignable(image, targetArguments[index], actualArguments[index]));
                if (Enumerable.Range(0, actualArguments.Length).All(Match)) return true;
            }
            if (!seen.Add(current) || image.FindType(current) is not { } definition) continue;
            if (definition.BaseType is { } parent) todo.Push(parent);
            foreach (var contract in definition.Interfaces) todo.Push(contract);
        }
        return false;
    }

    public static bool IsDerivedFrom(AssemblyModel image, string type, string parent)
    {
        var seen = new HashSet<string>();
        while (seen.Add(type))
        {
            if (type == parent) return true;
            if (image.FindType(type)?.BaseType is not { } next) return false;
            type = next;
        }
        return false;
    }

    private static bool SupportedType(AssemblyModel image, string type, int depth = 0)
    {
        if (depth > 64) return false;
        if (type.EndsWith('&')) return !type[..^1].EndsWith('&') && SupportedType(image, type[..^1], depth + 1);
        if (ArrayContracts.TryShape(type, out var element, out _)) return SupportedType(image, element, depth + 1);
        if (type.EndsWith("[]", StringComparison.Ordinal)) return SupportedType(image, type[..^2], depth + 1);
        if (DelegateContracts.IsDelegate(type, image) || RuntimeContracts.IsWeakReference(type)) return true;
        if (type is "System.Delegate" or "System.MulticastDelegate" or "System.IntPtr") return true;
        if (CliTypes.IsPrimitive(type) || type is "System.Array" or "System.Type" or "System.RuntimeTypeHandle" or "System.RuntimeFieldHandle" or "System.String" or "System.Object" or "System.ValueType" or "System.Enum" || IntrinsicCatalog.ExceptionTypes.Contains(type)) return true;
        var t = image.FindType(type);
        return t is { GenericArity: 0, ExplicitLayout: false } &&
            (t.BaseType is null || SupportedType(image, t.BaseType, depth + 1));
    }

    public static Instruction Normalize(Instruction i)
    {
        foreach (var op in new[] { "ldarg", "ldloc", "stloc" })
            if (i.Op.StartsWith(op + ".", StringComparison.Ordinal) && int.TryParse(i.Op[(op.Length + 1)..], out var n)) return i with { Op = op, Operand = n };
        if (i.Op == "ldc.i4.m1") return i with { Op = "ldc.i4", Operand = -1 };
        if (i.Op.StartsWith("ldc.i4.", StringComparison.Ordinal) && int.TryParse(i.Op[7..], out var value)) return i with { Op = "ldc.i4", Operand = value };
        return i.Op.EndsWith(".s", StringComparison.Ordinal) ? i with { Op = i.Op[..^2] } : i;
    }

}
