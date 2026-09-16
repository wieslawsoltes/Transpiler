# Compatibility ledger

Profile: `portable-mvp`. Both current backends use the same capability analysis and source-lowering decisions. **Implemented** means the code path exists; **tested** refers to the named corpus, not exhaustive CLI certification. Run `capabilities` to enumerate the current 138 normalized opcode names and 87 exact intrinsic signatures. These counts include normalization/conversion variants and must not be presented as a percentage of complete .NET support.

| Area | Current behavior | Evidence / boundary |
|---|---|---|
| C# frontend | Roslyn C# 14 emits real PE/PDB | All source fixtures; compiler uses host reference assemblies |
| Existing DLL input | PEReader/SRM import, no Assembly.Load | Every differential target compilation uses a pre-emitted DLL |
| IL bytes / metadata | Operand decoding, signatures, locals, EH | Positive corpus and malformed PE test; not hostile-input certification |
| Branches / switches / loops | Generated source dispatch with stack joins | ControlFlow, Hello, all Debug builds |
| Recursion | Host call stack | Fibonacci fixture; deep recursion/StackOverflowException not equivalent |
| Integer arithmetic | Explicit 32-/64-bit semantics; narrow storage | Integers, checked overflow, unsigned ops, division/remainder, shifts |
| 64-bit JavaScript precision | BigInt | Values above 2^53 in Integers, References, library ABI |
| Binary64 | Arithmetic, signed zero, NaN/unordered comparisons | FloatingPoint; exhaustive formatting/payload equivalence not claimed |
| Binary32 / decimal / SIMD | Rejected or unavailable | Single rejection; remaining surfaces not implemented |
| Native integers / pointers | Rejected | No portable pointer width or unsafe-memory profile yet |
| Classes / constructors / fields | Identity, inheritance, typed storage | Objects, References |
| Static initialization | Once/running/cached failure; deferred beforefieldinit | StaticInitialization; single-threaded only |
| Virtual calls / newslot | Internal class slot dispatch | Objects; MethodImpl maps conservatively rejected |
| External virtual overrides | Rejected when reachable type requires a bridge | ExternalVirtual; backend profile guard also checks implicit BCL invocation |
| Finalizable objects | Rejected by emission guard | Finalizer; no managed GC/finalization services |
| Interfaces | Calls/types rejected | Interface fixture; no explicit/default interface method support |
| General structs / enums | Rejected | ValueType; no layout/copying ABI yet |
| Primitive boxing / unboxing | Box identity, exact boxed type checks | Objects |
| Managed references | Aliased args/locals/fields/arrays; byref returns | References; complete escape/type-safety verification not implemented |
| SZ arrays | Null/bounds checks; typed storage; basic covariance | Arrays; nested covariance/interface surface not certified |
| Constant-array RVA initialization | Rejected through unsupported helper/token path | ArrayRva; direct handcrafted RVA-backed storage not a supported input |
| Rectangular / non-zero-bound arrays | Unsupported | Requires metadata/storage and BCL lowering |
| Strings | Identity, literal intern, UTF-16 length/index/slice, registered concat/equality | Strings; paired-surrogate reconstruction tested |
| String/number globalization | Limited invariant output only | No culture-aware BCL surface; isolated-surrogate streaming I/O not complete |
| Throw / catch / leave / finally | Explicit managed exception and continuation protocol | Exceptions, Arrays, StaticInitialization |
| Rethrow / replacement / nested finally catch | Implemented | Exceptions |
| Fault clauses | Runtime path implemented | Needs hand-authored IL differential tests; not fully certified |
| Exception filters | Rejected | Filter; full two-pass cross-frame search planned |
| Exception messages / traces | Explicit messages preserved; diagnostic defaults are limited | No exact localized defaults or .NET stack trace guarantee |
| Generic types / methods | Rejected | Generic; no reified/specialized instantiations yet |
| Delegates / closures / events | Rejected through types/opcodes/library dependencies | Delegate |
| Async / iterators / Tasks | Unsupported library/type closure | Requires value types, generics, interfaces and task/iterator runtime |
| Reflection / typeof tokens | Rejected | Reflection |
| Dynamic loading / Reflection.Emit | No implementation or hidden fallback | Planned separate opt-in dynamic profile |
| P/Invoke / native / mixed mode | Rejected | Body/native checks; no host ABI adapter |
| Volatile / atomics / threading | Unsupported | Prefix/modifier/host contracts not implemented |
| Files / sockets / general BCL | Exact bindings only; unregistered calls fail | ExternalLibrary; not arbitrary .NET library compatibility |
| Library host ABI | Public static exports; primitive/string/coerced-array inputs | library/host-interop; general object/byref/callback ABI unstable |
| Cross-assembly linking | Not implemented | References bind C# but do not link external method bodies |
| Deterministic target source | Byte-identical for same PE/toolchain | Re-emission check for every positive target program |
| Source maps / source-level diagnostics | Not implemented | IL offsets available; portable PDB is emitted but not mapped |
| JavaScript browser hosting | ES-module design supports explicit invocation/output callback | Initial conformance executes Node, not browsers |
| Python deployment | Generated Python plus standard library | CI Python 3.13; no CLR/Python.NET dependency |
| C++ | Planned | No C++ emitter in the MVP |
| SSA / optimization | Planned | Current backend is a statically emitted control-flow dispatcher |
| Security sandbox | Not provided | Compiler and generated programs require trusted inputs or OS isolation |

## What a positive result proves

For each accepted fixture, the same managed assembly executes under CoreCLR and is compiled to both target sources. The harness compares stdout and process exit code, not just successful parsing. It separately checks reproducible emission. This is stronger than a syntax-only smoke test, but does not observe every internal runtime detail.

For unsupported fixtures, both target compilers must fail with structured Transpiler diagnostics and must not create a new target file. The external-override and finalizer cases protect against semantics invoked implicitly rather than by an obvious direct call.

## What remains intentionally visible

No full-CLI compatibility percentage is published. Opcode coverage, valid operand combinations, metadata coverage, runtime behavior, BCL signatures, host capabilities, and performance are separate dimensions. An unsupported low-level construct must not become “supported” just because a high-level C# example happened not to exercise the missing behavior.
