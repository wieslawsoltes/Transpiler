using System.Reflection.Emit;

namespace Transpiler.Core;

/// <summary>
/// Proves that every reachable local read/address operation follows a store on every incoming path. Every instruction in a protected region conservatively contributes
/// its pre-instruction state to exception entries. Assignments performed solely by filters/finally
/// are not assumed at continuations; address-based first writes remain conservatively rejected.
/// </summary>
public static class DefiniteLocalAssignment
{
    public static void Validate(MethodDefinitionModel method)
    {
        if (method.InitLocals || method.Locals.Length == 0) return;
        var instructions = method.Instructions.ToDictionary(i => i.Offset);
        var states = new Dictionary<int, HashSet<int>> { [0] = [] };
        var work = new Queue<int>(); work.Enqueue(0);
        while (work.TryDequeue(out var pc))
        {
            if (!instructions.TryGetValue(pc, out var instruction)) continue; // Stack verification diagnoses malformed targets.
            var assigned = new HashSet<int>(states[pc]);
            void Merge(int target)
            {
                if (!states.TryGetValue(target, out var previous))
                { states[target] = new(assigned); work.Enqueue(target); }
                else
                {
                    var count = previous.Count;
                    previous.IntersectWith(assigned);
                    if (previous.Count != count) work.Enqueue(target);
                }
            }
            // Search can enter a catch/filter before finally runs. Never credit a store which
            // has not yet executed, or an assignment relying only on cleanup/filter acceptance.
            foreach (var clause in method.Exceptions.Where(c => c.TryStart <= pc && pc < c.TryEnd))
            {
                Merge(clause.HandlerStart);
                if (clause.Kind == "Filter") Merge(clause.FilterStart);
            }
            if (instruction.Op == "stloc" && instruction.Operand is int slot) assigned.Add(slot);
            if (instruction.Op == "endfilter")
            {
                foreach (var clause in method.Exceptions.Where(c => c.Kind == "Filter" && c.FilterStart <= pc && pc < c.HandlerStart)) Merge(clause.HandlerStart);
                continue;
            }
            if (instruction.Op is "ret" or "throw" or "rethrow" or "endfinally") continue;
            if (instruction.Op is "br" or "leave") { Merge((int)instruction.Operand!); continue; }
            if (instruction.Op == "switch") foreach (var target in (int[])instruction.Operand!) Merge(target);
            else if (instruction.Code.FlowControl == FlowControl.Cond_Branch) Merge((int)instruction.Operand!);
            Merge(instruction.NextOffset);
        }
        var errors = method.Instructions.Where(i => (i.Op is "ldloc" or "ldloca") &&
            states.TryGetValue(i.Offset, out var assigned) && !assigned.Contains((int)i.Operand!))
            .Select(i => new Diagnostic("TR2006", $"Local {i.Operand} is not definitely assigned before this read/address operation.", method.Key, i.Offset))
            .ToArray();
        if (errors.Length != 0) throw new CompilationException(errors);
    }
}
