using Transpiler.Core;

internal static class FlowChecks
{
    private static MethodDefinitionModel Method(params (string Op, object? Operand)[] body) => new(
        new(1, "Checks", "[Checks]Program", "M", [], "System.Void", false, 0),
        true, false, false, false, false, true, 8, [],
        body.Select((i, offset) => new Instruction(offset, offset + 1, i.Op, i.Operand)).ToArray(), []);

    public static void Run()
    {
        var catchBody = Method(("nop", null), ("leave", 4), ("pop", null), ("leave", 4), ("ret", null)) with
            { Exceptions = [new("Catch", 0, 2, 2, 4, "System.Exception", 0)] };
        var finallyBody = Method(("nop", null), ("leave", 4), ("nop", null), ("endfinally", null), ("ret", null)) with
            { Exceptions = [new("Finally", 0, 2, 2, 4, null, 0)] };
        MethodDefinitionModel Replace(MethodDefinitionModel method, int offset, string op, object? operand) => method with
            { Instructions = method.Instructions.Select(i => i.Offset == offset ? i with { Op = op, Operand = operand } : i).ToArray() };
        var call = new MethodReference(2, "Checks", "[Checks]S", "M", [], "System.Void", true, 0);
        var prefix = Method(("br", 1), ("constrained.", "[Checks]S"), ("callvirt", call), ("ret", null));
        var cases = new (string Name, bool Accept, MethodDefinitionModel Method)[]
        {
            ("simple-return", true, Method(("ret", null))),
            ("self-loop", true, Method(("br", 0))),
            ("valid-catch", true, catchBody),
            ("valid-finally", true, finallyBody),
            ("valid-fault", true, finallyBody with { Exceptions = [new("Fault", 0, 2, 2, 4, null, 0)] }),
            ("catch-to-associated-try", true, Replace(catchBody, 3, "leave", 0)),
            ("catch-to-associated-try-interior", true, Replace(catchBody, 3, "leave", 1)),
            ("try-entry-fallthrough", true, Method(("nop", null), ("leave", 4), ("pop", null), ("leave", 4), ("ret", null)) with
                { Exceptions = [new("Catch", 1, 2, 2, 4, "System.Exception", 0)] }),
            ("branch-out-of-try", false, Replace(catchBody, 1, "br", 4)),
            ("branch-into-handler", false, Replace(catchBody, 0, "br", 2)),
            ("branch-out-of-handler", false, Replace(catchBody, 3, "br", 4)),
            ("branch-into-try", false, Method(("br", 1), ("leave", 4), ("pop", null), ("leave", 4), ("ret", null)) with
                { Exceptions = [new("Catch", 1, 2, 2, 4, "System.Exception", 0)] }),
            ("fallthrough-out-of-try", false, Replace(catchBody, 1, "nop", null)),
            ("fallthrough-out-of-catch", false, Replace(catchBody, 3, "nop", null)),
            ("return-in-try", false, Replace(catchBody, 1, "ret", null)),
            ("return-in-handler", false, Replace(catchBody, 3, "ret", null)),
            ("leave-out-of-finally", false, Replace(finallyBody, 3, "leave", 4)),
            ("leave-out-of-fault", false, Replace(finallyBody, 3, "leave", 4) with
                { Exceptions = [new("Fault", 0, 2, 2, 4, null, 0)] }),
            ("leave-into-handler", false, Replace(catchBody, 1, "leave", 2)),
            ("endfinally-outside-handler", false, Method(("endfinally", null))),
            ("first-prefix-entry", true, prefix),
            ("prefix-interior-entry", false, Replace(prefix, 0, "br", 2)),
            ("dangling-prefix", false, Method(("constrained.", "[Checks]S"))),
            ("out-of-body-target", false, Method(("br", 99))),
            ("out-of-body-fallthrough", false, Method(("nop", null))),
            ("malformed-switch", false, Method(("switch", "invalid"), ("ret", null))),
            ("non-contiguous-body", false, Method(("nop", null), ("ret", null)) with
                { Instructions = [new(0, 2, "nop", null), new(1, 2, "ret", null)] }),
            ("empty-handler", false, catchBody with { Exceptions = [new("Catch", 0, 2, 2, 2, "System.Exception", 0)] }),
            ("same-clause-overlap", false, catchBody with { Exceptions = [new("Catch", 0, 3, 2, 4, "System.Exception", 0)] }),
            ("partially-overlapping-tries", false, Method(("nop", null), ("nop", null), ("leave", 8), ("leave", 8),
                ("pop", null), ("leave", 8), ("pop", null), ("leave", 8), ("ret", null)) with
                { Exceptions = [new("Catch", 0, 3, 4, 6, "System.Exception", 0), new("Catch", 1, 4, 6, 8, "System.Exception", 0)] }),
            ("nested-finally", true, Method(("nop", null), ("leave", 4), ("nop", null), ("endfinally", null),
                ("leave", 7), ("nop", null), ("endfinally", null), ("ret", null)) with
                { Exceptions = [new("Finally", 0, 2, 2, 4, null, 0), new("Finally", 0, 5, 5, 7, null, 0)] })
        };
        foreach (var test in cases)
        {
            try
            {
                CilControlFlow.Build(test.Method);
                if (!test.Accept) throw new InvalidOperationException("Incorrectly accepted " + test.Name);
            }
            catch (CompilationException e) when (!test.Accept && e.Diagnostics.All(d => d.Code == "TR2110")) { }
        }
        var graph = CilControlFlow.Build(finallyBody);
        if (graph.Blocks.Length != 3 || !graph.ExceptionalEdgesAreConservative ||
            !graph.Blocks[0].Successors.Any(e => e.Target == 4 && e.Kind == "leave-continuation") ||
            !graph.Blocks[0].Successors.Any(e => e.Target == 2 && e.Kind == "finally-unwind") ||
            !graph.Blocks[0].Successors.Any(e => e.Target == 2 && e.Kind == "exception-search" && e.ExceptionClause == 0) ||
            !graph.Blocks[1].Successors.Any(e => e.Target is null && e.Kind == "resume-continuation"))
            throw new InvalidOperationException("Graph lost the distinction between execution edges and exception continuations.");
        Console.WriteLine($"Control flow: {cases.Length} shapes and graph contracts passed");
    }
}
