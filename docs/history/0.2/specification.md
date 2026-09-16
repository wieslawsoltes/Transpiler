# Specification — portable compiler and managed BCL batch

Updated 2026-09-16. Artifact metadata schema: **2**. Existing profile identifier: **`portable-mvp`**. Optional substitution policy: **`portable-bcl-v1`**. This document replaces the [0.1 contract](history/0.1/specification.md); it does not claim complete ECMA-335, BCL, scheduling or GC conformance.

## 1. Inputs and outputs

The compiler accepts one root managed assembly or C# source files compiled into that root. `--reference` supplies explicit managed implementation dependencies and also serves source binding when compiling C#. A .NET 10 reference pack supplies framework source contracts. The compiler does not execute input module initializers or resolve dependencies over a network.

Output is a self-contained `.mjs` or `.py` file with translated method bodies and semantic helpers. Execution requires the target host, not .NET. Python uses structural pattern matching; CI qualifies Python 3.13. JavaScript is tested on Node 22. Browser imports are a supported design direction, not a completed browser test matrix.

## 2. CLI

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/PortableBcl.cs --bcl portable \
  --target js --out artifacts/bcl.mjs --manifest artifacts/bcl.manifest.json
node artifacts/bcl.mjs

dotnet "$CLI" compile samples/PortableBcl.cs --bcl portable \
  --target py --out artifacts/bcl.py
python3 artifacts/bcl.py
```

Commands: `compile`, `emit-pe`, `inspect`, `analyze`, `capabilities`. `inspect` decodes the input without claiming backend compatibility. `analyze` links, specializes and validates the shared representation; emission also applies backend profile guards.

| Option | Contract |
|---|---|
| `--target js|javascript|py|python` | Target required by compile |
| `--out`, `-o` | Target file; required by compile/emit-pe |
| `--reference`, `-r` | Repeatable explicit implementation DLL; no automatic transitive discovery |
| `--bcl portable|none` | Enable the documented library substitutions/original slice; default none |
| `--reference-pack` | Explicit directory containing framework reference DLLs |
| `--corelib` | Implementation DLL supplying the original BigMul slice when portable BCL is enabled |
| `--library` | Compile source as a library, exporting eligible public static methods |
| `--debug` | Roslyn Debug instead of Release source optimization |
| `--ir` | Shared compilation analysis JSON |
| `--manifest` | Input/method-origin provenance JSON for target compilation |
| `--diagnostics` | Structured compiler diagnostics |

Exit status: 0 success, 1 managed compilation/capability rejection, 2 usage/file failure. Target output is written only after successful analysis/emission, via temporary file and replacement. A failed compilation does not delete an older target already at that path. Sidecars are not a multi-file transaction; callers must inspect exit status.

## 3. Linking and specialization

The linker uses explicit inputs, assembly identity validation and deterministic token assignment. It permits one version per simple assembly name, rejects conflicting inputs/reference-assembly implementations, and checks requested identities against supplied implementations. The fixed framework normalization policy is not an arbitrary type-forwarding or binding-redirect engine.

Executable roots are the managed entry point. Library roots are public static non-open-generic methods on non-open-generic root types. Closed instantiations are discovered from roots; arbitrary runtime construction and exporting unconstrained open generic methods are not provided. Extra host roots retain managed array-enumerator and async-adapter methods.

Limits: 256 input modules, 16,384 specialized methods, 4,096 constructed types, 4,096-character constructed identities. These limits bound particular compiler structures, not total CPU/memory use. Complete hostile-input verification and execution quotas remain required for untrusted workloads.

## 4. Library origin and supported surface

`--bcl portable` combines selected abstract framework contracts, the original portable C# library, and one selected original .NET CoreLib implementation. The public API is still bound against the standard framework; substituted algorithms are explicitly selected during linkage.

**Original .NET IL:** `Math.BigMul(int,int):long`. The input implementation hash is recorded and its real method body is emitted. No claim is made that other Math overloads are imported; many existing scalar operations remain exact declared intrinsics.

**Portable C# collections:** selected List<T>, Queue<T>, Stack<T> constructors, storage, mutation, enumeration and materialization members. **Portable LINQ:** selected Range/Repeat, filtering/projection, Take/Skip, Count/Any/All/First/FirstOrDefault, Aggregate, numeric Sum and ToArray/ToList overloads. These are not complete declaring types or full interface surfaces. The [compatibility ledger](compatibility.md) is authoritative at feature level; exact method resolution rejects unsupported members.

**Portable async:** Task/Task<T>, TaskCompletionSource<T>, TaskAwaiter variants, YieldAwaitable, ConfiguredTaskAwaitable variants and AsyncTaskMethodBuilder variants as implemented in `Transpiler.Bcl/Tasks`. Ordinary Roslyn state machines execute through the same compiler; there is no syntax-only async shortcut.

The manifest includes `schema`, `target`, `bcl`, `assemblies` with identity/hash, source `referencePack` inputs where applicable, `transpiled` method origins and instruction counts, and external `intrinsics`. Store it with generated output. A successful source compilation against framework contracts alone does not establish implementation compatibility.

## 5. Managed execution semantics

Integer arithmetic retains fixed widths, signed/unsigned behavior, checked overflow, masked shifts and exact 64-bit representation. JavaScript uses BigInt for 64-bit values. The current profile retains its explicit CoreCLR x64 minimum-signed-value remainder policy; numerical details from the initial specification still apply unless superseded here. Binary64 is supported; Single/binary32 storage, decimal, arbitrary native pointers and SIMD remain unsupported.

Struct values are copied at load/store/call/return/box boundaries; addresses remain aliases. Nested field references follow their parent storage. Constructed generic types have separate statics. Internal class slots, tested interface/MethodImpl/variance combinations and constrained receivers are supported. Explicit native layout, every nullable/span/ref-struct rule, every generic constraint and all runtime type-system corner cases are not certified.

Managed strings preserve wrapper identity, literals and UTF-16 operations. Vector arrays have checked null/bounds/type behavior. Iterators preserve tested disposal/finally paths. General multidimensional/lower-bound arrays, field-RVA initializer helpers, complete globalization, default/localized exception texts and isolated-surrogate console streaming are not complete.

Throw/catch/rethrow/leave/finally use managed exception objects and explicit continuation state. Filters remain rejected because correct cross-frame search-before-unwind is not implemented. Finalizers remain rejected. Stack-overflow and resource-exhaustion behavior follows host limits, not exact CoreCLR behavior.

## 6. Cooperative task profile

Task state is pending, succeeded, faulted or canceled. Task builders classify an escaping OperationCanceledException as cancellation. In contrast, `Task.FromException` and completion-source `SetException` always create a fault even when the supplied exception is an OperationCanceledException. Completion sources cannot complete twice; Try variants report false. CompletedTask is a cached nongeneric completed Task.

Continuations use a single-threaded FIFO. GetResult may drive that queue; failure to make progress or exhaustion of its 100,000-step budget is an explicit profile failure. Host async invocation drives the same translated queue and yields to the host event loop between steps. This is not a .NET thread-pool, execution-context, synchronization-context or blocking-wait guarantee.

`ConfigureAwait(bool)` has no captured .NET context to switch to in this profile. Task.Run, Task.Delay, cancellation tokens, ValueTask, parallel scheduling, full continuation options and async streams are not implemented. Do not depend on task allocation/caching identity or all .NET continuation timing observations beyond the documented corpus.

## 7. Generated host ABI

```bash
dotnet "$CLI" compile samples/AsyncLibrary.cs --library --bcl portable \
  --target js --out artifacts/kernel.mjs
