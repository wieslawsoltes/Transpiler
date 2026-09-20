using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Transpiler.Core;

/// <summary>Metadata-only identity discovery. Does not decode method bodies or load executable code.</summary>
public sealed record AssemblyHeader(AssemblyIdentity Identity, AssemblyIdentity[] References, bool IsReferenceAssembly)
{
    public static AssemblyHeader Read(CompilationFile file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var pe = new PEReader(file.Content);
            if (!pe.HasMetadata) throw new BadImageFormatException("CLI metadata is required.");
            var reader = pe.GetMetadataReader();
            if (!reader.IsAssembly) throw new BadImageFormatException("Standalone netmodules are not supported.");
            var definition = reader.GetAssemblyDefinition();
            string Token(BlobHandle handle, bool full)
            {
                var bytes = reader.GetBlobBytes(handle);
                return bytes.Length == 0 ? "null" : Convert.ToHexString(full ? SHA1.HashData(bytes)[^8..].Reverse().ToArray() : bytes).ToLowerInvariant();
            }
            string Culture(StringHandle handle) => handle.IsNil ? "neutral" : reader.GetString(handle);
            bool ReferenceAttribute(CustomAttributeHandle handle)
            {
                var ctor = reader.GetCustomAttribute(handle).Constructor;
                EntityHandle type = ctor.Kind switch
                {
                    HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)ctor).Parent,
                    HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType(),
                    _ => default
                };
                if (type.Kind == HandleKind.TypeReference)
                {
                    var t = reader.GetTypeReference((TypeReferenceHandle)type);
                    return reader.GetString(t.Namespace) == "System.Runtime.CompilerServices" && reader.GetString(t.Name) == "ReferenceAssemblyAttribute";
                }
                if (type.Kind == HandleKind.TypeDefinition && !type.IsNil)
                {
                    var t = reader.GetTypeDefinition((TypeDefinitionHandle)type);
                    return reader.GetString(t.Namespace) == "System.Runtime.CompilerServices" && reader.GetString(t.Name) == "ReferenceAssemblyAttribute";
                }
                return false;
            }
            var references = new List<AssemblyIdentity>();
            foreach (var handle in reader.AssemblyReferences)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var reference = reader.GetAssemblyReference(handle);
                references.Add(new(reader.GetString(reference.Name), reference.Version.ToString(), Culture(reference.Culture),
                    Token(reference.PublicKeyOrToken, (reference.Flags & AssemblyFlags.PublicKey) != 0)));
            }
            return new(new(reader.GetString(definition.Name), definition.Version.ToString(), Culture(definition.Culture), Token(definition.PublicKey, true)),
                references.ToArray(), definition.GetCustomAttributes().Any(ReferenceAttribute));
        }
        catch (Exception error) when (error is BadImageFormatException or ArgumentException or InvalidOperationException or OverflowException or IndexOutOfRangeException)
        {
            throw new CompilationException(new Diagnostic("TR3030", $"Cannot inspect dependency '{file.Path}': {error.Message}"));
        }
    }
}

public sealed record DependencyBinding(string Requester, string Requested, string Kind, string? Sha256);
