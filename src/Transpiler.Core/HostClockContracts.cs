namespace Transpiler.Core;

/// <summary>Internal clock ABI. Assembly, type, instance flag and complete signatures must match.</summary>
public static class HostClockContracts
{
    public static string? Find(MethodReference method)
    {
        if (method.Assembly != "Transpiler.Bcl" ||
            method.Type != "[Transpiler.Bcl]Transpiler.Bcl.Tasks.HostClock" ||
            method.Instance || method.GenericArity != 0 || method.GenericArguments.Length != 0) return null;
        return (method.Name, method.ReturnType, string.Join(",", method.Parameters)) switch
        {
            ("Create", "System.Int32", "System.Int32,System.Action") => "clock.create",
            ("Change", "System.Void", "System.Int32,System.Int32") => "clock.change",
            ("HasFired", "System.Boolean", "System.Int32") => "clock.fired",
            ("Destroy", "System.Void", "System.Int32") => "clock.destroy",
            ("TakeReady", "System.Action", "") => "clock.take",
            ("Signal", "System.Void", "") => "clock.signal",
            _ => null
        };
    }
}
