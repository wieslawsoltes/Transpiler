using System.Reflection.Emit;

namespace Transpiler.Core;

public sealed record MethodAnalysis(MethodDefinitionModel Method, IReadOnlyDictionary<int, string[]> StackBefore);
public sealed record CompilationAnalysis(AssemblyModel Assembly, MethodAnalysis[] Methods, string[] Exports);

/// <summary>Closed-world reachability, fail-closed capability checks and fixed-point evaluation-stack analysis.</summary>
public static class CompilerAnalysis
{
    private static readonly HashSet<string> Supported = new(("nop ldarg ldarga starg ldloc ldloca stloc ldc.i4 ldc.i8 ldc.r8 ldc.r4 ldnull ldstr dup pop " +
        "add sub mul div div.un rem rem.un neg not and or xor shl shr shr.un add.ovf add.ovf.un sub.ovf sub.ovf.un mul.ovf mul.ovf.un " +
        "ceq cgt cgt.un clt clt.un br brtrue brfalse beq bne.un bge bge.un bgt bgt.un ble ble.un blt blt.un switch ret call callvirt newobj " +
        "ldfld stfld ldflda ldsfld stsfld ldsflda newarr ldlen ldelema ldelem stelem " +
        "ldelem.i1 ldelem.u1 ldelem.i2 ldelem.u2 ldelem.i4 ldelem.u4 ldelem.i8 ldelem.r8 ldelem.ref " +
        "stelem.i1 stelem.i2 stelem.i4 stelem.i8 stelem.r8 stelem.ref " +
        "ldind.i1 ldind.u1 ldind.i2 ldind.u2 ldind.i4 ldind.u4 ldind.i8 ldind.r8 ldind.ref stind.i1 stind.i2 stind.i4 stind.i8 stind.r8 stind.ref " +
        "ldobj stobj initobj box unbox unbox.any castclass isinst throw rethrow leave endfinally").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    private static readonly HashSet<string> Conversions = new(BuildConversions());
    public static IReadOnlyCollection<string> SupportedOpcodes => Supported.Concat(Conversions).Order().ToArray();
    private static IEnumerable<string> BuildConversions()
    {
        foreach (var type in new[] { "i1", "u1", "i2", "u2", "i4", "u4", "i8", "u8" })
        {
            yield return "conv." + type; yield return "conv.ovf." + type; yield return "conv.ovf." + type + ".un";
        }
        yield return "conv.r8"; yield return "conv.r.un";
    }

    public static CompilationAnalysis Analyze(AssemblyModel input)
    {
        var image = input with { Methods = input.Methods.Select(m => m with { Instructions = m.Instructions.Select(Normalize).ToArray() }).ToArray() };
        var errors = new List<Diagnostic>(); var done = new HashSet<int>(); var queue = new Queue<MethodDefinitionModel>();
        var roots = image.EntryPoint != 0 ? image.Methods.Where(m => m.Token == image.EntryPoint).ToArray()
            : image.Methods.Where(m => m.IsPublic && m.IsStatic && m.Reference.Name is not ".cctor").ToArray();
        if (roots.Length == 0) throw new CompilationException(new("TR2000", "No managed entry point or public static library exports were found."));
        foreach (var root in roots) queue.Enqueue(root);
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
            void Error(string code, string message, int? offset = null) => errors.Add(new(code, message, method.Key, offset));
            void Type(string type, int? offset = null) { if (!SupportedType(image, type)) Error("TR2003", $"Type '{type}' needs a capability outside portable-mvp.", offset); }
            if (method.IsPInvoke || method.IsAbstract || method.Instructions.Length == 0) Error("TR2004", "Reachable method has no supported managed body.");
            if (method.Reference.GenericArity != 0) Error("TR2005", "Generic methods require the planned reified/closed-generic lowering.");
            if (!method.InitLocals && method.Locals.Length != 0) Error("TR2006", "Uninitialized local storage is not accepted by portable-mvp.");
            Type(method.Reference.Type); Type(method.Reference.ReturnType);
            foreach (var type in method.Reference.Parameters.Concat(method.Locals)) Type(type);
            foreach (var field in image.Fields.Where(f => f.Reference.Type == method.Reference.Type)) Type(field.Reference.FieldType);
            EnqueueType(method.Reference.Type);
            foreach (var clause in method.Exceptions)
            {
                if (clause.Kind is not ("Catch" or "Finally" or "Fault")) Error("TR2010", $"Exception clause '{clause.Kind}' is not implemented; filters need two-pass search.", clause.FilterStart);
                if (clause.CatchType is not null) Type(clause.CatchType, clause.HandlerStart);
            }
            foreach (var i in method.Instructions)
            {
                if (!Supported.Contains(i.Op) && !Conversions.Contains(i.Op)) Error("TR2001", $"Opcode '{i.Op}' is not supported by portable-mvp.", i.Offset);
                if (i.Operand is MethodReference call)
                {
                    if (call.GenericArity != 0) Error("TR2005", "Generic method instantiations are not yet supported.", i.Offset);
                    foreach (var type in call.Parameters.Append(call.ReturnType)) Type(type, i.Offset);
                    if (image.Resolve(call) is { } target)
                    {
                        if (image.FindType(call.Type)?.IsInterface == true) Error("TR2011", "Interface dispatch is not yet implemented.", i.Offset);
                        queue.Enqueue(target); EnqueueType(call.Type);
                        if (i.Op == "callvirt" && target.IsVirtual)
                            foreach (var candidate in image.Methods.Where(m => m.IsVirtual && m.Reference.Name == call.Name &&
                                m.Reference.Parameters.SequenceEqual(call.Parameters) && IsDerivedFrom(image, m.Reference.Type, call.Type)))
                                queue.Enqueue(candidate);
                    }
                    else if (IntrinsicCatalog.Find(call) is null) Error("TR2002", $"No intrinsic or linked implementation for '{call.Assembly}:{call.Key}->{call.ReturnType}'.", i.Offset);
                }
                if (i.Operand is FieldReference field)
                {
                    Type(field.FieldType, i.Offset); EnqueueType(field.Type);
                    if (image.Resolve(field) is null) Error("TR2007", $"External field '{field.Key}' is not linked.", i.Offset);
                }
                if (i.Code.OperandType == OperandType.InlineType && i.Operand is string typeName) Type(typeName, i.Offset);
            }
            if (errors.Count == before)
            {
                try { analyses.Add(new(method, Verify(method))); }
                catch (CompilationException ex) { errors.AddRange(ex.Diagnostics); }
            }
        }
        if (errors.Count != 0) throw new CompilationException(errors.Distinct().ToArray());
        return new(image, analyses.OrderBy(a => a.Method.Token).ToArray(), roots.Select(m => m.Key).ToArray());
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
        if (type.EndsWith("[]", StringComparison.Ordinal)) return SupportedType(image, type[..^2], depth + 1);
        if (CliTypes.IsPrimitive(type) || type is "System.String" or "System.Object" || IntrinsicCatalog.ExceptionTypes.Contains(type)) return true;
        var t = image.FindType(type);
        return t is { IsValueType: false, IsInterface: false, GenericArity: 0 } &&
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

    private static IReadOnlyDictionary<int, string[]> Verify(MethodDefinitionModel method)
    {
        var instructions = method.Instructions.ToDictionary(i => i.Offset);
        var states = new Dictionary<int, string[]>(); var work = new Queue<int>();
        var args = method.IsStatic ? method.Reference.Parameters : new[] { method.Reference.Type }.Concat(method.Reference.Parameters).ToArray();
        void Fail(string message, int offset) => throw new CompilationException(new("TR2100", message, method.Key, offset));
        void Merge(int offset, string[] stack)
        {
            if (!instructions.ContainsKey(offset)) Fail("Control flow targets a non-instruction boundary.", offset);
            if (states.TryGetValue(offset, out var old))
            {
                if (!old.SequenceEqual(stack)) Fail($"Incompatible stack merge ({string.Join(',', old)}) vs ({string.Join(',', stack)}).", offset);
            }
            else { states[offset] = stack; work.Enqueue(offset); }
        }
        Merge(0, []);
        foreach (var c in method.Exceptions)
        {
            if (!instructions.ContainsKey(c.TryStart) || c.TryEnd <= c.TryStart || c.HandlerEnd <= c.HandlerStart)
                Fail("Invalid exception region boundaries.", c.TryStart);
            Merge(c.HandlerStart, c.Kind == "Catch" ? ["o"] : []);
        }
        while (work.TryDequeue(out var pc))
        {
            var i = instructions[pc]; var s = states[pc].ToList();
            string Pop()
            {
                if (s.Count == 0) Fail("Evaluation stack underflow.", pc);
                var v = s[^1]; s.RemoveAt(s.Count - 1); return v;
            }
            void Push(string type) => s.Add(type);
            void Expect(string expected)
            {
                var actual = Pop();
                if (actual != expected) Fail($"Expected stack kind {expected}, got {actual}.", pc);
            }
            string Slot(string[] slots)
            {
                var n = (int)i.Operand!; if (n < 0 || n >= slots.Length) Fail("Argument/local slot out of range.", pc);
                return slots[n];
            }
            var op = i.Op;
            if (op == "ldarg" || op == "ldloc") Push(CliTypes.StackKind(Slot(op == "ldarg" ? args : method.Locals)));
            else if (op == "ldarga" || op == "ldloca") Push(Slot(op == "ldarga" ? args : method.Locals) + "&");
            else if (op == "starg" || op == "stloc") Expect(CliTypes.StackKind(Slot(op == "starg" ? args : method.Locals)));
            else if (op == "ldc.i4") Push("i4");
            else if (op == "ldc.i8") Push("i8");
            else if (op is "ldc.r8" or "ldc.r4") Push("f");
            else if (op is "ldnull" or "ldstr") Push("o");
            else if (op == "dup") { var v = Pop(); Push(v); Push(v); }
            else if (op == "pop") Pop();
            else if (Conversions.Contains(op))
            {
                var v = Pop(); if (v is not ("i4" or "i8" or "f")) Fail("Numeric conversion on a non-number.", pc);
                Push(op is "conv.r8" or "conv.r.un" ? "f" : op.Contains("i8", StringComparison.Ordinal) || op.Contains("u8", StringComparison.Ordinal) ? "i8" : "i4");
            }
            else if (op is "neg" or "not")
            {
                var v = Pop(); if (v is not ("i4" or "i8" or "f") || (v == "f" && op == "not")) Fail("Invalid unary operand.", pc); Push(v);
            }
            else if (op is "call" or "callvirt" or "newobj")
            {
                var call = (MethodReference)i.Operand!;
                foreach (var type in call.Parameters.Reverse()) Expect(CliTypes.StackKind(type));
                if (call.Instance && op != "newobj")
                {
                    var receiver = Pop(); if (receiver != "o" && !receiver.EndsWith('&')) Fail("Invalid instance receiver.", pc);
                }
                if (op == "newobj") Push("o"); else if (call.ReturnType != "System.Void") Push(CliTypes.StackKind(call.ReturnType));
            }
            else if (op is "ldfld" or "ldflda" or "ldsfld" or "ldsflda" or "stfld" or "stsfld")
            {
                var field = (FieldReference)i.Operand!;
                if (op is "stfld" or "stsfld") Expect(CliTypes.StackKind(field.FieldType));
                if (op is "ldfld" or "ldflda" or "stfld") Expect("o");
                if (op is not ("stfld" or "stsfld")) Push(op.EndsWith('a') ? field.FieldType + "&" : CliTypes.StackKind(field.FieldType));
            }
            else if (op == "newarr") { Expect("i4"); Push("o"); }
            else if (op == "ldlen") { Expect("o"); Push("i4"); }
            else if (op.StartsWith("ldelem", StringComparison.Ordinal))
            {
                Expect("i4"); Expect("o"); Push(op == "ldelema" ? (string)i.Operand! + "&" : ElementKind(i));
            }
            else if (op.StartsWith("stelem", StringComparison.Ordinal)) { Expect(ElementKind(i)); Expect("i4"); Expect("o"); }
            else if (op.StartsWith("ldind", StringComparison.Ordinal) || op == "ldobj")
            {
                if (!Pop().EndsWith('&')) Fail("Indirect load requires a managed reference.", pc); Push(ElementKind(i));
            }
            else if (op.StartsWith("stind", StringComparison.Ordinal) || op == "stobj")
            {
                Expect(ElementKind(i)); if (!Pop().EndsWith('&')) Fail("Indirect store requires a managed reference.", pc);
            }
            else if (op == "initobj") { if (Pop() != (string)i.Operand! + "&") Fail("initobj reference type mismatch.", pc); }
            else if (op == "box") { Expect(CliTypes.StackKind((string)i.Operand!)); Push("o"); }
            else if (op is "unbox" or "unbox.any") { Expect("o"); Push(op == "unbox" ? (string)i.Operand! + "&" : CliTypes.StackKind((string)i.Operand!)); }
            else if (op is "castclass" or "isinst") { Expect("o"); Push("o"); }
            else if (op == "throw") { Expect("o"); s.Clear(); }
            else if (op == "rethrow" || op == "endfinally") { if (s.Count != 0) Fail("Exception terminator requires empty stack.", pc); }
            else if (op == "leave") s.Clear();
            else if (op == "ret")
            {
                if (method.Reference.ReturnType != "System.Void") Expect(CliTypes.StackKind(method.Reference.ReturnType));
                if (s.Count != 0) Fail("Return leaves values on the evaluation stack.", pc);
            }
            else if (op == "switch") Expect("i4");
            else if (op is "brtrue" or "brfalse")
            { if (Pop() is not ("i4" or "i8" or "o")) Fail("Invalid conditional branch operand.", pc); }
            else if (op is not ("br" or "nop"))
            {
                var right = Pop(); var left = Pop();
                var compare = op is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un" || i.Code.FlowControl == FlowControl.Cond_Branch;
                if (op is "shl" or "shr" or "shr.un")
                { if (left is not ("i4" or "i8") || right != "i4") Fail("Invalid shift operands.", pc); }
                else if (left != right || (!compare && left is not ("i4" or "i8" or "f"))) Fail("Incompatible arithmetic/comparison operands.", pc);
                if (op is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un") Push("i4");
                else if (!compare) Push(left);
            }
            if (s.Count > method.MaxStack) Fail("Declared maxstack exceeded.", pc);
            if (op is "ret" or "throw" or "rethrow" or "endfinally") continue;
            if (op is "br" or "leave") { Merge((int)i.Operand!, s.ToArray()); continue; }
            if (op == "switch") foreach (var target in (int[])i.Operand!) Merge(target, s.ToArray());
            else if (i.Code.FlowControl == FlowControl.Cond_Branch) Merge((int)i.Operand!, s.ToArray());
            Merge(i.NextOffset, s.ToArray());
        }
        return states;
    }

    public static string ElementType(Instruction i) => i.Operand is string t ? t : i.Op[(i.Op.IndexOf('.') + 1)..] switch
    {
        "i1" => "System.SByte", "u1" => "System.Byte", "i2" => "System.Int16", "u2" => "System.UInt16",
        "i4" => "System.Int32", "u4" => "System.UInt32", "i8" => "System.Int64", "r8" => "System.Double", "ref" => "System.Object",
        _ => throw new CompilationException(new("TR2101", $"Unknown element type for {i.Op}."))
    };
    private static string ElementKind(Instruction i) => CliTypes.StackKind(ElementType(i));
}
