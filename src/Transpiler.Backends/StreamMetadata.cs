using Transpiler.Core;

namespace Transpiler.Backends;

internal static class StreamMetadata
{
    public static object Build(CompilationAnalysis analysis)
    {
        var methods = analysis.Methods.Select(m => m.Method).ToArray();
        var bindings = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var export in methods.Where(m => analysis.Exports.Contains(m.Key)))
        {
            if (StreamContracts.Element(export.Reference.ReturnType) is not { } element) continue;
            var cursorType = StreamContracts.Cursor + "<" + element + ">";
            var members = methods.Where(m => m.Reference.Type == cursorType && StreamContracts.Members.Contains(m.Reference.Name)).ToArray();
            // A library compiled without the portable implementation has no stream adapter capability.
            if (members.Length == 0) continue;
            if (members.Length != StreamContracts.Members.Length || members.Any(m => m.Instructions.Length == 0))
                throw new CompilationException(new Diagnostic("TR2220", "Incomplete managed stream cursor linkage.", export.Key));
            var pump = methods.SingleOrDefault(m => m.Reference.Type == "[Transpiler.Bcl]Transpiler.Bcl.Tasks.Scheduler" && m.Reference.Name == "RunOne")
                ?? throw new CompilationException(new Diagnostic("TR2220", "Stream scheduler was not rooted.", export.Key));
            bindings[export.Reference.ReturnType] = new
            {
                policy = StreamContracts.Policy, element, pump = BackendMetadata.Id(pump.Token),
                methods = members.ToDictionary(m => m.Reference.Name, m => BackendMetadata.Id(m.Token), StringComparer.Ordinal)
            };
        }
        return bindings;
    }
}
