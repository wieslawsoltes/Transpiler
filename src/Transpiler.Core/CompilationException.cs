namespace Transpiler.Core;

// Kept in a separate file to make diagnostic construction unambiguous with target-typed new.
public static class DiagnosticFactory
{
    public static CompilationException Error(string code, string message, string? method = null, int? offset = null) =>
        new CompilationException(new Diagnostic(code, message, method, offset));
}
