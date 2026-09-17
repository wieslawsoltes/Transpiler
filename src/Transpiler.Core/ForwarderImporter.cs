using System.Reflection;
using System.Reflection.Metadata;
using System.Security.Cryptography;

namespace Transpiler.Core;

internal static class ForwarderImporter
{
    public static TypeForwarder[] Read(MetadataReader reader, Func<string, bool>? includeType, string assembly)
    {
        var cache = new Dictionary<ExportedTypeHandle, TypeForwarder?>();
        TypeForwarder? Resolve(ExportedTypeHandle handle, HashSet<ExportedTypeHandle> path)
        {
            if (cache.TryGetValue(handle, out var found)) return found;
            if (path.Count >= 64 || !path.Add(handle)) throw new BadImageFormatException("Cyclic or over-budget nested ExportedType metadata.");
            var type = reader.GetExportedType(handle);
            TypeForwarder? result = null;
            var name = reader.GetString(type.Name);
            if (type.Implementation.Kind == HandleKind.ExportedType)
            {
                var parent = Resolve((ExportedTypeHandle)type.Implementation, path);
                if (parent is not null) result = parent with { MetadataName = parent.MetadataName + "+" + name };
            }
            else if (type.Implementation.Kind == HandleKind.AssemblyReference && type.IsForwarder)
            {
                var target = reader.GetAssemblyReference((AssemblyReferenceHandle)type.Implementation);
                var key = reader.GetBlobBytes(target.PublicKeyOrToken);
                var token = key.Length == 0 ? "null" : Convert.ToHexString((target.Flags & AssemblyFlags.PublicKey) != 0
                    ? SHA1.HashData(key)[^8..].Reverse().ToArray() : key).ToLowerInvariant();
                var ns = reader.GetString(type.Namespace);
                result = new(ns.Length == 0 ? name : ns + "." + name,
                    new(reader.GetString(target.Name), target.Version.ToString(), target.Culture.IsNil ? "neutral" : reader.GetString(target.Culture), token));
            }
            path.Remove(handle);
            return cache[handle] = result;
        }
        return reader.ExportedTypes.Select(h => Resolve(h, [])).OfType<TypeForwarder>()
            .Where(f => includeType is null || includeType(AssemblyLinker.IsFramework(assembly) ? f.MetadataName : "[" + assembly + "]" + f.MetadataName)).ToArray();
    }
}
