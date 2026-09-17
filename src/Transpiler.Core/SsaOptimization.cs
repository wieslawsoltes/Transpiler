namespace Transpiler.Core;

/// <summary>
/// Monotone sparse constant propagation and backward dead-value elimination. Edges are not pruned:
/// unknown/cyclic incoming values cannot become proofs of unreachable control flow. Only total
/// Int32/Int64 operations are folded; checked arithmetic, division and all storage/call effects stay.
/// </summary>
internal static class SsaOptimization
{
    private readonly record struct Lattice(int State, SsaConstant? Constant = null)
    {
        public static Lattice Unknown => new(0);
        public static Lattice Overdefined => new(2);
        public static Lattice Known(string kind, long value) => new(1, new(kind, value));
    }

    public static StackSsaGraph Run(SsaValue[] values, SsaPhi[] phis, SsaBlock[] blocks)
    {
        var operations = blocks.SelectMany(b => b.Operations).ToArray();
        var producers = operations.Where(o => o.Result.HasValue).ToDictionary(o => o.Result!.Value);
        var phiByValue = phis.ToDictionary(p => p.Result);
        var states = new Lattice[values.Length];
        var users = Enumerable.Range(0, values.Length).Select(_ => new List<int>()).ToArray();
        foreach (var op in producers.Values)
            foreach (var input in op.Inputs.Distinct()) users[input].Add(op.Result!.Value);
        foreach (var phi in phis)
            foreach (var input in phi.Incoming.Select(i => i.Value).Distinct()) users[input].Add(phi.Result);
        var queue = new Queue<int>(Enumerable.Range(0, values.Length));
        var queued = Enumerable.Repeat(true, values.Length).ToArray();
        while (queue.TryDequeue(out var id))
        {
            queued[id] = false;
            var next = phiByValue.TryGetValue(id, out var phi)
                ? Merge(phi.Incoming.Select(i => states[i.Value]))
                : Evaluate(producers[id], values, states);
            // A lattice value never moves backwards, including self-referential phi cycles.
            var previous = states[id];
            if (previous.State == 2 || next.State == 0 || next == previous) continue;
            if (previous.State == 1 && next.State == 1 && previous != next) next = Lattice.Overdefined;
            states[id] = next;
            foreach (var user in users[id]) if (!queued[user]) { queued[user] = true; queue.Enqueue(user); }
        }
        var constants = values.Where(v => states[v.Id].State == 1).ToDictionary(v => v.Id, v => states[v.Id].Constant!);
        var live = new HashSet<int>(); var active = new HashSet<int>(); var todo = new Stack<int>();
        void Require(SsaOperation op)
        {
            if (!active.Add(op.Instruction.Offset)) return;
            foreach (var input in op.Inputs) todo.Push(input);
        }
        foreach (var op in operations) if (op.Effects != CilEffects.None) Require(op);
        while (todo.TryPop(out var value))
        {
            if (constants.ContainsKey(value) || !live.Add(value)) continue;
            if (phiByValue.TryGetValue(value, out var phi))
                foreach (var input in phi.Incoming) todo.Push(input.Value);
            else Require(producers[value]);
        }
        return new(values, phis, blocks, constants, live.Order().ToArray(), active.Order().ToArray());
    }

    private static Lattice Merge(IEnumerable<Lattice> incoming)
    {
        var result = Lattice.Unknown;
        var any = false;
        foreach (var value in incoming)
        {
            any = true;
            if (value.State == 2) return Lattice.Overdefined;
            if (value.State == 0) continue;
            if (result.State == 1 && result != value) return Lattice.Overdefined;
            result = value;
        }
        return any ? result : Lattice.Overdefined;
    }

    private static Lattice Evaluate(SsaOperation operation, SsaValue[] values, Lattice[] states)
    {
        var i = operation.Instruction;
        if (i.Op == "ldc.i4") return Lattice.Known("i4", (int)i.Operand!);
        if (i.Op == "ldc.i8") return Lattice.Known("i8", (long)i.Operand!);
        if (operation.Effects != CilEffects.None || operation.Inputs.Length == 0) return Lattice.Overdefined;
        if (i.Op is not ("dup" or "add" or "sub" or "mul" or "neg" or "not" or "and" or "or" or "xor" or
            "shl" or "shr" or "shr.un" or "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un")) return Lattice.Overdefined;
        if (operation.Inputs.Any(v => values[v].Kind is not ("i4" or "i8"))) return Lattice.Overdefined;
        var inputs = operation.Inputs.Select(v => states[v]).ToArray();
        if (inputs.Any(v => v.State == 2)) return Lattice.Overdefined;
        if (inputs.Any(v => v.State == 0)) return Lattice.Unknown;
        var kind = values[operation.Inputs[0]].Kind;
        var a = inputs[0].Constant!.Value; var b = inputs.Length > 1 ? inputs[1].Constant!.Value : 0;
        var bits = kind == "i4" ? 32 : 64;
        var unsignedA = bits == 32 ? unchecked((uint)a) : unchecked((ulong)a);
        var unsignedB = bits == 32 ? unchecked((uint)b) : unchecked((ulong)b);
        var shift = (int)(b & (bits - 1));
        long result = i.Op switch
        {
            "dup" => a, "add" => unchecked(a + b), "sub" => unchecked(a - b), "mul" => unchecked(a * b),
            "neg" => unchecked(-a), "not" => ~a, "and" => a & b, "or" => a | b, "xor" => a ^ b,
            "shl" => unchecked(a << shift), "shr" => a >> shift, "shr.un" => unchecked((long)(unsignedA >> shift)),
            "ceq" => a == b ? 1 : 0, "cgt" => a > b ? 1 : 0, "clt" => a < b ? 1 : 0,
            "cgt.un" => unsignedA > unsignedB ? 1 : 0, "clt.un" => unsignedA < unsignedB ? 1 : 0,
            _ => throw new InvalidOperationException("Unmodelled constant operation.")
        };
        var resultKind = values[operation.Result!.Value].Kind;
        return Lattice.Known(resultKind, resultKind == "i4" ? unchecked((int)result) : result);
    }
}
