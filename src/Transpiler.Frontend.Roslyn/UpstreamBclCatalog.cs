using System.Collections.Immutable;
using Transpiler.Core;

namespace Transpiler.Frontend.Roslyn;

/// <summary>An exact, reviewed contract for original implementation IL, never reference-assembly stubs.</summary>
public sealed record UpstreamMethod(string Type, string Name, string ReturnType, ImmutableArray<string> Parameters)
{
    public string Signature => $"{Type}::{Name}({string.Join(",", Parameters)})->{ReturnType}";
    public bool Matches(MethodReference method) => !method.Instance && method.GenericArity == 0 &&
        method.Type == Type && method.Name == Name && method.ReturnType == ReturnType &&
        method.Parameters.SequenceEqual(Parameters);
}

/// <summary>
/// This catalog selects original .NET method bodies; the ordinary compiler checks their dependency closure.
/// Registering an entry does not allow its declaring type's other members or hide unsupported runtime calls.
/// </summary>
public static class UpstreamBclCatalog
{
    public const string Policy = "corelib-integer-v1";
    public static ImmutableArray<UpstreamMethod> Methods { get; } = Build();

    private static ImmutableArray<UpstreamMethod> Build()
    {
        var entries = ImmutableArray.CreateBuilder<UpstreamMethod>();
        entries.Add(new("System.Math", "BigMul", "System.Int64", ["System.Int32", "System.Int32"]));
        foreach (var type in new[] { "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64" })
            foreach (var name in new[] { "Min", "Max" })
                entries.Add(new("System.Math", name, "System." + type, ["System." + type, "System." + type]));
        foreach (var type in new[] { "System.Int32", "System.Int64" })
        {
            entries.Add(new("System.Math", "DivRem", type, [type, type, type + "&"]));
            entries.Add(new("System.Math", "Sign", "System.Int32", [type]));
        }
        return entries.ToImmutable();
    }

    public static AssemblyModel Import(byte[] implementation)
    {
        var assembly = AssemblyImporter.Read(implementation,
            type => Methods.Any(m => m.Type == type), method => Methods.Any(m => m.Matches(method)));
        if (assembly.Name != "System.Private.CoreLib" || assembly.IsReferenceAssembly)
            throw new CompilationException(new Diagnostic("TR3200", "Original BCL input must be a System.Private.CoreLib implementation assembly."));
        var errors = Methods.Where(contract => !assembly.Methods.Any(m => contract.Matches(m.Reference) &&
            m.Instructions.Length > 0 && !m.IsAbstract && !m.IsPInvoke))
            .Select(contract => new Diagnostic("TR3200", $"The selected CoreLib is missing a managed implementation for '{contract.Signature}'."))
            .ToArray();
        if (errors.Length != 0) throw new CompilationException(errors);
        return assembly;
    }
}
