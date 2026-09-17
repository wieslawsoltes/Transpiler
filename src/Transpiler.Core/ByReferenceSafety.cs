namespace Transpiler.Core;

/// <summary>
/// Conservative origin analysis for managed addresses. Rejects returning a local/argument-slot address,
/// including aliases passed through other byref-returning methods. Byref-containing fields/arrays require
/// a separate ref-like storage profile. This is not a complete CLI verifier or a scoped-ref proof.
/// </summary>
public static class ByReferenceSafety
{
    [Flags] private enum Origin : byte { None = 0, Heap = 1, Caller = 2, Frame = 4, Unknown = 8 }
    private sealed record State(Origin[] Stack, Origin[] Locals, Origin[] Args);

    public static void Validate(AssemblyModel image, IEnumerable<MethodAnalysis> methods)
    {
        foreach (var analysis in methods)
        {
            var method = analysis.Method;
            var errors = new List<Diagnostic>();
            foreach (var field in image.Fields.Where(f => f.Reference.Type == method.Reference.Type))
                if (field.Reference.FieldType.EndsWith('&')) errors.Add(new("TR2120", "Byref-containing field storage requires the ref-like storage profile.", method.Key));
            foreach (var instruction in method.Instructions)
                if (instruction.Op is "box" or "newarr" && instruction.Operand is string t && (t.EndsWith('&') || t == "System.Void"))
                    errors.Add(new("TR2120", "Byref/void boxing and array element storage are not valid in this profile.", method.Key, instruction.Offset));
            if (errors.Count != 0) throw new CompilationException(errors.ToArray());
            if (!method.Reference.ReturnType.EndsWith('&') || method.Instructions.Length == 0) continue;
            ValidateReturn(image, analysis);
        }
    }

