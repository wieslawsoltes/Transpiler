# Security scope

Transpiler is a compiler/runtime research implementation, not a complete CLI verifier or a security sandbox. Do not expose unrestricted compilation or generated-code execution directly to untrusted users.

The importer reads PE metadata and IL without loading the submitted assembly for execution. Nonetheless, malicious/large metadata, recursive types, specialization graphs and source-emission edge cases can attack resources or unimplemented validation boundaries. Use separate restricted processes, bounded input/output directories, CPU/time/memory limits and no ambient credentials.

Assembly identity checks detect binding conflicts; they do not authenticate publishers. Reference-assembly filtering, stack analysis and definite local assignment are useful functional validations, not proof of complete type safety or hostile-input acceptance equivalence with CoreCLR.

Generated code is executable code. Host output callbacks and application-supplied objects are trusted code/data integrations, not isolation boundaries. The cooperative task pump budget counts iterations and cannot interrupt an infinite managed call. Timeout of a host adapter does not cancel the underlying translated task.

Default runtime root handles and logical-heap handles express ownership only; they are not authorization tokens. The logical heap validates its own address ownership/generations, but it is not a native memory protection mechanism or the default generated-object collector. Its quotas cover logical payload/reference/table capacities, not total process memory, host frame allocations or execution time.

Forced collection, finalizers, resurrection, pinning, native calls and broad I/O/threading capabilities are not implicitly enabled. Future adapters must declare their capability, lifetime and trust contracts explicitly. Add hostile metadata fuzzing, full region/byref verification, cancellation/resource controls and host matrices before production exposure.

## Host stream ownership

Native stream adapters own their acquired enumerator until disposal completes or fails terminally. A StreamCleanupPendingError leaves a retained cursor and outstanding operation; ignoring it is not safe resource cleanup. Keep the adapter, complete the application operation and retry close. Neither host garbage collection nor an asynchronous finalizer promises to execute abandoned managed cleanup.

Abort/cancellation and pump limits are cooperative and do not interrupt a non-returning managed method or a host yield callback that never resolves. Hooks are trusted, must not await their own adapter operations and must provide their own external limits. Adapters are one-event-loop/single-thread objects, not cross-thread synchronization primitives. Diagnostic cursor fields/counters are not security capabilities or a serialized ABI.
