using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class SsaFixture
{
    public static void Write(string path, bool library = false)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("SsaFixture"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("Main").DefineType("Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var main = type.DefineMethod("Main", (library ? MethodAttributes.Private : MethodAttributes.Public) | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        var m = main.GetILGenerator();
        void Print(MethodBuilder method, Type result)
        { m.Emit(OpCodes.Call, method); m.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [result])!); }
        var loop = type.DefineMethod("Loop", MethodAttributes.Public | MethodAttributes.Static, typeof(int), Type.EmptyTypes);
        var l = loop.GetILGenerator(); var counter = l.DeclareLocal(typeof(int)); var head = l.DefineLabel();
        l.Emit(OpCodes.Ldc_I4, 7); l.Emit(OpCodes.Stloc, counter); l.Emit(OpCodes.Ldc_I4_0); l.Emit(OpCodes.Br, head);
        l.MarkLabel(head); l.Emit(OpCodes.Ldloc, counter); l.Emit(OpCodes.Add);
        l.Emit(OpCodes.Ldloc, counter); l.Emit(OpCodes.Ldc_I4_1); l.Emit(OpCodes.Sub); l.Emit(OpCodes.Dup);
        l.Emit(OpCodes.Stloc, counter); l.Emit(OpCodes.Brtrue, head); l.Emit(OpCodes.Ret);
        Print(loop, typeof(int));
        foreach (var op in new[] { OpCodes.Add_Ovf, OpCodes.Div, OpCodes.Rem })
        {
            var method = type.DefineMethod("Unused" + op.Name!.Replace('.', '_'), MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, op == OpCodes.Add_Ovf ? int.MaxValue : 12);
            il.Emit(OpCodes.Ldc_I4, op == OpCodes.Add_Ovf ? 1 : 0);
            il.Emit(op); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ret);
            m.BeginExceptionBlock(); m.Emit(OpCodes.Call, method); m.Emit(OpCodes.Ldstr, "MISSING exception");
            m.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!);
            m.BeginCatchBlock(typeof(Exception)); m.Emit(OpCodes.Pop); m.Emit(OpCodes.Ldstr, "caught " + op.Name);
            m.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(string)])!); m.EndExceptionBlock(); m.Emit(OpCodes.Nop);
        }
        var random = new Random(0x535341);
        var operators = new[] { OpCodes.Add, OpCodes.Sub, OpCodes.Mul, OpCodes.And, OpCodes.Or, OpCodes.Xor, OpCodes.Shl, OpCodes.Shr, OpCodes.Shr_Un };
        for (var number = 0; number < 128; number++)
        {
            var wide = number >= 64;
            var method = type.DefineMethod("Random" + number, MethodAttributes.Public | MethodAttributes.Static,
                wide ? typeof(long) : typeof(int), Type.EmptyTypes);
            var il = method.GetILGenerator();
            void Constant() { if (wide) il.Emit(OpCodes.Ldc_I8, random.NextInt64(long.MinValue, long.MaxValue)); else il.Emit(OpCodes.Ldc_I4, unchecked((int)random.NextInt64())); }
            Constant();
            for (var step = 0; step < 12; step++)
            {
                var op = operators[random.Next(operators.Length)];
                if (op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un) il.Emit(OpCodes.Ldc_I4, random.Next(-130, 130));
                else Constant();
                il.Emit(op);
                if (step % 4 == 0) il.Emit(OpCodes.Neg);
                if (step % 5 == 0) il.Emit(OpCodes.Not);
            }
            il.Emit(OpCodes.Ret); Print(method, wide ? typeof(long) : typeof(int));
        }
        m.Emit(OpCodes.Ret); type.CreateType();
        var metadata = assembly.GenerateMetadata(out var ilStream, out var fieldData);
        var pe = new ManagedPEBuilder(library ? PEHeaderBuilder.CreateLibraryHeader() : PEHeaderBuilder.CreateExecutableHeader(), new MetadataRootBuilder(metadata), ilStream,
            mappedFieldData: fieldData, entryPoint: library ? default : MetadataTokens.MethodDefinitionHandle(main.MetadataToken));
        var blob = new BlobBuilder(); pe.Serialize(blob);
        using (var output = File.Create(path)) blob.WriteContentTo(output);
        File.WriteAllText(Path.ChangeExtension(path, "runtimeconfig.json"),
            "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}");
    }
}
