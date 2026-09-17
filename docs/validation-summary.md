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
