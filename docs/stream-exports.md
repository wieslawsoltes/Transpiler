# Generalized stream exports and factory ownership

Implemented 2026-09-20. Stream ABI **managed-stream-v2**, metadata schema 2, portable library policy `portable-bcl-v1`. This supersedes the original exact-interface-only discovery rule. The existing [host iteration protocol](host-streams.md) and [clock services](time-services.md) remain in effect.

## Supported declared results

| Declared result | Discovery and selection |
|---|---|
| `IAsyncEnumerable<T>` | Existing direct interface contract |
| Concrete class or struct implementing `IAsyncEnumerable<T>` | Closed implemented interfaces, including inherited base/interface contracts |
| Custom interface extending `IAsyncEnumerable<T>` | Closed inherited interface graph |
| `Task<S>` or `ValueTask<S>` | One asynchronous factory wrapper around a supported source `S` |
| `object`, `Task<object>`, `ValueTask<object>` | Explicit element selection among already-linked closed enumerable contracts; checked cast at acquisition |
| A source implementing multiple different `IAsyncEnumerable<T>` contracts | Explicit element selection; never choose an interface arbitrarily |

Concrete generic sources must be closed. Value-type sources are boxed and cast using ordinary managed semantics, not host duck typing. A factory is invoked exactly once per adapter, on its first next/anext, not by metadata inspection or adapter construction. Closing an unused adapter does not invoke it.

Nested wrappers such as `Task<Task<IAsyncEnumerable<T>>>`, arbitrary custom awaitables, runtime-only generic construction, ref-struct/pattern-only enumerables, and arbitrary dynamic loading are not supported discovery shapes. Methods returning unsupported shapes may still compile as ordinary exports; `stream` rejects the absent stream binding before executing them. Unsupported IL or library dependencies remain compiler errors.

## Running the standalone example

```sh
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll
mkdir -p artifacts/stream-factories-demo
dotnet "$CLI" compile samples/StreamFactories.cs --library --bcl portable --dispatch ssa --target js --out artifacts/stream-factories-demo/kernel.mjs
dotnet "$CLI" compile samples/StreamFactories.cs --library --bcl portable --dispatch ssa --target py --out artifacts/stream-factories-demo/kernel.py
cp samples/stream-factories/* artifacts/stream-factories-demo/
node artifacts/stream-factories-demo/javascript.mjs
python3 artifacts/stream-factories-demo/python.py
```

Both drivers print:

```text
9007199254740993
9007199254740994
9007199254740993
2
0 0 0
```

The example exports a concrete class, `Task<Counter>`, and `ValueTask<object>`. It exits one enumeration early, awaits managed asynchronous disposal, enumerates an erased result with explicit Int64 selection, and reports two disposals with no active streams, factories or timers. Generated modules run without a .NET process.

## Metadata and explicit selection

```js
import {stream, streamInfo} from './kernel.mjs';

const info = streamInfo('StreamFactories::Erased');
// {policy, returnType, sourceType, kind, requiresElement, elements}
for await (const value of stream('StreamFactories::Erased', [3], {
    elementType: 'System.Int64'
})) console.log(value.toString());
```

```python
from kernel import stream, stream_info

info = stream_info('StreamFactories::Erased')
async with stream('StreamFactories::Erased', [3], element_type='System.Int64') as values:
    async for value in values:
        print(value)
```

`kind` is `value`, `task` or `value-task`. `elements` contains canonical linked CLI type identities, including assembly scopes and closed generic arguments where necessary. Use the returned identities, not display names inferred from C# aliases. JS returns a frozen object/array; Python returns a detached dictionary with an immutable tuple. Neither inspection function runs the factory.

An erased result always requires `elementType`/`element_type`, even when only one candidate is linked. A non-erased source with one element infers it; multiple different elements require selection. An unknown identity, wrong arity, missing binding or ambiguous method name is rejected before application execution. Overloaded methods can be selected by their exact exported signature.

The object-erased catalog is the module's closed-world candidate set, **not proof that a particular returned object implements every choice**. The bridge casts the actual result to the selected interface. A wrong implemented contract produces a managed InvalidCastException; null results are rejected during acquisition. No reflection fallback, runtime compiler or JSON/object serialization is introduced.

## Compiler reachability and implementation origin

`StreamContracts.Elements` walks closed interfaces and base types with ordinal identities, cycle detection and a 4,096-node inspection limit. It does not infer a contract from method names. TR2221 identifies the graph budget. Existing constructed-type/method budgets also apply.

Stream discovery participates in the generic-specialization fixed point: factory bodies can introduce concrete source types after the initial export signatures were processed. Each discovered pair roots a closed `StreamFactory<TSource,TElement>` entry and the `StreamCursor<TElement>` ownership methods. The same discovery function builds metadata choices, avoiding a separate host-side guess about reachability. TR2220 rejects incomplete bridge/cursor/scheduler linkage.

