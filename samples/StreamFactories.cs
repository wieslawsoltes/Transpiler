using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// Compile as --library --bcl portable. This exports concrete and asynchronous factories,
// not just methods declared exactly as IAsyncEnumerable<T>.
public static class StreamFactories
{
    private static int _disposed;
    public static int Disposed() => _disposed;
    public static Counter Direct(int count) => new(count);
    public static async Task<Counter> Delayed(int count)
    {
        await Task.Delay(5);
        return new Counter(count);
    }
    public static async ValueTask<object> Erased(int count)
    {
        await Task.Yield();
        return new Counter(count);
    }

    public sealed class Counter : IAsyncEnumerable<long>
    {
        private readonly int _count;
        public Counter(int count) { _count = count; }
        public IAsyncEnumerator<long> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Enumerator(_count, cancellationToken);
    }
    private sealed class Enumerator : IAsyncEnumerator<long>
    {
        private readonly int _count;
        private readonly CancellationToken _cancellation;
        private int _index;
        public Enumerator(int count, CancellationToken cancellation)
        { _count = count; _cancellation = cancellation; }
        public long Current => 9007199254740992L + _index;
        public async ValueTask<bool> MoveNextAsync()
        {
            _cancellation.ThrowIfCancellationRequested();
            await Task.Yield();
            return ++_index <= _count;
        }
        public async ValueTask DisposeAsync()
        {
            await Task.Delay(2);
            _disposed++;
        }
    }
}
