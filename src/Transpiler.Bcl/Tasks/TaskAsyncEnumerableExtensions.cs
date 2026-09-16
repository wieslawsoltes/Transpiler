using Transpiler.Bcl.Async;
using Transpiler.Bcl.Threading;
namespace Transpiler.Bcl.Tasks;

public static class TaskAsyncEnumerableExtensions
{
    public static ConfiguredCancelableAsyncEnumerable<T> ConfigureAwait<T>(this IAsyncEnumerable<T> source, bool continueOnCapturedContext)
        => new ConfiguredCancelableAsyncEnumerable<T>(source, continueOnCapturedContext, default);
    public static ConfiguredCancelableAsyncEnumerable<T> WithCancellation<T>(this IAsyncEnumerable<T> source, CancellationToken cancellationToken)
        => new ConfiguredCancelableAsyncEnumerable<T>(source, true, cancellationToken);
    public static ConfiguredAsyncDisposable ConfigureAwait(this IAsyncDisposable source, bool continueOnCapturedContext)
        => new ConfiguredAsyncDisposable(source, continueOnCapturedContext);
}
