# Host clocks, delayed tasks and timed cancellation

Contract: `host-clock-v1`. Updated 2026-09-17. This is a single-threaded capability for the JavaScript and Python hosted profiles, not a thread pool, a general `System.Threading.Timer` implementation, or a claim of every Task/CTS overload.

## Implemented surface

With `--bcl portable`, the compiler links the managed implementations of `Task.Delay(Int32)`, `Task.Delay(Int32, CancellationToken)`, `CancellationTokenSource(Int32)` and `CancellationTokenSource.CancelAfter(Int32)`. The existing `TryReset`, `Cancel` and `Dispose` methods now participate in timer ownership. Delays accept -1 (infinite), zero and positive Int32 milliseconds. Values below -1 throw before state-dependent checks.

`Task.Delay(0)` completes immediately unless the supplied token is canceled. A pre-canceled token produces a canceled task with that token. `CancellationTokenSource(0)` is immediately canceled, whereas `CancelAfter(0)` schedules a host notification. Infinite delays allocate no timer; a cancellable infinite delay still owns a registration. Blocking `Wait`, `Result` and the synchronous generated `main` entry do not run an external event loop. Positive delays must be driven through `invokeAsync`, `invoke_async`, or the native stream adapter.

TimeSpan/TimeProvider overloads, periodic/public timers, timed WaitAsync, `Task.Run`, ExecutionContext, SynchronizationContext and cross-thread callbacks remain outside this contract. A missing member is a compiler diagnostic, not a silent fallback.

## Running a library

The example is [samples/HostTimers.cs](../samples/HostTimers.cs). From the repository root:

```sh
dotnet build Transpiler.slnx -c Release
dotnet src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll compile samples/HostTimers.cs --library --bcl portable --target js --out host-timers.mjs
dotnet src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll compile samples/HostTimers.cs --library --bcl portable --target py --out host_timers.py
```

JavaScript, in an importing module:

```js
import {invokeAsync, runtimeInfo} from './host-timers.mjs';
console.log(await invokeAsync('HostTimers::Delayed', [25]));
console.log(await invokeAsync('HostTimers::CancellationIdentity', [25]));
console.log(runtimeInfo().activeTimers); // 0
```

Python, with a running asyncio loop:

```python
import asyncio
from host_timers import invoke_async, runtime_info

async def main():
    print(await invoke_async('HostTimers::Delayed', [25]))
    print(await invoke_async('HostTimers::CancellationIdentity', [25]))
    print(runtime_info()['activeTimers'])  # 0

asyncio.run(main())
```

The default JS adapter uses monotonic `performance.now` and one-shot host timeouts. The Python adapter uses `time.monotonic` and `asyncio.call_later`. Native Python timers belong to the loop on which they were armed; sharing live timers across loops is rejected. Event-loop scheduling and host timer resolution can make completion late. No real-time deadline guarantee is made.

## Injectable adapter

Generated modules expose `setClock(adapter)` and `set_clock(adapter)`. Passing null/None, or no argument, restores the native adapter. Replacement is rejected while any timer or host waiter remains owned by the runtime.

Both adapters provide three methods: `now()` returns finite monotonic milliseconds; `schedule(milliseconds, notification)` returns an opaque native handle; `cancel(handle)` cancels that handle. Scheduling and cancellation occur on the same host execution context. Adapters must release native callbacks when canceled or delivered, must not execute unrelated managed work reentrantly, and should not throw from cancel. Cancellation is best-effort with respect to already delivered notifications; generation guards make stale notifications harmless. A test adapter may notify early or synchronously; the runtime checks the deadline and never executes the managed callback inline.

```js
class ManualClock {
    time = 0;
    events = new Set();
    now() { return this.time; }
    schedule(milliseconds, notification) {
        const event = {deadline: this.time + milliseconds, notification};
        this.events.add(event);
        return event;
    }
    cancel(event) { this.events.delete(event); }
    advance(milliseconds) {
        if (!Number.isFinite(milliseconds) || milliseconds < 0)
            throw new RangeError('Advance must be finite and nonnegative');
        this.time += milliseconds;
        for (const event of [...this.events]) {
            if (event.deadline <= this.time) {
                this.events.delete(event);
                event.notification();
            }
        }
    }
}
```

