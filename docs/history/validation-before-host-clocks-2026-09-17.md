# Validation evidence — async stream continuation

Recorded 2026-09-16. This ledger distinguishes observed results from configured coverage and historical claims.

## Recovered baseline

Commit `f87fe22d8f4c06605546cd788f81ebde9e996ef3`, workflow [35147796744](https://github.com/wieslawsoltes/Transpiler/actions/runs/35147796744), succeeded with **112 passed, 0 failed**. Artifact 10468940256 was downloaded, its report inspected, and its source-archive commit identity verified. This is the actual continuation baseline, including previously committed collections, numeric/array, cancellation and block-dispatch work.

## New implementation milestone

Commit `03ad4a5043020c58058da487c9bb67c57b094357`, workflow [35150624355](https://github.com/wieslawsoltes/Transpiler/actions/runs/35150624355), completed build, full conformance, notices and packaging successfully.

Downloaded artifact **10469550726**, SHA-256 `71ac74c8c92999b38cb1eb0f3dbd86e7c65a9fa162d4d0477ab10c0f9dd2725b`, contains **121 passed, 0 failed**. Environment: .NET SDK **10.0.401**, Node **22.23.2**, Python **3.13.15**, Linux x64/glibc 2.39. The embedded source ZIP identifies the exact commit above. Every source and test file was compared with the local tested implementation; there were zero mismatches.

The source-host protocol gate reports **98 translated portable managed methods per target**, exactly two source consumptions for its two operations, released callback state, and rejection of unsupported context/remote-stack calls. This is one fixture's method count, not the whole BCL inventory.

The block gate compares **48 instruction/block pairs** from 24 Debug/Release configurations. For the Release AsyncStreams fixture, each target reduced dispatch cases from **6,302 to 1,153**, preserving stdout and exit code. JavaScript source size changed from 1,805,437 to 1,656,869 bytes; Python from 1,871,339 to 1,721,618 bytes. These are emission-size measurements, not throughput speedups.

## Independent local checks

Before the full CI result, local builds and filtered differential runs passed source-backed ValueTask, async streams, stream-edge and source-edge fixtures in Debug/Release, plus the source-host protocol test. The local environment used SDK 10.0.100, Node 22.16.0 and Python 3.13.5. Filtered results are not presented as an independent full-suite pass.

The standalone sample `samples/AsyncStreams.cs` was compiled in block mode and run on both local targets. Both printed `asynchronous cleanup completed` followed by `30`, demonstrating awaited cleanup before reporting the result. Its local emission reported 3,456 instructions, 714 dispatch cases and 266 method entries; entries may include declarations, so this is not a count of distinct BCL algorithms.

## Scope

The 121 harness cases comprise 100 ordinary/BCL console configurations, five negative fixtures, ordinary library ABI, malformed PE and 14 extended gates. The first group generates 200 target executions. Block equivalence adds 96; host, graph and heap gates add further executions. Counts describe tests, not universal compatibility percentages.

The evidence covers the observed semantic, provenance, cleanup, deterministic-output and emission-size contracts. It does not certify threading/context capture, timers, all ValueTask misuse, full async LINQ, native host-generator marshalling, exact .NET traces, exception filters, full reflection/BCL/CLI support, performance or security isolation.

Later documentation-only commits do not alter the implementation milestone. The latest branch's own workflow and source ZIP are authoritative for its exact revision. Preserve final source, report, implementation inputs, toolchain and notices together.

## Native host-stream continuation — 2026-09-17

Recovered main `db6b778404be67b9dd660b8f49bb2799bb5d5415`, workflow 35151844720, was successful. Its source ZIP and 121-case report were inspected from artifact 10469761092. This is the actual baseline for the host ABI work, not an inferred state from earlier conversation text.

The new full local, unfiltered run passed **125 cases, 0 failures**, using SDK **10.0.100**, Node **22.16.0**, Python **3.13.5**. It includes all existing cases plus four native stream Debug/Release × instruction/block configurations. Each reports **22 JavaScript** and **23 Python** lifecycle groups and **84 translated closed-cursor method bodies** for its multi-element fixture. The method count includes several constructed element types and is not 84 different algorithms.

The four cases compare native streamed output with a CoreCLR consumer of the same input DLL, and each checks deterministic source re-emission. They cover exactly-once operation consumption, early closure, cancellation drain, pending cleanup retry, deferred acquisition, signal/reference retirement and native exception precedence. The standalone host sample additionally passed both targets and printed `managed enumerator disposed`, `30`, `0`.

These results do not establish full BCL/CLI conformance, immediate cleanup of uncooperative sources, cross-loop/thread operation, native generator send/asend semantics, all object marshalling or a preemptive timeout. The final commit and CI artifact should be preserved with their report and notices; subsequent documentation-only changes do not alter the tested compiler implementation.

The local Git tree of the complete tested source/test snapshot is `51d6d3c0c6bf5c26d7646ef391fd46cbe785591a`, exactly matching remote implementation commit `102cfb35e4f93b18e0c2f19895e23e0cd844c55a`. This verifies all file bytes and Git modes against the passed local implementation, independently of CI completion.


## Filters, forwarding and verification — 2026-09-17

The recovered starting point was `8985bb650586415c9b54ecdeed69a80c9e1bf772`, with the exception-search runtime committed but not enabled by the compiler. This continuation integrated and tested that runtime, added structural rewriting/explicit forwarding, and strengthened managed-address and exception-local validation.

Implementation/test commit **`4459251af2d6204537dd8fd676f4b88f720bfb58`**, tree **`a41758f59a7735213996fba22156852e3416ffe7`**, passed [workflow 35208855495](https://github.com/wieslawsoltes/Transpiler/actions/runs/35208855495). Build, full conformance, notices and packaging completed successfully.

Downloaded artifact **10491512104** has SHA-256 `bb0259e10637291503e34f8ae44aeb5571ff0daadfa043d31fec2d688eb078cd`. The source ZIP comment identifies the exact implementation commit. All compiler, test and workflow files were compared with the independently tested local checkout: **zero mismatches**.

The inspected report records **135 passed, 0 failed**, `complete: true`, `registeredCases: 135`, `selectedCases: 135`, an empty filter and two workers. CI used SDK **10.0.401**, Node **22.23.2**, Python **3.13.15**, Linux x64/glibc 2.39. The independent full local run also passed **135/135**, using SDK **10.0.100**, Node **22.16.0**, Python **3.13.5**, Linux x64/glibc 2.41. An earlier interrupted exploratory run is not counted as a full pass.

The added gates include 25 structural/forwarding assertions, 22 safety/filter-layout assertions, a client DLL left unchanged while its contract assembly is replaced with a facade, 32 filter/order/activation-retirement target executions, and four persisted fault-IL executions. The latter runs non-InitLocals exception code and observes `normal path`, `filter before fault`, `fault during unwind`, `filtered catch`, `42` on CoreCLR and both backends.

The standalone filter sample was executed locally with JavaScript block dispatch and Python instruction dispatch. Both observe the filter reading state 1 before the callee's finally changes it, followed by the catch observing state 2. Its generated methods total 36 analyzed instructions; block dispatch uses 10 cases and instruction dispatch 36. This is an emission inventory, not a runtime speedup claim.

This evidence covers the declared managed subset, not complete loader/signature/verification fidelity, native exception traces, all CLI inputs, context/thread/timer services, SSA, generalized reflection/marshalling, ordinary-object logical-GC integration or C++ backends. Documentation-only successors do not change this tested implementation; release source, reports and input notices must retain their exact revision relationship.


Independent execution-only replay of the downloaded artifact passed **374 target executions, 0 failures** under Node 22.16.0 and Python 3.13.5, with `dotnet` absent from the child execution PATH. Generated source bytes were not modified. Python frame-retirement driver scripts contain their original CI absolute import path; replay copies of those drivers changed only that path to the corresponding downloaded source location. The replay includes the 212 ordinary console outputs, block/reference comparisons, logical heaps, native streams, filter/order/retirement checks, forwarding, raw fault IL and selected library/source-host ABI drivers. This is separate evidence from compilation conformance and does not imply every possible target program was tested.
