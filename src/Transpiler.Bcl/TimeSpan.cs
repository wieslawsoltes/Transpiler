using System;

namespace Transpiler.Bcl;

/// <summary>Tick-exact duration arithmetic. Parsing, formatting and compound factories are separate contracts.</summary>
public readonly struct TimeSpan : IEquatable<TimeSpan>, IComparable<TimeSpan>, IComparable
{
    public const long TicksPerMicrosecond = 10;
    public const long TicksPerMillisecond = 10000;
    public const long TicksPerSecond = 10000000;
    public const long TicksPerMinute = 600000000;
    public const long TicksPerHour = 36000000000;
    public const long TicksPerDay = 864000000000;
    private readonly long _ticks;
    public static readonly TimeSpan Zero = new(0);
    public static readonly TimeSpan MinValue = new(long.MinValue);
    public static readonly TimeSpan MaxValue = new(long.MaxValue);
    public TimeSpan(long ticks) { _ticks = ticks; }
    public TimeSpan(int hours, int minutes, int seconds) : this(0, hours, minutes, seconds, 0) { }
    public TimeSpan(int days, int hours, int minutes, int seconds) : this(days, hours, minutes, seconds, 0) { }
    public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds)
    {
        long total = (((long)days * 24 + hours) * 60 + minutes) * 60 + seconds;
        total = total * 1000 + milliseconds;
        if (total < long.MinValue / TicksPerMillisecond || total > long.MaxValue / TicksPerMillisecond)
            throw new ArgumentOutOfRangeException(nameof(days));
        _ticks = total * TicksPerMillisecond;
    }
    public long Ticks => _ticks;
    public int Days => (int)(_ticks / TicksPerDay);
    public int Hours => (int)(_ticks / TicksPerHour % 24);
    public int Minutes => (int)(_ticks / TicksPerMinute % 60);
    public int Seconds => (int)(_ticks / TicksPerSecond % 60);
    public int Milliseconds => (int)(_ticks / TicksPerMillisecond % 1000);
    public int Microseconds => (int)(_ticks / TicksPerMicrosecond % 1000);
    public int Nanoseconds => (int)(_ticks % 10) * 100;
    public double TotalDays => (double)_ticks / TicksPerDay;
    public double TotalHours => (double)_ticks / TicksPerHour;
    public double TotalMinutes => (double)_ticks / TicksPerMinute;
    public double TotalSeconds => (double)_ticks / TicksPerSecond;
    public double TotalMilliseconds
    {
        get
        {
            double value = (double)_ticks / TicksPerMillisecond;
            if (value > long.MaxValue / TicksPerMillisecond) return long.MaxValue / TicksPerMillisecond;
            if (value < long.MinValue / TicksPerMillisecond) return long.MinValue / TicksPerMillisecond;
            return value;
        }
    }
    public double TotalMicroseconds => (double)_ticks / TicksPerMicrosecond;
    public double TotalNanoseconds => (double)_ticks * 100;
    public static TimeSpan FromTicks(long value) => new(value);
    private static TimeSpan Units(long value, long scale)
    {
        if (value < long.MinValue / scale || value > long.MaxValue / scale) throw new ArgumentOutOfRangeException(nameof(value));
        return new TimeSpan(value * scale);
    }
    private static TimeSpan Interval(double value, long scale)
    {
        if (double.IsNaN(value)) throw new ArgumentException("Duration must not be NaN.");
        double ticks = value * scale;
        if (ticks > (double)long.MaxValue || ticks < (double)long.MinValue) throw new OverflowException();
        if (ticks == (double)long.MaxValue) return MaxValue;
        return new TimeSpan(checked((long)ticks));
    }
    public static TimeSpan FromDays(int days) => Units(days, TicksPerDay);
    public static TimeSpan FromHours(int hours) => Units(hours, TicksPerHour);
    public static TimeSpan FromMinutes(long minutes) => Units(minutes, TicksPerMinute);
    public static TimeSpan FromSeconds(long seconds) => Units(seconds, TicksPerSecond);
    public static TimeSpan FromMilliseconds(long milliseconds) => Units(milliseconds, TicksPerMillisecond);
    public static TimeSpan FromMicroseconds(long microseconds) => Units(microseconds, TicksPerMicrosecond);
    public static TimeSpan FromDays(double value) => Interval(value, TicksPerDay);
    public static TimeSpan FromHours(double value) => Interval(value, TicksPerHour);
    public static TimeSpan FromMinutes(double value) => Interval(value, TicksPerMinute);
    public static TimeSpan FromSeconds(double value) => Interval(value, TicksPerSecond);
    public static TimeSpan FromMilliseconds(double value) => Interval(value, TicksPerMillisecond);
    public static TimeSpan FromMicroseconds(double value) => Interval(value, TicksPerMicrosecond);
    public TimeSpan Add(TimeSpan value) => this + value;
    public TimeSpan Subtract(TimeSpan value) => this - value;
    public TimeSpan Negate() => -this;
    public TimeSpan Duration() => _ticks < 0 ? -this : this;
    public bool Equals(TimeSpan other) => _ticks == other._ticks;
    public override bool Equals(object? other) => other is TimeSpan value && Equals(value);
    public static bool Equals(TimeSpan left, TimeSpan right) => left == right;
    public override int GetHashCode() => (int)_ticks ^ (int)(_ticks >> 32);
    public int CompareTo(TimeSpan other) => Compare(this, other);
    public int CompareTo(object? other) => other == null ? 1 : other is TimeSpan value ? CompareTo(value) : throw new ArgumentException("Expected TimeSpan.");
    public static int Compare(TimeSpan left, TimeSpan right) => left._ticks < right._ticks ? -1 : left._ticks > right._ticks ? 1 : 0;
    public static TimeSpan operator +(TimeSpan value) => value;
    public static TimeSpan operator -(TimeSpan value) => new(checked(-value._ticks));
    public static TimeSpan operator +(TimeSpan left, TimeSpan right) => new(checked(left._ticks + right._ticks));
    public static TimeSpan operator -(TimeSpan left, TimeSpan right) => new(checked(left._ticks - right._ticks));
    public static bool operator ==(TimeSpan left, TimeSpan right) => left._ticks == right._ticks;
    public static bool operator !=(TimeSpan left, TimeSpan right) => left._ticks != right._ticks;
    public static bool operator <(TimeSpan left, TimeSpan right) => left._ticks < right._ticks;
    public static bool operator >(TimeSpan left, TimeSpan right) => left._ticks > right._ticks;
    public static bool operator <=(TimeSpan left, TimeSpan right) => left._ticks <= right._ticks;
    public static bool operator >=(TimeSpan left, TimeSpan right) => left._ticks >= right._ticks;
}
