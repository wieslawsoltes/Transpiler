# Transpiler

[![Compiler conformance](https://github.com/wieslawsoltes/Transpiler/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/Transpiler/actions/workflows/ci.yml)

**Roslyn → real PE/CIL → standalone JavaScript and Python, including translated managed libraries.**

Transpiler compiles actual managed method bodies and metadata, not just C# syntax. It accepts C# sources or a root DLL with explicit implementation dependencies. Generated programs contain target-language methods and semantic helpers; they do not require .NET, Python.NET, a CLR subprocess, a runtime download or an IL instruction interpreter.

The current `portable-mvp` profile is a tested subset, not complete CLI or .NET compatibility. The optional `portable-bcl-v1` policy enables selected managed library implementations. C++ remains planned.

## Build and run

Use a .NET 10 SDK to build the compiler. The conformance suite additionally needs Node 22 and Python 3.13. Generated programs need only the chosen target runtime.

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

Compile an existing DLL through the same pipeline:

```bash
dotnet "$CLI" emit-pe samples/Hello.cs --out artifacts/Hello.dll
dotnet "$CLI" compile artifacts/Hello.dll --target js --out artifacts/from-il.mjs
dotnet "$CLI" compile artifacts/Hello.dll --target py --out artifacts/from-il.py
```

Supply repeatable `--reference path.dll` options for implementation dependencies. Source compilation uses a selected .NET reference pack; reference assemblies are not executable implementations. `--reference-pack` and `--corelib` provide explicit framework inputs. Full project/NuGet dependency discovery and arbitrary type forwarding are not implemented.

## Translate the BCL, not just application code

```bash
dotnet "$CLI" compile samples/PortableBcl.cs --bcl portable \
  --target js --out artifacts/bcl.mjs --manifest artifacts/bcl.manifest.json
node artifacts/bcl.mjs

dotnet "$CLI" compile samples/PortableBcl.cs --bcl portable \
  --target py --out artifacts/bcl.py
python3 artifacts/bcl.py
```

Both print:

```text
220
4611686014132420609
```

The example combines `List<int>` and lazy LINQ algorithms translated from the original portable C# implementation with `Math.BigMul(int,int)` imported from the selected actual .NET CoreLib implementation.

There are three distinct implementation origins:

| Origin | Delivered example |
|---|---|
| Original .NET implementation IL | 21 selected integer Math overloads: BigMul, Min/Max for eight integer types, DivRem and Sign for Int32/Int64 |
| Original portable C# library IL | Selected List/Queue/Stack/LINQ, iterators, tasks, awaiters and async builders |
| Explicit target runtime services | Managed storage, identity, integer widths, UTF-16 operations, host weak references, roots and async interoperability |

`--manifest` records supplying assemblies, input hashes, emitted methods/instruction counts and external bindings. The original-body test verifies that all 21 selected CoreLib methods become emitted bodies, not hidden Math intrinsics. Generated source containing original framework implementation code includes the upstream license notice.

This is not the entire BCL. Unsupported members, overloads and runtime requirements are rejected. Portable collection/task implementations are not described as unchanged upstream source.

## Async libraries and host roots

```bash
dotnet "$CLI" compile samples/AsyncLibrary.cs --library --bcl portable \
  --target js --out artifacts/kernel.mjs
dotnet "$CLI" compile samples/AsyncLibrary.cs --library --bcl portable \
  --target py --out artifacts/kernel.py
```

JavaScript, from a module next to `kernel.mjs`:

```javascript
import { invoke, invokeAsync, retain, dereference, release } from './kernel.mjs';

console.log((await invokeAsync('Kernel::Calculate', [5])).toString());
// 9007199254741003

const value = invoke('Kernel::Make');
const handle = retain(value);
console.log(dereference(handle) === value); // true
release(handle);
```

Python, next to `kernel.py`:

```python
import asyncio
from kernel import invoke_async

async def example():
    print(await invoke_async('Kernel::Calculate', [5]))
    # 9007199254741003

asyncio.run(example())
```

Actual Roslyn-generated state machines execute against translated C# builders/awaiters and a cooperative single-threaded FIFO. This is not a thread pool or full .NET context/timer/cancellation implementation. `Task.Run`, `Task.Delay` and unsupported scheduling requirements fail explicitly. Host pump budgets bound iterations, not an infinitely running managed call.

The default heap is the JS/Python host heap. Root handles retain explicitly selected objects; release removes a root but does not force destruction. Weak references, identity hashes and KeepAlive have documented host contracts. Forced CLR collection, finalization, resurrection and pinning are not simulated with no-ops.

## A runtime algorithm can itself be transpiled

`Transpiler.Runtime.Managed` contains a separate bounded logical mark/sweep heap implemented in C#. Its allocation, root/weak-handle and collection algorithms are compiled through the same IL engine.

```bash
HEAP=src/Transpiler.Runtime.Managed/bin/Release/net10.0/Transpiler.Runtime.Managed.dll

dotnet "$CLI" compile tests/runtime/LogicalHeap.cs --reference "$HEAP" \
  --bcl portable --target js --out artifacts/heap.mjs
node artifacts/heap.mjs
```

This heap owns only payloads explicitly allocated through its API. Keeping a `HeapReference` wrapper in a source local is not an automatic logical root. It does not replace ordinary generated-object GC. The [logical-heap contract](docs/logical-heap.md) documents ownership/generation checks, quotas, cycles, weak clearing and the remaining compiler-root/safepoint integration work.

## Compiler modules

| Project | Responsibility |
|---|---|
| Transpiler.Core | PE/CIL import, deterministic linkage, bounded specialization, stack/local proofs and capabilities |
| Transpiler.Frontend.Roslyn | C# compilation, reference packs, portable-library orchestration and upstream-body catalog |
| Transpiler.Bcl | Portable collection/LINQ/iterator/task algorithms in C# |
| Transpiler.Runtime.Managed | Independently linked logical-heap algorithms in C# |
| Transpiler.Backends | Shared source lowering, metadata/dispatch, value/address semantics and host services |
| Transpiler.Cli | Compile/inspect/analyze/capabilities commands and provenance |

The implemented semantic slices include struct copying, managed-address aliasing, bounded closed generics with distinct statics, tested interface/MethodImpl/variance dispatch, delegates and iterator disposal. Methods that omit local zero-initialization require a normal-flow definite-assignment proof. This is not a complete security verifier.

The source backend remains a conservative static control-flow dispatcher. Managed HIR/CFG, SSA optimization, source maps and structured-source recovery are planned rather than advertised as completed.

## Verification

```bash
python3 tests/conformance.py
dotnet "$CLI" inspect artifacts/Hello.dll --out artifacts/metadata.json
dotnet "$CLI" analyze artifacts/Hello.dll --out artifacts/analysis.json
dotnet "$CLI" capabilities --out artifacts/capabilities.json
```

The expanded gate passed **71 cases, 0 failures** in [CI run 35120849201](https://github.com/wieslawsoltes/Transpiler/actions/runs/35120849201), and independently under local SDK 10.0.100, Node 22.16.0 and Python 3.13.5. CI used SDK 10.0.401, Node 22.23.2 and Python 3.13.15. See the [validation record](docs/validation-summary.md) for exact commits and environments.

The corpus includes 52 Release/Debug ordinary/BCL console configurations executed on both targets, eight negative fixtures, library ABI and malformed PE checks, and nine extended gates. Extended gates include a three-assembly graph, method-origin proof, host async/roots, 11 hand-authored assignment shapes, host liveness and two logical-heap configurations with an independent graph oracle. Counts describe harness cases, not CLI coverage percentages.

## Research and documentation

| Document | Contents |
|---|---|
| [Industry research](docs/research/industry-state-2026-09-16.md) | Compiler landscape and primary-source survey |
| [BCL/runtime research](docs/research/bcl-runtime-2026-09-16.md) | Original versus portable IL, host GC, CoreCLR/SGen/Boehm/MMTk trade-offs |
| [Architecture](docs/architecture.md) | Current modules, semantics and integration boundaries |
| [Specification](docs/specification.md) | CLI, binding, execution, async and lifetime contracts |
| [Compatibility](docs/compatibility.md) | Implemented/tested slices and visible exclusions |
| [Logical heap](docs/logical-heap.md) | Executable usage and collector invariants |
| [Implementation plan](docs/implementation-plan.md) | Remaining priorities and acceptance gates |
| [Testing](docs/testing.md) | Reproduction and evidence interpretation |

Full BCL/CLI compatibility, broad native/I/O/threading support, filters, reflection/dynamic loading, full nullable/span/layout semantics and integrated ordinary-object logical GC remain work items. The compiler and generated programs are not sandboxes. Read [security scope](SECURITY.md) and [third-party notices](THIRD-PARTY-NOTICES.md).
