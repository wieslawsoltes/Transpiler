# Duration, provider, timeout and periodic timer contracts

Implemented 2026-09-17. Hosted ABI: **host-clock-v2**. Library policy remains `portable-bcl-v1` and metadata schema remains 2. This extends the original Int32 clock milestone; it does not implement a CLR thread pool or execution-context capture.

## Implemented APIs

| Type | Implemented surface |
|---|---|
| TimeSpan | Tick/component constructors (through milliseconds), constants and static bounds, tick/component/total properties, single-unit integral and double factories, equality/hash/comparison, checked addition/subtraction/negation and Duration |
| TimeProvider | System, protected constructor, virtual CreateTimer/GetTimestamp/TimestampFrequency, GetElapsedTime overloads; user subclasses and ITimer implementations are translated managed code |
| Task.Delay | Int32 and TimeSpan, cancellation overloads, and TimeSpan + TimeProvider with/without cancellation |
| CancellationTokenSource | Int32 and TimeSpan constructors, TimeSpan + TimeProvider constructor, Int32/TimeSpan CancelAfter, disposal and timer-aware TryReset |
| Task / Task<T> | WaitAsync(TimeSpan), TimeSpan + CancellationToken, TimeSpan + TimeProvider, and TimeSpan + TimeProvider + CancellationToken |
| Timer / TimerCallback / ITimer | Callback-only constructor; Int32/UInt32/Int64/TimeSpan due-time and period constructors/Change overloads; Dispose and DisposeAsync; interface dispatch |
| PeriodicTimer | TimeSpan and TimeSpan + TimeProvider constructors; Period property, single-consumer WaitForNextTickAsync, coalescing, cancellation and Dispose |

Parsing/formatting and compound multi-unit TimeSpan factories are not supplied by this slice. Neither are calendar/time-zone TimeProvider APIs, Timer.ActiveCount, Timer.Dispose(WaitHandle), OS wait handles, context capture, native parallel callbacks, or automatic finalizer-based cleanup. Missing APIs remain compiler errors. Use explicit disposal for owned timers.

## Standalone host usage

Compile [samples/TimeServices.cs](../samples/TimeServices.cs) as a library:

```sh
dotnet build Transpiler.slnx -c Release
dotnet src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll compile samples/TimeServices.cs --library --bcl portable --target js --out time-services.mjs
dotnet src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll compile samples/TimeServices.cs --library --bcl portable --target py --out time_services.py
```

```js
import {invokeAsync, runtimeInfo} from './time-services.mjs';
console.log(await invokeAsync('TimeServices::Delayed', [25]));
console.log(await invokeAsync('TimeServices::TimeoutWithoutCancelingInput'));
console.log(await invokeAsync('TimeServices::CountTicks', [3]));
console.log(runtimeInfo().activeTimers); // 0 after all three operations retire
```

```python
import asyncio
from time_services import invoke_async, runtime_info

async def main():
    print(await invoke_async('TimeServices::Delayed', [25]))
    print(await invoke_async('TimeServices::TimeoutWithoutCancelingInput'))
    print(await invoke_async('TimeServices::CountTicks', [3]))
    print(runtime_info()['activeTimers'])

asyncio.run(main())
```

Blocking generated main/Wait/Result do not pump a native event loop. Use the asynchronous host entry points or stream adapters. There is no new ownership transfer for arbitrary Task results: aborting a host wait is distinct from canceling a managed operation.

## Durations and extended deadlines

TimeSpan uses signed Int64 ticks, preserving values beyond JavaScript Number's exact range. Arithmetic uses managed checked operations; double factories reject NaN and overflow instead of relying on target casts. Single-unit integral factories enforce their own bounds. General struct copies and boxing continue to use the existing value-semantics pipeline.

Duration-based Task/CTS/Timer timeouts convert TotalMilliseconds and truncate toward zero, matching the reviewed .NET 10 implementation. The allowed truncated interval is -1 through **4,294,967,294 ms**. Consequently a small negative fractional duration may truncate to zero, and a duration between -2 and -1 ms may truncate to -1. Do not replace the conversion with floor or validate raw ticks as though these were equivalent. PeriodicTimer instead accepts a duration truncating to at least 1 ms, or the exact InfiniteTimeSpan sentinel.

The internal Create/Change ABI now takes Int64 milliseconds (while handle identifiers remain Int32). JavaScript converts only these bounded millisecond values to Number. Native JS timeout requests are capped at 2,147,483,647 ms per arm; an early notification is rearmed against the original monotonic deadline. This avoids host timeout overflow turning a long wait into an immediate completion. Injected adapters receive the full bounded interval. Previously generated modules retain their embedded v1 runtime; mixing implementations from different generated modules is not a supported linking mechanism.