`StreamFactory`, `IStreamFactory` and `StreamCursor` are C# in Transpiler.Bcl and become ordinary emitted managed methods. The manifest proves their origin and instruction counts. JavaScript/Python owns only host iteration, option checking, scheduling and value unwrapping. No executable IL interpreter or CLR process is embedded.

## Acquisition state and single consumption

```text
unopened -> factory pending -> enumerator acquired -> move pending -> current/end
                 |                    |                    |
                 +--------- close/drain ------------------+
                                      |
                               disposal pending -> closed
```

A Task factory is retained until it completes. A ValueTask factory is converted with **one AsTask call**, using the existing translated source bridge. It is never simultaneously awaited and converted, or converted again on cleanup retry. The factory holder clears its task field before fetching the result and performing the managed cast; source result exceptions cannot cause a second consumption.

The cursor owns a cancellation source before waiting for acquisition. When the factory completes it acquires `GetAsyncEnumerator` using that token and checks for a null enumerator. Factory faults/cancellation, null Task/results, invalid casts and acquisition errors retire cursor ownership. Multiple adapters own independent acquisitions; overlapping next/anext calls on one adapter are rejected throughout acquisition, not only during moves.

Values retain existing representation rules: strings and booleans are unwrapped, JavaScript Int64 is BigInt, structs are copied and exposed as opaque managed wrappers. The regression includes struct elements with Int64 fields above 2^53. This does not make those wrappers a stable object serialization or cross-module transport format.

## Cancellation, close and retry

Aborting during factory acquisition requests the **enumeration token**, not arbitrary cancellation of the returned factory Task. There is no generic safe operation that cancels every Task. A factory needing cancellation must receive it through the application's own supported managed API. The adapter retains the factory until completion, including during close.

Closing while acquisition is pending drains that same factory. After it succeeds, the adapter obtains its enumerator with the canceled enumeration token and disposes it **without issuing MoveNextAsync**. This intentionally calls GetAsyncEnumerator to obtain the standard disposable ownership boundary. It cannot roll back undisclosed resources allocated by a factory or an acquisition method that throws without returning an enumerator.

`StreamCleanupPendingError.phase` and adapter `pending` can now be `factory`, in addition to `move` or `dispose`. When a cleanup step budget expires, the cursor and exact factory remain retained; it is not closed and no new factory call is issued. After the application's completion mechanism resolves the pending factory, retry return/aclose. An acquisition fault on retry remains visible and retires ownership. Successful acquisition is followed by awaited disposal, including timer-backed disposal.

The cleanup wait is not canceled by the ordinary abort signal that ended iteration. Native clock waiters are detached at cancellation/retirement. Asyncio cancellation follows Python exception propagation and can itself be interrupted again; pending cleanup must then be retained and retried. There is no automatic asynchronous finalizer.

`maxSteps`/`max_steps` now bounds the ordinary factory wait as well as each move wait. `cleanupSteps`/`cleanup_steps` independently bounds each factory/move/disposal drain phase. These are iteration budgets, not total elapsed-time watchdogs. A native/manual clock wait, nonresolving host hook, uncooperative factory or infinite managed method can still remain pending. Use host process isolation/watchdogs where required and retain adapters needing explicit recovery.

## Observability and versioning

`runtimeInfo()` / `runtime_info()` reports `streamPolicy: 'managed-stream-v2'`, `activeStreams` and `activeStreamFactories`. Active streams includes pending acquisition; active factories is the subset whose factory result has not yet been consumed. Counts return to zero only after actual retirement, not merely after cancellation or a cleanup timeout. Timer/root counters remain separate.

Generated metadata stays schema 2; the versioned stream descriptor now contains `kind`, `sourceType`, `requiresElement` and a per-element `choices` table with its bridge entry, scheduler and cursor method IDs. Previously generated v1 modules retain their embedded runtime. Recompile to opt into v2. Mixing private descriptor/runtime fragments across generated modules is not a supported ABI.

## Verification and primary references

The configured gate adds **30 compiler discovery assertions** and **six Debug/Release × instruction/block/SSA configurations**. Each executes **45 scenarios on both hosts: 540 translated scenario executions per full run**, alongside deterministic repeated emission, translated-body provenance and a CoreCLR consumer of the same input DLL. Existing stream, timer, native C++ and compiler regressions remain in the full suite. Observed results and exact source/CI identity are recorded in [validation](validation-summary.md).

Primary .NET contracts: [ValueTask<T> consumption](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0) and [GetAsyncEnumerator cancellation](https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerable-1.getasyncenumerator?view=net-10.0). The lifecycle policy above is this project's explicitly implemented host ABI, not a claim of full CLR scheduling or arbitrary stream-shape compatibility.
