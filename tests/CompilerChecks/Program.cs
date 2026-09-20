using Transpiler.Core;

if (args.Length == 1 && args[0] == "--streams") { StreamContractChecks.Run(); return; }

if (args.Length == 1 && args[0] == "--ssa") { SsaChecks.Run(); return; }
if (args.Length == 2 && args[0] == "--emit-ssa-library") { SsaFixture.Write(args[1], true); return; }
if (args.Length == 2 && args[0] == "--emit-ssa") { SsaFixture.Write(args[1]); return; }

if (args.Length == 2 && args[0] == "--emit-fault") { FaultFixture.Write(args[1]); return; }
if (args.Length == 1 && args[0] == "--identity") { IdentityChecks.Run(); return; }
if (args.Length == 1 && args[0] == "--safety") { SafetyChecks.Run(); return; }

if (args.Length == 1 && args[0] == "--flow") { FlowChecks.Run(); return; }

// No external test packages: exercise hand-authored normalized CIL shapes unreachable from safe C# source.
static MethodDefinitionModel Method(params (string Op, object? Operand)[] body) => new(
    new(1, "Checks", "[Checks]Program", "M", [], "System.Int32", false, 0),
    true, false, false, false, false, false, 2, ["System.Int32"],
    body.Select((i, offset) => new Instruction(offset, offset + 1, i.Op, i.Operand)).ToArray(), []);

var cases = new (string Name, bool Accept, MethodDefinitionModel Method)[]
{
    ("store-then-read", true, Method(("ldc.i4", 4), ("stloc", 0), ("ldloc", 0), ("ret", null))),
    ("read-before-store", false, Method(("ldloc", 0), ("ret", null))),
    ("both-branches-store", true, Method(("ldc.i4", 1), ("brtrue", 5), ("ldc.i4", 2), ("stloc", 0),
        ("br", 7), ("ldc.i4", 3), ("stloc", 0), ("ldloc", 0), ("ret", null))),
    ("join-missing-store", false, Method(("ldc.i4", 1), ("brtrue", 4), ("ldc.i4", 2), ("stloc", 0), ("ldloc", 0), ("ret", null))),
    ("initialized-loop", true, Method(("ldc.i4", 3), ("stloc", 0), ("ldloc", 0), ("brtrue", 2), ("ldloc", 0), ("ret", null))),
    ("loop-not-initialization", false, Method(("br", 3), ("ldc.i4", 4), ("stloc", 0), ("ldloc", 0), ("brtrue", 1), ("ldloc", 0), ("ret", null))),
    ("address-after-store", true, Method(("ldc.i4", 4), ("stloc", 0), ("ldloca", 0), ("ldind.i4", null), ("ret", null))),
    ("address-before-store", false, Method(("ldloca", 0), ("ldind.i4", null), ("ret", null))),
    ("unreachable-read", true, Method(("br", 2), ("ldloc", 0), ("ldc.i4", 5), ("ret", null))),
    ("initialized-flag", true, Method(("ldloc", 0), ("ret", null)) with { InitLocals = true }),
    ("exception-region-conservative", false, Method(("ldc.i4", 4), ("stloc", 0), ("ldloc", 0), ("ret", null)) with
        { Exceptions = [new("Catch", 0, 2, 2, 4, "System.Exception", 0)] })
};
foreach (var test in cases)
{
    try
    {
        DefiniteLocalAssignment.Validate(test.Method);
        if (!test.Accept) throw new InvalidOperationException("Incorrectly accepted " + test.Name);
    }
    catch (CompilationException exception) when (!test.Accept && exception.Diagnostics.All(d => d.Code == "TR2006")) { }
}
Console.WriteLine($"Definite assignment: {cases.Length} passed");
