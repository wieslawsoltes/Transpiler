using System;
using System.Collections.Generic;
using System.Threading.Tasks;
public static class Oracle
{
    private static async Task Consume(IAsyncEnumerable<int> stream)
    { await foreach (var value in stream) Console.WriteLine(value); }
    public static async Task Main()
    {
        await Consume(StreamExports.Concrete());
        await Consume(StreamExports.Struct());
        await Consume(StreamExports.Interface());
        await Consume(await StreamExports.TaskConcrete());
        await Consume(await StreamExports.TaskStruct());
        await Consume(await StreamExports.AsyncValue());
        await Consume((IAsyncEnumerable<int>)await StreamExports.TaskErased());
        await Consume(await StreamExports.CompletedSource());
        await foreach (var text in await StreamExports.Covariant()) Console.WriteLine(text);
        await foreach (var pair in await StreamExports.ValueElements()) { Console.WriteLine(pair.X); Console.WriteLine(pair.Y); }
        Console.WriteLine(StreamExports.Consumptions());
        Console.WriteLine(StreamExports.Disposals());
    }
}
