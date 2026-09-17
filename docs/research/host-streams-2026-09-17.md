# Native async-stream interoperability research

Checked 2026-09-17 against primary language/runtime documentation. This research informed the implemented managed-stream-v1 adapter, not a replacement for the compiler's CIL semantics.

## Contracts which are not interchangeable

**Managed iteration** returns a ValueTask<bool> for MoveNextAsync and exposes the current element separately. Source-backed ValueTask has a consumption contract; polling or abandoning a host wait must not cause the same managed operation to be issued or consumed again. The adapter therefore retains an explicit operation slot in managed storage. [S1, S2]

**JavaScript iteration** uses next() results and asynchronous return() for cleanup. The ECMAScript AsyncIteratorClose algorithm awaits the returned close result, but preserves an existing throwing completion before propagating a close failure. The actual host loop should enforce this order; the transpiler should not falsely promise C# exception precedence for a JavaScript loop body. [S3]

**Python iteration** requires explicit deterministic closing around early exits. Python documents contextlib.aclosing for early break/exception cleanup in the correct execution context. The adapter is consequently both an async iterator and an async context manager; plain async-for break is not advertised as sufficient. [S4]

**Python task cancellation** is delivered as CancelledError, a BaseException subclass, and cleanup should normally propagate it after resource release. Shielding one awaited operation is not a universal guarantee against cancellation of the caller. Our adapter protects coordination without inventing an uninterruptible or background disposal promise. [S5]

## Design derived from those contracts

Use a translated C# cursor to own IAsyncEnumerator<T>, its CTS and its outstanding move/disposal values. Keep the host layers responsible for native iteration objects, event-loop yielding, cancellation notifications and result unwrapping. This avoids duplicating the ValueTask source protocol across JS and Python and ensures ordinary compiler reachability tests can audit the managed algorithms.

A host-only call is still a compiler root. When an export is declared IAsyncEnumerable<T>, generic specialization closes StreamCursor<T> and retains its exact member set. Backend metadata supplies method identifiers and the scheduler entry point; it does not embed bytecode. Unknown or incomplete contracts are rejected instead of dynamically probing methods.

Acquisition is lazy. One adapter owns one enumeration. Never overlap a move and dispose, even when the source ignores cancellation. When waiting expires, retain the operation and provide a recoverable pending-cleanup state. Calling DisposeAsync concurrently simply to make a timeout appear successful would violate that ownership policy.

## Cancellation is a request, not reclamation

AbortSignal and Python cancellation are mapped to the cursor's managed token. Closing an in-flight operation requests cancellation and drains that operation; close between items can dispose without altering the token. Timed-out drain/disposal produces StreamCleanupPendingError and preserves a retryable cursor. Retirement occurs after actual disposal completion or terminal disposal failure, not merely after a host stops waiting.

This policy has an unavoidable limitation: application code may never complete a pending operation. The adapter cannot provide both non-overlapping disposal and unconditional prompt cleanup in that case. It reports the unfinished ownership explicitly. The user must retain and complete/close the operation, or terminate an isolated process if policy requires preemption. There is no asynchronous-finalizer guarantee or hidden worker that finishes later.

Pump budgets measure iterations, not wall-clock time. A hung managed method or completion hook cannot be interrupted by this mechanism. Trusted hooks must not await operations on their own stream, which creates an await cycle. These are explicit API constraints, not claims of a general deadlock detector.

## What the tests observe

Custom source probes count MoveNextAsync, GetResult, DisposeAsync and disposal GetResult independently. They fail if disposal overlaps a move or is issued twice. Ignored cancellation and delayed disposal exercise retries without consuming or starting another operation. Acquisition, Current and both synchronous/result-stage errors exercise cleanup boundaries. JS tests count signal listener registration/removal; both hosts check retired fields and active-stream accounting.

Native host consumers are compared with a CoreCLR consumer of the same library for values, integer precision, UTF-16 strings and Boolean output. Separate host tests preserve each language's exception-precedence behavior. All scenarios run for both emitters and source optimization modes. This is stronger than an emitted-code syntax test, but not complete scheduler, serialization or hostile-input certification.

## Delivered versus proposed

Delivered: exact-interface export roots, managed cursor, native iterator objects, Python close scopes, JS return/throw and abort integration, sequential operation enforcement, source-safe retry, active ownership diagnostics, sample drivers and tests. The host objects implement native protocols; they are not implemented with native generator syntax and do not offer arbitrary send/asend semantics.

Not delivered: arbitrary concrete/object/Task-wrapped stream-return discovery, conversion of all managed object graphs into native values, cross-module type identity, threads, execution contexts, BCL timers, native I/O capabilities, forced interruption or default-GC replacement. The implementation remains a specific ABI on the existing single-threaded portable runtime.

## Primary sources

Sources accessed 2026-09-17; mutable documentation can change. No third-party implementation code was copied for this adapter.

- S1: .NET 10 IAsyncEnumerator<T>.MoveNextAsync: https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.iasyncenumerator-1.movenextasync?view=net-10.0
- S2: .NET 10 ValueTask<T> consumption contract: https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0
- S3: ECMAScript AsyncIteratorClose: https://tc39.es/ecma262/multipage/abstract-operations.html#sec-asynciteratorclose
- S4: Python 3.13 contextlib.aclosing: https://docs.python.org/3.13/library/contextlib.html#contextlib.aclosing
- S5: Python 3.13 task cancellation: https://docs.python.org/3.13/library/asyncio-task.html#task-cancellation
