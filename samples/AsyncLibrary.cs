using System.Threading.Tasks;

public static class Kernel
{
    public static async Task<long> Calculate(int count)
    {
        long result = 9007199254740993L;
        for (int i = 0; i < count; i++)
        {
            await Task.Yield();
            result += i;
        }
        return result;
    }

    public static async Task<string> Text()
    {
        await Task.Yield();
        return "translated C# state machine";
    }

    public static object Make() => new object();
}
