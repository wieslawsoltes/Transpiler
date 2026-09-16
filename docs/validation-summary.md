# Validation evidence — managed BCL and runtime delivery

Recorded 2026-09-16. This is an evidence ledger for observed commits and environments, not exhaustive compatibility certification.

## Prior integrated batch

Commit `dbecfa6adf68d05ab46f83a6b7a596d2a74f2b36`, [run 35105000729](https://github.com/wieslawsoltes/Transpiler/actions/runs/35105000729): success. Downloaded artifact 10450037211 contained a report with 64 passed and 0 failed. This was the recovered starting point for the continuation, including linking, portable BCL/tasks and host lifetime services.

## Expanded original-IL and logical-heap gate

Commit `c45129cfe524e7ddca2239d36c7247dd2614ccde`, [run 35120849201](https://github.com/wieslawsoltes/Transpiler/actions/runs/35120849201): success. Build, complete conformance, license packaging and artifact upload all succeeded.

Downloaded artifact **10457485577**: SHA-256 `2a37c6470cfb81e6f97211c1a9c8c8450bb687bea779b61b6c80dcf1cca9bc15`. Its source archive comment identifies the exact commit above. Its inspected report records **71 passed, 0 failed**, SDK **10.0.401**, Node **22.23.2**, Python **3.13.15**, Linux x64/glibc 2.39.

The original-method gate reported `[21, 21]` real CoreLib bodies for JavaScript and Python. The portable Collections fixture emitted 43 managed Transpiler.Bcl methods and one original BigMul method. Both logical-heap source configurations passed 20 independent graph-oracle rounds and their 963-allocation primary-heap checks.

## Independent local source validation

A full local build and unfiltered run passed **71 cases, 0 failures**, using SDK **10.0.100**, Node **22.16.0**, Python **3.13.5**, Linux x64/glibc 2.41. The downloaded CI source implementation was compared against the local implementation; the only test-script difference was whitespace. No cross-version byte-identical PE claim is made.

## Scope and interpretation

The 71 harness cases comprise 52 normal/BCL Release/Debug console configurations, eight negative fixtures, library ABI, malformed PE and nine extended checks. The 52 configurations execute 104 generated console programs; logical-heap configurations add four. Other graph/host cases include further executions. The assignment case includes 11 control-flow assertions, and portable rejection boundaries include multiple programs and both targets.

This proves the observed stdout/exit, deterministic-emission, provenance, rejection, host-ABI and graph contracts on these environments. It does not prove full BCL/CLI support, all valid IL shapes, default host-GC timing, CLR finalization/resurrection, native layout, concurrent scheduling, browser compatibility, performance or security isolation.

Subsequent documentation-only commits update the source snapshot and descriptions without changing this compiler milestone. The workflow badge and newest artifact remain authoritative for the latest branch state. Release packaging should retain the final commit, source ZIP, report, implementation/reference inputs and notices together.
