# Current portable compiler specification

Updated 2026-09-16. Generated metadata schema **2**; compiler profile **portable-mvp**; optional BCL policy **portable-bcl-v1**; original-body catalog **corelib-integer-v1**. The preceding detailed specification is retained in [history/0.2](history/0.2/specification.md); this document supersedes its status and original-method inventory.

## Inputs, output and CLI

Input is one root managed DLL or C# files compiled into it. Repeatable --reference options supply implementation DLLs. Roslyn binds framework source APIs against a selected .NET 10 reference pack. The compiler does not execute input initializers, use Assembly.Load for import or probe a network for missing dependencies.

Output is standalone .mjs or .py source, including required semantic helpers and translated method bodies. Generated programs need their target runtime, not .NET. CI uses Node 22 and Python 3.13; browser hosting is not yet a qualified matrix.

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/PortableBcl.cs --bcl portable \
  --target js --out artifacts/bcl.mjs --manifest artifacts/bcl.json
node artifacts/bcl.mjs

dotnet "$CLI" compile samples/PortableBcl.cs --bcl portable \
  --target py --out artifacts/bcl.py
python3 artifacts/bcl.py
```

Commands remain compile, emit-pe, inspect, analyze and capabilities. Options: --target js|py, --out/-o, --reference/-r, --library, --debug, --bcl portable|none, --reference-pack directory, --corelib implementation.dll, --ir, --manifest and --diagnostics. Defaults use no portable BCL, Release source compilation and installed .NET 10 reference-pack discovery. --reference-pack and --corelib allow explicit inputs. No .csproj/source-generator/NuGet graph driver is provided yet.

Exit codes are 0 for success, 1 for managed compilation/capability rejection and 2 for usage/file failures. Output is replaced only after successful emission through a temporary-file rename. Failure does not delete an older artifact at that path; sidecars are not one atomic multi-file transaction.

## Linking and limits

Application types are assembly-scoped. One version per simple assembly name is accepted; conflicting inputs, mismatched requested identities and reference assemblies used as implementations are rejected. Reference-only abstract interfaces can be deliberately imported as metadata contracts. Complete facade/type-forwarding and multiple load contexts are outside this profile.

Entry points root executables; eligible public static, non-open-generic methods root libraries. Closed instantiations are discovered from roots. Separate host roots retain array-enumerator and async-ABI operations. Limits are 256 modules, 16,384 specialized methods, 4,096 constructed types and 4,096 characters per constructed identity. These are not total CPU/memory sandbox limits.

## Original and portable BCL implementations

With --bcl portable, the loader imports the portable managed library and **21 original CoreLib Math methods**: BigMul(Int32,Int32); Min/Max for the eight signed/unsigned 8/16/32/64-bit integer types; DivRem(Int32,Int32,out Int32) and its Int64 counterpart; Sign(Int32) and Sign(Int64). Matching requires static/non-generic status, declaring type, name, return and parameter signature. The selected implementation must supply real managed bodies. Other Math overloads are not implicitly adopted.

Portable C# implementations cover selected List<T>, Queue<T>, Stack<T>, Enumerable, Task/Task<T>, completion-source, awaiter and async-builder operations. These are not complete declaring types or full BCL coverage. An unsupported member such as List<T>.Sort remains an error. The manifest lists emitted bodies with supplying assembly/instruction counts, external bindings, assembly hashes and available source reference-pack inputs. Original framework bodies include the upstream notice.

## Execution contract

Integer widths, signed/unsigned operations, checked overflow, masked shifts and JavaScript BigInt 64-bit values remain explicit. Signed minimum remainder by minus one follows the documented selected CoreCLR x64 policy. Binary64 covers tested arithmetic, NaN and signed-zero paths, not exhaustive formatting/payload equivalence. Binary32 storage, decimal, SIMD and general native-pointer operations remain unsupported.

Struct value copying, managed-address aliasing, constructed generic static storage, tested interface/MethodImpl/variance dispatch, constrained receivers, delegates and iterator disposal are implemented. Explicit-layout structs, every nullable/span/ref-struct/constraint combination and arbitrary external virtual overrides remain outside the guarantee.

Strings retain reference identity and UTF-16 operations. Vector arrays check null/bounds/type conditions. Multidimensional/lower-bound arrays, field-RVA constant-array helpers, all globalization/default exception text and isolated-surrogate streaming output are not completed surfaces.

For methods without InitLocals, every normal-flow local read/address acquisition must be proven to follow a store on all paths. Exceptional-region initialization and first initialization through an address remain conservatively rejected. This does not certify arbitrary unverifiable IL.

Throw/catch/rethrow/leave/finally use managed exception objects and explicit continuations. Filters and finalizers remain rejected. Exact managed stack traces, stack-overflow recovery and out-of-memory equivalence are not promised.

## Cooperative async profile

Tasks have pending/success/fault/canceled state. Async builders classify an escaping OperationCanceledException as cancellation; FromException and completion-source SetException still create faults for that exception type. Completion is single-assignment. CompletedTask is nongeneric and cached by this implementation.

Continuations execute on a single-threaded FIFO. Awaiters/builders are translated managed algorithms, not direct substitutions of async method syntax with promises. GetResult may pump the queue and rejects inability to progress or its fixed step budget. Host invokeAsync/invoke_async pumps cooperatively. ConfigureAwait(bool) has no captured .NET context to switch to in this profile.

No Task.Run/Delay, thread pool, full context propagation, cancellation tokens, ValueTask or async streams are implemented. Pump budgets count iterations, not elapsed time, and cannot interrupt an infinite translated call. Timeout does not cancel a pending task.

## Generated-module ABI

```javascript
import { invoke, invokeAsync, retain, dereference, release, runtimeInfo } from './kernel.mjs';
const answer = await invokeAsync('Kernel::Calculate', [5]);
console.log(answer.toString());
const value = invoke('Kernel::Make');
const handle = retain(value);
console.log(dereference(handle) === value);
release(handle);
console.log(runtimeInfo());
```

```python
import asyncio
from kernel import invoke_async
async def example():
    print(await invoke_async('Kernel::Calculate', [5]))
