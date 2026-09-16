public static class Kernel
{
    public static long Add(long left, long right) => checked(left + right);
    public static int Gcd(int a, int b)
    {
        while (b != 0) { int next = a % b; a = b; b = next; }
        return a;
    }
    public static string Echo(string value) => value;
    public static bool Positive(int value) => value > 0;
}
