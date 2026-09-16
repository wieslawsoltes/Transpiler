using System;
public static class Program
{
    public static void Main()
    {
        var values = new int[12];
        for (int i = 0; i < values.Length; i++) values[i] = i * i - 3;
        int sum = 0; foreach (int value in values) sum += value;
        Console.WriteLine(sum);
        var small = new byte[2]; small[0] = 255; Console.WriteLine((int)small[0]);
        var longValues = new long[2]; longValues[1] = 9007199254740993L; Console.WriteLine(longValues[1]);
        object[] refs = new string[2]; refs[0] = "alpha"; Console.WriteLine(refs[0]);
        try { refs[1] = new object(); } catch (ArrayTypeMismatchException) { Console.WriteLine("covariance store checked"); }
        try { Console.WriteLine(values[-1]); } catch (IndexOutOfRangeException) { Console.WriteLine("negative index checked"); }
        try { Console.WriteLine(values[12]); } catch (IndexOutOfRangeException) { Console.WriteLine("upper bound checked"); }
        int[] missing = null;
        try { Console.WriteLine(missing[0]); } catch (NullReferenceException) { Console.WriteLine("null array checked"); }
    }
}
