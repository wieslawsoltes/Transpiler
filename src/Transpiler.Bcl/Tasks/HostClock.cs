using System;
using System.Runtime.CompilerServices;

namespace Transpiler.Bcl.Tasks;

/// <summary>Exact host-clock-v1 boundary. Native callbacks only publish readiness.</summary>
internal static class HostClock
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern int Create(long milliseconds, Action callback);
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Change(int handle, long milliseconds);
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern bool HasFired(int handle);
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Destroy(int handle);
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern Action? TakeReady();
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern void Signal();
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern double Now();
}

/// <summary>Single-owner timer; queued history survives changes until disposal.</summary>
internal sealed class HostTimer : IDisposable
{
    private int _handle;
    internal HostTimer(long milliseconds, Action callback)
    { _handle = HostClock.Create(milliseconds, callback); }
    internal void Change(long milliseconds) => HostClock.Change(_handle, milliseconds);
    internal bool TryReset()
    {
        if (_handle == 0) return false;
        HostClock.Change(_handle, -1);
        if (HostClock.HasFired(_handle)) return false;
        Dispose();
        return true;
    }
    public void Dispose()
    {
        int handle = _handle;
        _handle = 0;
        if (handle != 0) HostClock.Destroy(handle);
    }
}
