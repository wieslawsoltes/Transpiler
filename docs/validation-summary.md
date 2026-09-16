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
