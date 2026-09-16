using System;
namespace Transpiler.Bcl;

/// <summary>
/// Preserves the managed exception object for generated async cleanup/rethrow paths.
/// Target stack traces are not .NET stack traces; remote stack injection remains unsupported.
/// </summary>
public sealed class ExceptionDispatchInfo
{
    public Exception SourceException { get; }
    private ExceptionDispatchInfo(Exception source) { SourceException = source; }
    public static ExceptionDispatchInfo Capture(Exception source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        return new ExceptionDispatchInfo(source);
    }
    public void Throw() => throw SourceException;
    public static void Throw(Exception source) => Capture(source).Throw();
}
