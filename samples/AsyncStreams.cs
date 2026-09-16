using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public static class Program
{
    private static async IAsyncEnumerable<long> Squares(int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return (long)i * i;
            }
        }
        finally { await Task.Yield(); Console.WriteLine("asynchronous cleanup completed"); }
    }
    public static async Task Main()
    {
        using var source = new CancellationTokenSource();
        long sum = 0;
        await foreach (var value in Squares(20).WithCancellation(source.Token).ConfigureAwait(false))
        {
            sum += value;
            if (value == 16) break;
        }
        Console.WriteLine(sum); // 30; iterator disposal has already run.
    }
}
