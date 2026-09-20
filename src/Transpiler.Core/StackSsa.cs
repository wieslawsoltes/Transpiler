using System.Reflection.Emit;
using System.Text.Json.Serialization;

namespace Transpiler.Core;

public sealed record SsaValue(int Id, string Kind, int Block, int? Offset);
public sealed record SsaIncoming(int Predecessor, int Value);
public sealed record SsaPhi(int Result, SsaIncoming[] Incoming);
public sealed record SsaOperation(Instruction Instruction, int[] Inputs, int? Result, CilEffects Effects);
public sealed record SsaBlock(int Start, int[] Parameters, SsaOperation[] Operations, int[] ExitValues, int[] Successors);
public sealed record SsaConstant(string Kind, long Value);
public sealed record StackSsaGraph(SsaValue[] Values, SsaPhi[] Phis, SsaBlock[] Blocks,
    IReadOnlyDictionary<int, SsaConstant> Constants, int[] LiveValues, int[] ActiveOffsets)
{
    [JsonIgnore] public int EliminatedInstructions => Blocks.Sum(b => b.Operations.Length) - ActiveOffsets.Length;
}

/// <summary>
/// Typed evaluation-stack SSA over a verified normal-flow CFG. Block parameters are phi definitions;
/// arguments, locals and interior owners deliberately remain explicit storage operations. No EH edge
/// is treated as an ordinary branch. This profile falls back before handling protected methods.
/// </summary>
public static class StackSsa
{
    public static CompilationAnalysis Prepare(CompilationAnalysis analysis, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var search = analysis.Methods.Any(m => m.Method.Exceptions.Any(c => c.Kind == "Filter"));
        return analysis with { Methods = analysis.Methods.Select(m =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var reason = search ? "module-two-pass-search" : Exclusion(m);
            return m with { Ssa = reason is null ? Build(m) : null, SsaExclusion = reason };
        }).ToArray() };
    }

    public static string? Exclusion(MethodAnalysis analysis)
    {
        if (analysis.StackBefore.Count == 0) return "no-managed-body";
        if (analysis.Method.Exceptions.Length != 0) return "protected-region";
        if (analysis.Method.Instructions.Any(i => i.Op is "leave" or "rethrow" or "endfinally" or "endfilter"))
            return "exception-continuation";
        return null;
    }

    public static StackSsaGraph Build(MethodAnalysis analysis)
    {
        if (Exclusion(analysis) is { } reason)
            throw new CompilationException(new Diagnostic("TR2200", "Stack SSA exclusion: " + reason, analysis.Method.Key));
        void Fail(string message, int offset) => throw new CompilationException(new Diagnostic("TR2201", message, analysis.Method.Key, offset));
        var flow = analysis.ControlFlow ?? CilControlFlow.Build(analysis.Method);
        var reachable = flow.Blocks.Where(b => analysis.StackBefore.ContainsKey(b.Start)).ToArray();
        var values = new List<SsaValue>();
        int NewValue(string kind, int block, int? offset)
        { var id = values.Count; values.Add(new(id, kind, block, offset)); return id; }
        var parameters = reachable.ToDictionary(b => b.Start,
            b => analysis.StackBefore[b.Start].Select(k => NewValue(k, b.Start, null)).ToArray());
        var instructions = analysis.Method.Instructions.ToDictionary(i => i.Offset);
        var blocks = new List<SsaBlock>();
        foreach (var block in reachable)
        {
            var stack = parameters[block.Start].ToList();
            var operations = new List<SsaOperation>();
            foreach (var offset in block.InstructionOffsets)
            {
                if (!analysis.StackBefore.TryGetValue(offset, out var kinds)) continue;
                var i = instructions[offset];
                if (!stack.Select(v => values[v].Kind).SequenceEqual(kinds)) Fail("SSA input stack differs from verified stack.", offset);
                var pop = i.Op == "dup" ? 0 : PopCount(i, analysis.Method);
                if (pop > stack.Count) Fail("SSA stack underflow.", offset);
                var inputs = i.Op == "dup" ? new[] { stack[^1] } : stack.Skip(stack.Count - pop).ToArray();
                if (pop != 0) stack.RemoveRange(stack.Count - pop, pop);
                int? result = null;
                if (Pushes(i))
                {
                    if (!analysis.StackBefore.TryGetValue(i.NextOffset, out var after) || after.Length == 0)
                        Fail("Value-producing instruction has no verified successor stack.", offset);
                    result = NewValue(analysis.StackBefore[i.NextOffset][^1], block.Start, offset);
                    stack.Add(result.Value);
                }
                operations.Add(new(i, inputs, result, CilEffectAnalysis.Classify(i, kinds)));
            }
            blocks.Add(new(block.Start, parameters[block.Start], operations.ToArray(), stack.ToArray(),
                block.Successors.Where(e => e.Target.HasValue).Select(e => e.Target!.Value).Distinct().ToArray()));
        }
        var incoming = parameters.Values.SelectMany(p => p).ToDictionary(v => v, _ => new List<SsaIncoming>());
        foreach (var block in blocks)
            foreach (var target in block.Successors)
            {
                if (!parameters.TryGetValue(target, out var destination)) { Fail("SSA edge targets an unverified block.", block.Start); continue; }
                if (block.ExitValues.Length != destination.Length) Fail("SSA edge stack height mismatch.", block.Start);
                for (var slot = 0; slot < destination.Length; slot++)
                {
                    var value = block.ExitValues[slot];
                    if (values[value].Kind != values[destination[slot]].Kind) Fail("SSA phi kind mismatch.", target);
                    incoming[destination[slot]].Add(new(block.Start, value));
                }
            }
        var phis = incoming.Select(p => new SsaPhi(p.Key, p.Value.ToArray())).ToArray();
        return SsaOptimization.Run(values.ToArray(), phis, blocks.ToArray());
    }

    private static bool Pushes(Instruction i) => i.Op is "call" or "callvirt"
        ? ((MethodReference)i.Operand!).ReturnType != "System.Void"
        : i.Op == "newobj" || i.Code.StackBehaviourPush != StackBehaviour.Push0;

    private static int PopCount(Instruction i, MethodDefinitionModel method)
    {
        if (i.Op is "call" or "callvirt" or "newobj")
        {
            var call = (MethodReference)i.Operand!;
            return call.Parameters.Length + (call.Instance && i.Op != "newobj" ? 1 : 0);
        }
        if (i.Op == "ret") return method.Reference.ReturnType == "System.Void" ? 0 : 1;
        return i.Code.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
            StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8 or
                StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8 or
                StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref or StackBehaviour.Popref_popi_pop1 => 3,
            _ => throw new CompilationException(new Diagnostic("TR2201", "Unmodelled SSA stack effect: " + i.Op, method.Key, i.Offset))
        };
    }
}
