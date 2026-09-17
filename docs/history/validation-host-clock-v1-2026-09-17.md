# Validation evidence — host clocks and timed cancellation

Recorded 2026-09-17. Earlier observed milestones are preserved unchanged in the [historical validation ledger](history/validation-before-host-clocks-2026-09-17.md). Those older counts and exclusions describe their own revisions, not the current implementation.

## Exact implementation and CI provenance

Validated commit: **`135da38e298bddc44e9f588031bbbcbee4875423`**. Git tree: **`0ba40c448747d701dca8c6a774dd9056a0b7ff26`**.

[Workflow 35222038685](https://github.com/wieslawsoltes/Transpiler/actions/runs/35222038685) completed both **conformance** and **clock-lifecycle** jobs successfully. Build, the unfiltered differential suite, redistribution notices and artifact packaging all passed.

Downloaded full artifact **10497484225**, `transpiler-build-and-conformance`, has SHA-256 **`d7df120270d5ab09a3871c209ebe6414f87fad09d5aa93b87ea27f42f94c6a44`**. Its nested source ZIP comment identifies the exact commit above. All **190 compiler, test and workflow files** were compared byte-for-byte with the implementation retained for the documentation update: **zero mismatches**.

The separate clock artifact is **10497727577**, `transpiler-host-clock-regressions`, SHA-256 `a871915ea9edd9cdaa1484a199d749131d604be968e558c2a60dd9c6e6e680c3`. Its targeted job does not replace the unfiltered result.

## Observed full report

```json
{
  "registeredCases": 253,
  "selectedCases": 253,
  "passed": 253,
  "failed": 0,
  "complete": true,
  "filter": "",
  "workers": 2,
  "dotnet": "10.0.401",
  "node": "v22.23.2",
  "python": "3.13.15"
}
```

The report identifies Linux x64, kernel 6.17.0-1022-azure and glibc 2.39. Counts represent registered test groups, not full-CLI/BCL compatibility percentages.

## Clock-specific evidence

The direct clock groups passed **13 checks per host, 26 total**. Compiled instruction and SSA groups each passed **12 lifecycle scenarios per target, 48 total**. Repeated source emission was byte-identical. Both groups also executed the CoreCLR async oracle and observed `42`, `9`, `11`, `22`, `1` in that order.

TimerValidation passed ordinary and SSA Debug/Release differential configurations. The portable-boundary group passed **18 rejection checks across nine categories**, including adjacent TimeSpan delay, public Timer and timed WaitAsync APIs which remain unsupported.

The timer scenarios cover successful reset before readiness; failed reset after queuing; disarm preserving queued cancellation; stale and duplicate native notifications; cancellation before/after readiness; cancellation-token identity; registration detachment; two simultaneous host waiters; host-wait cancellation without implicit Task cancellation; bulk timer cleanup; native delays; and close during a delayed move with awaited iterator-finally cleanup. Ownership counters return to zero after terminal cleanup.

## Independent execution-only replay

The downloaded generated JavaScript/Python timer modules and drivers were replayed unchanged with **Node 22.16.0** and **Python 3.13.5**. All **48 lifecycle scenario executions passed**. This environment had no .NET SDK; generated programs ran independently of the compiler. This is additional runtime evidence, not an independent compiler build or a second full-suite claim.

## Scope and revision relationship

This completes the current host-clock-v1 Int32 Task.Delay / CTS constructor / CancelAfter continuation, including timer-aware TryReset, ownership, event-loop wakeups, tests and documentation. It does not establish TimeSpan/TimeProvider overloads, public/periodic timers, context/thread services, external I/O completion, real-time deadlines or synchronous native-loop pumping. See [the implemented clock contract](host-clocks.md).

Pre-existing bounded SSA, scalar native-std output and other compiler tests remain in the complete gate. Passing them does not complete general effect-aware optimization, a managed-object native runtime or the entire [remaining plan](implementation-plan.md).

Subsequent documentation/sample-only commits do not change the 190 validated compiler/test/workflow files. The CI evidence above belongs to its exact implementation commit; preserve that relationship rather than claiming a documentation successor independently reran the suite.
