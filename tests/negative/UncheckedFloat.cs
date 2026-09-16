using System; public static class Program { static int Convert(double x) => unchecked((int)x); public static void Main() { Console.WriteLine(Convert(3.5)); } }
