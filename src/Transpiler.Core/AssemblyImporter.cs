using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Transpiler.Core;

/// <summary>Imports CLI metadata and CIL without loading the input into the compiler's CLR.</summary>
public static class AssemblyImporter
{
    public static AssemblyModel Read(byte[] image, Func<string, bool>? includeType = null, Func<MethodReference, bool>? includeMethod = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata || pe.PEHeaders.CorHeader is not { } cor)
                throw new BadImageFormatException("A managed PE/CLI assembly is required.");
            // Selected managed bodies may be imported from a ReadyToRun implementation image.
            // Native code is never executed; unrestricted mixed-mode input remains rejected.
            var selectedReadyToRun = includeType is not null && includeMethod is not null &&
                cor.ManagedNativeHeaderDirectory.Size >= 4 &&
                pe.GetSectionData(cor.ManagedNativeHeaderDirectory.RelativeVirtualAddress).GetReader().ReadUInt32() == 0x00525452;
            if (((cor.Flags & CorFlags.ILOnly) == 0 && !selectedReadyToRun) || (cor.Flags & CorFlags.NativeEntryPoint) != 0)
                throw new CompilationException(new Diagnostic("TR1002", "Mixed-mode/native-entry assemblies are not supported by the portable profile."));
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) throw new BadImageFormatException("Standalone netmodules are not supported.");
            var name = reader.GetString(reader.GetAssemblyDefinition().Name);
            var provider = new Signatures(reader, name);
            string Scope(EntityHandle h)
            {
                if (h.Kind == HandleKind.TypeReference)
                {
                    var scope = reader.GetTypeReference((TypeReferenceHandle)h).ResolutionScope;
                    return scope.Kind == HandleKind.AssemblyReference ? reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)scope).Name)
                        : scope.Kind == HandleKind.TypeReference ? Scope(scope) : name;
                }
                if (h.Kind == HandleKind.TypeSpecification)
                {
                    var type = provider.TypeName(h);
                    return type.StartsWith('[') ? type[1..type.IndexOf(']')] : "System.Private.CoreLib";
                }
                return name;
            }
            MethodReference Method(EntityHandle h)
            {
                var token = MetadataTokens.GetToken(h);
                if (h.Kind == HandleKind.MethodSpecification)
                {
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)h);
                    return Method(spec.Method) with { Token = token, GenericArguments = spec.DecodeSignature(provider, null).ToArray() };
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
                OperandType.InlineTok when MetadataTokens.EntityHandle(token).Kind == HandleKind.FieldDefinition => Field(MetadataTokens.EntityHandle(token)),
                OperandType.InlineTok when MetadataTokens.EntityHandle(token).Kind is HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification => provider.TypeName(MetadataTokens.EntityHandle(token)),
                OperandType.InlineType => provider.TypeName(MetadataTokens.EntityHandle(token)),
                _ => token
            };
            string EnumConstant(FieldDefinition field)
            {
                var constant = reader.GetConstant(field.GetDefaultValue());
                var blob = reader.GetBlobReader(constant.Value);
                object value = constant.TypeCode switch
                {
                    ConstantTypeCode.SByte => blob.ReadSByte(), ConstantTypeCode.Byte => blob.ReadByte(),
                    ConstantTypeCode.Int16 => blob.ReadInt16(), ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                    ConstantTypeCode.Int32 => blob.ReadInt32(), ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                    ConstantTypeCode.Int64 => blob.ReadInt64(), ConstantTypeCode.UInt64 => blob.ReadUInt64(),
                    _ => throw new BadImageFormatException("Invalid enum constant encoding.")
                };
                return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
            }
            var types = new List<TypeDefinitionModel>();
            var methods = new List<MethodDefinitionModel>();
            var fields = new List<FieldDefinitionModel>();
            var totalData = 0;
            byte[]? ReadData(FieldDefinition field)
            {
                var rva = field.GetRelativeVirtualAddress();
                if (rva == 0) return null;
                var type = field.DecodeSignature(provider, null);
                var size = type switch
                {
                    "System.Boolean" or "System.Byte" or "System.SByte" => 1,
                    "System.Char" or "System.Int16" or "System.UInt16" => 2,
                    "System.Int32" or "System.UInt32" or "System.Single" => 4,
                    "System.Int64" or "System.UInt64" or "System.Double" => 8,
                    _ => reader.TypeDefinitions.Where(h => provider.TypeName(h) == type)
                        .Select(h => reader.GetTypeDefinition(h).GetLayout().Size).FirstOrDefault()
                };
                if (size <= 0 || size > 16 * 1024 * 1024 || totalData > 64 * 1024 * 1024 - size)
                    throw new CompilationException(new Diagnostic("TR1020", "Field-RVA layout is unknown or exceeds the 16 MiB field / 64 MiB assembly data budget."));
                totalData += size;
                return pe.GetSectionData(rva).GetContent(0, size).ToArray();
            }
            foreach (var handle in reader.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var type = reader.GetTypeDefinition(handle);
                var typeName = provider.TypeName(handle);
                if (includeType is not null && !includeType(typeName)) continue;
                var baseName = type.BaseType.IsNil ? null : provider.TypeName(type.BaseType);
                types.Add(new(typeName, baseName, (type.Attributes & TypeAttributes.Interface) != 0,
                    baseName is "System.ValueType" or "System.Enum", (type.Attributes & TypeAttributes.BeforeFieldInit) != 0,
                    type.GetGenericParameters().Count, type.GetInterfaceImplementations().Select(i =>
                        provider.TypeName(reader.GetInterfaceImplementation(i).Interface)).ToArray())
                {
                    GenericVariance = type.GetGenericParameters().Select(h => (int)(reader.GetGenericParameter(h).Attributes & GenericParameterAttributes.VarianceMask)).ToArray(),
                    Overrides = type.GetMethodImplementations().Select(h => reader.GetMethodImplementation(h))
                        .Select(m => new MethodOverride(Method(m.MethodBody), Method(m.MethodDeclaration))).ToArray(),
                    EnumUnderlyingType = baseName == "System.Enum" ? type.GetFields().Select(h => Field(h))
                        .First(f => f.Name == "value__").FieldType : null,
                    EnumFlags = baseName == "System.Enum" && type.GetCustomAttributes().Select(h => reader.GetCustomAttribute(h)).Any(a =>
                        a.Constructor.Kind is HandleKind.MemberReference or HandleKind.MethodDefinition && Method(a.Constructor).Type == "System.FlagsAttribute"),
                    EnumValues = baseName == "System.Enum" ? type.GetFields().Select(h => reader.GetFieldDefinition(h))
                        .Where(f => (f.Attributes & FieldAttributes.Literal) != 0)
                        .Select(f => new EnumValue(reader.GetString(f.Name), EnumConstant(f))).ToArray() : [],
                    RuntimeObligations = type.GetMethods().Select(h => reader.GetMethodDefinition(h))
                        .Where(m => (m.Attributes & MethodAttributes.Virtual) != 0 && (m.Attributes & MethodAttributes.NewSlot) == 0)
                        .Select(m => reader.GetString(m.Name)).Where(n => n is "ToString" or "GetHashCode" or "Equals" or "Finalize").ToArray(),
                    ExplicitLayout = (type.Attributes & TypeAttributes.LayoutMask) == TypeAttributes.ExplicitLayout
                });
                foreach (var f in type.GetFields())
                {
                    var definition = reader.GetFieldDefinition(f);
                    fields.Add(new(Field(f), (definition.Attributes & FieldAttributes.Static) != 0,
                        (definition.Attributes & FieldAttributes.Literal) != 0, null) { InitialData = ReadData(definition) });
                }
                foreach (var m in type.GetMethods())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var definition = reader.GetMethodDefinition(m);
                    var reference = Method(m);
                    if (includeMethod is not null && !includeMethod(reference)) continue;
                    if (definition.DecodeSignature(provider, null).Header.CallingConvention == SignatureCallingConvention.VarArgs)
                        throw new CompilationException(new Diagnostic("TR1011", "Vararg calling conventions are outside portable-mvp.", reference.Key));
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
            var assembly = reader.GetAssemblyDefinition();
            string PublicKey(byte[] key, bool full)
            {
                if (key.Length == 0) return "null";
                return Convert.ToHexString(full ? System.Security.Cryptography.SHA1.HashData(key)[^8..].Reverse().ToArray() : key).ToLowerInvariant();
            }
            var identity = new AssemblyIdentity(name, assembly.Version.ToString(), assembly.Culture.IsNil ? "neutral" : reader.GetString(assembly.Culture),
                PublicKey(reader.GetBlobBytes(assembly.PublicKey), true));
            return new(name, cor.EntryPointTokenOrRelativeVirtualAddress, types.ToArray(), methods.ToArray(), fields.ToArray())
            {
                Identity = identity,
                Forwarders = ForwarderImporter.Read(reader, includeType, name),
                Inputs = [new(identity.ToString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image)).ToLowerInvariant())],
                References = reader.AssemblyReferences.Select(h => reader.GetAssemblyReference(h)).Select(a => new AssemblyIdentity(
                    reader.GetString(a.Name), a.Version.ToString(), a.Culture.IsNil ? "neutral" : reader.GetString(a.Culture),
                    PublicKey(reader.GetBlobBytes(a.PublicKeyOrToken), (a.Flags & AssemblyFlags.PublicKey) != 0))).ToArray(),
                IsReferenceAssembly = assembly.GetCustomAttributes().Select(h => reader.GetCustomAttribute(h)).Any(a =>
                    a.Constructor.Kind is HandleKind.MemberReference or HandleKind.MethodDefinition &&
                    Method(a.Constructor).Type == "System.Runtime.CompilerServices.ReferenceAssemblyAttribute")
            };
        }
        catch (CompilationException) { throw; }
        catch (Exception e) when (e is BadImageFormatException or ArgumentException or InvalidOperationException or OverflowException or IndexOutOfRangeException)
        { throw new CompilationException(new Diagnostic("TR1001", $"Invalid or unsupported PE/metadata: {e.Message}")); }
    }

    private sealed class Signatures(MetadataReader reader, string assemblyName) : ISignatureTypeProvider<string, object?>
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
            return !t.GetDeclaringType().IsNil ? TypeName(t.GetDeclaringType()) + "+" + n : Qualify(assemblyName, ns.Length == 0 ? n : ns + "." + n);
        }
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte rawTypeKind)
        {
            var t = r.GetTypeReference(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace);
            var scope = t.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? r.GetString(r.GetAssemblyReference((AssemblyReferenceHandle)t.ResolutionScope).Name) : assemblyName;
            return t.ResolutionScope.Kind == HandleKind.TypeReference ? TypeName(t.ResolutionScope) + "+" + n : Qualify(scope, ns.Length == 0 ? n : ns + "." + n);
        }
        private static string Qualify(string scope, string type) => AssemblyLinker.IsFramework(scope) ? type : "[" + scope + "]" + type;
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
