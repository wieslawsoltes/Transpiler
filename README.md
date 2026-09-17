# Transpiler

[![Compiler conformance](https://github.com/wieslawsoltes/Transpiler/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/Transpiler/actions/workflows/ci.yml)

**Roslyn → real PE/CIL → standalone JavaScript and Python, including translated managed libraries and async streams.**

C# source and existing managed DLLs use one assembly-based compiler. Generated programs contain target-language methods and semantic helpers, not a CLR subprocess, Python.NET dependency, runtime download or original IL instruction stream. The compiler itself requires .NET; generated programs do not.

The current `portable-mvp` profile and optional `portable-bcl-v1` library policy are explicit, tested subsets. They are not complete CLI, BCL, threading, reflection or native-platform compatibility. C++ remains planned.

## Build and run

Compiler: .NET 10 SDK. Test hosts: Node 22 and Python 3.13.

```bash
git clone https://github.com/wieslawsoltes/Transpiler.git
cd Transpiler
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/Hello.cs --target js --out artifacts/hello.mjs
node artifacts/hello.mjs hello

dotnet "$CLI" compile samples/Hello.cs --target py --out artifacts/hello.py
python3 artifacts/hello.py hello
```

To compile existing assemblies, pass the root DLL instead of source and repeat `--reference dependency.dll` for implementation dependencies. Framework source binding uses a selected reference pack, not executable reference stubs. `--reference-pack`, `--corelib`, `--manifest`, `--ir` and `--diagnostics` expose those boundaries.

## Two-pass filters, forwarding and verification

Exception filters now execute against live managed activations **before** callee finally/fault unwinding. Tests cover rejecting/throwing filters, helper exceptions, initializer wrapping, rethrow identity and cleanup replacement. Modules needing filters select the `two-pass-managed-v1` runtime; both dispatch modes are supported.

The linker now reads explicit ExportedType forwarders and resolves moved generic/nested/value types without recompiling the consumer DLL. Type rewriting is structural and scope-aware instead of substring replacement. The new address analysis rejects local-frame byref escapes and incompatible indirect storage; local assignment propagates conservative exception-entry facts.

```bash
dotnet "$CLI" compile samples/ExceptionFilters.cs --dispatch block \
  --target js --out artifacts/filters.mjs
node artifacts/filters.mjs
```

This is not complete signature/loader fidelity or a full security verifier. Read the [implemented contract](docs/linking-verification.md) and [primary-source research](docs/research/filters-forwarding-2026-09-17.md).

## Async streams: actual compiler-generated state machines

```bash
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

The sample combines `await foreach`, `yield return`, `Task.Yield`, cancellation configuration, early loop exit and an awaited finally block. The core, awaiters, builder and configuration algorithms are C# in Transpiler.Bcl and pass through the same IL pipeline as the application.

New support includes source-backed ValueTask, IValueTaskSource interfaces, a reusable versioned completion core, AsTask/Preserve bridges, async iterator/disposal contracts and identity-preserving ExceptionDispatchInfo paths. Tests cover token validation, single consumption, callback-state release, linked cancellation, struct copies and cleanup-exception replacement. [Read the contract](docs/async-streams.md).

Scheduling remains cooperative and single-threaded. Forwarding continuation flags does not implement ExecutionContext or SynchronizationContext. Timers, Task.Run/Delay, concurrent source operations and exact .NET exception traces remain unsupported.

## Library and runtime origins

| Origin | Implemented examples |
|---|---|
| Original .NET implementation IL | 21 reviewed integer Math bodies imported from the selected actual CoreLib |
| Original portable C# algorithms | Collections, comparers, LINQ, nullable, task composition/cancellation, source-backed awaitables and iterators |
| Independently translated runtime algorithm | Bounded logical mark/sweep heap, explicitly linked rather than installed as default GC |
| Target runtime services | Managed storage/identity, numeric and array representations, dispatch, host roots, weak references and async ABI |

`--manifest` identifies supplied assembly hashes, emitted methods and external bindings. Generated derivatives containing original CoreLib bodies include the upstream notice. The portable library is not represented as an unchanged copy of upstream source. Exact unsupported members remain diagnostics.

The recovered work also includes Dictionary/HashSet and comparer paths; selected Object/ValueType bridges; nullable; binary32 and RVA initializers; rectangular/lower-bound arrays and limited type identity; cancellation tokens and Task composition; protected-region CFG validation and block dispatch. See the [compatibility ledger](docs/compatibility.md), not an old milestone's exclusions, for the current status.

## Two source emitters, one semantic pipeline

`--dispatch instruction` is the reference mode and default. `--dispatch block` coalesces straight-line instructions into validated basic blocks while retaining semantic helpers and fault offsets. This is not SSA optimization or idiomatic source reconstruction. The cross-emitter gate compares 48 target/configuration pairs across 12 fixtures, including async streams and source-backed values.

```csharp
var analysis = CompilerAnalysis.Analyze(linkedAssembly);
var source = SourceEmitter.Emit(analysis, SourceTarget.JavaScript,
    new SourceEmissionOptions(DispatchMode.BasicBlock));
```

These types live in Transpiler.Core and Transpiler.Backends. RoslynFrontend and PortableCompilation supply source/reference-pack and optional library orchestration.

## Native host streams

The generated `stream(name, args, options)` adapter consumes exports declared as `IAsyncEnumerable<T>` directly from native host iteration. Its cursor and ValueTask ownership code are translated C#, with explicit reachability roots. Factory invocation is lazy and cleanup is single-consumption.

```javascript
import { stream } from "./kernel.mjs";
for await (const value of stream("StreamKernel::Squares", [20])) {
    console.log(value.toString());
    if (value === 16n) break;
}
```

```python
from kernel import stream
async with stream("StreamKernel::Squares", [20]) as values:
    async for value in values:
        print(value)
        if value == 16:
            break
```

Compile `samples/HostStreams.cs` with `--library --bcl portable` and the target of your choice. Complete commands and sample drivers are in the [host-stream contract](docs/host-streams.md). JavaScript early exit awaits return; Python early exit requires `async with`, `contextlib.aclosing`, or explicit `aclose`. Cancellation drains an outstanding move before disposal. A source that ignores cancellation can leave cleanup pending; the adapter reports this explicitly and supports safe close retry. No finalizer or background disposal is promised.

## Host invocation and lifetime

Generated modules expose `invoke` and `invokeAsync` / `invoke_async` for supported public static exports. Task and ValueTask results, including source-backed operations, work with the async ABI. Use BigInt for JavaScript 64-bit integer values beyond Number's exact range. Root APIs are `retain`, `dereference` and `release`; output callbacks are `setOutput` / `set_output`.

Native async-iterator protocol adapters are implemented for exports declared exactly as IAsyncEnumerable<T>. Arbitrary concrete/object/Task-wrapped stream exports and general object serialization remain unsupported. `runtimeInfo`/`runtime_info` reports `activeStreams` and `streamPolicy`; pending-cleanup cursors remain active until real retirement. Pump-step budgets do not interrupt infinite managed code or a host hook that never resolves.

Ordinary objects use host GC. The [logical collector](docs/logical-heap.md) manages only its own explicit payload/root domain; forced CLR collection, finalizers, resurrection and pinning are not no-op substitutes.

## Verification and documentation

```bash
python3 tests/conformance.py
dotnet "$CLI" capabilities --out artifacts/capabilities.json
```

The full local and implementation-CI gates passed **135 cases, 0 failures**. The corpus contains **135 cases**: 106 ordinary/BCL Debug/Release configurations, four negative fixtures, library/malformed-PE checks and 23 extended gates. The ordinary configurations account for 212 generated console executions; instruction/block, live-frame retirement, host-stream, forwarding and raw-IL gates add further executions. Identity checks include 25 assertions and the new safety gate includes 22. Counts are not CLI coverage percentages. See [validation](docs/validation-summary.md) for observed results and exact commits.

The harness records whether a run is complete or filtered and refuses empty success. `TRANSPILER_TEST_WORKERS=2 python3 tests/conformance.py` uses bounded parallel case execution with deterministic report ordering; CI clears the filter and uses two workers.

[Architecture](docs/architecture.md) · [Specification](docs/specification.md) · [Compatibility](docs/compatibility.md) · [Implementation plan](docs/implementation-plan.md) · [Testing](docs/testing.md) · [Native host streams](docs/host-streams.md) · [Host interop research](docs/research/host-streams-2026-09-17.md) · [Async-stream research](docs/research/async-streams-2026-09-16.md) · [Industry research](docs/research/industry-state-2026-09-16.md) · [BCL/runtime research](docs/research/bcl-runtime-2026-09-16.md)

The project does not yet provide complete signature/loader or verification fidelity, general reflection/dynamic loading, native I/O/threads, all layout/span/decimal semantics, integrated ordinary-object logical GC, SSA or C++ output. Managed two-pass filters and explicit scoped forwarding are implemented, with remaining limits documented. It is not a security sandbox. Read [SECURITY.md](SECURITY.md) and [third-party notices](THIRD-PARTY-NOTICES.md).
