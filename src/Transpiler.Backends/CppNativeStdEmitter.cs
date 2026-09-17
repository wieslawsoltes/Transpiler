using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Transpiler.Core;

namespace Transpiler.Backends;

public sealed record NativeExport(string Method, string Symbol, string ReturnType, string[] Parameters);

/// <summary>
/// Closed-world integral-library C++20 profile. This is not cpp-managed: no object representation,
/// GC, managed exception handlers, native layout, platform interop or ownership inference is assumed.
/// </summary>
public static class CppNativeStdEmitter
{
    public const string Profile = "native-std-scalar-v1";
    private static readonly Dictionary<string, string> AbiTypes = new(StringComparer.Ordinal)
    {
        ["System.Void"] = "void", ["System.Boolean"] = "bool", ["System.Char"] = "std::uint16_t",
        ["System.SByte"] = "std::int8_t", ["System.Byte"] = "std::uint8_t",
        ["System.Int16"] = "std::int16_t", ["System.UInt16"] = "std::uint16_t",
        ["System.Int32"] = "std::int32_t", ["System.UInt32"] = "std::uint32_t",
        ["System.Int64"] = "std::int64_t", ["System.UInt64"] = "std::uint64_t"
    };
    private static readonly Dictionary<string, string> ConversionTypes = new(StringComparer.Ordinal)
    {
        ["i1"] = "std::int8_t", ["u1"] = "std::uint8_t", ["i2"] = "std::int16_t", ["u2"] = "std::uint16_t",
        ["i4"] = "std::int32_t", ["u4"] = "std::uint32_t", ["i8"] = "std::int64_t", ["u8"] = "std::uint64_t"
    };
    private static readonly HashSet<string> Opcodes = new(("nop ldarg starg ldloc stloc ldc.i4 ldc.i8 dup pop " +
        "add sub mul div div.un rem rem.un neg not and or xor shl shr shr.un add.ovf add.ovf.un sub.ovf sub.ovf.un mul.ovf mul.ovf.un " +
        "ceq cgt cgt.un clt clt.un br brtrue brfalse beq bne.un bge bge.un bgt bgt.un ble ble.un blt blt.un switch ret call").Split(' '));
    public static IReadOnlyCollection<string> SupportedTypes => AbiTypes.Keys.Order(StringComparer.Ordinal).ToArray();
    public static IReadOnlyCollection<string> SupportedOpcodes => Opcodes.Concat(ConversionTypes.Keys.SelectMany(k =>
        new[] { "conv." + k, "conv.ovf." + k, "conv.ovf." + k + ".un" })).Order(StringComparer.Ordinal).ToArray();

