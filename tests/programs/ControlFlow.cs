using System;
public static class Program
{
    static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
    static int Choose(int n)
    {
        switch (n) { case 0: return 11; case 1: return 29; case 2: return -3; case 3: return 45; case 4: return 17; case 5: return 103; default: return 7; }
    }
    static int Sum(int n)
    {
        int sum = 0;
        for (int i = 0; i < n; i++) { if (i == 3) continue; if (i == 9) break; sum += i * i; }
        while (n > 0) { sum += Choose(n); n--; }
        do { sum--; } while (sum > 400);
        return sum;
    }
    public static void Main()
    {
        Console.WriteLine(Fib(12)); Console.WriteLine(Sum(12));
        for (int i = -1; i < 8; i++) Console.WriteLine(Choose(i));
        Console.WriteLine(Sum(0) < Sum(9) && Fib(4) == 3);
    }
}