    private static void ValidateReturn(AssemblyModel image, MethodAnalysis analysis)
    {
        var method = analysis.Method;
        var argTypes = method.IsStatic ? method.Reference.Parameters : new[] { method.Reference.Type + (image.FindType(method.Reference.Type)?.IsValueType == true ? "&" : "") }.Concat(method.Reference.Parameters).ToArray();
        var initial = new State([], method.Locals.Select(t => t.EndsWith('&') ? Origin.Unknown : Origin.None).ToArray(),
            argTypes.Select(t => t.EndsWith('&') ? Origin.Caller : Origin.None).ToArray());
        var states = new Dictionary<int, State>(); var work = new Queue<int>();
        var instructions = method.Instructions.ToDictionary(i => i.Offset);
        void Merge(int offset, State state)
        {
            if (!analysis.StackBefore.ContainsKey(offset)) return;
            if (!states.TryGetValue(offset, out var prior))
            { states[offset] = new((Origin[])state.Stack.Clone(), (Origin[])state.Locals.Clone(), (Origin[])state.Args.Clone()); work.Enqueue(offset); return; }
            bool changed = false;
            void Union(Origin[] a, Origin[] b)
            {
                for (int n = 0; n < a.Length; n++) { var value = a[n] | b[n]; if (a[n] != value) { a[n] = value; changed = true; } }
            }
            Union(prior.Stack, state.Stack); Union(prior.Locals, state.Locals); Union(prior.Args, state.Args);
            if (changed) work.Enqueue(offset);
        }
        Merge(0, initial);
        while (work.TryDequeue(out var pc))
        {
            var old = states[pc]; var ins = instructions[pc]; var op = ins.Op;
            var stack = old.Stack.ToList(); var locals = (Origin[])old.Locals.Clone(); var args = (Origin[])old.Args.Clone();
            Origin Pop() { var value = stack[^1]; stack.RemoveAt(stack.Count - 1); return value; }
            void Push(Origin value) => stack.Add(value);
            // Conservatively preserve origins on every exceptional edge, including filter search.
            foreach (var clause in method.Exceptions.Where(c => c.TryStart <= pc && pc < c.TryEnd))
            {
                Merge(clause.HandlerStart, new(clause.Kind is "Catch" or "Filter" ? [Origin.None] : [], locals, args));
                if (clause.Kind == "Filter") Merge(clause.FilterStart, new([Origin.None], locals, args));
            }
            if (op == "ldarg") Push(args[(int)ins.Operand!]);
            else if (op == "ldloc") Push(locals[(int)ins.Operand!]);
            else if (op is "ldarga" or "ldloca") Push(Origin.Frame);
            else if (op == "starg") args[(int)ins.Operand!] = Pop();
            else if (op == "stloc") locals[(int)ins.Operand!] = Pop();
            else if (op == "dup") Push(stack[^1]);
            else if (op == "ldsflda") Push(Origin.Heap);
            else if (op == "ldflda")
            {
                var receiver = Pop(); var field = (FieldReference)ins.Operand!;
                Push(image.FindType(field.Type)?.IsValueType == true ? receiver == Origin.None ? Origin.Frame : receiver : Origin.Heap);
            }
            else if (op == "ldelema") { Pop(); Pop(); Push(Origin.Heap); }
            else if (op == "unbox") { Pop(); Push(Origin.Heap); }
            else if (op is "call" or "callvirt" or "newobj")
            {
                var call = (MethodReference)ins.Operand!;
                var count = call.Parameters.Length + (call.Instance && op != "newobj" ? 1 : 0);
                var origin = Origin.Heap;
                for (int n = 0; n < count; n++) origin |= Pop();
                if (op == "newobj" || call.ReturnType != "System.Void") Push(call.ReturnType.EndsWith('&') ? origin : Origin.None);
            }
            else if (op == "ret")
            {
                var origin = Pop();
                if ((origin & (Origin.Frame | Origin.Unknown)) != 0 || origin == Origin.None)
                    throw new CompilationException(new Diagnostic("TR2120", "Returned managed address may refer to this method's frame or uninitialized storage.", method.Key, pc));
                continue;
            }
            else
            {
                int pop = ins.Code.StackBehaviourPop switch
                {
                    System.Reflection.Emit.StackBehaviour.Pop0 => 0,
                    System.Reflection.Emit.StackBehaviour.Pop1 or System.Reflection.Emit.StackBehaviour.Popi or System.Reflection.Emit.StackBehaviour.Popref => 1,
                    System.Reflection.Emit.StackBehaviour.Pop1_pop1 or System.Reflection.Emit.StackBehaviour.Popi_pop1 or System.Reflection.Emit.StackBehaviour.Popi_popi or
                    System.Reflection.Emit.StackBehaviour.Popi_popi8 or System.Reflection.Emit.StackBehaviour.Popi_popr4 or System.Reflection.Emit.StackBehaviour.Popi_popr8 or
                    System.Reflection.Emit.StackBehaviour.Popref_pop1 or System.Reflection.Emit.StackBehaviour.Popref_popi => 2,
                    System.Reflection.Emit.StackBehaviour.Varpop => 0,
                    _ => 3
                };
                for (int n = 0; n < pop; n++) Pop();
                if (ins.Code.StackBehaviourPush != System.Reflection.Emit.StackBehaviour.Push0) Push(Origin.None);
            }
            if (op is "throw" or "rethrow" or "endfinally" or "endfilter") continue;
            if (op == "leave") stack.Clear();
            var next = new State(stack.ToArray(), locals, args);
            if (op is "br" or "leave")
            {
                if (op == "leave") foreach (var clause in method.Exceptions.Where(c => c.Kind == "Finally" && c.TryStart <= pc && pc < c.TryEnd))
                    Merge(clause.HandlerStart, new([], locals, args));
                Merge((int)ins.Operand!, next); continue;
            }
            if (op == "switch") foreach (var target in (int[])ins.Operand!) Merge(target, next);
            else if (ins.Code.FlowControl == System.Reflection.Emit.FlowControl.Cond_Branch) Merge((int)ins.Operand!, next);
            Merge(ins.NextOffset, next);
        }
    }

    public static void ValidateIndirect(AssemblyModel image, Instruction instruction, string address, string method)
    {
        if (!address.EndsWith('&')) throw new CompilationException(new Diagnostic("TR2121", "Indirect access requires a typed managed address.", method, instruction.Offset));
        var type = address[..^1]; var requested = CompilerAnalysis.ElementType(instruction);
        if (type == requested) return;
        if (instruction.Op is "ldobj" or "stobj")
            throw new CompilationException(new Diagnostic("TR2121", "Object load/store address and token types differ.", method, instruction.Offset));
        var storage = image.FindType(type)?.EnumUnderlyingType ?? type;
        string Category(string t) => t switch
        {
            "System.Boolean" or "System.SByte" or "System.Byte" => "i1",
            "System.Char" or "System.Int16" or "System.UInt16" => "i2",
            "System.Int32" or "System.UInt32" => "i4", "System.Int64" or "System.UInt64" => "i8",
            "System.Single" => "r4", "System.Double" => "r8",
            _ => image.FindType(t)?.IsValueType != true && CliTypes.StackKind(t) == "o" ? "ref" : "value"
        };
        if (Category(storage) != Category(requested) || Category(storage) == "value")
            throw new CompilationException(new Diagnostic("TR2121", "Indirect opcode does not match the address storage type/width.", method, instruction.Offset));
    }
}
