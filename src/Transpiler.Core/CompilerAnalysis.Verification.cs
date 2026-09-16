using System.Reflection.Emit;

namespace Transpiler.Core;

public static partial class CompilerAnalysis
{
    private static IReadOnlyDictionary<int, string[]> Verify(AssemblyModel image, MethodDefinitionModel method)
    {
        string Kind(string type) => image.FindType(type) is { IsValueType: true } value
            ? value.EnumUnderlyingType is { } underlying ? CliTypes.StackKind(underlying) : "v:" + type : CliTypes.StackKind(type);
        var instructions = method.Instructions.ToDictionary(i => i.Offset);
        var end = method.Instructions[^1].NextOffset;
        var states = new Dictionary<int, string[]>(); var work = new Queue<int>();
        var args = method.IsStatic ? method.Reference.Parameters : new[] { method.Reference.Type + (image.FindType(method.Reference.Type)?.IsValueType == true ? "&" : "") }.Concat(method.Reference.Parameters).ToArray();
        void Fail(string message, int offset) => throw new CompilationException(new Diagnostic("TR2100", message, method.Key, offset));
        bool Boundary(int offset) => offset == end || instructions.ContainsKey(offset);
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
            if (!instructions.ContainsKey(c.TryStart) || !Boundary(c.TryEnd) || !Boundary(c.HandlerEnd) || c.TryEnd <= c.TryStart || c.HandlerEnd <= c.HandlerStart)
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
            if (op == "ldarg" || op == "ldloc") Push(Kind(Slot(op == "ldarg" ? args : method.Locals)));
            else if (op == "ldarga" || op == "ldloca") Push(Slot(op == "ldarga" ? args : method.Locals) + "&");
            else if (op == "starg" || op == "stloc") Expect(Kind(Slot(op == "starg" ? args : method.Locals)));
            else if (op == "ldc.i4") Push("i4");
            else if (op == "ldc.i8") Push("i8");
            else if (op == "ldc.r8") Push("f");
            else if (op == "ldc.r4") Push("f4");
            else if (op == "ldtoken") Push(i.Operand is string ? "typehandle" : "fieldhandle");
            else if (op == "ckfinite") { var kind = Pop(); if (kind is not ("f" or "f4")) Fail("ckfinite requires floating-point input.", pc); Push(kind); }
            else if (op is "ldnull" or "ldstr") Push("o");
            else if (op == "dup") { var v = Pop(); Push(v); Push(v); }
            else if (op == "pop") Pop();
            else if (Conversions.Contains(op))
            {
                var v = Pop(); if (v is not ("i4" or "i8" or "f" or "f4")) Fail("Numeric conversion on a non-number.", pc);
                if (v is "f" or "f4" && op is not ("conv.r4" or "conv.r8" or "conv.r.un") && !op.Contains(".ovf", StringComparison.Ordinal))
                    Fail("Unchecked floating-to-integer conversion requires a target-specific undefined-range policy; use checked conversion in portable-mvp.", pc);
                Push(op == "conv.r4" ? "f4" : op is "conv.r8" or "conv.r.un" ? "f" : op.Contains("i8", StringComparison.Ordinal) || op.Contains("u8", StringComparison.Ordinal) ? "i8" : "i4");
            }
            else if (op is "neg" or "not")
            {
                var v = Pop(); if (v is not ("i4" or "i8" or "f" or "f4") || (v is "f" or "f4" && op == "not")) Fail("Invalid unary operand.", pc); Push(v);
            }
            else if (op is "ldftn" or "ldvirtftn")
            { if (op == "ldvirtftn") Expect("o"); Push("fn"); }
            else if (op is "call" or "callvirt" or "newobj")
            {
                var call = (MethodReference)i.Operand!;
                foreach (var type in call.Parameters.Reverse()) Expect(Kind(type));
                if (call.Instance && op != "newobj")
                {
                    var receiver = Pop(); if (receiver != "o" && !receiver.EndsWith('&')) Fail("Invalid instance receiver.", pc);
                }
                if (op == "newobj") Push(Kind(call.Type)); else if (call.ReturnType != "System.Void") Push(Kind(call.ReturnType));
            }
            else if (op is "ldfld" or "ldflda" or "ldsfld" or "ldsflda" or "stfld" or "stsfld")
            {
                var field = (FieldReference)i.Operand!;
                if (op is "stfld" or "stsfld") Expect(Kind(field.FieldType));
                if (op is "ldfld" or "ldflda" or "stfld")
                { var receiver = Pop(); if (receiver != "o" && receiver != "v:" + field.Type && receiver != field.Type + "&") Fail("Invalid field receiver.", pc); }
                if (op is not ("stfld" or "stsfld")) Push(op.EndsWith('a') ? field.FieldType + "&" : Kind(field.FieldType));
            }
            else if (op == "newarr") { Expect("i4"); Push("o"); }
            else if (op == "ldlen") { Expect("o"); Push("i4"); }
            else if (op.StartsWith("ldelem", StringComparison.Ordinal))
            {
                Expect("i4"); Expect("o"); Push(op == "ldelema" ? (string)i.Operand! + "&" : Kind(ElementType(i)));
            }
            else if (op.StartsWith("stelem", StringComparison.Ordinal)) { Expect(Kind(ElementType(i))); Expect("i4"); Expect("o"); }
            else if (op.StartsWith("ldind", StringComparison.Ordinal) || op == "ldobj")
            {
                if (!Pop().EndsWith('&')) Fail("Indirect load requires a managed reference.", pc); Push(Kind(ElementType(i)));
            }
            else if (op.StartsWith("stind", StringComparison.Ordinal) || op == "stobj")
            {
                Expect(Kind(ElementType(i))); if (!Pop().EndsWith('&')) Fail("Indirect store requires a managed reference.", pc);
            }
            else if (op == "cpobj") { var type = (string)i.Operand!; Expect(type + "&"); Expect(type + "&"); }
            else if (op == "constrained.")
            {
                if (!instructions.TryGetValue(i.NextOffset, out var next) || next.Op != "callvirt") Fail("constrained. must precede callvirt.", pc);
            }
            else if (op == "initobj") { if (Pop() != (string)i.Operand! + "&") Fail("initobj reference type mismatch.", pc); }
            else if (op == "box") { Expect(Kind((string)i.Operand!)); Push("o"); }
            else if (op is "unbox" or "unbox.any") { Expect("o"); Push(op == "unbox" ? (string)i.Operand! + "&" : Kind((string)i.Operand!)); }
            else if (op is "castclass" or "isinst") { Expect("o"); Push("o"); }
            else if (op == "throw") { Expect("o"); s.Clear(); }
            else if (op == "rethrow" || op == "endfinally")
            {
                if (s.Count != 0) Fail("Exception terminator requires empty stack.", pc);
                if (!method.Exceptions.Any(c => c.HandlerStart <= pc && pc < c.HandlerEnd &&
                    (op == "rethrow" ? c.Kind == "Catch" : c.Kind is "Finally" or "Fault"))) Fail("Exception terminator outside its handler.", pc);
            }
            else if (op == "leave") s.Clear();
            else if (op == "ret")
            {
                if (method.Reference.ReturnType != "System.Void") Expect(Kind(method.Reference.ReturnType));
                if (s.Count != 0) Fail("Return leaves values on the evaluation stack.", pc);
            }
            else if (op == "switch") Expect("i4");
            else if (op is "brtrue" or "brfalse") { if (Pop() is not ("i4" or "i8" or "o")) Fail("Invalid conditional branch operand.", pc); }
            else if (op is not ("br" or "nop"))
            {
                var right = Pop(); var left = Pop();
                var compare = op is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un" || i.Code.FlowControl == FlowControl.Cond_Branch;
                if (op is "shl" or "shr" or "shr.un")
                { if (left is not ("i4" or "i8") || right != "i4") Fail("Invalid shift operands.", pc); }
                else if (left != right || (!compare && left is not ("i4" or "i8" or "f" or "f4"))) Fail("Incompatible arithmetic/comparison operands.", pc);
                if (left is "f" or "f4" && !compare && op is not ("add" or "sub" or "mul" or "div" or "rem")) Fail("Invalid floating-point operation.", pc);
                if (left == "o" && compare && op is not ("ceq" or "beq" or "bne.un" or "cgt.un")) Fail("Ordered object comparisons are outside portable-mvp.", pc);
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
        "i4" => "System.Int32", "u4" => "System.UInt32", "i8" => "System.Int64", "r4" => "System.Single", "r8" => "System.Double", "ref" => "System.Object",
        _ => throw new CompilationException(new Diagnostic("TR2101", $"Unknown element type for {i.Op}."))
    };
}
