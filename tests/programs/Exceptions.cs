using System;
public static class Program
{
    static int Nested(bool fail)
    {
        try
        {
            try { if (fail) throw new InvalidOperationException("original"); return 42; }
            finally { Console.WriteLine("inner finally"); }
        }
        catch (InvalidOperationException e) { Console.WriteLine(e.Message); return -1; }
        finally { Console.WriteLine("outer finally"); }
    }
    static void Replace()
    {
        try { throw new Exception("discarded"); }
        finally { throw new ArgumentException("replacement"); }
    }
    static void InnerCatchInFinally()
    {
        try { throw new InvalidOperationException("pending"); }
        finally
        {
            try { throw new ArgumentException("temporary"); }
            catch (ArgumentException e) { Console.WriteLine(e.Message); }
            Console.WriteLine("resume pending exception");
        }
    }
    static void Rethrow()
    {
        try { throw new Exception("identity preserved"); }
        catch (Exception e) { Console.WriteLine(e.Message); throw; }
    }
    public static void Main()
    {
        Console.WriteLine(Nested(false)); Console.WriteLine(Nested(true));
        try { Replace(); } catch (ArgumentException e) { Console.WriteLine(e.Message); }
        try { InnerCatchInFinally(); } catch (InvalidOperationException e) { Console.WriteLine(e.Message); }
        try { Rethrow(); } catch (Exception e) { Console.WriteLine(e.Message); }
        try { throw null; } catch (NullReferenceException) { Console.WriteLine("throw null checked"); }
        for (int i = 0; i < 3; i++) { try { if (i == 1) continue; Console.WriteLine(i); } finally { Console.WriteLine("loop finally"); } }
    }
}
