using System;
public class BrokenFilterType
{
    public static int Value;
    static BrokenFilterType() { try { throw new ArgumentException("init"); } finally { Console.WriteLine("initializer finally"); } }
}
public static class Program
{
    static int State;
    static void Leaf()
    {
        try { State = 1; throw new InvalidOperationException("original"); }
        finally { Console.WriteLine("leaf finally"); State = 2; }
    }
    static bool Observe(string name, bool result) { Console.WriteLine(name); Console.WriteLine(State); return result; }
    static bool FilterThrows()
    {
        try { Console.WriteLine("throwing filter"); throw new ArgumentException("discard me"); }
        finally { Console.WriteLine("filter helper finally"); }
    }
    static bool InnerHandled()
    {
        try { try { throw new ArgumentException(); } finally { Console.WriteLine("nested helper finally"); } }
        catch (ArgumentException) when (Observe("nested helper filter", true)) { Console.WriteLine("nested helper catch"); }
        return false;
    }
    static void Middle()
    {
        try { Leaf(); }
        catch (Exception) when (Observe("middle reject", false)) { Console.WriteLine("unreachable"); }
        finally { Console.WriteLine("middle finally"); }
    }
    static void Replacement()
    {
        try { Leaf(); } finally { Console.WriteLine("replacing finally"); throw new ArgumentException("replacement"); }
    }
    static bool Assign(ref int value) { value += 7; Console.WriteLine(value); return true; }
    public static void Main()
    {
        try { Middle(); }
        catch (Exception) when (FilterThrows()) { Console.WriteLine("unreachable"); }
        catch (Exception) when (InnerHandled()) { Console.WriteLine("unreachable"); }
        catch (Exception e) when (Observe("outer accept", true)) { Console.WriteLine(e.Message); Console.WriteLine(State); }
        try { Replacement(); }
        catch (InvalidOperationException) when (Observe("original selected", true)) { Console.WriteLine("unreachable"); }
        catch (ArgumentException e) when (Observe("replacement selected", true)) { Console.WriteLine(e.Message); }
        int local = 10;
        try { Leaf(); } catch (InvalidOperationException) when (Assign(ref local)) { Console.WriteLine(local); }
        var original = new Exception("identity");
        try
        {
            try { throw original; }
            catch (Exception e) when (object.ReferenceEquals(e, original)) { Console.WriteLine("rethrow"); throw; }
        }
        catch (Exception e) when (object.ReferenceEquals(e, original)) { Console.WriteLine("identity preserved"); }
        try { Console.WriteLine(BrokenFilterType.Value); }
        catch (ArgumentException) when (Observe("must not see intercepted initializer", true)) { Console.WriteLine("unreachable"); }
        catch (TypeInitializationException) when (Observe("wrapper filter", true)) { Console.WriteLine("wrapped"); }
        // A locally caught exception inside finally must preserve the pending outer search plan.
        try
        {
            try { throw original; }
            finally
            {
                try { throw new ArgumentException(); }
                catch (ArgumentException) when (Observe("cleanup filter", true)) { Console.WriteLine("cleanup handled"); }
            }
        }
        catch (Exception e) when (object.ReferenceEquals(e, original)) { Console.WriteLine("pending identity preserved"); }
    }
}
