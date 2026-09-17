using System.Reflection.Emit;

namespace Transpiler.Core;

public sealed record CilFlowEdge(int? Target, string Kind, int? ExceptionClause = null);
public sealed record CilBasicBlock(int Start, int End, int[] InstructionOffsets, CilFlowEdge[] Successors);
public sealed record CilControlFlowGraph(CilBasicBlock[] Blocks, ExceptionClause[] ExceptionRegions,
    bool ExceptionalEdgesAreConservative = true);

/// <summary>
/// Validates instruction/prefix boundaries and protected-region transfers before exposing a block graph.
/// Exception-search and leave/finally continuation edges are explicitly distinguished from normal branches.
/// This is not a complete ECMA type/lifetime verifier or an executable exception-lowering graph.
/// </summary>
public static class CilControlFlow
{
    private sealed record Region(string Kind, int Start, int End)
    {
        public bool Contains(int offset) => Start <= offset && offset < End;
    }

    public static CilControlFlowGraph Build(MethodDefinitionModel method)
    {
        if (method.Instructions.Length == 0) return new([], method.Exceptions);
        void Fail(string message, int offset) => throw new CompilationException(new Diagnostic("TR2110", message, method.Key, offset));
        var instructions = method.Instructions;
        var expected = 0;
        foreach (var i in instructions)
        {
            if (i.Offset != expected || i.NextOffset <= i.Offset) Fail("Instruction offsets are not a contiguous, ordered CIL body.", i.Offset);
            expected = i.NextOffset;
        }
        var end = expected;
        var byOffset = instructions.ToDictionary(i => i.Offset);
        var prefixInteriors = instructions.Where(i => i.Code.OpCodeType == OpCodeType.Prefix).Select(i => i.NextOffset).ToHashSet();
        void Boundary(int offset, bool endAllowed = false)
        {
            if ((!endAllowed || offset != end) && !byOffset.ContainsKey(offset)) Fail("Control-flow boundary is not an instruction start.", offset);
            if (prefixInteriors.Contains(offset)) Fail("Control must enter at the first prefix, not inside a prefixed instruction.", offset);
        }
        if (method.Exceptions.Length > 512) Fail("Exception-clause budget exceeds 512 entries.", 0);
        var regions = new HashSet<Region>();
        foreach (var clause in method.Exceptions)
        {
            if (clause.Kind is not ("Catch" or "Finally" or "Fault" or "Filter")) Fail("Unsupported exception region in this control-flow profile.", clause.TryStart);
            Boundary(clause.TryStart); Boundary(clause.TryEnd, true); Boundary(clause.HandlerStart); Boundary(clause.HandlerEnd, true);
            if (clause.TryStart >= clause.TryEnd || clause.HandlerStart >= clause.HandlerEnd ||
                clause.TryStart < clause.HandlerEnd && clause.HandlerStart < clause.TryEnd)
                Fail("Empty or overlapping try/handler ranges in one clause.", clause.TryStart);
            regions.Add(new("Try", clause.TryStart, clause.TryEnd));
            regions.Add(new(clause.Kind == "Filter" ? "Catch" : clause.Kind, clause.HandlerStart, clause.HandlerEnd));
            if (clause.Kind == "Filter")
            {
                Boundary(clause.FilterStart);
                if (clause.FilterStart >= clause.HandlerStart || clause.TryStart < clause.HandlerStart && clause.FilterStart < clause.TryEnd)
                    Fail("Filter overlaps its protected range or is empty.", clause.FilterStart);
                regions.Add(new("Filter", clause.FilterStart, clause.HandlerStart));
            }
        }
        foreach (var filter in method.Exceptions.Where(c => c.Kind == "Filter"))
        {
            var endings = instructions.Where(i => filter.FilterStart <= i.Offset && i.Offset < filter.HandlerStart && i.Op == "endfilter").ToArray();
            if (endings.Length != 1 || endings[0].NextOffset != filter.HandlerStart)
                Fail("A filter requires one final endfilter immediately before its handler.", filter.FilterStart);
            if (method.Exceptions.Any(c => filter.FilterStart <= c.TryStart && c.TryStart < filter.HandlerStart))
                Fail("A filter block cannot contain an embedded try region; call a helper for nested handling.", filter.FilterStart);
        }
        var orderedRegions = regions.OrderBy(r => r.Start).ThenByDescending(r => r.End).ThenBy(r => r.Kind, StringComparer.Ordinal).ToArray();
        for (int a = 0; a < orderedRegions.Length; a++)
            for (int b = a + 1; b < orderedRegions.Length; b++)
            {
                var x = orderedRegions[a]; var y = orderedRegions[b];
                if (y.Start >= x.End) break;
                if (x.Start < y.Start && y.Start < x.End && x.End < y.End)
                    Fail("Protected regions overlap without proper nesting.", y.Start);
            }
        Region[] At(int offset) => orderedRegions.Where(r => r.Contains(offset)).ToArray();
        var leaders = new SortedSet<int> { 0 };
        foreach (var region in orderedRegions) { leaders.Add(region.Start); if (region.End < end) leaders.Add(region.End); }
        int[] Targets(Instruction i)
        {
            if (i.Op == "switch") return i.Operand is int[] table ? table : throw new CompilationException(new Diagnostic("TR2110", "Invalid switch operand.", method.Key, i.Offset));
            if (i.Code.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
                return i.Operand is int target ? [target] : throw new CompilationException(new Diagnostic("TR2110", "Invalid branch operand.", method.Key, i.Offset));
            return [];
        }
        void Transfer(Instruction instruction, int target, bool fallthrough)
        {
            if (!fallthrough) Boundary(target);
            else if (!byOffset.ContainsKey(target)) Fail("Reachable fallthrough leaves the method body.", instruction.Offset);
            var source = At(instruction.Offset); var destination = At(target);
            var removed = source.Except(destination).ToArray(); var entered = destination.Except(source).ToArray();
            if (instruction.Op == "leave")
            {
                if (removed.Any(r => r.Kind is "Finally" or "Fault" or "Filter")) Fail("leave cannot exit a finally/fault handler.", instruction.Offset);
                foreach (var r in entered)
                {
                    var associatedCatch = r.Kind == "Try" && method.Exceptions.Any(c => c.Kind is "Catch" or "Filter" &&
                        c.HandlerStart <= instruction.Offset && instruction.Offset < c.HandlerEnd && c.TryStart == r.Start && c.TryEnd == r.End);
                    if (!associatedCatch) Fail("leave enters a new protected region rather than an enclosing scope or associated try.", instruction.Offset);
                }
            }
            else if (fallthrough)
            {
                if (removed.Length != 0 || entered.Any(r => r.Kind != "Try" || r.Start != target))
                    Fail("Fallthrough crosses a handler boundary or exits protected code without leave.", instruction.Offset);
            }
            else if (removed.Length != 0 || entered.Length != 0)
                Fail("Ordinary branch crosses a protected-region boundary; use a valid leave transfer.", instruction.Offset);
        }
        foreach (var i in instructions)
        {
            var targets = Targets(i);
            foreach (var target in targets) { Transfer(i, target, false); leaders.Add(target); }
            var terminator = i.Op is "ret" or "throw" or "rethrow" or "endfinally" or "endfilter";
            if (i.Op == "endfilter" && !At(i.Offset).OrderBy(r => r.End-r.Start).Take(1).Any(r => r.Kind == "Filter"))
                Fail("endfilter must terminate the active filter region.", i.Offset);
            if (i.Op == "ret" && At(i.Offset).Length != 0) Fail("ret cannot leave a protected region.", i.Offset);
            if (i.Op == "endfinally" && !At(i.Offset).OrderBy(r => r.End-r.Start).Take(1).Any(r => r.Kind is "Finally" or "Fault"))
                Fail("endfinally must terminate the active finally/fault region.", i.Offset);
            if (!terminator && i.Code.FlowControl != FlowControl.Branch) Transfer(i, i.NextOffset, true);
            if ((terminator || i.Code.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch) && i.NextOffset < end) leaders.Add(i.NextOffset);
        }
        var starts = leaders.ToArray();
        var blocks = new List<CilBasicBlock>();
        var cursor = 0;
        for (int index = 0; index < starts.Length; index++)
        {
            var start = starts[index]; var finish = index + 1 < starts.Length ? starts[index + 1] : end;
            var first = cursor;
            while (cursor < instructions.Length && instructions[cursor].Offset < finish) cursor++;
            var body = instructions[first..cursor];
            var last = body[^1]; var edges = new List<CilFlowEdge>();
            foreach (var target in Targets(last))
                edges.Add(new(target, last.Op == "leave" ? "leave-continuation" : last.Op == "switch" ? "switch" : "branch"));
            if (last.Op == "leave")
                foreach (var c in method.Exceptions.Where(c => c.Kind == "Finally" && c.TryStart <= last.Offset && last.Offset < c.TryEnd &&
                    !((int)last.Operand! >= c.TryStart && (int)last.Operand! < c.TryEnd)).OrderBy(c => c.TryEnd-c.TryStart))
                    edges.Add(new(c.HandlerStart, "finally-unwind"));
            else if (last.Op == "endfilter") edges.Add(new(null, "filter-result"));
            else if (last.Op == "endfinally") edges.Add(new(null, "resume-continuation"));
            else if (last.Op is "throw" or "rethrow") edges.Add(new(null, "propagate-exception"));
            else if (last.Op != "ret" && last.Code.FlowControl != FlowControl.Branch) edges.Add(new(last.NextOffset, "fallthrough"));
            for (int clauseIndex = 0; clauseIndex < method.Exceptions.Length; clauseIndex++)
            {
                var clause = method.Exceptions[clauseIndex];
                if (clause.TryStart < finish && start < clause.TryEnd) edges.Add(new(clause.Kind == "Filter" ? clause.FilterStart : clause.HandlerStart, "exception-search", clauseIndex));
            }
            blocks.Add(new(start, finish, body.Select(i => i.Offset).ToArray(), edges.Distinct().ToArray()));
        }
        return new(blocks.ToArray(), method.Exceptions);
    }
}
