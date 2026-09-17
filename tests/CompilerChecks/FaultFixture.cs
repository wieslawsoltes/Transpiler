using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal static class FaultFixture
{
    // A real persisted assembly tests clauses safe C# cannot express, without asking the compiler
    // under test to synthesize its own expected IL. This producer runs only in the test process.
    public static void Write(string path)
    {
        var assembly = new PersistedAssemblyBuilder(new AssemblyName("FaultFixture"), typeof(object).Assembly);
        var type = assembly.DefineDynamicModule("Main").DefineType("Program", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var print = typeof(Console).GetMethod("WriteLine", [typeof(string)])!;
        void Text(ILGenerator il, string message) { il.Emit(OpCodes.Ldstr, message); il.Emit(OpCodes.Call, print); }
        MethodBuilder Leaf(string name, bool throws)
        {
            var method = type.DefineMethod(name, MethodAttributes.Private | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
            var il = method.GetILGenerator();
            il.BeginExceptionBlock();
            if (throws) { il.Emit(OpCodes.Newobj, typeof(ArgumentException).GetConstructor(Type.EmptyTypes)!); il.Emit(OpCodes.Throw); }
            else Text(il, "normal path");
            il.BeginFaultBlock();
            Text(il, throws ? "fault during unwind" : "unreachable fault");
            il.EndExceptionBlock(); il.Emit(OpCodes.Ret);
            return method;
        }
        var fault = Leaf("Fault", true);
        var normal = Leaf("Normal", false);
        var main = type.DefineMethod("Main", MethodAttributes.Public | MethodAttributes.Static, typeof(void), Type.EmptyTypes);
        main.InitLocals = false;
        var m = main.GetILGenerator();
        var local = m.DeclareLocal(typeof(int));
        m.Emit(OpCodes.Ldc_I4, 41); m.Emit(OpCodes.Stloc, local);
        m.Emit(OpCodes.Call, normal);
        m.BeginExceptionBlock(); m.Emit(OpCodes.Call, fault);
        m.BeginExceptFilterBlock(); m.Emit(OpCodes.Pop);
        Text(m, "filter before fault");
        m.Emit(OpCodes.Ldloc, local); m.Emit(OpCodes.Ldc_I4_1); m.Emit(OpCodes.Add); m.Emit(OpCodes.Stloc, local);
        m.Emit(OpCodes.Ldc_I4_1);
        m.BeginCatchBlock(null); m.Emit(OpCodes.Pop); Text(m, "filtered catch"); m.EndExceptionBlock();
        m.Emit(OpCodes.Ldloc, local); m.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", [typeof(int)])!);
        m.Emit(OpCodes.Ret);
        type.CreateType();
        var metadata = assembly.GenerateMetadata(out var ilStream, out var fieldData);
        var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateExecutableHeader(), new MetadataRootBuilder(metadata), ilStream,
            mappedFieldData: fieldData, entryPoint: MetadataTokens.MethodDefinitionHandle(main.MetadataToken));
        var blob = new BlobBuilder(); pe.Serialize(blob);
        using (var output = File.Create(path)) blob.WriteContentTo(output);
        File.WriteAllText(Path.ChangeExtension(path, "runtimeconfig.json"),
            "{\"runtimeOptions\":{\"tfm\":\"net10.0\",\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}");
    }
}
