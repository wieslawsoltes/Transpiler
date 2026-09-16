using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;

namespace Transpiler.Core;

/// <summary>Decodes ECMA-335 instruction bytes without loading or executing the input assembly.</summary>
public static class CilDecoder
{
    private static readonly Dictionary<ushort, OpCode> Codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => unchecked((ushort)o.Value));
    public static readonly IReadOnlyDictionary<string, OpCode> OpCodesByName = Codes.Values.ToDictionary(o => o.Name!);

    public static Instruction[] Decode(byte[] bytes, Func<OperandType, int, object> resolve)
    {
        var result = new List<Instruction>();
        var p = 0;
        int I4() { Need(4); var x = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(p)); p += 4; return x; }
        long I8() { Need(8); var x = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(p)); p += 8; return x; }
        int U1() { Need(1); return bytes[p++]; }
        int U2() { Need(2); var x = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(p)); p += 2; return x; }
        void Need(int n) { if (n < 0 || p > bytes.Length - n) throw new BadImageFormatException("Truncated CIL operand."); }
        while (p < bytes.Length)
        {
            var start = p;
            var value = (ushort)U1();
            if (value == 0xfe) value = (ushort)(0xfe00 | U1());
            if (!Codes.TryGetValue(value, out var code)) throw new BadImageFormatException($"Invalid opcode 0x{value:x4} at IL_{start:x4}.");
            object? operand = null;
            switch (code.OperandType)
            {
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineI: operand = (int)unchecked((sbyte)U1()); break;
                case OperandType.InlineI: operand = I4(); break;
                case OperandType.InlineI8: operand = I8(); break;
                case OperandType.ShortInlineR: operand = (double)BitConverter.Int32BitsToSingle(I4()); break;
                case OperandType.InlineR: operand = BitConverter.Int64BitsToDouble(I8()); break;
                case OperandType.ShortInlineVar: operand = U1(); break;
                case OperandType.InlineVar: operand = U2(); break;
                case OperandType.ShortInlineBrTarget: { var delta = unchecked((sbyte)U1()); operand = checked(p + delta); break; }
                case OperandType.InlineBrTarget: { var delta = I4(); operand = checked(p + delta); break; }
                case OperandType.InlineSwitch:
                    var count = I4();
                    if (count < 0 || count > (bytes.Length - p) / 4) throw new BadImageFormatException("Invalid switch table.");
                    var targets = new int[count];
                    for (var i = 0; i < count; i++) targets[i] = I4();
                    for (var i = 0; i < count; i++) targets[i] = checked(targets[i] + p);
                    operand = targets;
                    break;
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType: operand = resolve(code.OperandType, I4()); break;
                default: throw new BadImageFormatException($"Unsupported operand encoding {code.OperandType}.");
            }
            result.Add(new(start, p, code.Name!, operand));
        }
        return result.ToArray();
    }
}
