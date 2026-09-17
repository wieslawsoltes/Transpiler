using System.Reflection.Emit;

namespace Transpiler.Core;

[Flags]
public enum CilEffects
{
    None = 0, ReadStorage = 1, WriteStorage = 2, MayThrow = 4, Allocate = 8,
    TypeInitialization = 16, Call = 32, MaySuspend = 64, ControlFlow = 128,
    PreserveOwner = 256, CopyValue = 512
}

/// <summary>
/// Conservative observable-effect classification. Unknown operations are full barriers, never pure.
/// Addressable arguments/locals remain storage: a value SSA graph is not a proof of non-aliasing.
/// </summary>
public static class CilEffectAnalysis
{
    public static CilEffects Classify(Instruction instruction, IReadOnlyList<string> stack)
    {
        var op = instruction.Op;
        const CilEffects opaque = CilEffects.ReadStorage | CilEffects.WriteStorage | CilEffects.MayThrow |
            CilEffects.Allocate | CilEffects.TypeInitialization | CilEffects.Call | CilEffects.MaySuspend;
        if (op is "nop" or "pop" or "ldnull" || op.StartsWith("ldc.", StringComparison.Ordinal)) return CilEffects.None;
        if (op is "ldarg" or "ldloc" or "dup")
            return (op == "dup" ? CilEffects.None : CilEffects.ReadStorage) |
                (stack.Any(k => k.StartsWith("v:", StringComparison.Ordinal)) || op != "dup"
                    ? CilEffects.CopyValue : CilEffects.None);
        if (op is "starg" or "stloc") return CilEffects.WriteStorage | CilEffects.CopyValue;
        if (op is "ldarga" or "ldloca") return CilEffects.ReadStorage | CilEffects.PreserveOwner | CilEffects.Allocate;
        if (op is "call" or "callvirt" or "newobj") return opaque;
        if (op is "throw" or "rethrow" or "leave" or "endfinally" or "endfilter") return opaque | CilEffects.ControlFlow;
        if (op == "ret" || instruction.Code.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch)
            return CilEffects.ControlFlow;
        if (op is "add" or "sub" or "mul" or "neg" or "not" or "and" or "or" or "xor" or "shl" or "shr" or "shr.un" or
            "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un") return CilEffects.None;
        if (op is "div" or "div.un" or "rem" or "rem.un" or "ckfinite" || op.Contains(".ovf", StringComparison.Ordinal))
            return CilEffects.MayThrow;
        if (op.StartsWith("conv.", StringComparison.Ordinal)) return CilEffects.None;
        if (op is "ldfld" or "ldsfld" or "ldflda" or "ldsflda")
            return CilEffects.ReadStorage | CilEffects.MayThrow | CilEffects.CopyValue |
                (op.StartsWith("lds", StringComparison.Ordinal) ? CilEffects.TypeInitialization : CilEffects.None) |
                (op.EndsWith('a') ? CilEffects.PreserveOwner | CilEffects.Allocate : CilEffects.None);
        if (op is "stfld" or "stsfld")
            return CilEffects.WriteStorage | CilEffects.MayThrow | CilEffects.CopyValue |
                (op == "stsfld" ? CilEffects.TypeInitialization : CilEffects.None);
        if (op.StartsWith("ldelem", StringComparison.Ordinal) || op.StartsWith("ldind", StringComparison.Ordinal) || op is "ldobj" or "ldlen")
            return CilEffects.ReadStorage | CilEffects.MayThrow | CilEffects.CopyValue |
                (op == "ldelema" ? CilEffects.PreserveOwner | CilEffects.Allocate : CilEffects.None);
        if (op.StartsWith("stelem", StringComparison.Ordinal) || op.StartsWith("stind", StringComparison.Ordinal) || op is "stobj" or "initobj" or "cpobj")
            return CilEffects.ReadStorage | CilEffects.WriteStorage | CilEffects.MayThrow | CilEffects.CopyValue;
        if (op is "newarr" or "box" or "ldstr" or "ldtoken" or "ldftn" or "ldvirtftn")
            return CilEffects.Allocate | CilEffects.MayThrow | CilEffects.PreserveOwner;
        if (op is "unbox" or "unbox.any" or "castclass" or "isinst")
            return CilEffects.MayThrow | CilEffects.ReadStorage | CilEffects.CopyValue | CilEffects.PreserveOwner;
        if (op == "constrained.") return CilEffects.ControlFlow; // Preserve adjacency to its callvirt.
        return opaque;
    }
}
