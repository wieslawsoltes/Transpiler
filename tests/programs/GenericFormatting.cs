using System;
[Flags] public enum Options : ulong { None=0, One=1, Two=2, High=9223372036854775808UL }
public enum Signed : int { Negative=-7, Zero=0, Positive=3 }
public struct Plain { public int Value; }
public class Label { }
public static class Program
{
    static string Text<T>(T value) => value.ToString();
    public static void Main()
    {
        Console.WriteLine(Text(true)); Console.WriteLine(Text(false)); Console.WriteLine(Text(1.0));
        Console.WriteLine(Text(uint.MaxValue)); Console.WriteLine(Text(9007199254740993L));
        Console.WriteLine(Text(new Plain())); Console.WriteLine(Text(new Label()));
        Console.WriteLine(Text(Options.One|Options.Two)); Console.WriteLine((object)Options.High);
        Console.WriteLine(Text(Options.High|Options.One)); Console.WriteLine(Text((Options)4));
        Console.WriteLine(Text(Signed.Negative)); Console.WriteLine((object)Signed.Positive); Console.WriteLine(Text((Signed)(-9)));
    }
}
