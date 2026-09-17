using System.Collections.Immutable;
using System.Globalization;

namespace Transpiler.Core;

public enum CliTypeForm { Named, TypeParameter, MethodParameter, Vector, Array, ByReference, Pointer, Pinned, RequiredModifier, OptionalModifier }
public sealed record NamedTypeIdentity(string? AssemblyScope, string MetadataName)
{
    public string Text => AssemblyScope is null ? MetadataName : "[" + AssemblyScope + "]" + MetadataName;
}

/// <summary>
/// Immutable structural type syntax for the compiler's metadata-name codec, with bounded parsing,
/// ordinal structural equality and scope-aware rewriting. Rewriting preserves wrappers represented by that codec; full CLI signatures remain a separate model.
/// The assembly linker resolves scope identity; this does not authenticate assemblies or parse C# syntax.
/// </summary>
public sealed class CliTypeIdentity : IEquatable<CliTypeIdentity>
{
    public CliTypeForm Form { get; }
    public NamedTypeIdentity? Name { get; }
    public ImmutableArray<CliTypeIdentity> Arguments { get; }
    public CliTypeIdentity? Element { get; }
    public CliTypeIdentity? Modifier { get; }
    public int IndexOrRank { get; }
    public string CanonicalName { get; }

    private CliTypeIdentity(CliTypeForm form, string text, NamedTypeIdentity? name = null,
        ImmutableArray<CliTypeIdentity> arguments = default, CliTypeIdentity? element = null,
        CliTypeIdentity? modifier = null, int indexOrRank = 0)
    { Form = form; Name = name; Arguments = arguments.IsDefault ? [] : arguments;
      Element = element; Modifier = modifier; IndexOrRank = indexOrRank;
      CanonicalName = form switch
      {
          CliTypeForm.Named => name!.Text + (Arguments.Length == 0 ? "" : "<" + string.Join(",", Arguments.Select(a => a.CanonicalName)) + ">"),
          CliTypeForm.TypeParameter => "!" + indexOrRank.ToString(CultureInfo.InvariantCulture),
          CliTypeForm.MethodParameter => "!!" + indexOrRank.ToString(CultureInfo.InvariantCulture),
          CliTypeForm.Vector => element!.CanonicalName + "[]",
          CliTypeForm.Array => element!.CanonicalName + "[rank=" + indexOrRank.ToString(CultureInfo.InvariantCulture) + "]",
          CliTypeForm.ByReference => element!.CanonicalName + "&", CliTypeForm.Pointer => element!.CanonicalName + "*",
          CliTypeForm.Pinned => "pinned " + element!.CanonicalName,
          CliTypeForm.RequiredModifier => "modreq(" + modifier!.CanonicalName + ") " + element!.CanonicalName,
          CliTypeForm.OptionalModifier => "modopt(" + modifier!.CanonicalName + ") " + element!.CanonicalName,
          _ => throw new ArgumentOutOfRangeException(nameof(form))
      }; }

    public static CliTypeIdentity Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is 0 or > 4096) throw new CompilationException(new Diagnostic("TR3020", "Type signature exceeds its length budget or is empty."));
        return ParseCore(text, 0);
    }
    private static CliTypeIdentity ParseCore(string text, int depth)
    {
        if (depth > 64 || text.Length == 0) throw new CompilationException(new Diagnostic("TR3020", "Type signature exceeds its nesting budget or contains an empty element."));
        CliTypeIdentity Child(string value) => ParseCore(value, depth + 1);
        if (text.StartsWith("pinned ", StringComparison.Ordinal))
            return new(CliTypeForm.Pinned, text, element: Child(text[7..]));
        if (text.StartsWith("modreq(", StringComparison.Ordinal) || text.StartsWith("modopt(", StringComparison.Ordinal))
        {
            var end = text.IndexOf(") ", StringComparison.Ordinal);
            if (end < 7) throw new CompilationException(new Diagnostic("TR3020", "Malformed custom modifier."));
            return new(text[3] == 'r' ? CliTypeForm.RequiredModifier : CliTypeForm.OptionalModifier, text,
                element: Child(text[(end + 2)..]), modifier: Child(text[7..end]));
        }
        if (text.EndsWith('&') || text.EndsWith('*') && text != "method*")
            return new(text[^1] == '&' ? CliTypeForm.ByReference : CliTypeForm.Pointer, text, element: Child(text[..^1]));
        if (text.EndsWith("[]", StringComparison.Ordinal)) return new(CliTypeForm.Vector, text, element: Child(text[..^2]), indexOrRank: 1);
        if (ArrayContracts.TryShape(text, out var element, out var rank)) return new(CliTypeForm.Array, text, element: Child(element), indexOrRank: rank);
        if (text[0] == '!')
        {
            var start = text.StartsWith("!!", StringComparison.Ordinal) ? 2 : 1;
            if (int.TryParse(text.AsSpan(start), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                return new(start == 2 ? CliTypeForm.MethodParameter : CliTypeForm.TypeParameter, text, indexOrRank: index);
        }
        var (definition, arguments) = GenericSpecializer.Split(text);
        string? scope = null;
        if (definition.StartsWith('['))
        {
            var close = definition.IndexOf(']');
            if (close < 2 || close == definition.Length - 1) throw new CompilationException(new Diagnostic("TR3020", "Malformed assembly-scoped type identity."));
            scope = definition[1..close]; definition = definition[(close + 1)..];
        }
        return new(CliTypeForm.Named, text, new(scope, definition), arguments.Select(Child).ToImmutableArray());
    }

    public CliTypeIdentity Rewrite(Func<NamedTypeIdentity, NamedTypeIdentity> rewrite)
    {
        ArgumentNullException.ThrowIfNull(rewrite);
        string Result()
        {
            if (Form == CliTypeForm.Named)
            {
                var name = rewrite(Name!) ?? throw new ArgumentException("A named-type rewrite cannot return null.", nameof(rewrite));
                return name.Text + (Arguments.Length == 0 ? "" : "<" + string.Join(",", Arguments.Select(a => a.Rewrite(rewrite).CanonicalName)) + ">");
            }
            if (Element is null) return CanonicalName;
            var child = Element.Rewrite(rewrite).CanonicalName;
            return Form switch
            {
                CliTypeForm.Vector => child + "[]", CliTypeForm.Array => child + "[rank=" + IndexOrRank.ToString(CultureInfo.InvariantCulture) + "]",
                CliTypeForm.ByReference => child + "&", CliTypeForm.Pointer => child + "*", CliTypeForm.Pinned => "pinned " + child,
                CliTypeForm.RequiredModifier or CliTypeForm.OptionalModifier => (Form == CliTypeForm.RequiredModifier ? "modreq(" : "modopt(") + Modifier!.Rewrite(rewrite).CanonicalName + ") " + child,
                _ => throw new InvalidOperationException("Unknown type wrapper.")
            };
        }
        var text = Result();
        return text == CanonicalName ? this : Parse(text);
    }

    public bool Equals(CliTypeIdentity? other) => other is not null && StringComparer.Ordinal.Equals(CanonicalName, other.CanonicalName);
    public override bool Equals(object? obj) => obj is CliTypeIdentity other && Equals(other);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(CanonicalName);
    public override string ToString() => CanonicalName;
}
