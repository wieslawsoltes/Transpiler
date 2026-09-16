using System;
public class Cell { public int Value; }
public static class Program
{
    static int Shared;
    static void Change(ref int first, ref int second) { first += 4; second *= 3; }
    static void Set(out long value) { value = 9007199254740995L; }
    static ref int At(int[] values, int index) => ref values[index];
    public static void Main()
    {
        int value = 2; Change(ref value, ref value); Console.WriteLine(value);
        var cell = new Cell(); cell.Value = 5;
        Change(ref cell.Value, ref value); Console.WriteLine(cell.Value); Console.WriteLine(value);
        var array = new int[3]; array[1] = 8;
        Change(ref array[1], ref Shared); Console.WriteLine(array[1]); Console.WriteLine(Shared);
        ref int alias = ref At(array, 1); alias += 9; Console.WriteLine(array[1]);
        Set(out long large); Console.WriteLine(large);
        Console.WriteLine(value.ToString()); Console.WriteLine(large.ToString());
    }
}
