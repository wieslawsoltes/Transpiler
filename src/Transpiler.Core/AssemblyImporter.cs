using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Transpiler.Core;

public static class AssemblyImporter
{
    public static AssemblyModel Read(byte[] image)
    {
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is not { } cor)
                throw new BadImageFormatException("A managed PE/CLI assembly is required.");
            if ((cor.Flags & CorFlags.ILOnly) == 0 || (cor.Flags & CorFlags.NativeEntryPoint) != 0)
                throw new CompilationException(new("TR1002", "Mixed-mode/native-entry assemblies are not supported by the portable profile."));
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) throw new BadImageFormatException("Standalone netmodules are not supported.");
            var provider = new Signatures(reader);
            var name = reader.GetString(reader.GetAssemblyDefinition().Name);
            string Scope(EntityHandle h)
            {
                if (h.Kind == HandleKind.TypeReference)
                {
                    var scope = reader.GetTypeReference((TypeReferenceHandle)h).ResolutionScope;
                    return scope.Kind == HandleKind.AssemblyReference ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
                        : scope.Kind == HandleKind.TypeReference ? Scope(scope) : name;
                }
                return h.Kind == HandleKind.TypeSpecification ? "<generic>" : name;
            }
            MethodReference Method(EntityHandle h)
            {
                var token = MetadataTokens.GetToken(h);
                if (h.Kind == HandleKind.MethodSpecification)
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)h);
                    return Method(spec.Method) with { Token = token, GenericArity = spec.DecodeSignature(provider, null).Length };
                }
                if (h.Kind == HandleKind.MethodDefinition)
                {
                    var d = reader.GetMethodDefinition((MethodDefinitionHandle)h);
                    var sig = d.DecodeSignature(provider, null);
                    return new(token, name, provider.TypeName(d.GetDeclaringType()), reader.GetString(d.Name),
                        sig.ParameterTypes.ToArray(), sig.ReturnType, sig.Header.IsInstance, sig.GenericParameterCount);
                }
                var member = reader.GetMemberReference((MemberReferenceHandle)h);
                var signature = member.DecodeMethodSignature(provider, null);
                return new(token, Scope(member.Parent), provider.TypeName(member.Parent), reader.GetString(member.Name),
                    signature.ParameterTypes.ToArray(), signature.ReturnType, signature.Header.IsInstance, signature.GenericParameterCount);
            }
            FieldReference Field(EntityHandle h)
            {
                var token = MetadataTokens.GetToken(h);
                if (h.Kind == HandleKind.FieldDefinition)
                {
                    var d = reader.GetFieldDefinition((FieldDefinitionHandle)h);
                    return new(token, name, provider.TypeName(d.GetDeclaringType()), reader.GetString(d.Name), d.DecodeSignature(provider, null));
                }
                var member = reader.GetMemberReference((MemberReferenceHandle)h);
                return new(token, Scope(member.Parent), provider.TypeName(member.Parent), reader.GetString(member.Name), member.DecodeFieldSignature(provider, null));
            }
            object Resolve(OperandType kind, int token) => kind switch
            {
                OperandType.InlineString => reader.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)),
                OperandType.InlineMethod => Method(MetadataTokens.EntityHandle(token)),
                OperandType.InlineField => Field(MetadataTokens.EntityHandle(token)),
                OperandType.InlineType => provider.TypeName(MetadataTokens.EntityHandle(token)),
                _ => token // Unsupported signatures/tokens stay visible for capability diagnostics.
            };
            var types = new List<TypeDefinitionModel>();
            var methods = new List<MethodDefinitionModel>();
            var fields = new List<FieldDefinitionModel>();
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                var typeName = provider.TypeName(handle);
                var baseName = type.BaseType.IsNil ? null : provider.TypeName(type.BaseType);
                types.Add(new(typeName, baseName, (type.Attributes & TypeAttributes.Interface) != 0,
                    baseName is "System.ValueType" or "System.Enum", (type.Attributes & TypeAttributes.BeforeFieldInit) != 0,
                    type.GetGenericParameters().Count, type.GetInterfaceImplementations().Select(i =>
                        provider.TypeName(reader.GetInterfaceImplementation(i).Interface)).ToArray()));
                foreach (var f in type.GetFields())
                {
                    var definition = reader.GetFieldDefinition(f);
                    // Literal constants are represented, but field-RVA storage is deliberately not materialized.
                    fields.Add(new(Field(f), (definition.Attributes & FieldAttributes.Static) != 0,
                        (definition.Attributes & FieldAttributes.Literal) != 0, null));
                }
                foreach (var m in type.GetMethods())
                {
                    var definition = reader.GetMethodDefinition(m);
                    var reference = Method(m);
                    var body = definition.RelativeVirtualAddress == 0 ? null : pe.GetMethodBody(definition.RelativeVirtualAddress);
                    var locals = body is null || body.LocalSignature.IsNil ? [] :
                        reader.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(provider, null).ToArray();
                    var exceptions = body?.ExceptionRegions.Select(e => new ExceptionClause(e.Kind.ToString(), e.TryOffset,
                        e.TryOffset + e.TryLength, e.HandlerOffset, e.HandlerOffset + e.HandlerLength,
                        e.Kind == ExceptionRegionKind.Catch ? provider.TypeName(e.CatchType) : null, e.FilterOffset)).ToArray() ?? [];
                    methods.Add(new(reference, (definition.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
                        (definition.Attributes & MethodAttributes.Virtual) != 0, (definition.Attributes & MethodAttributes.NewSlot) != 0,
                        (definition.Attributes & MethodAttributes.Abstract) != 0, (definition.Attributes & MethodAttributes.PinvokeImpl) != 0,
                        body?.LocalVariablesInitialized ?? true, body?.MaxStack ?? 0, locals,
                        body is null ? [] : CilDecoder.Decode(body.GetILBytes() ?? [], Resolve), exceptions));
                }
            }
            return new(name, cor.EntryPointTokenOrRelativeVirtualAddress, types.ToArray(), methods.ToArray(), fields.ToArray());
        }
        catch (CompilationException) { throw; }
        catch (Exception e) when (e is BadImageFormatException or ArgumentException or InvalidOperationException or OverflowException or IndexOutOfRangeException)
        { throw new CompilationException(new("TR1001", $"Invalid or unsupported PE/metadata: {e.Message}")); }
    }

    private sealed class Signatures(MetadataReader reader) : ISignatureTypeProvider<string, object?>
    {
        public string TypeName(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(reader, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(reader, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null),
            _ => $"<unsupported:{handle.Kind}>"
        };
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte rawTypeKind)
        {
            var t = r.GetTypeDefinition(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace);
            return !t.GetDeclaringType().IsNil ? TypeName(t.GetDeclaringType()) + "+" + n : ns.Length == 0 ? n : ns + "." + n;
        }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte rawTypeKind)
        {
            var t = r.GetTypeReference(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace);
            return t.ResolutionScope.Kind == HandleKind.TypeReference ? TypeName((EntityHandle)t.ResolutionScope) + "+" + n : ns.Length == 0 ? n : ns + "." + n;
        }
        public string GetPrimitiveType(PrimitiveTypeCode code) => "System." + code;
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[rank=" + shape.Rank + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPinnedType(string elementType) => "pinned " + elementType;
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => (isRequired ? "modreq(" + modifier + ") " : "") + unmodifiedType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "method*";
        public string GetTypeFromSpecification(MetadataReader r, object? context, TypeSpecificationHandle h, byte rawTypeKind) => r.GetTypeSpecification(h).DecodeSignature(this, context);
    }
}
