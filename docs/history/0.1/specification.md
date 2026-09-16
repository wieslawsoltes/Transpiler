# Transpiler MVP specification

Version: **0.1 / portable-mvp**, 2026-09-16. Metadata schema: 1. This document describes the implementation and its declared boundaries, not complete ECMA-335 conformance.

## 1. Product contract

Input is one managed PE assembly, or one or more C# source files compiled to that assembly by Roslyn. Output is a standalone JavaScript ES module or Python module with bundled target-language semantic helpers. Existing managed assembly input bypasses Roslyn source compilation.

The compiler must diagnose known unsupported reachable opcodes, types, and external calls before replacing the requested target output. It does not silently fall back to a CLR subprocess, Python.NET, WebAssembly, `eval`, or runtime IL decoding. The initial verifier is not complete enough to certify arbitrary hostile/unverifiable assemblies.

The supported semantics are measured by the checked-in conformance corpus. A listed opcode is not a promise that every possible ECMA operand/type combination is implemented. The machine-readable capability command describes registered operations, while the compatibility matrix describes restrictions and test evidence.

## 2. Toolchain and commands

Build the compiler with a .NET 10 SDK. The repository selects C# 14 and uses the compiler assemblies supplied by the resolved SDK. Node 22 and Python 3.13 are the CI execution targets. Python output uses structural pattern matching and therefore requires Python 3.10 or later at the syntax level; versions other than CI's selected version have not been exhaustively tested.

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/Hello.cs --target js --out artifacts/hello.mjs
node artifacts/hello.mjs welcome

