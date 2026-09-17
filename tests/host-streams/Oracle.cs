using System;
using System.Threading.Tasks;
public static class Program
{
    public static async Task Main()
    {
        await foreach (var item in Kernel.Numbers(3)) Console.WriteLine(item);
        await foreach (var item in Kernel.Text()) Console.WriteLine(item);
        await foreach (var item in Kernel.Flags()) Console.WriteLine(item);
    }
}
