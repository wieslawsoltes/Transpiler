# Security scope

Transpiler is an initial compiler/runtime PoC, not a sandbox or a complete CLI verifier. Do not expose it directly as an unrestricted public code-execution service.

The PE importer does not load or execute the submitted assembly. However, malicious or very large metadata can still attack resource usage or unimplemented validation boundaries. Compilation needs OS-level isolation, input limits, time/memory budgets, and a constrained output directory when inputs are untrusted.

Generated programs are executable code. Their current profile has limited host integrations, but that is not a security guarantee. Execute untrusted output in a separate restricted process/container, with no credentials or sensitive filesystem access. Host callbacks supplied through the output/module ABI are trusted application code.

The initial implementation does not guarantee bounded recursion, managed StackOverflowException behavior, exact out-of-memory recovery, hostile metadata acceptance/rejection equivalence with the CLR, or protection against every source-emission edge case. The test suite is a functional conformance corpus, not a security audit.

Future filesystem/network/native/dynamic-code adapters must declare capabilities explicitly and must not be silently enabled by a backend. See the hardening milestones in docs/implementation-plan.md.
