using System;
using CancellationToken = Transpiler.Bcl.Threading.CancellationToken;
namespace Transpiler.Bcl;

public class OperationCanceledException : SystemException
{
    public CancellationToken CancellationToken { get; private set; }
    public OperationCanceledException() : base("The operation was canceled.") { }
    public OperationCanceledException(string? message) : base(message) { }
    public OperationCanceledException(string? message, Exception? innerException) : base(message, innerException) { }
    public OperationCanceledException(CancellationToken token) : this() { CancellationToken = token; }
    public OperationCanceledException(string? message, CancellationToken token) : this(message) { CancellationToken = token; }
    public OperationCanceledException(string? message, Exception? innerException, CancellationToken token) : this(message, innerException)
    { CancellationToken = token; }
}
