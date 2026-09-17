# Async streams and reusable completion sources

Implemented 2026-09-16 for the single-threaded cooperative `portable-bcl-v1` library policy. JavaScript and Python execute translated C# library algorithms and actual Roslyn-generated iterator state machines. This is not complete .NET scheduling or runtime compatibility.

## Run the sample

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/AsyncStreams.cs --bcl portable \
  --dispatch block --target js --out artifacts/streams.mjs \
  --manifest artifacts/streams.manifest.json
node artifacts/streams.mjs

dotnet "$CLI" compile samples/AsyncStreams.cs --bcl portable \
  --dispatch block --target py --out artifacts/streams.py
python3 artifacts/streams.py
```

Both print:

```text
asynchronous cleanup completed
30
```

The sample yields squares after Task.Yield, exits enumeration early at 16, and awaits the iterator's finally block before printing the sum. Compile the same source with `--dispatch instruction` to use the reference emitter.

## Implemented contracts

| Contract | Behavior |
|---|---|
| IValueTaskSource / IValueTaskSource<T> | Status, token, continuation and result protocol |
| ManualResetValueTaskSourceCore<T> | Sequential completion, reset/version validation, error/status, configurable queued continuation |
| ValueTask / ValueTask<T> | Inline/Task/source storage, awaiters, AsTask, Preserve, state checks and selected factory/equality members |
| IAsyncEnumerable<T> / IAsyncEnumerator<T> | Generic enumeration with cancellation and asynchronous move/disposal |
| AsyncIteratorMethodBuilder | Existing managed state-machine continuation machinery |
| ConfiguredCancelableAsyncEnumerable<T> | Token and await-policy forwarding, including chained reconfiguration |
| IAsyncDisposable / ConfiguredAsyncDisposable | Awaited disposal, including configured await using |
| ExceptionDispatchInfo | Capture, SourceException, instance/static Throw with exception-object identity only |

These public framework identities are bound by explicit LibrarySubstitution entries. The portable implementations themselves use independent C# namespaces and are compiled as ordinary managed IL. Missing members are still rejected by exact linkage.

## Source operation state

A completion source can represent successive operations. A ValueTask captures both source and version. Reset increments the short version, clears result/error/callback state, and makes values from the previous version invalid until token wraparound. Do not copy a mutable completion core into independent storage and expect it to share state; its containing field must be used consistently.

GetResult requires completion. OnCompleted accepts one consumer registration per version. Completion clears the registered callback/state before invoking it, so a callback may reset and reuse the source. A continuation registered after completion is queued even when RunContinuationsAsynchronously is false. The queue is the portable managed Scheduler, not a native thread pool.

Consume a source-backed value once. AsTask produces a Task which can be awaited repeatedly; Preserve provides an analogous Task-backed ValueTask. Repeatedly converting or consuming the original source-backed value is not a supported usage. ToString on a completed generic source consumes at most one result; the regression suite uses a source that resets when consumed to verify this.

### Continuation flags

| Awaiter call | Flags forwarded to a custom source |
|---|---|
| GetAwaiter().OnCompleted | UseSchedulingContext + FlowExecutionContext |
| GetAwaiter().UnsafeOnCompleted | UseSchedulingContext |
| ConfigureAwait(false).GetAwaiter().OnCompleted | FlowExecutionContext |
| ConfigureAwait(false).GetAwaiter().UnsafeOnCompleted | None |

Forwarding is observable to custom source code. It is not an implementation of ExecutionContext or SynchronizationContext. The portable completion core operates within the declared no-context, single-thread scheduler. Explicit context APIs remain unsupported.

## Cancellation, errors and disposal

GetAsyncEnumerator receives the configured token. Roslyn's imported code handles EnumeratorCancellation and linking of distinct iterator/enumerator tokens through the portable CTS implementation. Cancellation is cooperative: the iterator or awaited operation must observe it.

MoveNextAsync and DisposeAsync can both suspend. Early break, await using, nested streams, repeated independent enumeration, mutable struct results, and awaited finally blocks are tested. Exception-object identity is preserved through generated rethrow/cleanup paths. A new failure from disposal can replace the earlier active exception, as observed by the differential fixtures.

The completion bridge distinguishes a Canceled source from a Faulted source whose GetResult throws OperationCanceledException. It does not relabel every such fault as cancellation. AsTask and host invocation must not consume the same operation twice.

## Host interoperability

The existing `invokeAsync`/`invoke_async` ABI supports exported Task and ValueTask results, including source-backed results. JavaScript can provide a `yieldHost` callback to supply a pending result; Python can schedule completion through asyncio. The dedicated host test verifies one consumption and callback-state release.

Update 2026-09-17: the generated `stream` API now adapts exports declared as IAsyncEnumerable<T> to native JavaScript/Python async-iterator protocols. Python provides asynchronous context management and aclose; use a close scope for early break. Each managed cursor consumes one outstanding operation at a time and retains pending cleanup for a retry rather than disposing concurrently. See [host-stream ownership and cancellation](host-streams.md). This is a protocol adapter, not arbitrary object serialization or native generator send/asend support. Host pump limits count steps; they do not interrupt an infinite managed call or a nonresolving completion hook.

## Verification

`ValueTaskSources.cs` tests normal source completion, stale tokens, flags, asynchronous callbacks, reentrant reset, fault/cancellation and struct results. `ValueTaskSourceEdges.cs` tests single ToString consumption, status-versus-exception classification, duplicate registration/completion and 16-bit version rollover. `AsyncStreams.cs` and `AsyncStreamEdges.cs` test iterator/disposal/cancellation semantics in Debug and Release.

The instruction/block equivalence gate includes async streams and source-backed values. The host-source gate checks that completion-core and Task-conversion algorithms appear as emitted managed bodies, not new JS/Python intrinsic implementations. See [testing](testing.md) for the full gate and [research](research/async-streams-2026-09-16.md) for source references.

No claim is made for concurrent Reset/completion/registration, pool allocation parity, context capture, arbitrary async LINQ, general timer/thread-pool APIs, full exception traces or all adversarial IL. Update 2026-09-17: managed exception filters now use the tested two-pass search protocol; see [linking and verification](linking-verification.md). This does not add native trace/context/thread parity.

The separately implemented [host-clock-v2 contract](host-clocks.md) adds duration/provider delays, timed CTS/CancelAfter and [serialized/coalescing timers](time-services.md). Native callbacks publish readiness; translated managed scheduler code performs completion. Task and stream adapters wait on native notifications when idle, including awaited iterator cleanup. This does not add cross-thread or execution-context semantics.
