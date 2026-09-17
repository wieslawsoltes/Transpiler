using System;
namespace Transpiler.Bcl.Threading;

public static class Timeout
{
    public const int Infinite = -1;
    public static readonly TimeSpan InfiniteTimeSpan = TimeSpan.FromTicks(-TimeSpan.TicksPerMillisecond);
    internal const long MaximumMilliseconds = 4294967294;
    internal static long Validate(TimeSpan value, string name)
    {
        // Match the framework's floating TotalMilliseconds conversion, truncating toward zero.
        return Validate(checked((long)value.TotalMilliseconds), name);
    }
    internal static long Validate(long value, string name)
    {
        if (value < -1 || value > MaximumMilliseconds) throw new ArgumentOutOfRangeException(name);
        return value;
    }
    internal static long Unsigned(uint value) => value == uint.MaxValue ? -1 : value;
}
