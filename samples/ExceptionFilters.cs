using System;

public static class Program
{
    private static int State;

    private static void Callee()
    {
        try
        {
            State = 1;
            throw new InvalidOperationException("failure");
        }
        finally
        {
            Console.WriteLine("callee finally");
            State = 2;
        }
    }

    private static bool Inspect()
    {
        Console.WriteLine("filter sees");
        Console.WriteLine(State);
        return true;
    }

    public static void Main()
    {
        try { Callee(); }
        catch (InvalidOperationException) when (Inspect())
        {
            Console.WriteLine("catch sees");
            Console.WriteLine(State);
        }
    }
}