    public static GeneratedSource Emit(CompilationAnalysis input)
    {
        var analysis = StackSsa.Prepare(input);
        var allowed = SupportedOpcodes.ToHashSet(StringComparer.Ordinal);
        var errors = new List<Diagnostic>();
        if (analysis.Assembly.EntryPoint != 0) errors.Add(new("TR2300", Profile + " accepts managed libraries only; compile source with --library."));
        foreach (var item in analysis.Methods)
        {
            var method = item.Method;
            void Reject(string message, int? offset = null) => errors.Add(new("TR2300", message, method.Key, offset));
            if (!method.IsStatic || method.IsPInvoke || method.IsAbstract || method.Reference.Name == ".cctor" || item.Ssa is null)
                Reject("Native scalar methods must be static managed bodies without type initialization or protected regions: " + item.SsaExclusion);
            if (!AbiTypes.ContainsKey(method.Reference.ReturnType)) Reject("Unsupported native scalar return type: " + method.Reference.ReturnType);
            foreach (var type in method.Reference.Parameters.Concat(method.Locals))
                if (type == "System.Void" || !AbiTypes.ContainsKey(type)) Reject("Unsupported native scalar storage type: " + type);
            if (item.StackBefore.Values.SelectMany(v => v).Any(k => k is not ("i4" or "i8"))) Reject("Native scalar evaluation stacks must contain only i4/i8 values.");
            // Check the entire reachable method body BEFORE DCE, not merely surviving operations.
            foreach (var instruction in method.Instructions)
            {
                if (!allowed.Contains(instruction.Op)) Reject("Opcode is outside " + Profile + ": " + instruction.Op, instruction.Offset);
                if (instruction.Op == "call" && instruction.Operand is MethodReference call &&
                    (call.Instance || analysis.Assembly.Resolve(call) is not { IsStatic: true }))
                    Reject("Only closed-world managed static calls are supported; no intrinsic/native callback fallback.", instruction.Offset);
            }
        }
        if (errors.Count != 0) throw new CompilationException(errors.ToArray());
        var module = "a_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(analysis.Assembly.Identity.ToString())))[..16].ToLowerInvariant();
        var ns = "transpiler_generated::" + module;
        var exports = analysis.Methods.Where(m => analysis.Exports.Contains(m.Method.Key, StringComparer.Ordinal)).Select(m =>
            new NativeExport(m.Method.Key, ns + "::export_" + Identifier(m.Method.Reference.Name) + "_" + Id(m.Method.Token),
                AbiTypes[m.Method.Reference.ReturnType], m.Method.Reference.Parameters.Select(p => AbiTypes[p]).ToArray())).ToArray();
        var output = new StringBuilder();
        output.AppendLine("// Generated by Transpiler. Profile: " + Profile + "; C++20; integral libraries only.");
        output.AppendLine("#pragma once\n#include <bit>\n#include <cstdint>\n#include <limits>\n#include <stdexcept>\n#include <type_traits>");
        output.AppendLine("namespace " + ns + " {\nnamespace detail {");
        output.AppendLine(CppNativeRuntime.Source);
        foreach (var method in analysis.Methods) output.AppendLine(Signature(method.Method) + ";");
        foreach (var method in analysis.Methods) EmitMethod(output, method, analysis.Assembly);
        output.AppendLine("} // namespace detail");
        foreach (var export in exports)
        {
            var method = analysis.Methods.Single(m => m.Method.Key == export.Method).Method;
            var name = export.Symbol[(export.Symbol.LastIndexOf("::", StringComparison.Ordinal) + 2)..];
            output.AppendLine("// " + System.Text.Json.JsonSerializer.Serialize(method.Key));
            output.AppendLine("inline " + export.ReturnType + " " + name + "(" + string.Join(", ", export.Parameters.Select((t, i) => t + " a" + i)) + ") {");
            var arguments = method.Reference.Parameters.Select((p, i) => IntoStack("a" + i, p));
            var call = "detail::m_" + Id(method.Token) + "(" + string.Join(", ", arguments) + ")";
            output.AppendLine("    " + (method.Reference.ReturnType == "System.Void" ? call : "return " + FromStack(call, method.Reference.ReturnType)) + ";\n}");
        }
        output.AppendLine("} // namespace " + ns);
        return new(output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal), ".hpp", analysis.Methods.Length, analysis.Methods.Sum(m => m.StackBefore.Count))
        {
            Profile = Profile, Dispatch = DispatchMode.StackSsa, DispatchCaseCount = analysis.Methods.Sum(m => m.Ssa!.Blocks.Length),
            SsaMethodCount = analysis.Methods.Length, SsaEliminatedInstructions = analysis.Methods.Sum(m => m.Ssa!.EliminatedInstructions), NativeExports = exports
        };
    }

    private static string Identifier(string name)
    {
        var result = new StringBuilder();
        foreach (var c in name.Take(80))
            if (char.IsAsciiLetterOrDigit(c)) result.Append(c);
            else if (result.Length != 0 && result[^1] != '_') result.Append('_');
        return result.Length == 0 ? "method" : result.ToString().TrimEnd('_');
    }
    private static string Id(int token) => unchecked((uint)token).ToString("x8", CultureInfo.InvariantCulture);
    private static string StackType(string type) => type == "System.Void" ? "void" : KindType(CliTypes.StackKind(type));
    private static string KindType(string kind) => kind == "i8" ? "std::int64_t" : "std::int32_t";
    private static string Signature(MethodDefinitionModel method) => "inline " + StackType(method.Reference.ReturnType) + " m_" + Id(method.Token) +
        "(" + string.Join(", ", method.Reference.Parameters.Select((t, i) => "[[maybe_unused]] " + StackType(t) + " a" + i)) + ")";
    private static string IntoStack(string expression, string type) => type switch
    {
        "System.UInt32" => "detail::bits<std::int32_t>(" + expression + ")",
        "System.UInt64" => "detail::bits<std::int64_t>(" + expression + ")",
        _ => "static_cast<" + StackType(type) + ">(" + expression + ")"
    };
    private static string FromStack(string expression, string type) => type is "System.UInt32" or "System.UInt64"
        ? "detail::uns(" + expression + ")" : "static_cast<" + AbiTypes[type] + ">(" + expression + ")";
    private static string Coerce(string expression, string type) => type switch
    {
        "System.Boolean" => "static_cast<std::int32_t>((" + expression + ") != 0)",
        "System.SByte" or "System.Byte" or "System.Char" or "System.Int16" or "System.UInt16" => "convert<" + AbiTypes[type] + ", false, false>(" + expression + ")",
        _ => expression
    };
    private static string Literal(long value, string kind) => kind == "i8"
        ? "bits<std::int64_t>(std::uint64_t{0x" + unchecked((ulong)value).ToString("x16", CultureInfo.InvariantCulture) + "ULL})"
        : "bits<std::int32_t>(std::uint32_t{0x" + unchecked((uint)value).ToString("x8", CultureInfo.InvariantCulture) + "U})";

    private static void EmitMethod(StringBuilder output, MethodAnalysis analysis, AssemblyModel assembly)
    {
        var method = analysis.Method; var graph = analysis.Ssa!;
        var live = graph.LiveValues.ToHashSet(); var active = graph.ActiveOffsets.ToHashSet();
        string Value(int id) => graph.Constants.TryGetValue(id, out var constant) ? Literal(constant.Value, constant.Kind) : "v" + id;
        void Line(string text) => output.AppendLine("                " + text);
        output.AppendLine("// " + System.Text.Json.JsonSerializer.Serialize(method.Key) + " [native-stack-ssa]");
        output.AppendLine(Signature(method) + " {");
        foreach (var local in method.Locals.Select((type, i) => (type, i))) output.AppendLine("    [[maybe_unused]] " + StackType(local.type) + " l" + local.i + "{};");
        foreach (var value in graph.Values.Where(v => live.Contains(v.Id))) output.AppendLine("    [[maybe_unused]] " + KindType(value.Kind) + " v" + value.Id + "{};");
        output.AppendLine("    int pc = " + graph.Blocks[0].Start + ";\n    for (;;) {\n        switch (pc) {");
        foreach (var block in graph.Blocks)
        {
            output.AppendLine("            case " + block.Start + ": {");
            var terminated = false;
            foreach (var operation in block.Operations)
            {
                if (!active.Contains(operation.Instruction.Offset)) continue;
                var i = operation.Instruction; var op = i.Op;
                var arguments = operation.Inputs.Select(Value).ToArray();
                var kind = operation.Inputs.Length == 0 ? "i4" : graph.Values[operation.Inputs[0]].Kind;
                string Input(int n) => arguments[n];
                void Result(string expression)
                {
                    if (operation.Result is { } result && live.Contains(result)) Line("v" + result + " = " + expression + ";");
                    else Line("(void)(" + expression + ");");
                }
                string? expression = op switch
                {
                    "ldarg" => "a" + i.Operand, "ldloc" => "l" + i.Operand,
                    "ldc.i4" => Literal((int)i.Operand!, "i4"), "ldc.i8" => Literal((long)i.Operand!, "i8"), "dup" => Input(0),
                    "neg" => "neg(" + Input(0) + ")", "not" => "bit_not(" + Input(0) + ")",
                    "add" or "sub" or "mul" or "shl" or "shr" => op + "(" + string.Join(", ", arguments) + ")",
                    "shr.un" => "shr_u(" + string.Join(", ", arguments) + ")",
                    "and" or "or" or "xor" => "bit_" + op + "(" + string.Join(", ", arguments) + ")",
                    "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un" => "static_cast<std::int32_t>(" + Compare(op, Input(0), Input(1)) + ")",
                    _ => null
                };
                if (expression is not null) { Result(expression); continue; }
                if (op.StartsWith("conv.", StringComparison.Ordinal))
                {
                    var checkedConversion = op.StartsWith("conv.ovf.", StringComparison.Ordinal);
                    var unsigned = op.EndsWith(".un", StringComparison.Ordinal);
                    var key = op[(checkedConversion ? 9 : 5)..]; if (unsigned) key = key[..^3];
                    Result("convert<" + ConversionTypes[key] + ", " + (checkedConversion ? "true" : "false") + ", " + (unsigned ? "true" : "false") + ">(" + Input(0) + ")"); continue;
                }
                if (op.StartsWith("add.ovf", StringComparison.Ordinal) || op.StartsWith("sub.ovf", StringComparison.Ordinal) || op.StartsWith("mul.ovf", StringComparison.Ordinal))
                {
                    Result(op[..3] + "_checked<" + KindType(kind) + ", " + (op.EndsWith(".un", StringComparison.Ordinal) ? "true" : "false") + ">(" + string.Join(", ", arguments) + ")"); continue;
                }
                if (op is "div" or "div.un" or "rem" or "rem.un")
                {
                    Result("divide<" + KindType(kind) + ", " + (op.EndsWith(".un", StringComparison.Ordinal) ? "true" : "false") + ", " + (op.StartsWith("rem", StringComparison.Ordinal) ? "true" : "false") + ">(" + string.Join(", ", arguments) + ")"); continue;
                }
                switch (op)
                {
                    case "starg": Line("a" + i.Operand + " = " + Coerce(Input(0), method.Reference.Parameters[(int)i.Operand!]) + ";"); break;
                    case "stloc": Line("l" + i.Operand + " = " + Coerce(Input(0), method.Locals[(int)i.Operand!]) + ";"); break;
                    case "call":
                        var call = (MethodReference)i.Operand!; var target = assembly.Resolve(call)!;
                        Result("m_" + Id(target.Token) + "(" + string.Join(", ", arguments.Select((a, n) => Coerce(a, call.Parameters[n]))) + ")"); break;
                    case "ret": Line(method.Reference.ReturnType == "System.Void" ? "return;" : "return " + Coerce(Input(0), method.Reference.ReturnType) + ";"); terminated = true; break;
                    case "br": Line("pc = " + i.Operand + ";"); break;
                    case "brtrue": case "brfalse": Line("pc = (" + Input(0) + (op == "brtrue" ? " != 0" : " == 0") + ") ? " + i.Operand + " : " + i.NextOffset + ";"); break;
                    case "beq": case "bne.un": case "bge": case "bge.un": case "bgt": case "bgt.un": case "ble": case "ble.un": case "blt": case "blt.un":
                        Line("pc = (" + Compare(op, Input(0), Input(1)) + ") ? " + i.Operand + " : " + i.NextOffset + ";"); break;
                    case "switch":
                        Line("switch (uns(" + Input(0) + ")) {");
                        foreach (var pair in ((int[])i.Operand!).Select((target, n) => (target, n))) Line("case " + pair.n + ": pc = " + pair.target + "; break;");
                        Line("default: pc = " + i.NextOffset + "; break; }"); break;
                    case "nop": case "pop": break;
                    default: throw new InvalidOperationException("Unmodelled native operation: " + op);
                }
            }
            if (!terminated)
            {
                var tail = block.Operations[^1].Instruction;
                if (tail.Code.FlowControl is not (System.Reflection.Emit.FlowControl.Branch or System.Reflection.Emit.FlowControl.Cond_Branch)) Line("pc = " + tail.NextOffset + ";");
                foreach (var target in block.Successors)
                {
                    var destination = graph.Blocks.Single(b => b.Start == target);
                    var moves = destination.Parameters.Select((value, slot) => (value, slot)).Where(p => live.Contains(p.value)).ToArray();
                    if (moves.Length == 0) continue;
                    Line("if (pc == " + target + ") {");
                    foreach (var move in moves) Line("    const auto p" + move.slot + " = " + Value(block.ExitValues[move.slot]) + ";");
                    foreach (var move in moves) Line("    v" + move.value + " = p" + move.slot + ";");
                    Line("}");
                }
                Line("continue;");
            }
            output.AppendLine("            }");
        }
        output.AppendLine("            default: throw std::logic_error(\"Invalid translated control flow\");\n        }\n    }\n}");
    }

    private static string Compare(string op, string left, string right)
    {
        if (op.EndsWith(".un", StringComparison.Ordinal)) { left = "uns(" + left + ")"; right = "uns(" + right + ")"; }
        var symbol = op.Split('.')[0] switch { "ceq" or "beq" => "==", "bne" => "!=", "cgt" or "bgt" => ">", "clt" or "blt" => "<", "bge" => ">=", "ble" => "<=", _ => throw new InvalidOperationException(op) };
        return left + " " + symbol + " " + right;
    }
}
