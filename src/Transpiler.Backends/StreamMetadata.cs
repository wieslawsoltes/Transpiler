using Transpiler.Core;

namespace Transpiler.Backends;

internal static class StreamMetadata
{
    public static object Build(CompilationAnalysis analysis)
    {
        var methods = analysis.Methods.Select(m => m.Method).ToArray();
        var image = analysis.Assembly;
        var bindings = new SortedDictionary<string, object>(StringComparer.Ordinal);
        foreach (var export in methods.Where(m => analysis.Exports.Contains(m.Key)))
        {
            var shape = StreamContracts.Discover(export.Reference.ReturnType, image.FindType, image.Types.Select(t => t.Name));
            if (shape.Elements.Length == 0) continue;
            var choices = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var element in shape.Elements)
            {
                var cursorType = StreamContracts.Cursor + "<" + element + ">";
                var members = methods.Where(m => m.Reference.Type == cursorType && StreamContracts.Members.Contains(m.Reference.Name)).ToArray();
                // Without the portable BCL there is no stream capability, not a partly linked adapter.
                if (members.Length == 0) continue;
                if (members.Length != StreamContracts.Members.Length || members.Any(m => m.Instructions.Length == 0))
                    throw new CompilationException(new Diagnostic("TR2220", "Incomplete managed stream cursor linkage.", export.Key));
                var open = methods.SingleOrDefault(m => m.Reference.Type == shape.BridgeType(element) && m.Reference.Name == shape.OpenMethod)
                    ?? throw new CompilationException(new Diagnostic("TR2220", "Stream factory bridge was not rooted.", export.Key));
                var pump = methods.SingleOrDefault(m => m.Reference.Type == "[Transpiler.Bcl]Transpiler.Bcl.Tasks.Scheduler" && m.Reference.Name == "RunOne")
                    ?? throw new CompilationException(new Diagnostic("TR2220", "Stream scheduler was not rooted.", export.Key));
                choices[element] = new
                {
                    element, pump = BackendMetadata.Id(pump.Token), open = BackendMetadata.Id(open.Token),
                    methods = members.ToDictionary(m => m.Reference.Name, m => BackendMetadata.Id(m.Token), StringComparer.Ordinal)
                };
            }
            if (choices.Count != 0)
                bindings[shape.ReturnType] = new
                {
                    policy = StreamContracts.Policy, kind = shape.Kind, sourceType = shape.SourceType,
                    requiresElement = shape.Erased || choices.Count != 1, choices
                };
        }
        return bindings;
    }
}
