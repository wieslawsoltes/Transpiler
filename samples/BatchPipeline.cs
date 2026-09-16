using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class Program
{
    private static async Task<float> Measure(float[] samples)
    {
        await Task.Yield();
        float sum = 0;
        foreach (float sample in samples) sum += sample;
        return sum;
    }

    public static async Task Main()
    {
        // Initialized data comes from PE field-RVA bytes, not parsed C# source text.
        float[] first = { 0.25f, 0.5f, 1f, 2f };
        float[] second = { 10f, 20f, 30f, 40f };
        float[] measurements = await Task.WhenAll(new[] { Measure(first), Measure(second) });

        var list = new List<float>(measurements);
        var liveView = list.AsReadOnly();
        list[0] *= 2;

        var lookup = new Dictionary<string, float>(StringComparer.Ordinal);
        lookup.Add("first", liveView[0]);
        lookup.Add("second", liveView[1]);
        Console.WriteLine(lookup["first"]);   // 7.5
        Console.WriteLine(lookup["second"]);  // 100
        Console.WriteLine(liveView.Count);   // 2
        Console.WriteLine("CIL -> binary32 + collections + cooperative tasks");
    }
}
