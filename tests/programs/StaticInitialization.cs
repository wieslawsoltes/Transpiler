using System;
public class State
{
    public static int Count;
    static State() { Count = 7; Console.WriteLine("initialized once"); }
    public static int Read() => Count;
}
public class Broken
{
    public static int Value;
    static Broken() { Console.WriteLine("failure attempted once"); throw new InvalidOperationException("boom"); }
}
public static class Program
{
    public static void Main()
    {
        Console.WriteLine(State.Read()); State.Count++; Console.WriteLine(State.Read());
        for (int i = 0; i < 2; i++)
            try { Console.WriteLine(Broken.Value); } catch (TypeInitializationException) { Console.WriteLine("cached initialization failure"); }
    }
}