TimeProvider.System timestamps use the installed monotonic clock with a portable 10,000,000-tick/second frequency. This is a declared host counter, not a claim of native Stopwatch counter identity or precision. Custom providers can override frequency and timestamp. GetElapsedTime rejects nonpositive frequency; extreme floating conversions beyond the tested representable range remain outside a blanket cross-runtime guarantee.

## Timed-wait ownership

Timed WaitAsync owns an input-task observer, optional timer and cancellation registration. Its result mirrors input success, faults and cancellation, or completes with a TimeoutException/explicit wait-token cancellation. Every winning path detaches the losing resources. Timing out does **not** complete or cancel the input task, and canceled waiters do not remain registered on an indefinitely pending input.

Validation precedes fast paths. A completed input is returned unchanged after validation; for a pending input an already-canceled wait token takes precedence over a zero timeout. Infinite noncancellable waits return the original task. Fault propagation preserves the input exception object/array instead of manufacturing a timeout or cancellation.

A custom provider may invoke the callback synchronously, complete the input while CreateTimer executes, or throw during creation. The implementation checks completion after the returned timer is assigned and disposes that timer when necessary. Creation failure detaches any already-registered input observer. Completion state is published before cleanup to prevent reentrant cleanup callbacks from winning twice.

## Timer callback and disposal policy

HostClock native callbacks only publish readiness. Scheduler.RunOne executes translated managed callbacks serially. A public Timer rearms before invoking user code so a reentrant Change or Dispose wins over the earlier period. Missed periodic notifications coalesce; a slow callback does not cause parallel invocations or a burst attempting to replay every elapsed period. There is no cross-thread scheduling or ExecutionContext flow guarantee.

Period zero or -1 disables repetition. Due-time -1 disarms a timer without disposing its managed owner. Timer(callback) is initially disabled and passes the timer instance as callback state. Valid Change on a disposed timer returns false, matching the .NET 10 oracle; invalid duration arguments still throw before that check.

Dispose removes the host handle and callback/state references. DisposeAsync additionally waits for any callback already executing on the cooperative stack. Calling it inside a callback returns a pending ValueTask that completes after callback exit; synchronously blocking on that same result inside the callback would be a causal deadlock, not an operation the runtime can safely force complete. Timer callback exceptions propagate through the host pump; this is not CLR unhandled-thread-pool process termination behavior.

CTS retains the original queued-callback/ever-queued distinction. TryReset succeeds only before cancellation or any queued callback, and only for its own default-provider timer. Arbitrary custom-provider timers are not resettable by CTS, even when a custom provider returns a public Timer. Custom CTS construction forwards the original duration to CreateTimer, whereas Task.Delay and timed WaitAsync forward their validated, truncated milliseconds.

## PeriodicTimer consumption

PeriodicTimer exposes one pending wait at a time. Multiple ticks coalesce into a signal. Starting a second wait before consuming the first is rejected even when the first has completed. A source-backed operation is reset only after GetResult; stale or early reads cannot reset a different active wait.

Canceling a wait preserves the cancellation token and leaves the timer alive for later waits. Dispose wakes a pending wait with false and voids an unconsumed tick. All later uncanceled waits return false. Period changes rearm the timer; zero/sub-millisecond invalid periods and changes after disposal are tested separately. Explicit disposal is required: this hosted implementation does not provide the CLR PeriodicTimer finalizer.

## Validation and primary references

TimeValues runs on CoreCLR and both targets in Debug/Release under instruction and SSA emission. It covers large ticks, signs/components/hash, factories, overflow, exact duration boundaries, argument/state ordering, immediate/infinite waits and disposal results.

`tests/time_batch.py` adds four Debug/Release × instruction/SSA configurations. Each executes **22 scenarios per target**, for **176 translated lifecycle scenario executions**, deterministic repeated emission, managed-body provenance assertions and a CoreCLR oracle built from the same C# source. Scenarios include all timed-wait winners, long deadlines, periodic callbacks, callback Change/DisposeAsync, custom-provider synchronous completion/failure, tick coalescing/cancellation and native asyncio/Node operation. The older 48 timer lifecycle scenarios remain in the unfiltered gate.

The current complete/filtered report is the evidence of observed success; configured counts alone are not. See [validation](validation-summary.md).

Primary implementation contracts reviewed at the pinned .NET 10 tag:

- [Task and WaitAsync](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs)
- [Timer](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/Timer.cs)
- [PeriodicTimer](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/PeriodicTimer.cs)
- [TimeProvider](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/Common/src/System/TimeProvider.cs)
- [TimeSpan](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/TimeSpan.cs)
- [CancellationTokenSource](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs)

The portable implementations are original code, not imported native timer queues. Broader CLI identity, verification, reflection, object-collector integration and native backends remain governed by [the remaining implementation plan](implementation-plan.md).
