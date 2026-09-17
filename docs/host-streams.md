# Native host async streams

Implemented 2026-09-17. ABI policy: **managed-stream-v1**. This is an addition to generated metadata schema 2 and the portable-bcl-v1 library policy. It adapts exported managed streams to native JavaScript/Python iteration without replacing their C# state-machine or ValueTask implementation.

## Run the example

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll
mkdir -p artifacts/host-stream-demo

dotnet "$CLI" compile samples/HostStreams.cs --library --bcl portable \
  --dispatch block --target js --out artifacts/host-stream-demo/kernel.mjs \
  --manifest artifacts/host-stream-demo/javascript.manifest.json

dotnet "$CLI" compile samples/HostStreams.cs --library --bcl portable \
  --dispatch block --target py --out artifacts/host-stream-demo/kernel.py \
  --manifest artifacts/host-stream-demo/python.manifest.json

cp samples/host-streams/javascript.mjs samples/host-streams/python.py artifacts/host-stream-demo/
node artifacts/host-stream-demo/javascript.mjs
python3 artifacts/host-stream-demo/python.py
```

Both drivers exit early and print:

```text
managed enumerator disposed
30
0
```

The last zero is the active-stream count after awaited disposal, not a host-GC statistic.

## Export contract

Declare an eligible public static library export returning **IAsyncEnumerable<T>**. Generic instantiation and the portable implementation must be linked. The current ABI recognizes that declared interface exactly; arbitrary concrete return types, object-returning factories and Task<IAsyncEnumerable<T>> are not automatically adapted. `stream` rejects missing/ambiguous names, wrong arity, invalid options and absent bindings before invoking application code.

The stream factory and GetAsyncEnumerator are lazy: the first next/anext invokes them. Closing an unused adapter does not construct an enumerator. Each adapter is single-use and owns one enumeration; calling stream again creates a separate adapter. The returned managed factory is still responsible for the normal semantics of repeated enumeration.

```javascript
import { stream } from './kernel.mjs';
for await (const value of stream('StreamKernel::Squares', [20])) {
    console.log(value.toString()); // Int64 -> BigInt
    if (value === 16n) break;
}
```

```python
from kernel import stream
async with stream('StreamKernel::Squares', [20]) as values:
    async for value in values:
        print(value)
        if value == 16:
            break
