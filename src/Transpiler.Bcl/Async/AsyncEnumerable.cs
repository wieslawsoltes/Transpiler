using Transpiler.Bcl.Tasks;
using Transpiler.Bcl.Threading;

namespace Transpiler.Bcl.Async;

public interface IAsyncDisposable
{
    ValueTask DisposeAsync();
}
public interface IAsyncEnumerable<out T>
{
    IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);
}
public interface IAsyncEnumerator<out T> : IAsyncDisposable
{
    T Current { get; }
    ValueTask<bool> MoveNextAsync();
}

/// <summary>Forwards the selected token and await policy to the actual translated enumerator.</summary>
public readonly struct ConfiguredCancelableAsyncEnumerable<T>
{
    private readonly IAsyncEnumerable<T> _enumerable;
    private readonly CancellationToken _token;
    private readonly bool _capture;
    internal ConfiguredCancelableAsyncEnumerable(IAsyncEnumerable<T> enumerable, bool capture, CancellationToken token)
    { _enumerable = enumerable; _capture = capture; _token = token; }
    public ConfiguredCancelableAsyncEnumerable<T> ConfigureAwait(bool continueOnCapturedContext)
        => new ConfiguredCancelableAsyncEnumerable<T>(_enumerable, continueOnCapturedContext, _token);
    public ConfiguredCancelableAsyncEnumerable<T> WithCancellation(CancellationToken cancellationToken)
        => new ConfiguredCancelableAsyncEnumerable<T>(_enumerable, _capture, cancellationToken);
    public Enumerator GetAsyncEnumerator() => new Enumerator(_enumerable.GetAsyncEnumerator(_token), _capture);
    public readonly struct Enumerator
    {
        private readonly IAsyncEnumerator<T> _enumerator;
        private readonly bool _capture;
        internal Enumerator(IAsyncEnumerator<T> enumerator, bool capture) { _enumerator = enumerator; _capture = capture; }
        public T Current => _enumerator.Current;
        public ConfiguredValueTaskAwaitable<bool> MoveNextAsync() => _enumerator.MoveNextAsync().ConfigureAwait(_capture);
        public ConfiguredValueTaskAwaitable DisposeAsync() => _enumerator.DisposeAsync().ConfigureAwait(_capture);
    }
}
public readonly struct ConfiguredAsyncDisposable
{
    private readonly IAsyncDisposable _resource;
    private readonly bool _capture;
    internal ConfiguredAsyncDisposable(IAsyncDisposable resource, bool capture) { _resource = resource; _capture = capture; }
    public ConfiguredValueTaskAwaitable DisposeAsync() => _resource.DisposeAsync().ConfigureAwait(_capture);
}
