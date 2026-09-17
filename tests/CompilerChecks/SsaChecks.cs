using System.Text.Json;
using Transpiler.Core;

internal static class SsaChecks
{
    private static MethodAnalysis Analyze(string result, string[] arguments, params (string Op, object? Operand)[] body)
    {
        var method = new MethodDefinitionModel(new(1, "Checks", "[Checks]Program", "M", arguments, result, false, 0),
            true, false, false, false, false, true, 8, [],
            body.Select((i, offset) => new Instruction(offset, offset + 1, i.Op, i.Operand)).ToArray(), []);
        var image = new AssemblyModel("Checks", 0, [new("[Checks]Program", "System.Object", false, false, true, 0, [])], [method], [])
            { ExportRoots = [method.Key] };
        return CompilerAnalysis.Analyze(image).Methods.Single();
    }

    public static void Run()
    {
        var count = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); count++; }
        var folded = StackSsa.Build(Analyze("System.Int32", [], ("ldc.i4", int.MaxValue), ("ldc.i4", 1), ("add", null), ("ret", null)));
        Check(folded.Constants.Values.Any(v => v.Value == int.MinValue), "unchecked Int32 wrap");
        Check(folded.ActiveOffsets.SequenceEqual([3]), "folded definitions removed but return retained");
        var wide = StackSsa.Build(Analyze("System.Int64", [], ("ldc.i8", long.MaxValue), ("ldc.i8", 1L), ("add", null), ("ret", null)));
        Check(wide.Constants.Values.Any(v => v.Value == long.MinValue), "unchecked Int64 wrap");
        var shift = StackSsa.Build(Analyze("System.Int64", [], ("ldc.i8", -1L), ("ldc.i4", 65), ("shr.un", null), ("ret", null)));
        Check(shift.Constants.Values.Any(v => v.Value == long.MaxValue), "unsigned Int64 shift mask");
        foreach (var op in new[] { "add.ovf", "add.ovf.un", "sub.ovf", "mul.ovf", "div", "div.un", "rem", "rem.un" })
        {
            var graph = StackSsa.Build(Analyze("System.Void", [], ("ldc.i4", int.MinValue), ("ldc.i4", -1), (op, null), ("pop", null), ("ret", null)));
            Check(graph.ActiveOffsets.Contains(2), "unused throwing operation: " + op);
            Check(!graph.LiveValues.Contains(graph.Blocks[0].Operations[2].Result!.Value), "discarded result: " + op);
        }
        var diamond = Analyze("System.Int32", ["System.Boolean"],
            ("ldarg", 0), ("brtrue", 4), ("ldc.i4", 7), ("br", 5), ("ldc.i4", 7), ("ret", null));
        var joined = StackSsa.Build(diamond);
        Check(joined.Phis.Length == 1 && joined.Phis[0].Incoming.Length == 2, "diamond predecessors");
        Check(joined.Constants[joined.Phis[0].Result].Value == 7, "identical phi constants");
        var differing = StackSsa.Build(diamond with { Method = diamond.Method with { Instructions = diamond.Method.Instructions.Select(i => i.Offset == 4 ? i with { Operand = 8 } : i).ToArray() } });
        Check(!differing.Constants.ContainsKey(differing.Phis[0].Result), "different phi constants are overdefined");
        var loop = StackSsa.Build(Analyze("System.Int32", ["System.Boolean"],
            ("ldc.i4", 0), ("br", 2), ("ldc.i4", 1), ("add", null), ("ldarg", 0), ("brtrue", 2), ("ret", null)));
        Check(loop.Phis.Length >= 1 && loop.Phis.Any(p => p.Incoming.Any(i => i.Predecessor == 2)), "loop phi backedge");
        Check(loop.Phis.Where(p => p.Incoming.Length > 1).All(p => !loop.Constants.ContainsKey(p.Result)), "cyclic growing value is not constant");
        Check(JsonSerializer.Serialize(loop) == JsonSerializer.Serialize(StackSsa.Build(Analyze("System.Int32", ["System.Boolean"],
            ("ldc.i4", 0), ("br", 2), ("ldc.i4", 1), ("add", null), ("ldarg", 0), ("brtrue", 2), ("ret", null)))), "deterministic SSA graph");
        Check(CilEffectAnalysis.Classify(new(0, 1, "call", null), []).HasFlag(CilEffects.MaySuspend), "opaque call is a suspension barrier");
        Check(CilEffectAnalysis.Classify(new(0, 1, "ldsfld", null), []).HasFlag(CilEffects.TypeInitialization), "static read retains initialization");
        Check(CilEffectAnalysis.Classify(new(0, 1, "ldloca", 0), []).HasFlag(CilEffects.PreserveOwner), "interior owner retained");
        Check(CilEffectAnalysis.Classify(new(0, 1, "dup", null), ["v:S"]).HasFlag(CilEffects.CopyValue), "struct dup copies");
        Check(CilEffectAnalysis.Classify(new(0, 1, "localloc", null), []).HasFlag(CilEffects.MayThrow), "unknown opcode is not pure");
        var protectedMethod = diamond with { Method = diamond.Method with { Exceptions = [new("Finally", 0, 2, 2, 3, null, 0)] } };
        Check(StackSsa.Exclusion(protectedMethod) == "protected-region", "EH boundary is explicit");
        try { StackSsa.Build(protectedMethod); throw new InvalidOperationException("protected method accepted"); }
        catch (CompilationException e) { Check(e.Diagnostics.Single().Code == "TR2200", "EH diagnostic"); }
        Console.WriteLine($"Stack SSA: {count} passed");
    }
}