```

**Python early exit needs a scope:** use async with as above, contextlib.aclosing, or an explicit aclose in finally. Plain async-for break is not a deterministic close protocol. Exhaustion and errors from the adapter's move/current operations perform cleanup automatically. There is no asynchronous finalizer which promises to repair an abandoned enumeration.

## Managed ownership and host operations

`Transpiler.Bcl.Interop.StreamCursor<T>` is C#, compiled through the ordinary PE/CIL pipeline. It owns the enumerator, a cancellation source and at most one outstanding move/dispose ValueTask. StreamContracts adds explicit host roots; StreamMetadata binds the closed cursor methods and scheduler. Incomplete cursor linkage fails with TR2220. The manifest shows real managed cursor bodies, not a new family of target-language BCL intrinsics.

The host layer owns scheduling and native iteration protocol. It never calls DisposeAsync concurrently with MoveNextAsync. A completed operation is removed from the cursor before GetResult; a throwing result therefore cannot be consumed again accidentally. Successful or failed terminal disposal retires the enumerator, cancellation source, ValueTask storage and host yield callback. A pending cleanup deliberately retains its cursor for a retry.

JavaScript provides next(), return(value), throw(error), Symbol.asyncIterator, and a conditional Symbol.asyncDispose method when that host symbol exists. throw closes and then propagates the host error; it does not inject that error into the managed iterator body. Python provides __aiter__/__anext__, aclose and asynchronous context management. Overlapping next/anext calls on one adapter are rejected; separate adapters may make progress cooperatively on the same event loop.

## Options and observability

| JavaScript | Python | Meaning |
|---|---|---|
| maxSteps | max_steps | Maximum pump iterations per ordinary move wait; default 100,000 |
| cleanupSteps | cleanup_steps | Maximum iterations per drain/disposal wait; defaults to move budget |
| yieldHost | yield_host | Trusted cooperative completion hook returning an awaitable |
| signal | cancel() / asyncio task cancellation | Host cancellation entry point |

Budgets are positive safe integers, capped at JavaScript's safe-integer range on both targets. The default JS yield uses a host timer turn; Python yields with asyncio.sleep(0). The callback can supply external managed completion, but **must not await operations on its own stream**. Direct detected reentrant close is rejected; arbitrary causal await cycles are not generally detectable.

Adapter `closed` is true only after terminal retirement or closure before acquisition. `pending` is null, move or dispose. These observations are not permission to overlap operations or mutate implementation fields. `runtimeInfo()` / `runtime_info()` adds `activeStreams` and `streamPolicy`. Active-stream accounting includes pending-cleanup cursors and excludes never-opened adapters. It is not an allocation/root count and does not root all objects itself.

String and Boolean elements are unwrapped, Int64/UInt64 retain exact target integer representations, and floating values retain tested NaN/signed-zero behavior. Struct Current values are copied by managed value semantics and exposed as opaque runtime wrappers; this is not a general object-to-JSON or cross-module ABI. Object/array values retain the existing wrapper policy.

## Cancellation and close during a move

The managed cursor passes an owned CTS token into GetAsyncEnumerator. JS AbortSignal or cancel(reason), and Python cancel(), request that token once. A signal which is already aborted does not start the factory. Python asyncio cancellation while awaiting the next item also requests managed cancellation during the drain path. It propagates the original CancelledError after successful cleanup; repeated interruption may instead report cleanup still pending.

Closing while a move is outstanding requests cancellation, waits for that same operation and consumes it before starting disposal. No replacement MoveNextAsync is issued. An item or managed cancellation/fault obtained while intentionally closing is consumed and discarded. Cleanup faults remain visible. An ordinary early break between items does not needlessly cancel the enumerator token before disposal.

Cancellation is cooperative. A source can ignore its token and require external completion. A budget does not interrupt an infinite managed call or a yield callback that never resolves, and it is not a wall-clock timeout. No thread pool, timer-backed BCL API, execution context or cancellation preemption is added by this ABI.

## Cleanup pending: retain, complete, retry

When an outstanding move or disposal cannot finish within the cleanup budget, the adapter raises **StreamCleanupPendingError**. It retains the exact operation and does not report closed, issue overlapping disposal, or pretend resources were released. The phase property identifies move or dispose. JS attaches cause and, where applicable, operationError; Python uses exception chaining.

Retain the adapter, arrange completion through the application's established mechanism, then await return()/aclose() again. The retry polls and consumes the existing operation, not a new one. Calling next/anext while cleanup is pending fails.

```javascript
import { stream, StreamCleanupPendingError } from './kernel.mjs';
const values = stream('Application::Export', [], { maxSteps: 64, cleanupSteps: 64 });
try {
    await values.next();
} catch (error) {
    if (!(error instanceof StreamCleanupPendingError)) throw error;
    // Complete the pending application operation through its own API.
    // Retain `values` until that completion is available, then:
    await values.return();
}
```

The example illustrates retry ownership, not a magic completion API. A source that never completes cannot be safely disposed concurrently under this contract. Ignoring a cleanup-pending result can leak application resources; dropping the adapter cannot guarantee managed finally execution.

## Native exception precedence

Managed move/current failure initiates disposal; a disposal failure can replace it, matching an explicit finally-style cleanup path. Host loop syntax has its own contract: JavaScript for-await preserves an existing body exception over a return/close failure, while Python context-manager cleanup failure becomes the active exception and chains the earlier one. Tests preserve those host differences instead of normalizing them away. A pending cleanup may consequently be masked by the JS loop's original exception; callers which need recovery should retain the adapter and inspect/finish its close explicitly.

## Validation and remaining scope

Four conformance configurations cover Debug/Release and instruction/block emitters. Each executes 22 JS and 23 Python lifecycle scenario groups, compares native-host streamed stdout against a CoreCLR consumer of the same assembly, checks exact managed cursor provenance, and repeats deterministic emission. Cases include custom IValueTaskSource counters, no overlapping move/dispose, ignored cancellation, deferred disposal, Current/acquisition failures, listener removal, repeated close, struct copies and independent adapters.

This does not provide a complete stream scheduler, cross-thread/event-loop use, bounded total execution, full serialization, runtime schema migration or general reflection. The rest of the compiler's stated exclusions remain. See [research](research/host-streams-2026-09-17.md) and [validation](validation-summary.md).
