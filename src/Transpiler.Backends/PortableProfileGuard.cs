using Transpiler.Core;

namespace Transpiler.Backends;

/// <summary>Emission-time checks for object-layout and host-runtime obligations not visible as direct calls.</summary>
internal static class PortableProfileGuard
{
    public static void Validate(CompilationAnalysis analysis)
    {
        var image = analysis.Assembly;
        var errors = new List<Diagnostic>();
        var active = new HashSet<string>(StringComparer.Ordinal);
        void Type(string type)
        {
            if (type.EndsWith('&')) type = type[..^1];
            while (type.EndsWith("[]", StringComparison.Ordinal)) type = type[..^2];
            while (image.FindType(type) is { } definition && active.Add(type))
            {
                if (definition.BaseType is null) break;
                type = definition.BaseType;
            }
        }
        bool UnsafeName(string name) => name.IndexOfAny(['\r', '\n', '\u2028', '\u2029']) >= 0;
        foreach (var method in analysis.Methods.Select(m => m.Method))
        {
            Type(method.Reference.Type);
            foreach (var type in method.Reference.Parameters.Concat(method.Locals).Append(method.Reference.ReturnType)) Type(type);
            if (UnsafeName(method.Key)) errors.Add(new("TR2213", "Metadata names containing source line separators are outside portable-mvp.", method.Key));
            foreach (var instruction in method.Instructions)
            {
                if (instruction.Operand is MethodReference call) Type(call.Type);
                if (instruction.Operand is FieldReference field && image.Resolve(field) is { } definition)
                {
                    Type(field.Type); Type(field.FieldType);
                    var staticAccess = instruction.Op is "ldsfld" or "stsfld" or "ldsflda";
                    if (definition.IsStatic != staticAccess)
                        errors.Add(new("TR2212", "Field access does not match its static/instance storage kind.", method.Key, instruction.Offset));
                }
            }
        }
        foreach (var type in active)
        {
            foreach (var obligation in (image.FindType(type)?.RuntimeObligations ?? []).Where(n => n is not ("ToString" or "Equals" or "GetHashCode")))
                errors.Add(new(obligation == "Finalize" ? "TR2211" : "TR2210", $"'{type}' requires the unsupported external runtime slot '{obligation}'."));
            if (UnsafeName(type) || type is "__proto__" or "constructor" or "prototype")
                errors.Add(new("TR2213", $"Metadata name '{type}' is outside the portable source-emission profile."));
            // Implicit calls through the BCL (Console.WriteLine(object), finalization, etc.) can invoke an
            // override even when ordinary call-graph reachability never visits the override body.
            foreach (var method in image.Methods.Where(m => m.Reference.Type == type && m.IsVirtual && !m.NewSlot))
            {
                if (method.Reference.Name == "Finalize")
                {
                    errors.Add(new("TR2211", "Finalizable classes require managed lifetime services, which are not implemented.", method.Key));
                    continue;
                }
                var internalSlot = image.Methods.Any(m => m.IsVirtual && m.Reference.Type != type &&
                    CompilerAnalysis.IsDerivedFrom(image, type, m.Reference.Type) && m.Reference.Name == method.Reference.Name &&
                    m.Reference.ReturnType == method.Reference.ReturnType && m.Reference.Parameters.SequenceEqual(method.Reference.Parameters));
                if (!internalSlot && !ValueSemanticsContracts.IsObjectOverride(method))
                    errors.Add(new("TR2210", "This class overrides an external virtual slot. An explicit BCL override bridge is required.", method.Key));
            }
        }
        if (errors.Count != 0) throw new CompilationException(errors.Distinct().ToArray());
    }
}
