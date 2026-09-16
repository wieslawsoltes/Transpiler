using System;
public class Base
{
    public int Value;
    public Base(int value) { Value = value; }
    public virtual int Read(int add) => Value + add;
    public int Plain() => Value;
}
public class Derived : Base
{
    public int Other;
    public Derived(int value) : base(value) { Other = 4; }
    public override int Read(int add) => base.Read(add) * Other;
}
public class NewSlot : Derived
{
    public NewSlot(int value) : base(value) { }
    public new virtual int Read(int add) => add + 100;
}
public static class Program
{
    public static void Main()
    {
        Base value = new Derived(7);
        Console.WriteLine(value.Read(3)); Console.WriteLine(value.Plain());
        Base hidden = new NewSlot(2);
        Console.WriteLine(hidden.Read(3)); Console.WriteLine(((NewSlot)hidden).Read(3));
        Console.WriteLine(value is Derived); Console.WriteLine(value is NewSlot);
        object same = value; Console.WriteLine(object.ReferenceEquals(value, same));
        Console.WriteLine(value == same);
        object boxed = 42; Console.WriteLine((int)boxed); Console.WriteLine(boxed);
        object large = 9007199254740993L; Console.WriteLine((long)large);
        Console.WriteLine(object.ReferenceEquals(boxed, (object)42));
        try { Console.WriteLine((long)boxed); } catch (InvalidCastException) { Console.WriteLine("bad unbox"); }
        try { Console.WriteLine(((NewSlot)value).Other); } catch (InvalidCastException) { Console.WriteLine("bad cast"); }
        Base missing = null;
        try { Console.WriteLine(missing.Plain()); } catch (NullReferenceException) { Console.WriteLine("callvirt null"); }
    }
}
