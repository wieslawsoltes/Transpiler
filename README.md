# Transpiler

[![Compiler conformance](https://github.com/wieslawsoltes/Transpiler/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/Transpiler/actions/workflows/ci.yml)

**Roslyn → real PE/CIL → standalone JavaScript and Python.**

Transpiler is a working, compatibility-oriented multi-target compiler MVP. It accepts C# source or an existing managed assembly, analyzes the actual IL and metadata, and generates executable target-language source with bundled semantic helpers. Generated programs do **not** require .NET, Python.NET, a CLR subprocess, or a runtime IL interpreter.

The long-term goal is broad CLI/.NET translation through a shared managed-semantics engine. The initial **`portable-mvp` profile is an explicit subset**, not a claim that every MSIL feature, .NET library, or target language already works. C++ is designed in the roadmap but not implemented yet.

## Build and run

Requirements for the compiler/tests: **.NET 10 SDK**, **Node 22**, and **Python 3.13**. Generated programs need only their target runtime.

```bash
git clone https://github.com/wieslawsoltes/Transpiler.git
cd Transpiler

dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/Hello.cs --target js --out artifacts/hello.mjs
node artifacts/hello.mjs hello

dotnet "$CLI" compile samples/Hello.cs --target py --out artifacts/hello.py
python3 artifacts/hello.py hello
```

Both programs print:

```text
Roslyn -> CIL -> JavaScript / Python
8944394323791464
49
bounds checked
finally executed
hello
```

Compile an actual managed DLL rather than source:

```bash
dotnet "$CLI" emit-pe samples/Hello.cs --out artifacts/Hello.dll
dotnet "$CLI" compile artifacts/Hello.dll --target js --out artifacts/from-il.mjs
dotnet "$CLI" compile artifacts/Hello.dll --target py --out artifacts/from-il.py
```

## Call a translated library from its host language

```bash
dotnet "$CLI" compile samples/Library.cs --library --target js --out artifacts/kernel.mjs
dotnet "$CLI" compile samples/Library.cs --library --target py --out artifacts/kernel.py
```

JavaScript, in `artifacts/use-kernel.mjs`:

```javascript
import { invoke } from './kernel.mjs';
console.log(invoke('Kernel::Add', [9007199254740993n, 2n]).toString());
console.log(invoke('Kernel::Gcd', [84, 30]));
console.log(invoke('Kernel::Echo', ['hello']));
```

Python, in `artifacts/use_kernel.py`:

```python
from kernel import invoke
print(invoke('Kernel::Add', [9007199254740993, 2]))
print(invoke('Kernel::Gcd', [84, 30]))
print(invoke('Kernel::Echo', ['hello']))
```

Both produce `9007199254740995`, `6`, and `hello`. The JavaScript example intentionally exceeds Number's exact integer range: the managed 64-bit ABI uses BigInt.

## What is implemented

The shared pipeline imports signatures, method/field metadata, locals, IL bodies, and exception regions; normalizes compact instructions; computes reachable methods; validates evaluation-stack joins and capabilities; links virtual slots and static initialization; and emits statically generated method bodies.

The current registry contains **138 normalized opcode names and 87 exact intrinsic signatures**. Supported paths include integer arithmetic and checked conversions, binary64 operations, branches/switches/loops/recursion, classes and fields, internal virtual/newslot dispatch, primitive boxing, managed reference aliasing, checked vector arrays, UTF-16 string operations, static initialization, and non-filter exception unwinding. These counts are not full-CLI coverage percentages. See the [compatibility ledger](docs/compatibility.md) for tested combinations and limitations.

The runtime deliberately preserves distinctions that host operators alone would lose: fixed integer widths, unsigned operations, BigInt 64-bit values, reference versus content equality, managed addresses, UTF-16 indexing, null/bounds/type checks, exception identity, and pending finally continuations.

## What is not implemented

General structs/enums, generic instantiations, interface dispatch, delegates, async/iterators, reflection/dynamic code, native interop, threads, broad BCL linking, and cross-assembly implementation linking remain roadmap work. Exception filters are rejected until a correct two-pass cross-frame search protocol exists. Finalizers and external virtual overrides requiring implicit runtime/BCL support are rejected by emission guards.

Single/binary32, decimal, all formatting/globalization cases, all metadata/verifier edge cases, exact managed stack traces, and managed GC/finalization behavior are not certified. Browser ES-module integration is designed but the initial runtime tests execute Node. This PoC is **not a security sandbox**.

## Architecture

```text
C# sources ── Roslyn ──┐
                      ├─ PE/CIL + metadata ── import / normalize
Existing managed DLL ─┘                         │
                                        reachability / stack analysis
                                               │
                                      exact capability validation
                                               │
                                    linkage + source method emission
                                         ┌─────┴─────┐
                                   JavaScript      Python
                                   + helpers       + helpers
```

The current generated control-flow dispatcher selects statically emitted source blocks; it does not decode an instruction stream at runtime. Explicit CFG/SSA optimization and structured-source recovery are planned. The runtime/reference backend is intentionally retained as a correctness baseline for that work.

| Project | Responsibility |
|---|---|
| `Transpiler.Core` | Metadata/CIL import, model, analysis, capabilities and diagnostics |
| `Transpiler.Frontend.Roslyn` | C# → real PE and portable PDB |
| `Transpiler.Backends` | JavaScript/Python source generation and semantic runtimes |
| `Transpiler.Cli` | Compilation, inspection, analysis and capability commands |

Use the compiler programmatically:

```csharp
using Transpiler.Backends;
using Transpiler.Core;
using Transpiler.Frontend.Roslyn;

var managed = RoslynFrontend.Compile(
    [new SourceFile("Kernel.cs", "public static class Kernel { public static int Add(int a, int b) => a + b; }")],
    name: "Kernel",
    library: true);

var assembly = AssemblyImporter.Read(managed.Pe);
var analysis = CompilerAnalysis.Analyze(assembly);
var javascript = SourceEmitter.Emit(analysis, SourceTarget.JavaScript);
var python = SourceEmitter.Emit(analysis, SourceTarget.Python);

File.WriteAllText("kernel.mjs", javascript.Text);
File.WriteAllText("kernel.py", python.Text);
```

For existing assemblies, call `AssemblyImporter.Read(File.ReadAllBytes(path))` directly. This public API currently returns immutable records and uses explicit target selection; a dynamically loaded backend plugin registry is future work.

## Verification

```bash
python3 tests/conformance.py

dotnet "$CLI" inspect artifacts/Hello.dll --out artifacts/metadata.json
dotnet "$CLI" analyze artifacts/Hello.dll --out artifacts/analysis.json
dotnet "$CLI" capabilities --out artifacts/capabilities.json
```

CI emits each positive fixture once per Release/Debug configuration, executes that DLL with CoreCLR, then compiles the same bytes to JavaScript and Python. It compares stdout and exit code, checks byte-identical re-emission, tests library host interop, and verifies unsupported inputs fail without a new target artifact.

The current corpus has **36 cases**: 22 program configurations across both targets, 12 rejected feature categories across both targets, library ABI checks, and malformed PE handling. The hardened compiler milestone is green in [run 35087274373](https://github.com/wieslawsoltes/Transpiler/actions/runs/35087274373). The latest workflow artifact contains the machine-readable report, generated programs, compiler binaries, and source snapshot. The green baseline's 44 console outputs were additionally reproduced on a host with no .NET executable installed.

This is a measured corpus, not exhaustive certification. Read [testing](docs/testing.md), [validation evidence](docs/validation-summary.md), and [security scope](SECURITY.md).

## Research and design documents

| Document | Contents |
|---|---|
| [Industry research, 2026-09-16](docs/research/industry-state-2026-09-16.md) | 26 primary sources; Fable, Transpose/H5, WebSharper, JSIL, IL2JS, IL2CPP, IL2C, Native AOT, WebAssembly, Python.NET, ILGPU/Burst, MLIR |
| [Architecture](docs/architecture.md) | Implemented modules, object/storage model, exception protocol, future HIR/SSA and target profiles |
| [Specification](docs/specification.md) | CLI, input contract, exact behavior/boundaries, diagnostics, generated-module ABI |
| [Compatibility ledger](docs/compatibility.md) | Per-feature implementation/test status and unsupported surfaces |
| [Implementation plan](docs/implementation-plan.md) | Dependency-ordered milestones, acceptance gates, suggested granular commits |
| [Architecture decisions](docs/decisions.md) | Why actual CIL, explicit runtimes, fail-closed profiles, and differential tests |
| [Testing](docs/testing.md) | Reproduction, evidence, regression findings, next test infrastructure |

The next highest-value engineering path is **hermetic multi-assembly linking → value-type storage/copying → generic identities and interfaces → delegates/core collections → async and host adapters**, while developing stronger verification and optimized CFG/SSA lowering in parallel.
