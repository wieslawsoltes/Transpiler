# Portable compiler specification

Updated 2026-09-16 after the async-stream continuation. Output metadata schema 2; compiler profile `portable-mvp`; optional library policy `portable-bcl-v1`; original-body catalog `corelib-integer-v1`. Earlier documents in history describe earlier subsets.

## Inputs, binding and CLI

Input is C# source files or one root managed assembly. Repeatable implementation DLLs are supplied with --reference. Roslyn uses the selected .NET 10 reference pack for source contracts. Reference metadata is not executable implementation IL. Import does not execute module initializers or retrieve dependencies over a network.

Commands: compile, emit-pe, inspect, analyze and capabilities.

| Option | Meaning |
|---|---|
| --target js / py | JavaScript ES module or Python source |
| --out / -o | Target file |
| --reference / -r | Explicit implementation dependency; repeatable |
| --bcl portable / none | Select portable library substitutions and reviewed upstream bodies; default none |
| --reference-pack / --corelib | Explicit framework contract directory / original implementation assembly |
| --library / --debug | Library roots / Debug source optimization |
| --dispatch instruction / block | Reference instruction mode (default) or validated basic-block coalescing |
| --ir / --manifest / --diagnostics | Analysis, input/method provenance and structured diagnostics |

Exit status is 0 success, 1 compilation/capability rejection, 2 usage/file failure. Successful target writes replace via a temporary file. Failure does not delete an older file at that path; callers must check status. Sidecar writes are not one multi-file transaction.

Linking is single-load-context with one version per assembly simple name and explicit inputs. It checks requested identities and rejects conflicting/reference-only implementation inputs. It is not general forwarding, multi-version binding or automatic package restore. Executable entry points and eligible public static root-library methods are roots; open generic exports require further design.

Selected budgets remain 256 modules, 16,384 specialized methods, 4,096 constructed types and 4,096 characters per constructed identity. CFG validation limits exception clauses to 512. These are not complete process resource quotas.

## Managed semantics and library origin

Both emitters consume the same analyzed method bodies. Integer widths, signed/unsigned operations, BigInt Int64, checked overflow, binary32/64, struct copies, managed storage addresses, nullable boxing and tested class/interface/delegate paths have explicit implementations. RVA primitive data and rectangular/lower-bound arrays are supported slices. Limited type handles do not imply arbitrary member reflection.

Portable library algorithms are C# compiled into Transpiler.Bcl. Collections, comparers, selected LINQ, task composition/cancellation, source-backed awaitables and async-stream contracts are translated through ordinary IL. Unsupported adjacent members still fail exact binding. The [ledger](compatibility.md) states the boundaries; it supersedes initial-MVP exclusions.

The reviewed original CoreLib catalog contains BigMul(Int32,Int32), integer Min/Max for eight widths/sign combinations, DivRem with out remainder for Int32/Int64, and Sign(Int32/Int64): 21 actual bodies. Input hashes and emitted method origins are recorded. Selected original bodies take precedence over intrinsic shortcuts and retain the upstream notice.

## Control flow and exceptions

Stack joins, local assignment and protected-region/prefix transfers are checked. Exceptional CFG edges are conservative metadata, not a full executable exception-search IR. Non-InitLocals methods require normal-flow must-assignment before local reads/address acquisition; address-first and exceptional initialization remain conservative.

Block dispatch reduces cases by coalescing straight-line instructions. Managed checks and fault offsets remain. It is not SSA, preemption or an optimizer allowed to reorder side effects. Instruction mode remains the differential baseline.

Throw/catch/rethrow/leave/finally preserve tested managed identity and continuation behavior. ExceptionDispatchInfo supports Capture, SourceException and instance/static Throw only as identity-preserving managed operations. .NET stack/Watson state, remote stack injection and full fault-clause behavior are not certified. Exception filters remain rejected until correct cross-frame search-before-unwind exists.

## Cooperative async and source-backed values

Task composition, cancellation and ValueTask execute against a single-threaded managed FIFO. Source-backed values carry a source and short version token. The completion core checks sequential registration/completion/token state, clears callback storage before invoking user code, and queues requested asynchronous/late-registration continuations. The 16-bit token wraps; it is not a security generation.

AsTask consumes one source operation and exposes repeatable Task results; Preserve makes a source-backed value Task-backed. Do not repeatedly consume or convert the original source. Generic ToString obtains its result once. Source status distinguishes cancellation from a fault whose exception happens to be OperationCanceledException.

AsyncIteratorMethodBuilder and mapped enumeration/disposal contracts support the actual Roslyn state-machine IL. Awaited cleanup, early break, nested/independent enumeration, struct values, configuration and linked cancellation are tested. Continuation flags are forwarded to custom sources. No actual ExecutionContext/SynchronizationContext capture or concurrent completion protocol is implemented. See [the detailed async contract](async-streams.md).

## Generated host ABI

`invoke(name,args)` selects an exact signature or unambiguous Type::Method export. Primitive/string results are converted; supported host-array inputs are copied into managed wrappers. JavaScript Int64/UInt64 uses BigInt outside Number's exact integer range. General interprocess/byref/callback marshalling is not supplied.

`invokeAsync(name,args,{maxSteps,yieldHost})` and Python `invoke_async(name,args,max_steps=100000)` handle Task and ValueTask results, including source-backed operations. JavaScript yields through the supplied async callback or default host scheduling; Python cooperates with asyncio. These adapters do not automatically turn exported IAsyncEnumerable objects into native host async generators. An exported Task method can consume a managed stream.

Step budgets count pump iterations, not wall-clock work inside a call. Timeout does not cancel a source or interrupt an infinite method. ExecutionContext, threads, Task.Run/Delay and full timer/continuation-option surfaces remain unsupported. ConfigureAwait can alter flags passed to a source but cannot select a nonexistent .NET context.

Each generated module owns separate runtime/static state. `setOutput`/`set_output` configure output. Root APIs retain/dereference/release own explicit host roots, not authorization or cross-module managed identities.

## Heap, reproducibility and exclusions

Ordinary objects use host GC. Weak references, identity hashes and KeepAlive have declared host semantics; JS uses the kept-alive WeakRef mechanism. Forced CLR collection, finalizers, resurrection, pinning and exact heap statistics remain unsupported. The separately linked LogicalHeap manages only its own explicit payloads and roots; it does not enable System.GC.Collect or automatic frame-root scanning.

Target source is deterministic for identical compiler and assembly inputs; installed SDK/reference-pack discovery is not a lockfile. Keep the manifest, source, toolchain and notices with releases. No full BCL/CLI, decimal/native layout/span, general reflection/dynamic code, browser/native-platform, SSA or C++ claim is made.

Diagnostics retain method/IL context where available: TR2002 for missing implementation, TR2006 for local assignment, TR2110 for CFG/region boundaries, TR300x for linkage, TR310x for specialization and TR3200 for missing original-body contracts. This is not a complete verifier or sandbox; use external process isolation and quotas for untrusted inputs.
