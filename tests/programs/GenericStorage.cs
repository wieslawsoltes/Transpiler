using System;
public class Box<T>
{
    public static int Count;
    public T Value;
    static Box() { Count = 10; }
    public Box(T value) { Value = value; Count++; }
    public T Get() => Value;
    public U Echo<U>(U value) => value;
    public Box<string> Other() => new Box<string>("separate context");
}
public static class Program
{
    static T Id<T>(T x) => x;
    static U Second<T,U>(T ignored,U value) => Id<U>(value);
    public static void Main()
    {
        var a = new Box<int>(42); var b = new Box<long>(9007199254740993L);
        Console.WriteLine(a.Get()); Console.WriteLine(b.Get());
        Console.WriteLine(Box<int>.Count); Console.WriteLine(Box<long>.Count);
        new Box<int>(7); Console.WriteLine(Box<int>.Count); Console.WriteLine(Box<long>.Count);
        Console.WriteLine(a.Echo<string>("generic method"));
        Console.WriteLine(a.Other().Get());
        Console.WriteLine(Second<int,string>(1,"substitution"));
    }
}
