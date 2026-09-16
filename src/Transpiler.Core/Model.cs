using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json.Serialization;

namespace Transpiler.Core;

public sealed record Diagnostic(string Code, string Message, string? Method = null, int? Offset = null)
{
    public override string ToString() => $"{Code}: {Message}" +
        (Method is null ? "" : $" [{Method}" + (Offset is null ? "]" : $" IL_{Offset:x4}]") );
}

public sealed class CompilationException : Exception
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public CompilationException(params Diagnostic[] diagnostics) : base(string.Join("\n", diagnostics.Select(x => x.ToString()))) => Diagnostics = diagnostics;
}

public sealed record MethodReference(int Token, string Assembly, string Type, string Name,
    string[] Parameters, string ReturnType, bool Instance, int GenericArity)
{
    public string[] GenericArguments { get; init; } = [];
    public string Key => $"{Type}::{Name}" + (GenericArguments.Length == 0 ? "" : "<" + string.Join(",", GenericArguments) + ">") + $"({string.Join(",", Parameters)})";
}

public sealed record FieldReference(int Token, string Assembly, string Type, string Name, string FieldType)
{
    public string Key => $"{Type}::{Name}";
}

public sealed record Instruction(int Offset, int NextOffset, string Op, object? Operand)
{
    [JsonIgnore] public OpCode Code => CilDecoder.OpCodesByName[Op];
}

public sealed record ExceptionClause(string Kind, int TryStart, int TryEnd, int HandlerStart, int HandlerEnd, string? CatchType, int FilterStart);
public sealed record TypeDefinitionModel(string Name, string? BaseType, bool IsInterface, bool IsValueType,
    bool BeforeFieldInit, int GenericArity, string[] Interfaces)
{
    public int[] GenericVariance { get; init; } = [];
    public MethodOverride[] Overrides { get; init; } = [];
    public string? EnumUnderlyingType { get; init; }
    public bool EnumFlags { get; init; }
    public EnumValue[] EnumValues { get; init; } = [];
    public bool ExplicitLayout { get; init; }
    public string[] RuntimeObligations { get; init; } = [];
}
public sealed record EnumValue(string Name, string Value);
public sealed record MethodOverride(MethodReference Body, MethodReference Declaration);
public sealed record AssemblyIdentity(string Name, string Version, string Culture, string PublicKeyToken)
{
    public override string ToString() => $"{Name}, Version={Version}, Culture={Culture}, PublicKeyToken={PublicKeyToken}";
}
public sealed record AssemblyInput(string Identity, string Sha256);
public sealed record FieldDefinitionModel(FieldReference Reference, bool IsStatic, bool IsLiteral, object? Constant);
public sealed record MethodDefinitionModel(MethodReference Reference, bool IsPublic, bool IsVirtual, bool NewSlot,
    bool IsAbstract, bool IsPInvoke, bool InitLocals, int MaxStack, string[] Locals,
    Instruction[] Instructions, ExceptionClause[] Exceptions)
{
    [JsonIgnore] public string Key => Reference.Key;
    [JsonIgnore] public int Token => Reference.Token;
    [JsonIgnore] public bool IsStatic => !Reference.Instance;
}

public sealed record AssemblyModel(string Name, int EntryPoint, TypeDefinitionModel[] Types,
    MethodDefinitionModel[] Methods, FieldDefinitionModel[] Fields)
{
    public const int SchemaVersion = 2;
    public AssemblyIdentity Identity { get; init; } = new(Name, "0.0.0.0", "neutral", "null");
    public AssemblyIdentity[] References { get; init; } = [];
    public AssemblyInput[] Inputs { get; init; } = [];
    public string RootAssembly { get; init; } = Name;
    public bool IsReferenceAssembly { get; init; }
    public bool ContractsOnly { get; init; }
    public string[] ExportRoots { get; init; } = [];
    public string[] HostRoots { get; init; } = [];
    public MethodDefinitionModel? Resolve(MethodReference method) => Methods.FirstOrDefault(m =>
        m.Reference.Assembly == method.Assembly && m.Key == method.Key && m.Reference.ReturnType == method.ReturnType);
    public FieldDefinitionModel? Resolve(FieldReference field) => Fields.FirstOrDefault(f =>
        f.Reference.Assembly == field.Assembly && f.Reference.Key == field.Key && f.Reference.FieldType == field.FieldType);
    public TypeDefinitionModel? FindType(string name) => Types.FirstOrDefault(t => t.Name == name);
}

public static class CliTypes
{
    public static string StackKind(string type) => type switch
    {
        "System.Void" => "void",
        "System.IntPtr" => "fn",
        "System.Int64" or "System.UInt64" => "i8",
        "System.Single" or "System.Double" => "f",
        "System.Boolean" or "System.Char" or "System.SByte" or "System.Byte" or
        "System.Int16" or "System.UInt16" or "System.Int32" or "System.UInt32" => "i4",
        _ when type.EndsWith('&') => type,
        _ => "o"
    };

    public static bool IsPrimitive(string type) => type is "System.Void" or "System.Boolean" or
        "System.Char" or "System.SByte" or "System.Byte" or "System.Int16" or "System.UInt16" or
        "System.Int32" or "System.UInt32" or "System.Int64" or "System.UInt64" or "System.Double";
}
