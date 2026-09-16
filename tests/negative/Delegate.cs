using System; public static class Program { static int Double(int x) => x * 2; public static void Main() { Func<int,int> f = Double; Console.WriteLine(f(3)); } }