dotnet "$CLI" compile samples/Hello.cs --target py --out artifacts/hello.py
python3 artifacts/hello.py welcome
```

Assembly-first operation:

```bash
dotnet "$CLI" emit-pe samples/Hello.cs --out artifacts/Hello.dll
dotnet "$CLI" inspect artifacts/Hello.dll --out artifacts/metadata.json
dotnet "$CLI" analyze artifacts/Hello.dll --out artifacts/analysis.json
dotnet "$CLI" compile artifacts/Hello.dll --target js --out artifacts/from-il.mjs
dotnet "$CLI" compile artifacts/Hello.dll --target py --out artifacts/from-il.py
```

`emit-pe` also produces a portable PDB for source input and a .NET 10 runtime configuration for executable input. The command does not execute the emitted assembly. `inspect` decodes without performing portable-profile analysis. `analyze` reports normalized methods and stack states. `capabilities` prints JSON containing the opcode/intrinsic registry and profile limitations.

Common flags: `--library`, `--debug`, repeatable `--reference path.dll`, `--ir path.json`, `--diagnostics path.json`. The CLI accepts `-o`, `-t`, and `-r` aliases. Source files must have distinct file names. It does not yet load a `.csproj`, resolve NuGet assets, run source generators, or link multiple implementation assemblies.

Exit codes: 0 success; 1 compilation/capability failure; 2 usage or file-access failure. Unexpected compiler implementation faults are not disguised as normal capability diagnostics.

Output writes use a temporary file in the output directory followed by replacement. Compilation/capability failures do not create a new target artifact. An older artifact already at that path is not automatically deleted; callers must use the exit code rather than file existence to infer success. Analysis/diagnostic sidecars are not a multi-file transaction.

## 3. Reachability and linking

An executable has one managed entry point. A library exports public static methods. The entry point may take no arguments or a string vector and return void/int in normal Roslyn-produced programs. Library overloads are selected by their full managed signature; the short `Type::Method` spelling is accepted only when unambiguous.

The current identity model uses the assembly name, type name, method name, parameters, and return type for method resolution. It is not a complete loader identity model with assembly versions, cultures, public-key tokens, load contexts, and type forwarding. Ambiguous or adversarial metadata remains outside the supported input contract.

Internal calls resolve into the input assembly. External calls must match a registered signature and an allowed framework assembly name. A reference supplied to Roslyn does not automatically link that reference's IL. This distinction is particularly important for existing NuGet libraries.

Conservative import rejections include mixed-mode/native-entry images, netmodules, MethodImpl maps, and vararg calling conventions. Metadata import and stack analysis do not constitute full managed type safety verification.

## 4. Execution semantics

### Integers

Storage types include Boolean, Char, signed/unsigned 8-, 16-, 32-, and 64-bit integers. Evaluation stack kinds collapse narrow integers to `i4`. Helpers interpret signedness from the operation, not solely from the host representation. Storage coercion implements narrowing; unchecked arithmetic wraps; checked operations throw a managed OverflowException when out of range. Signed division truncates toward zero, and remainder follows that quotient convention. Shift counts are masked to five or six bits for 32-/64-bit values.

The profile selects CoreCLR x64 behavior for `minSigned / -1` and `minSigned % -1`: both raise OverflowException. The latter is explicitly documented as an Intel-specific possibility in the official `OpCodes.Rem` API documentation: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.rem?view=net-10.0 . The initial oracle is Linux x64; this is not a claim that every CLR architecture chooses the same remainder behavior.

JavaScript 64-bit values use BigInt. Public host calls must use BigInt outside the exact Number integer range. Python uses integers with explicit width normalization. Native-sized integers and pointers are unsupported.

### Floating point

Binary64 arithmetic, signed zero, infinity, NaN comparisons, and checked floating-to-integer conversion are implemented. Unchecked floating-to-integer conversion is rejected because this MVP has not committed to all unspecified/out-of-range target policies. Single/binary32 storage and rounding are rejected. Decimal and SIMD are not implemented.

Current numeric text formatting covers invariant, common round-trip/general cases exercised by tests; it is not a complete .NET formatting/culture implementation. NaN payload preservation, all shortest-decimal tie cases, and exhaustive binary64 conversion/formatting equivalence are not certified.

### Objects, arrays, strings, and references

Class objects preserve identity. Instance fields, constructors, static fields, supported type initialization, local class virtual slots, and `newslot` dispatch are implemented. General structs, interfaces, generic types, arbitrary MethodImpl dispatch, and external virtual overrides require additional lowering.

Arrays are checked zero-based single-dimensional vectors. Loads/stores check null and bounds; reference-element stores check runtime compatibility. General rectangular/non-zero-lower-bound arrays are unsupported. Nested covariance and the full array interface surface are not certified.

Strings have explicit reference identity. Literal interning, ordinal content equality, UTF-16 length/indexing, substring slicing, and the registered concatenation overloads are implemented. Python normalizes paired-surrogate representations so concatenating separately sliced halves yields the original UTF-16 sequence. String comparison, normalization, globalization, and all BCL overloads are not implied by those operations. Console encoding of isolated surrogates across write boundaries is not a completed compatibility surface.

Managed references model argument/local/field/array/box storage. Tests cover aliasing and byref returns. This is not a complete byref escape/lifetime verifier or support for unmanaged addresses. Primitive boxing preserves boxed type and copied value; general struct boxing is not implemented.

### Exceptions and initialization

Registered managed exception objects can be thrown and caught by assignable type. `leave`, `finally`, `rethrow`, nested unwinding, and exception replacement are implemented. The runtime contains fault-handler lowering, but the C# corpus does not provide full fault-clause coverage. Exception filters are rejected. Exact managed stack traces, exception-dispatch stack preservation, all framework default/localized message text, and serialization are outside the current guarantee. Explicitly supplied exception messages are preserved.

Type initializers have running/completed/failed state. Failure remains cached. `beforefieldinit` allows deferred initialization. Cross-thread synchronization and explicit GC/finalization services are unsupported.

## 5. Exact library surface

Run the registry command for the authoritative list:

```bash
dotnet "$CLI" capabilities --out artifacts/capabilities.json
```

The initial registry includes selected Console.Write/WriteLine overloads; Object construction/reference equality/string conversion; String equality, length, indexing, substring, and selected concatenation overloads; primitive ToString; selected exception constructors and Message; Math.Abs/Min/Max/Sqrt/Floor/Ceiling/Truncate; and Double.IsNaN/IsInfinity.

Each binding includes managed type, member name, parameter types, return type, static/instance distinction, and an accepted framework assembly name. Matching a signature does not imply the entire declaring class is implemented. There is no general fallback for filesystem, networking, reflection, Tasks, collections, delegates, LINQ, globalization, native interop, or UI frameworks.

## 6. Generated-module ABI

Compile the example library:

```bash
dotnet "$CLI" compile samples/Library.cs --library --target js --out artifacts/kernel.mjs
dotnet "$CLI" compile samples/Library.cs --library --target py --out artifacts/kernel.py
```

JavaScript:

```javascript
import { invoke, setOutput, manifest } from './kernel.mjs';
console.log(invoke('Kernel::Add', [9007199254740993n, 2n]));
console.log(invoke('Kernel::Gcd(System.Int32,System.Int32)', [84, 30]));
console.log(invoke('Kernel::Echo', ['hello']));
setOutput(text => process.stdout.write(text));
console.log(manifest.exports);
```

Python:

```python
from kernel import invoke, set_output
print(invoke('Kernel::Add', [9007199254740993, 2]))
print(invoke('Kernel::Gcd(System.Int32,System.Int32)', [84, 30]))
print(invoke('Kernel::Echo', ['hello']))
set_output(lambda text: print(text, end=''))
```

`invoke(name, args)` validates export selection and arity, coerces primitive/string/array inputs, and unwraps string/Boolean outputs. General object marshalling, mutable host-array identity, promises/awaitables, callbacks, and byref host ABI are not stable public surfaces. Array inputs are copied into managed wrappers; array outputs remain runtime wrappers in this MVP. Each imported generated module has its own static/runtime state.

Executable modules expose `main(args)` and automatically invoke it only when executed as the main file. A browser may import the JavaScript ES module and call `main` explicitly with a custom output writer; browser integration is designed but not covered by the initial Node-based conformance run.

## 7. Diagnostics

| Code family | Meaning |
|---|---|
| `TR0001` / `TR0002` | CLI/file access or diagnostic sidecar errors |
| Roslyn `CS...` | Source compilation errors |
| `TR0101` | Missing compiler-host framework reference set |
| `TR1001` / `TR1002` | Invalid PE/metadata or native/mixed-mode input |
| `TR1010` / `TR1011` | Unsupported MethodImpl map / varargs |
| `TR2000` | No entry point or library roots |
| `TR2001` / `TR2002` / `TR2003` | Unsupported opcode / external call / type |
| `TR2004`–`TR2008` | Missing body, generics, local initialization, or field capability issues |
| `TR2010`–`TR2012` | Filter/handler, interface dispatch, or external virtual-slot limitation |
| `TR2100` / `TR2101` | Stack/control-flow/type-shape validation |
| `TR2200` and later | Backend linkage/profile validation |

Diagnostics include method signatures and IL offsets when available. Portable PDBs are emitted but source-level mappings from IL diagnostics and generated source maps are planned rather than implemented.

## 8. Determinism and safety

Re-emitting the same assembly under the same toolchain produces byte-identical target source in CI. This is not a claim of byte-identical PE output across different SDK/reference-pack versions. Generated source contains target-language method implementations and semantic helpers, not the original executable image.

Resource quotas, cancellation, hostile-metadata fuzz hardening, recursion trampolines, stack-overflow compatibility, and a capability sandbox remain production-hardening requirements. Run untrusted compilation and generated code in separate restricted processes/containers. Do not expose the PoC directly as a public arbitrary-code execution service.
