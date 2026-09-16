# Validation evidence — initial delivery

Date: 2026-09-16. This file records observed milestones, not a timeless compatibility claim.

## Green compiler baseline

Commit: `ae6d8511584ebabf09befdb838f1a1dc803c7494`.

Workflow: https://github.com/wieslawsoltes/Transpiler/actions/runs/35086884135 . Result: **success**.

Downloaded report: **34 passed, 0 failed**. Environment: Linux x64, .NET SDK 10.0.401, Node v22.23.2, Python 3.13.15. The corpus included 22 Release/Debug console configurations compiled to both targets, 10 negative categories on both targets, library host interop, and malformed PE handling.

The generated output was downloaded and rerun in a separate environment under Node 22.16.0 and Python 3.13.5. All **44 target console executions** matched the report's CoreCLR stdout and exit codes. That environment had no `dotnet` executable, providing direct evidence that generated programs do not need a CLR at execution time.

## Hardened compiler milestone

Commit: `e60090d211b0be0db7cbb04e6f826059e525c7af`.

Workflow: https://github.com/wieslawsoltes/Transpiler/actions/runs/35087274373 . Result: **success**.

This adds emission-time checks for implicit external virtual-slot behavior and finalization, plus two rejection fixtures. The configured corpus now contains **36 cases**. The guard rejects unsupported behavior even when ordinary direct-call reachability would not visit the relevant override/finalizer body.

## Measured capability inventory

The emitted capability manifest contains **138 normalized opcode names** and **87 exact intrinsic signatures**. Compact opcode encodings are normalized; some operand/type combinations are deliberately rejected. These numbers are not a percentage of CLI coverage or a guarantee of full declaring-class/BCL support.

## Scope of the evidence

The gate checks stdout and process exit code against the same DLL executed by CoreCLR; deterministic target source; ordinary library ABI values; structured rejection; and malformed PE failure. It does not establish full type safety, exhaustive metadata correctness, all floating formatting cases, all host operating systems, ARM64 behavior, browser integration, high performance, or security isolation.

A later documentation-only commit can update the repository without changing the compiler tested above. The workflow badge and newest artifact report are authoritative for the current branch. Release packaging should preserve the exact commit, report, source archive, and toolchain manifest together.
