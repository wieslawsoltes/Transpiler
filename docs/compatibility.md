# Current compatibility ledger

Updated 2026-09-17. Both targets share the compiler analysis and lowering. Entries describe implemented/tested slices, not exhaustive ECMA/.NET compatibility. Metadata schema 2, profile `portable-mvp`, optional `portable-bcl-v1`.

| Area | Implemented/tested | Still outside the guarantee |
|---|---|---|
| Frontend / input | C# 14 via Roslyn, actual PE/CIL, Debug and Release | Full project/source-generator/NuGet driver |
| Assembly graph | Explicit multi-assembly linkage, scoped ExportedType chains, nested forwarding, identity conflicts and deterministic order | General framework facade policy, redirects, multiple versions/load contexts |
| Structural identity | Recursive scope-aware rewriting of the existing type codec; exact substitution boundaries | Lossless CLI signatures, every custom modifier/function pointer and full loader context |
| Generics | Bounded closed specialization and distinct statics | Dynamic/open construction, full constraints/code sharing |
| Values / addresses | Struct copies, aliases, boxing, enums and nullable paths | Explicit native layouts, complete span/ref-struct/byref verification |
| Dispatch | Classes/newslot, tested interfaces/MethodImpl/variance, delegates, selected Object/ValueType bridges | All default-interface/GVM/interop/delegate combinations |
| Numeric model | Fixed integer widths, checked arithmetic, BigInt Int64, binary64 and binary32 slices | Decimal, general native pointers/SIMD, exhaustive formatting/globalization |
| Initialization data | Selected RVA-backed primitive array initializers | Arbitrary data blobs/native layouts |
| Arrays | SZ and rectangular arrays, lower bounds, checked Get/Set/Address, selected creation/clone/clear/enumeration/conversion | Complete Array overload/interface/interop surface |
| Type identity | typeof/type handles, GetType and selected element/rank/equality operations | Member reflection, general Name/FullName APIs, dynamic loading/emit |
| Strings | Identity, interning, selected UTF-16 operations, ordinal comparer | Full culture/case-folding/formatting and isolated-surrogate I/O parity |
| Collections | Selected List/Queue/Stack/Dictionary/HashSet and read-only wrappers; comparer/equality paths; collision/alias tests | Complete declaring-type parity, exact capacity/performance and every overload |
| List algorithms | Selected range/search/reverse/sort/binary-search/conversion operations | Every pathological comparer and full framework optimization behavior |
| LINQ | Selected lazy filtering/projection/flattening/set/materialization/aggregation operations | Entire LINQ surface, IQueryable, arbitrary async LINQ |
| Original BCL | 21 reviewed CoreLib integer Math bodies, proven by origin manifests | Complete Math or arbitrary CoreLib closure |
| Tasks | Task/Task<T>, composition, completion sources, fault aggregation, selected waits | Threads, Task.Run/Delay, full continuation/context/option surface |
| Cancellation | Tokens, registrations, linked CTS, reentrant callbacks, selected WaitAsync and token-carrying faults | Timers/WaitHandle, concurrent cancellation, context capture |
| ValueTask | Result/Task/source-backed values, awaiters, AsTask/Preserve, selected factories/equality | Pooling/allocation parity and arbitrary invalid multiple consumption |
| Completion sources | IValueTaskSource interfaces and sequential reusable ManualResetValueTaskSourceCore | Multithreaded registration/completion, actual execution/scheduling context |
| Iterators | Ordinary and asynchronous Roslyn state machines, independent enumeration, generic/covariant values | All compiler/runtime combinations; arbitrary concrete/Task-wrapped stream exports |
| Async disposal | IAsyncDisposable, configured await using, awaited finally, early exit and exception replacement | All resources/host lifetime integrations |
| Exceptions | Throw/catch/rethrow/leave/finally, live-frame two-pass filters, persisted fault-clause tests, selected aggregation and identity-preserving ExceptionDispatchInfo | Exact .NET traces/Watson state, native exceptions, remote stack injection and exhaustive handler certification |
| CFG / verification | Prefix/region/filter validation, stack joins, conservative exception-entry assignment, returned-byref origins and typed indirect access | Complete verifier, scoped-ref/interprocedural escape summaries, address-first and precise cleanup assignment |
| Source emission | Instruction reference mode and validated basic-block mode | SSA, stack elimination, source maps, idiomatic reconstruction |
| Host ABI | Primitive/string/selected array inputs, Task/ValueTask results, native stream protocol adapters, output/root APIs | General byref/callback/serialization/object transport across modules |
| Host stream ownership | Lazy exact-interface exports, single-consumption moves/disposal, cancellation, Python close scopes, resumable cleanup | Uncooperative-source prompt cleanup, cross-loop/thread use, abandoned-adapter finalizers |
| Host lifetime | Weak references, identity hash, KeepAlive and explicit roots | Forced CLR collection, finalizers/resurrection/pinning |
| Logical GC | Translated C# bounded mark/sweep with its own explicit-root graph oracle | Ordinary-object integration, implicit locals, relocation/generations/concurrency |
| Platforms | Node/Python differential hosts | Browser/OS/ARM64 qualification and actual C++ backends |
| Security | Import without executing assemblies, bounded selected structures, explicit failures | Full hostile-input/resource isolation and sandboxing |

## Evidence and terminology

The configured gate contains **135 cases**: 106 ordinary/BCL console configurations, four negative fixtures, library ABI and malformed PE, plus 23 extended gates. Each ordinary/BCL configuration runs both generated targets: 212 executions. The block comparison checks 12 programs in two source modes and two targets: 48 instruction/block pairs, or 96 target executions. Additional host, graph and logical-heap tests perform further executions.

The four new source/stream fixtures add eight differential cases, and the source-host/provenance test adds one extended case. Source callback flags, stale tokens, single consumption, reset reentrancy, cleanup and cancellation are directly tested. The current report, not the configured count, records observed success; see [validation](validation-summary.md).

Capabilities lists opcodes and fixed intrinsic signatures. Generic contract patterns, legal type combinations, metadata, BCL members, host services and performance are separate dimensions. No full-CLI percentage is claimed. [Async contracts](async-streams.md) describe the no-context scheduling and exception-trace boundaries precisely.

The native-host continuation adds four Debug/Release × instruction/block configurations. Each runs 22 JavaScript and 23 Python lifecycle groups plus an ordinary CoreCLR stream consumer oracle. It checks deferred acquisition, no overlapping move/disposal, exact source consumption, timed-out cleanup recovery, listener retirement and error precedence. `StreamCleanupPendingError` is not successful disposal; active ownership remains visible.

The filter/identity/verification continuation adds six ordinary configurations and promotes the former Filter rejection fixture into the positive corpus. Extended checks cover 32 filter/retirement target executions, an unchanged consumer through forwarding facades, 25 structural identity assertions, 22 safety assertions and four persisted fault-IL executions. Passing these slices does not establish universal compatibility; [linking-verification.md](linking-verification.md) states their conservative boundaries.