dotnet "$CLI" compile samples/AsyncLibrary.cs --library --bcl portable \
  --target py --out artifacts/kernel.py
```

```javascript
import { invoke, invokeAsync, retain, dereference, release, runtimeInfo } from './kernel.mjs';
console.log((await invokeAsync('Kernel::Calculate', [5])).toString());
const object = invoke('Kernel::Make');
const handle = retain(object);
console.log(dereference(handle) === object);
release(handle);
console.log(runtimeInfo());
```

```python
import asyncio
from kernel import invoke_async, runtime_info
async def example():
    print(await invoke_async('Kernel::Calculate', [5]))
    print(runtime_info())
asyncio.run(example())
```

`invoke` selects a full signature or an unambiguous short `Type::Method` name, checks arity and marshals the supported primitive/string inputs and results. Array inputs are copied into wrappers; general object/ref/callback marshalling is not a stable interprocess ABI. Use BigInt for JavaScript Int64/UInt64 values outside the exact Number range.

`invokeAsync(name,args,{maxSteps})` and `invoke_async(name,args,max_steps=...)` await a linked Task result; non-task exports return their ordinary value. Default step budget is 100,000. A step budget does not interrupt an infinitely running managed method. Python uses asyncio cooperation; JavaScript uses host promises.

Output callbacks remain `setOutput(writer)` and `set_output(writer)`. Each generated module owns separate statics/runtime state. Cross-module managed-object interchange is not supported as one shared CLR load context.

## 8. Host heap contract

`WeakReference<T>` supports the one-target constructor, SetTarget and TryGetTarget. `GC.KeepAlive(object)` establishes an object-use boundary; `RuntimeHelpers.GetHashCode(object)` gives a runtime-stable identity hash, with zero for null. Numeric hash equality with another .NET process is not promised. Weak reference eligibility requires the host service; absence is explicit.

`retain` creates a strong opaque root; `dereference` returns its target; `release` removes it and returns whether it existed. Handles are monotonic safe integers and are not reused. A second release returns false; dereferencing a released/unknown handle fails. Root removal does not force reclamation.

`runtimeInfo`/`runtime_info` reports host GC, weak-reference availability, explicit-root count, cooperative scheduling, and false for forced collection, managed finalizers and pinning. GC.Collect, WaitForPendingFinalizers, resurrection tracking and native addresses are deliberately not no-op implementations. Ordinary wrapper objects are reclaimed by the host; no secondary managed collector is claimed.

## 9. Diagnostics, reproducibility and safety

The original TR000x/100x/200x/210x families remain. TR3000–3004 cover graph budget, reference implementation misuse, input conflicts, requested identity mismatch and duplicate definitions. TR3100/3101 cover generic budgets/open instances; TR3200 covers unavailable selected CoreLib body. Unsupported members and runtime services retain actionable method/IL diagnostics.

Target output is deterministic for identical compiler and assembly inputs; dependency argument ordering is tested. Reference-pack discovery and SDK roll-forward are conveniences, not cross-version reproducibility promises. Keep the exact manifest, SDK, source snapshot and licenses for releases.

The compiler is not a full security verifier or sandbox. Metadata identity checks do not authenticate assemblies. Host root handles are ownership conveniences, not authorization tokens. Run untrusted compilation and execution in isolated, resource-limited processes. See [security scope](../SECURITY.md).
