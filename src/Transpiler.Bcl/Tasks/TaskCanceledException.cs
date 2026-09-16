using System;
using CancellationToken = Transpiler.Bcl.Threading.CancellationToken;
namespace Transpiler.Bcl.Tasks;

public class TaskCanceledException : OperationCanceledException
{
    public Task? Task { get; }
    public TaskCanceledException() : base("A task was canceled.") { }
    public TaskCanceledException(string? message) : base(message) { }
    public TaskCanceledException(string? message, Exception? innerException) : base(message, innerException) { }
    public TaskCanceledException(string? message, Exception? innerException, CancellationToken token) : base(message, innerException, token) { }
    public TaskCanceledException(Task? task) : base("A task was canceled.", task == null ? default : task.Cancellation)
    { Task = task; }
}
