# Validation evidence — duration and provider time services

Recorded 2026-09-17. The [host-clock-v1 validation ledger](history/validation-host-clock-v1-2026-09-17.md) and its preceding history preserve earlier exact revisions and counts. They do not describe the current API exclusions.

## Validated implementation

Commit: **`699cb46e1a2a780b4afaf9373a7039310b4daa45`**. Git tree: **`0ba460870719fa2101a9bae24e51452ad3b48f54`**. The uploaded tree equals the locally tested compiler/test/workflow index exactly.

[GitHub workflow 35251304501](https://github.com/wieslawsoltes/Transpiler/actions/runs/35251304501) completed successfully. Both the unfiltered **conformance** job and the expanded **clock-lifecycle** job passed. The latter includes old timer ownership scenarios and the new duration/provider/periodic-timer configurations, not merely standalone clock mocks.

The full artifact **10510136043**, `transpiler-build-and-conformance`, has SHA-256 `4327672724da53b574555abd31e56deefb0ea9a70dd2ca92dbdcb549cf95da70`. The downloaded hash was checked. Its nested source ZIP comment identifies the commit above. All **201 compiler, test and workflow files** were compared byte-for-byte with the retained local source: **zero mismatches**.

The separate clock artifact **10509786299**, `transpiler-host-clock-regressions`, has verified SHA-256 `ef9e48b37641282fe0f68f79a4d56831ddd9825fdb7bf932b528b802c24ae2f4`. Its report is filtered; it is additional evidence, not a replacement for the complete gate.

## Two observed complete runs

| Environment | Registered / selected | Passed | Failed | Complete / filter |
|---|---|---|---|---|
| Local Linux, SDK 10.0.100, Node 22.16.0, Python 3.13.5 | 261 / 261 | 261 | 0 | true / empty |
| GitHub Linux x64, SDK 10.0.401, Node 22.23.2, Python 3.13.15 | 261 / 261 | 261 | 0 | true / empty |

Both runs used two test workers. The local Release build reported zero warnings and errors. CI also passed its build and packaging steps. Counts represent registered test groups, not full CLI/BCL compatibility percentages.

Local report SHA-256: `c83a6c2208196645f939cc1d42a3ec3b9a2e1c141251bdb5a5aaf93885659346`.

CI report SHA-256: `0535bbd7e6420d3f277fc924ef266c8b775557e0677b220feb75a40864309898`.

## New and retained coverage

The duration/provider batch adds four Debug/Release × instruction/SSA configurations. Each executes 22 lifecycle groups on JavaScript and Python: **176 translated scenario executions per full run**. Each configuration also checks repeated byte-identical emission, actual managed method-body provenance and a CoreCLR oracle built from the same C# library.

TimeValues contributes four more differential configurations, exercising large signed ticks, component/hash/factory results, checked overflow, fractional timeout conversion, validation order, completed/infinite waits, timer disposal and PeriodicTimer boundaries. The earlier **26 direct clock checks** and **48 compiled timer lifecycle scenarios** remain passing in the complete suite, as do native C++, SSA, forwarding, verification, exception and stream tests.

The new scenarios cover input success/fault/timeout/cancellation winners; loser observer and registration detachment; deadlines through 4,294,967,294 ms; serialized periodic callbacks; reentrant Change; DisposeAsync during a callback; callback-only constructor state; zero-period single shot; tick coalescing; rejection of overlapping/unconsumed waits; cancellation of one tick without stopping the timer; disposal voiding a pending tick; synchronous/throwing/reentrant custom providers; custom CTS duration forwarding and reset rejection; monotonic timestamps; and native event-loop execution.

The nine-category boundary group still executes 18 target checks. Duration parsing, provider calendar APIs and Timer.Dispose(WaitHandle) are adjacent rejections; the newly supported APIs were promoted to positive tests rather than left in obsolete negative fixtures.

## Standalone example

`samples/TimeServices.cs` was separately compiled to standalone JavaScript and Python and executed through their asynchronous host entry points. Both produced 42, true, 3 and an active-timer count of zero (Python prints True). No .NET process was required to execute the generated modules. The source and commands are in [time-services.md](time-services.md).

## Boundaries and documentation successor

This implements the duration/provider/timed-wait/public Timer/PeriodicTimer continuation under **host-clock-v2**, including tests, examples and contract documentation. It does not provide full TimeSpan parsing/formatting/compound factories, calendar/time-zone TimeProvider APIs, Timer.ActiveCount/WaitHandle, native parallel callbacks, execution-context capture or finalizer-based timer cleanup. Explicit disposal and asynchronous hosted entry points remain required where stated.

The compiler-wide [remaining plan](implementation-plan.md) still includes lossless signatures, deeper verification, broader BCL/reflection, generalized stream export discovery, context/thread and I/O services, ordinary-object collector integration, broader optimization and managed-object native backends. The successful timing milestone does not certify those separate capabilities.

The final documentation/sample successor preserves all 201 validated compiler/test/workflow files unchanged. CI evidence belongs to the implementation commit above; the documentation successor does not claim an independent compiler-suite rerun.
