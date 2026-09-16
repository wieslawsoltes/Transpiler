using System; public static class Program { static T Id<T>(T x) => x; public static void Main() { Console.WriteLine(Id(42)); } }
