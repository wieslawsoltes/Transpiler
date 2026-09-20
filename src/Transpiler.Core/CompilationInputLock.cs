using System.Text.Json;
using System.Text.Json.Serialization;

namespace Transpiler.Core;

/// <summary>Path-independent identity/content contract. No timestamps, absolute paths or restore hints.</summary>
public sealed record CompilationInputLock(int Schema, string Policy, string Target, string Dispatch, string Bcl,
    AssemblyInput[] Assemblies, AssemblyInput[] ReferencePack, AssemblyInput[] Toolchain)
{
    public const string CurrentPolicy = "compilation-input-lock-v1";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static CompilationInputLock Create(string target, string dispatch, string bcl,
        IEnumerable<AssemblyInput> assemblies, IEnumerable<AssemblyInput>? referencePack, IEnumerable<AssemblyInput> toolchain) =>
        new(1, CurrentPolicy, target, dispatch, bcl, Sort(assemblies), Sort(referencePack ?? []), Sort(toolchain));

    private static AssemblyInput[] Sort(IEnumerable<AssemblyInput> inputs) => inputs.Distinct()
        .OrderBy(a => a.Identity, StringComparer.Ordinal).ThenBy(a => a.Sha256, StringComparer.Ordinal).ToArray();

    public string Serialize()
    {
        Validate();
        return JsonSerializer.Serialize(this, Json) + "\n";
    }

    public static CompilationInputLock Parse(string text)
    {
        try
        {
            // JSON duplicate properties are rejected rather than silently using the last value.
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
            void Unique(JsonElement value)
            {
                if (value.ValueKind == JsonValueKind.Object)
                {
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var property in value.EnumerateObject())
                    { if (!names.Add(property.Name)) throw new JsonException("Duplicate property: " + property.Name); Unique(property.Value); }
                }
                else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) Unique(item);
            }
            Unique(json.RootElement);
            var result = json.RootElement.Deserialize<CompilationInputLock>(Json) ?? throw new JsonException("A lock document is required.");
            result.Validate(); return result;
        }
        catch (JsonException error) { throw new CompilationException(new Diagnostic("TR3310", "Invalid input lock: " + error.Message)); }
    }

    private void Validate()
    {
        if (Schema != 1 || Policy != CurrentPolicy || Target is not ("javascript" or "python" or "native-std") ||
            Dispatch is not ("instruction" or "block" or "ssa") || string.IsNullOrWhiteSpace(Bcl))
            Fail("TR3310", "Unsupported or incomplete input-lock schema/profile/options.");
        void Check(AssemblyInput[]? entries, string section)
        {
            if (entries is null || entries.Length > 16384) Fail("TR3310", $"Missing or oversized lock section '{section}'.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries!)
                if (entry is null || string.IsNullOrEmpty(entry.Identity) || entry.Sha256 is not { Length: 64 } ||
                    !entry.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') || !seen.Add(entry.Identity))
                    Fail("TR3310", $"Invalid or duplicate identity/hash in lock section '{section}'.");
        }
        Check(Assemblies, "assemblies"); Check(ReferencePack, "referencePack"); Check(Toolchain, "toolchain");
        if (Assemblies.Length == 0 || Toolchain.Length == 0) Fail("TR3310", "A lock must identify its managed inputs and compiler toolchain.");
    }

    /// <summary>Validates before target writes. Reordering and relocation do not constitute drift.</summary>
    public void Verify(CompilationInputLock actual)
    {
        Validate(); actual.Validate();
        var changes = new List<string>();
        if (Target != actual.Target) changes.Add($"target: {Target} -> {actual.Target}");
        if (Dispatch != actual.Dispatch) changes.Add($"dispatch: {Dispatch} -> {actual.Dispatch}");
        if (Bcl != actual.Bcl) changes.Add($"bcl: {Bcl} -> {actual.Bcl}");
        void Compare(string section, AssemblyInput[] expected, AssemblyInput[] observed)
        {
            var old = expected.ToDictionary(a => a.Identity, a => a.Sha256, StringComparer.Ordinal);
            var current = observed.ToDictionary(a => a.Identity, a => a.Sha256, StringComparer.Ordinal);
            foreach (var identity in old.Keys.Union(current.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                if (!old.TryGetValue(identity, out var hash)) changes.Add($"{section}: unexpected '{identity}'");
                else if (!current.TryGetValue(identity, out var present)) changes.Add($"{section}: missing '{identity}'");
                else if (hash != present) changes.Add($"{section}: SHA-256 changed for '{identity}' ({hash} -> {present})");
            }
        }
        Compare("assemblies", Assemblies, actual.Assemblies);
        Compare("referencePack", ReferencePack, actual.ReferencePack);
        Compare("toolchain", Toolchain, actual.Toolchain);
        if (changes.Count != 0) Fail("TR3311", "Compilation inputs differ from the lock: " + string.Join("; ", changes.Take(32)) +
            (changes.Count > 32 ? $"; and {changes.Count - 32} further differences." : "."));
    }

    private static void Fail(string code, string message) => throw new CompilationException(new Diagnostic(code, message));
}
