using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Transpiler.Core;
using Diagnostic = Transpiler.Core.Diagnostic;

namespace Transpiler.Frontend.Roslyn;

public sealed record SourceFile(string Path, string Text);
public sealed record ManagedCompilation(byte[] Pe, byte[] Pdb, string CompilerVersion)
{
    public ReferencePack? ReferencePack { get; init; }
}

public static class RoslynFrontend
{
    public static AssemblyInput[] CompilerInputs(CompilationInputSession inputs) =>
        new[] { typeof(RoslynFrontend).Assembly, typeof(AssemblyImporter).Assembly, typeof(CSharpCompilation).Assembly, typeof(Compilation).Assembly, typeof(object).Assembly }
            .Select(a => inputs.Read(a.Location).Fingerprint(a.GetName().FullName!)).ToArray();
    public static ManagedCompilation Compile(IEnumerable<SourceFile> sources, string name = "Application", bool library = false,
        bool optimize = true, IEnumerable<string>? references = null, string? referencePackDirectory = null, CompilationInputSession? inputs = null)
    {
        inputs ??= new();
        var token = inputs.CancellationToken;
        token.ThrowIfCancellationRequested();
        var referencePack = ReferencePackResolver.Resolve(referencePackDirectory, inputs);
        var paths = referencePack.Assemblies;
        var metadata = paths.Concat(references ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(p => MetadataReference.CreateFromImage(inputs.Read(p).Content, filePath: Path.GetFileName(p))).ToArray();
        var trees = sources.OrderBy(s => s.Path, StringComparer.Ordinal).Select(s => CSharpSyntaxTree.ParseText(
            SourceText.From(s.Text, Encoding.UTF8), new CSharpParseOptions(LanguageVersion.CSharp14), path: s.Path, cancellationToken: token));
        var compilation = CSharpCompilation.Create(name, trees, metadata, new CSharpCompilationOptions(
            library ? OutputKind.DynamicallyLinkedLibrary : OutputKind.ConsoleApplication,
            optimizationLevel: optimize ? OptimizationLevel.Release : OptimizationLevel.Debug,
            deterministic: true, allowUnsafe: false, concurrentBuild: false));
        using var pe = new MemoryStream(); using var pdb = new MemoryStream();
        var result = compilation.Emit(pe, pdb, options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb), cancellationToken: token);
        if (!result.Success) throw new CompilationException(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => new Diagnostic(d.Id, d.ToString())).ToArray());
        return new(pe.ToArray(), pdb.ToArray(), typeof(CSharpCompilation).Assembly.GetName().Version?.ToString() ?? "unknown") { ReferencePack = referencePack };
    }
}
