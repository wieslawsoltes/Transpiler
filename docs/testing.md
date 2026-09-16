# Testing and reproducibility

Updated 2026-09-16. Run from a built checkout:

```bash
dotnet build Transpiler.slnx -c Release
python3 tests/conformance.py
```

The Python harness uses only standard-library packages and launches dotnet, Node and Python child processes with timeouts. Tests write assemblies, PDBs, generated code, analyses, manifests and a machine-readable report under `artifacts/conformance`. Globalization is invariant in the oracle; UTF-8 is selected for test I/O.

## What the 71-case gate contains

There are 52 ordinary/BCL Release/Debug console configurations, eight negative fixtures, library ABI and malformed PE checks, and nine extended checks. Each ordinary/BCL configuration executes the same DLL under CoreCLR and compiles those bytes to both target languages: 104 generated console executions. The logical-heap Release/Debug extended cases add four generated console executions. Other extended ABI/graph cases execute additional programs.

The nine extended gates are definite local assignment, original CoreLib catalog, JavaScript liveness barrier, logical heap Release, logical heap Debug, three-assembly linking, portable BCL provenance, host async/root ABI, and portable rejection boundaries. A harness case can contain many assertions; counts are not opcode/BCL compatibility percentages.

## Differential protocol

Each positive console fixture is emitted once per source optimization mode. Its CoreCLR stdout and process exit status are the oracle. Both target programs must match. Repeat compilation must produce byte-identical source. Ordinary test output is not normalized to hide discrepancies. The Arguments program returns 7 deliberately, so success is not equated with exit zero in every application.

Library/host checks cover supported primitive/string/Boolean/Int64 values, cooperative task results and faults, pending-task budget failure, root retention/release and stale-handle rejection. General host-object/byref/callback transport is not certified by these tests.

Negative programs must fail with Transpiler diagnostics without producing a new target file. Portable boundary tests include forced collection, finalizer waits, resurrection-tracking weak references, thread-pool/timer APIs, unsupported collection members and reflection. Importing a reference assembly as an executable implementation is not a valid fallback.

## Evidence specific to the new runtime/BCL work

`tests/bcl/OriginalMath.cs` reaches all 21 reviewed original CoreLib integer methods. The provenance gate requires those methods to appear as real emitted bodies and rejects Math intrinsic substitution in that fixture. It also checks the upstream notice. Separate portable-library provenance proves emitted algorithms originate in Transpiler.Bcl.

`tests/CompilerChecks` is a package-free C# executable with 11 hand-authored normalized IL control-flow shapes. It checks must-assignment at joins and loops, address acquisition, unreachable code, InitLocals and conservative exceptional-region rejection. This is a targeted proof regression suite, not a complete verifier audit.

`tests/runtime/LogicalHeap.cs` checks logical ownership, generation/stale-reference behavior, explicit roots, weak clearing, cycles, quotas, bounds and accounting. Twenty deterministic randomized graph rounds are checked against an independent integer-index reachability oracle. This matters because three executions of the same incorrect collector could otherwise agree. The primary heap performs 963 allocations per fixture run; smaller auxiliary heaps exercise edge cases.

The JavaScript liveness test instruments the WeakRef barrier operation and removes the host capability to verify explicit failure. It does not assert when a real collector reclaims an object. The logical graph collector has deterministic explicit collection; the ordinary host heap does not acquire that contract.

## Filtered development runs

`TRANSPILER_TEST_FILTER` selects case names for local development. A filtered green report is not a complete gate. CI explicitly clears the filter. When reporting conformance, preserve the report's environment, complete case list and commit rather than quoting a filtered result as full success.

## Observed environments and artifacts

The full 71-case gate passed locally with SDK 10.0.100, Node 22.16.0 and Python 3.13.5. CI at commit c45129cfe524e7ddca2239d36c7247dd2614ccde passed the same gate with SDK 10.0.401, Node 22.23.2 and Python 3.13.15 on Linux x64. The downloaded source archive's commit comment was checked against that commit, and the report was inspected directly. See [validation](validation-summary.md).

GitHub Actions uploads the compiler, generated programs, manifests, reports, source snapshot and applicable notices. The source ZIP identifies its exact commit. Preserve artifacts needed for release provenance before their hosting retention expires. A later documentation-only commit is not a change to the tested compiler, but the latest commit's own CI remains the branch gate.

## Remaining qualification

Expand hand-authored IL and hostile metadata tests; exception-region/byref verification; property-based numeric/type/dispatch tests; Windows/macOS/ARM64/browser matrices; scheduler/context/reentrancy tests; memory-pressure/liveness tests without timing assumptions; and performance benchmarks. Measure startup, output size, allocation, runtime throughput and compilation resources separately from compatibility.
