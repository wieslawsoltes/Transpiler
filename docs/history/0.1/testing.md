# Testing and reproducibility

## Run the complete gate

```bash
dotnet build Transpiler.slnx -c Release
python3 tests/conformance.py
```

The harness uses only Python's standard library. It runs external `dotnet` and `node` processes with a timeout. The .NET oracle runs with invariant globalization; UTF-8 output is selected for the test environment. Test results are written to `artifacts/conformance/report.json`, with generated code, imported analyses, DLLs, portable PDBs, runtime configurations, diagnostics, and capability output alongside it.

A successful process exit from the harness requires every recorded case to pass. Build errors stop the CI job before conformance. Tests are unconditional; the workflow does not skip the gate when its script is missing.

## Test topology

```text
                       ┌─ CoreCLR execution ───── stdout + exit ───┐
C# ─ Roslyn ─ same DLL ┼─ JS compilation ─ Node ─ stdout + exit ──┼─ equality
                       └─ Python compilation ─── stdout + exit ───┘

same DLL ─ repeat target compilation ─ byte equality
unsupported source ─ target compilation ─ diagnostics + no new target artifact
```

Every console fixture is compiled in Release and Debug. This matters because Debug locals/branches and Release lowering patterns stress different instruction forms. The `Arguments` fixture also checks a nonzero successful application exit code (7), so the oracle does not assume all correct programs return zero.

The sample library exercises both source compilation and host invocation. JavaScript passes 64-bit arguments as BigInt; Python passes exact integers. Signature-qualified and unambiguous short export names are supported.

## Corpus inventory

`tests/programs` contains arithmetic boundaries, recursive/control-flow programs, class dispatch and boxing, managed reference aliasing, checked arrays, UTF-16/identity-sensitive string operations, nested exception/finally/rethrow behavior, static initialization failure, floating point, and command-line arguments. `samples/Hello.cs` is also a differential fixture.

`tests/negative` covers generic methods, unlinked library calls, filters, structs, delegates, Single, reflection, interface calls, unchecked floating conversion, RVA-backed constant-array initialization, implicit external virtual overrides, and finalization. Rejection is part of the contract, not a disabled test.

The machine-readable report is the source of truth for case counts. One harness case can contain multiple target compilations/executions. Do not confuse a case count with opcode coverage or the number of individual assertions.

## Artifacts and evidence

GitHub Actions publishes `transpiler-build-and-conformance` containing the framework-dependent compiler, source archive, generated target programs, and test reports. Artifacts have the hosting service's retention policy; archive important reports for release provenance. The generated programs can be copied to a machine with Node/Python and **without .NET**.

The first green baseline, commit `ae6d8511584ebabf09befdb838f1a1dc803c7494`, passed 34 cases under SDK 10.0.401, Node 22.23.2, and Python 3.13.15 on Linux x64. Its 44 generated console programs were also executed independently under Node 22.16.0/Python 3.13.5 in an environment with no `dotnet` executable; stdout and exit codes matched the stored oracle. This is a point-in-time evidence record, not a promise about all future environments.

## Bugs caught during the initial implementation

The tests exposed a Python evaluation-order bug: accessing `array.data` before calling the null-check helper raised a Python AttributeError instead of the managed NullReferenceException. The load now checks first.

An identity test exposed full-range `Substring` allocating a new wrapper rather than returning the original string. UTF-16 split-surrogate reconstruction also required normalization in the Python representation, while preserving isolated code units for slicing.

The integer oracle exposed the CoreCLR x64 `minSigned % -1` overflow edge. The fixture now catches that exception, and both runtimes implement the explicitly selected profile behavior. The official `OpCodes.Rem` documentation is linked in the specification. This illustrates why the reference is a declared runtime/profile rather than an assumed platform-independent mathematical result.

## Adding a semantic feature

Add a minimal positive fixture and error/boundary cases before changing capability declarations. Exercise both Release/Debug code generation and both targets. Include aliasing, identity, exception timing, and initialization effects where relevant. Add negative cases for adjacent combinations that remain unsupported.

Do not modify expected output to hide a backend discrepancy. The ordinary expected output comes from the same emitted DLL under the declared .NET oracle. An intentional profile difference must be documented explicitly and tested separately, not normalized away by a global output filter.

## Next test infrastructure

The next gates should add hand-authored IL for fault clauses, invalid region transfers, prefix combinations, malformed signatures, and verifier edge cases; property-based numeric and CFG generation; reference-implementation cross-checking of intrinsic signatures; explicit emitted-opcode coverage; reflection/delegate/generic stress cases as those layers are implemented; and Windows/macOS/ARM64/browser matrices.

Performance belongs in a separate benchmark suite: compile time, peak memory, source size, startup, hot loops, allocations, calls, exceptions, and realistic workloads. The current conformance suite is not a performance benchmark.
