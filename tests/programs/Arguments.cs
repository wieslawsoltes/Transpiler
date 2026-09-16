using System;
public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine(args.Length);
        foreach (string argument in args) Console.WriteLine(argument);
        return 7;
    }
}