Advancing this clock publishes readiness. The host async operation then resumes and pumps managed execution. Custom JS `yieldHost` and stream `yield_host` callbacks retain their explicit scheduling behavior; they are not silently replaced by clock waits.

## Execution, ordering and wakeup protocol

Managed Task/CTS algorithms remain C# IL translated into the target language. Only six exact internal signatures on `[Transpiler.Bcl]Transpiler.Bcl.Tasks.HostClock` cross the host boundary: Create, Change, HasFired, Destroy, TakeReady and Signal. The intrinsic matcher checks assembly, complete type, static/instance flag, generic arity, argument types and result type.

Each timer owns a non-reused Int32 identifier, a callback reference, a deadline, a native-handle generation and separate ready/ever-queued state. The callback protocol is:

1. Native notification checks timer identity and generation, invalidates that notification and publishes readiness only when the monotonic deadline has arrived. Early wakeups are hints; the next pump rearms the remaining interval.
2. `Scheduler.RunOne` takes one ready timer callback, otherwise one FIFO continuation, and executes it in managed execution. Timer priority prevents a self-posting continuation from indefinitely starving a due timer.
3. A scheduler action signals all host waiters in a finally block. Task completion signals independently, even when no continuation is posted. The wake generation is captured before checking completion and pumping, preventing a second host operation from sleeping after the first operation consumes the final continuation.

When a default async adapter has no immediate managed work and a native timer is armed, it suspends on a notification rather than repeatedly sleeping/polling. Without a timer capability that can wake the operation, the previous bounded cooperative yield path remains. Pump budgets count iterations; they are not wall-clock timeouts and do not preempt synchronous managed execution. An unadvanced manual clock may therefore leave an operation pending. Use a host-owned watchdog/abort policy where required.

## Cancellation and ownership

`CancelAfter(-2)` throws ArgumentOutOfRangeException even when the source is disposed. Valid delays on a disposed source throw ObjectDisposedException. Already-canceled sources ignore valid rescheduling requests.

Disarming a timer cancels future native delivery but does not retract a callback already queued for managed execution. `TryReset` therefore disarms first and checks persistent ever-queued history. A queued callback makes reset fail, without deleting registrations or erasing the pending cancellation. A successful reset destroys its timer and unregisters previous callbacks. A canceled source never becomes uncanceled.

Delay completion and cancellation both detach the token registration and destroy the owned timer before completing the promise. Manual cancellation, source disposal, reset and stale callbacks have distinct state transitions. A canceled task intentionally retains its cancellation identity; detaching a subscription is not a promise that every object reachable from a task is immediately collectible.

Aborting a JS `invokeAsync` wait or canceling the Python host task detaches its host waiter. It does not assume ownership of the exported managed Task and does not implicitly cancel that Task. The application must expose or retain an explicit managed cancellation path. Stream adapters differ: they own their cursor and linked cancellation source, so return/aclose drains an outstanding move, consumes it once and awaits asynchronous disposal. A cleanup delay is not aborted by the signal that ended ordinary enumeration.

## Diagnostics and tests

`runtimeInfo()` / `runtime_info()` includes `clock: 'host-clock-v1'`, `activeTimers`, `readyTimers`, `hostWaiters` and `idlePolicy`. `activeTimers` includes a disarmed but still owned CTS timer; it reaches zero on reset/disposal, cancellation or delay completion. Ready counts include early notification hints. These are ownership diagnostics, not general heap/GC measurements.

The direct service suites contain 13 checks per host. Compiled lifecycle tests run 12 scenario groups per host under instruction and SSA emission, plus deterministic repeated emission and a real CoreCLR async oracle. TimerValidation is also included in Debug/Release ordinary and SSA differential runs. Tests exercise queued-reset history, early/stale/duplicate notification, synchronous adapter delivery, failed arm cleanup, broadcast wakeups, abort retirement, bulk cancellation, registration detachment and delayed iterator-finally cleanup.

The clock-specific CI job supplies fast feedback without replacing the full conformance job. Read `artifacts/conformance/report.json` for observed pass/fail counts, target environment and filter status; a filtered clock report does not establish a complete compiler-suite pass.

Reference semantics: [.NET 10 CancellationTokenSource source](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs). The implementation here is original and scoped to cooperative single-thread execution; it does not copy or emulate the native timer queue's threading guarantees.
