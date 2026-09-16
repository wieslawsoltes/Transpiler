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

## Host invocation and lifetime

Generated modules expose `invoke` and `invokeAsync` / `invoke_async` for supported public static exports. Task and ValueTask results, including source-backed operations, work with the async ABI. Use BigInt for JavaScript 64-bit integer values beyond Number's exact range. Root APIs are `retain`, `dereference` and `release`; output callbacks are `setOutput` / `set_output`.

Native JS/Python async-generator marshalling of an exported IAsyncEnumerable is not implemented. Consume the stream inside an exported managed Task method when using the existing host ABI. Pump-step budgets do not interrupt an infinite managed call or cancel an underlying operation.

Ordinary objects use host GC. The [logical collector](docs/logical-heap.md) manages only its own explicit payload/root domain; forced CLR collection, finalizers, resurrection and pinning are not no-op substitutes.

## Verification and documentation

```bash
python3 tests/conformance.py
dotnet "$CLI" capabilities --out artifacts/capabilities.json
```

The implementation milestone passed **121 cases, 0 failures** in [CI run 35150624355](https://github.com/wieslawsoltes/Transpiler/actions/runs/35150624355). The gate contains **121 harness cases**: 100 ordinary/BCL Debug/Release console configurations, five negative fixtures, library/malformed-PE checks and 14 extended gates. Console configurations generate 200 target executions; the cross-emitter gate adds 96, and other graph/host/heap tests add further executions. One case may contain many assertions. These are not CLI coverage percentages. Observed results and exact environments are in [validation](docs/validation-summary.md).

[Architecture](docs/architecture.md) · [Specification](docs/specification.md) · [Compatibility](docs/compatibility.md) · [Implementation plan](docs/implementation-plan.md) · [Testing](docs/testing.md) · [Async-stream research](docs/research/async-streams-2026-09-16.md) · [Industry research](docs/research/industry-state-2026-09-16.md) · [BCL/runtime research](docs/research/bcl-runtime-2026-09-16.md)

The project does not yet provide full exception filters, general reflection/dynamic loading, native I/O/threads, all layout/span/decimal semantics, integrated ordinary-object logical GC, SSA or C++ output. It is not a security sandbox. Read [SECURITY.md](SECURITY.md) and [third-party notices](THIRD-PARTY-NOTICES.md).