asyncio.run(example())
```

Compile samples/AsyncLibrary.cs with --library --bcl portable for these examples. invoke selects a full signature or unambiguous Type::Method name and checks arity. Primitive/string conversions are provided; host arrays are copied into managed wrappers. Use BigInt for JavaScript Int64/UInt64 outside the safe Number range. General byref/callback/interprocess marshalling is not a stable ABI.

invokeAsync(name,args,{maxSteps}) and invoke_async(name,args,max_steps=...) default to 100,000 pump steps. String/Boolean task results are unwrapped. Non-task exports return ordinary results. Output callbacks are setOutput/set_output. Each generated module owns separate statics and runtime state; passing runtime objects across independently generated modules is not a shared CLR load context.

## Host lifetime services

WeakReference<T> supports its single-target constructor, SetTarget and TryGetTarget. RuntimeHelpers.GetHashCode is stable for an object within a runtime, with zero for null; it is not numerically portable across processes. JavaScript KeepAlive uses WeakRef kept-alive semantics through the synchronous job boundary and requires that host capability; Python retains the argument through the call.

retain returns a monotonically increasing safe-integer root handle. dereference validates it. release removes the root, returns false for a second release and never forces destruction. runtimeInfo/runtime_info reports host GC, weak-reference availability, explicit-root count, cooperative scheduling, and false for forced collection, managed finalizers and pinning.

GC.Collect, WaitForPendingFinalizers, resurrection tracking, native addresses and CLR generation/statistics APIs are not emulated as no-ops.

## Separate logical heap

Transpiler.Runtime.Managed can be explicitly linked using --reference. Its bounded non-moving C# mark/sweep collector manages only allocations through LogicalHeap, including explicit strong/weak handles, checked generation/ownership, byte/reference quotas and statistics. Source-local HeapReference wrappers are not automatic logical roots. It does not change the default host heap or enable System.GC.Collect. See [logical-heap specification](logical-heap.md).

## Diagnostics and safety

TR3000–3004 identify graph budget/reference misuse/conflicts/identity mismatch/duplicate definitions; TR3100/3101 identify generic budget/open-instantiation failures; TR3200 identifies missing original implementation contracts. TR2006 now includes definite-assignment failure locations. The other import, opcode, linkage and stack diagnostics remain available.

Identical compiler/assembly inputs produce deterministic target source, including reordered explicit dependencies. Installed SDK/reference-pack discovery is not a cross-version lockfile. Preserve source, manifests, toolchain and applicable notices.

This compiler is not a complete verifier or sandbox. Run untrusted compilation and output with OS isolation and external resource limits. Heap handles are ownership mechanisms, not authorization credentials. See [SECURITY.md](../SECURITY.md).
