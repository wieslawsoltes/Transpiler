using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Transpiler.Core;
using Diagnostic = Transpiler.Core.Diagnostic;

namespace Transpiler.Frontend.Roslyn;

public sealed record SourceFile(string Path, string Text);
public sealed record ManagedCompilation(byte[] Pe, byte[] Pdb, string CompilerVersion);

public static class RoslynFrontend
{
    public static ManagedCompilation Compile(IEnumerable<SourceFile> sources, string name = "Application", bool library = false,
        bool optimize = true, IEnumerable<string>? references = null)
    {
        var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))?.Split(Path.PathSeparator)
            ?? throw new CompilationException(new Diagnostic("TR0101", "The compiler host has no framework reference set."));
        // The MVP records the host framework version. An SDK reference-pack resolver is a separate planned service.
        var metadata = paths.Concat(references ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(p => MetadataReference.CreateFromFile(p)).ToArray();
        var trees = sources.OrderBy(s => s.Path, StringComparer.Ordinal).Select(s => CSharpSyntaxTree.ParseText(
            SourceText.From(s.Text, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.CSharp14), path: s.Path));
        var compilation = CSharpCompilation.Create(name, trees, metadata, new CSharpCompilationOptions(
            library ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication,
            optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
            deterministic: true, allowUnsafe: false, concurrentBuild: false));
        using var pe = new MemoryStream(); using var pdb = new MemoryStream();
        var result = compilation.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb));
        if (!result.Success) throw new CompilationException(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => new Diagnostic(d.Id, d.ToString())).ToArray());
        return new(pe.ToArray(), pdb.ToArray(), typeof(CSharpCompilation).Assembly.GetName().Version?.ToString() ?? "unknown");
    }
}
