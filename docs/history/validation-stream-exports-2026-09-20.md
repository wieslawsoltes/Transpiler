# Validation evidence — generalized stream exports

Recorded 2026-09-20. The [duration/provider validation ledger](history/validation-time-services-2026-09-17.md) preserves the previous milestone and exact revision. Earlier counts and exclusions are historical, not the current stream contract.

## Exact implementation and source identity

Validated commit: **`643ad75ad7613b1e4ddc7e4802a9bb0a6b35283d`**. Git tree: **`897478fa40668ac053d2baa454b57a43478e2bb1`**. The uploaded tree equals the locally tested compiler/test/workflow index exactly.

[GitHub workflow 35490492908](https://github.com/wieslawsoltes/Transpiler/actions/runs/35490492908) completed all three jobs successfully: **conformance**, **stream-lifecycle**, and **clock-lifecycle**. The full job passed build, the unfiltered differential gate, redistribution notices and artifact packaging.

The full artifact **10598653391**, `transpiler-build-and-conformance`, has verified SHA-256 **`10a06127042a9c2a5f9bc52c8a60b6ec36092d9b57b856df5eabe87413f9722c`**. Its nested source ZIP comment identifies the implementation commit above. All **210 compiler, test and workflow files** were compared byte-for-byte against the retained source: **zero missing, extra or differing files**.

The focused stream artifact **10597909631**, `transpiler-stream-export-regressions`, has verified SHA-256 `7855c58567e739e37b014d67dd5dab47ede4d4c02694c32804a35a0578d5621d`. Its report selects six cases and is marked filtered; it is not substituted for the complete result.

## Two complete observed runs

| Environment | Registered / selected | Passed | Failed | Complete / filter |
|---|---|---|---|---|
| Local Linux, SDK 10.0.100, Node 22.16.0, Python 3.13.5 | 268 / 268 | 268 | 0 | true / empty |
| GitHub Linux x64, SDK 10.0.401, Node 22.23.2, Python 3.13.15 | 268 / 268 | 268 | 0 | true / empty |

Both runs used two harness workers. The local Release build reported zero warnings and errors. CI passed its builds independently. Harness counts group tests; they are not full-CLI or BCL compatibility percentages.

Local report SHA-256: `a56da99b1e9ec381d23592645d3df8dc4e37ba26d24e9cd590699500f8a53c4d`.

CI complete report SHA-256: `20e3b9981aee3bac5d7002d9f240f270f3a234391ff537210c7ec99360b6f16f`.

## New coverage and retained regressions

One compiler gate runs **30 structural stream-discovery assertions**, including scoped/inherited/struct/multiple-interface shapes, one-wrapper recognition, erased catalogs, nested-wrapper rejection, cycle handling and the 4,096-node graph budget.

Six Debug/Release × instruction/block/SSA configurations execute **45 lifecycle scenarios per host: 540 translated scenario executions per complete run**. Each configuration emits a real library DLL, compares a CoreCLR consumer of that same DLL with the generated consumers, checks translated StreamFactory/StreamCursor method-body provenance, and requires byte-identical repeated emission.

The lifecycle scenarios cover direct/concrete/inherited/custom-interface/value-type sources; Task/ValueTask/source-backed factories; explicit erased and ambiguous choices; no factory execution during inspection/upfront rejection; null/fault/cancellation/acquisition failures; one source consumption; pending-factory cleanup retries; close during acquisition without a move; abort/asyncio cancellation while waiting; delayed and faulting disposal; independent factories; exact Int64 fields; and repeated early-close cleanup. Ownership counters are checked at each scenario boundary.

All earlier native-stream, clock/time-service, native C++, SSA, filter, forwarding, verification, collections and logical-heap gates remain in the complete suite. The existing four host-stream configurations were also run separately before the complete suite and passed.

## Independent generated-code replay and example

The downloaded focused CI artifact was replayed unchanged on local **Node 22.16.0** and **Python 3.13.5**. All **540 scenario executions** passed again, with zero active streams, factories, timers and host waiters at terminal cleanup. No compiler or .NET process participated in that replay. This is execution-only evidence, not a third complete compiler build.

The new `samples/StreamFactories.cs` and both sample drivers were separately compiled and executed. They exercise Task<Counter>, explicitly selected ValueTask<object>, exact Int64 values, early break and awaited asynchronous disposal. Both print `9007199254740993`, `9007199254740994`, `9007199254740993`, `2`, and `0 0 0` on successive lines. [Commands and ownership rules](stream-exports.md).

## Scope and documentation successor

This completes the managed-stream-v2 generalized discovery and asynchronous factory-ownership slice. Nested/custom awaitable factories, runtime-only generic construction, complete ref-struct support, arbitrary object serialization, cross-module object transport and native threading/context semantics remain separate capabilities. The module-wide erased candidate catalog requires explicit selection and a checked actual-result cast, not a promise that every candidate fits every factory result.

Compiler-wide remaining work includes lossless signatures/loader policy, deeper verification, broader BCL/reflection, context/thread and I/O services, ordinary-object collector integration, broader optimization and managed-object native backends. See [the remaining plan](implementation-plan.md).

The documentation/sample successor preserves all 210 validated compiler/test/workflow files unchanged. CI evidence belongs to the exact implementation commit above; a documentation successor does not claim another compiler-suite run.
