using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

public static class StreamKernel
{
    // The declared interface is the stable host-stream export contract.
    public static IAsyncEnumerable<long> Squares(int count) => Enumerate(count);

    private static async IAsyncEnumerable<long> Enumerate(
        int count, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        try
        {
            for (int i = 0; i < count; i++)
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                yield return (long)i * i;
            }
        }
        finally
        {
            await Task.Yield();
            Console.WriteLine("managed enumerator disposed");
        }
    }
}
