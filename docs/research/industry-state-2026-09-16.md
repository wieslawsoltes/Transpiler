# Industry state and research findings

Research snapshot: **2026-09-16**. Scope: compiling C#/.NET/CLI programs to other implementation languages, especially standalone JavaScript and Python. Sources below are specifications, official documentation, and the projects' own repositories. Status statements describe the retrieved documentation, not an independently audited compatibility certification. This research does not claim to have benchmarked or executed the surveyed third-party products.

## Executive conclusion

Build a **managed-semantics compiler with multiple source backends**, not a collection of C# syntax rewriters. Use Roslyn as the C# producer and real PE/CIL plus metadata as the durable interchange. Preserve richer semantics above the final target syntax. Treat the base class library, runtime services, and host/OS capabilities as separately versioned components.

The strongest recurring industry pattern is not a magical universal code printer. Successful systems choose one of three boundaries: source-language compilation with a target library, managed IL compilation with a compatibility runtime, or a restricted high-performance subset. A fourth category embeds an existing runtime rather than translating the program. These boundaries should be visible in our product contract.

The initial implementation in this repository deliberately chooses one managed assembly, an explicit portable subset, standalone generated source, and a small bundled semantic runtime. It is a foundation for broader CLI coverage, not evidence that all CLI/.NET programs already work.

## 1. The contractual baseline

**ECMA-335** defines the Common Language Infrastructure: type system, metadata, executable representation, instruction set, and related library/profile material. The official publication page still identifies the sixth edition, June 2012. Modern .NET also maintains an **ECMA-335 Augments** document. Therefore a current compiler needs both a pinned ECMA baseline and an explicit policy for runtime-specific extensions; “implements MSIL” alone is underspecified. [S1, S2]

**Roslyn** exposes syntax trees, symbols, semantic models, compilations, and emission. Its `Compilation.Emit` API produces managed executable output. Roslyn is an excellent C# frontend, but its public C# syntax/semantic API is not an arbitrary-assembly IL importer. Our pipeline uses Roslyn to generate an actual assembly, then imports that assembly through `PEReader` and `System.Reflection.Metadata`. [S3–S5]

**System.Reflection.Metadata** supplies low-level metadata readers and signature decoding contracts. **Mono.Cecil** provides an alternative higher-level assembly/object model, including modification and writing. **ILSpy** is valuable research material for type-system handling and reconstruction of structured source from IL. A decompiler's reconstruction strategy is useful input, but this project should preserve IL semantics directly rather than round-trip every program through reconstructed C#. [S5–S7]

## 2. Current comparison matrix

| System | Actual boundary | Relevant current evidence | Architectural lesson |
|---|---|---|---|
| Fable | F# compiler frontend to multiple target languages with target libraries | The official target table lists JavaScript/TypeScript as stable and Python as beta; other targets have separate maturity levels. The 2026-02-27 Fable 5 release-candidate article discusses updated Python packaging. | Multi-target infrastructure is viable; backend maturity and library compatibility must be independent. This is not a drop-in arbitrary-CIL importer. [S8, S9] |
| Transpose / H5 | C# source and Roslyn semantics to JavaScript | H5's repository directs new projects to Transpose. Transpose describes a modern Roslyn-based compiler and separate compiler/runtime concerns. | Research the successor, not just historical H5/Bridge comparisons. Strong C# support does not automatically establish support for arbitrary assemblies produced by every CLI language. [S10, S11] |
| WebSharper | C#/F# compilation to JavaScript, metadata, proxies, and web integration | Its documentation describes source compilation, selective translation, compiler APIs, and merging metadata/proxies from references. | Explicit library proxies and metadata linkage are first-class compiler architecture, not incidental glue. [S12, S13] |
| JSIL | Managed IL to JavaScript | The repository documents a direct .NET-to-JavaScript approach. We inspected it as an architectural precedent, not as a verified current .NET 10 compatibility baseline. | Study managed identity, library substitutions, and IL lowering. Do not infer current support from historical feature claims. [S14] |
| IL2JS, Reactive-Extensions | Historical IL to JavaScript | Its repository is explicitly archived, dated 2018-04-20, and describes its historical .NET 3.5 scope. | Useful earlier work, not a current production dependency recommendation. Distinguish similarly named projects. [S15] |
| Unity IL2CPP | IL to C++ followed by platform-native AOT compilation | Unity documents the C++ compilation pipeline, managed compatibility behavior, and platform/AOT limitations. Current limitations include an explicit discussion of exception-filter timing differences. | Generating C++ does not remove runtime obligations. Compatibility details, not output language alone, determine correctness. [S16, S17] |
| IL2C | IL to C with a runtime model | The project's repository describes IL-to-C translation and its accompanying runtime-oriented implementation. | A small explicit runtime can make generated low-level code understandable. It does not mean every managed service becomes a standard-C operation. [S18] |
| .NET Native AOT | Managed code to native executables with linked runtime support | Official documentation lists AOT deployment behavior and restrictions, including dynamic-code limitations. | AOT and absence of a separately installed CLR are not equivalent to absence of managed runtime semantics. [S19] |
| .NET WebAssembly / Blazor AOT | .NET execution through a WebAssembly runtime/toolchain | Official documentation describes build tools and AOT compilation. | An important compatibility alternative and oracle; not the same deliverable as editable standalone JavaScript or Python source. [S20] |
| Python.NET | Python and CLR interoperability | Official docs describe loading and using .NET types from Python and runtime selection. | Valuable when CLR hosting is acceptable; it does not satisfy this project's standalone Python-source output requirement. [S21] |
| ILGPU / Unity Burst | Restricted high-performance managed-language compilation | ILGPU kernel docs reject managed reference types and several runtime-dependent behaviors. Burst documents HPC# as a subset. | Restriction is a valid, productive profile, but should never be marketed as universal managed compatibility. [S22, S23] |
| MLIR / EmitC | Multi-level IR infrastructure and C/C++-oriented lowering | EmitC documents an IR dialect suitable for emitting C/C++ constructs. | A possible future native backend substrate. A managed-semantics dialect must precede it; EmitC alone does not supply CLI object, exception, or generic semantics. [S24] |

The matrix intentionally does not rank performance: there is no controlled benchmark in this research. It also does not assert that no other compiler exists. The defensible conclusion is that the surveyed primary sources do not establish a single currently verified, drop-in engine covering arbitrary modern CLI assemblies to both standalone JavaScript and Python with all .NET semantics.

## 3. Why syntax translation is not the right canonical layer

A C# construct is not necessarily a CLI primitive. `async`, iterators, pattern matching, closures, interpolation, and many modern conveniences are lowered by the source compiler into combinations of generated types, methods, calls, and metadata. Reimplementing each source feature separately in every backend creates a multiplicative maintenance problem.

Conversely, IL is not sufficient without metadata and runtime contracts. An instruction's meaning depends on signatures, type identity, field layout, virtual slots, exception regions, generic context, and the referenced library operation. A compiler that prints every opcode but silently substitutes approximate library calls has not solved compatibility.

**Design recommendation:** retain a Roslyn sidecar for source provenance and optional high-level recognition, but ensure that the compatibility path operates on the real assembly. Source-specific optimization must either prove equivalence or fall back to the same managed IR. Never require a reconstructed C# source representation as the only bridge between IL and a backend.

## 4. Hard semantic boundaries that influence this MVP

### Exact numerics

JavaScript's Number and BigInt are distinct numeric models; Python integers do not overflow at the CLI's fixed widths, and Python division/remainder behavior must not be substituted blindly for signed CLI arithmetic. Target-language specifications, not visual similarity of operators, determine valid lowering. [S25, S26]

The MVP therefore normalizes integer width explicitly and uses exact integer arithmetic in helpers. A later optimized backend may select `Math.imul`, native bitwise operations, or bounded Number arithmetic after proving the same result. This is a design choice, not a performance claim. Binary32 storage, decimal, native-sized integers, and unchecked floating-to-integer range policies need separate conformance work.

### Strings and object identity

Represent managed strings with explicit identity and UTF-16 behavior. A host Python string's indexing model is not the CLI string contract. Equality of contents and reference identity must remain separate operations. UTF-16 slices can split surrogate pairs, and concatenating those slices must reconstruct the original code-unit sequence. This is why the runtime does not implement all managed strings as unannotated host values. [S25, S26]

### Exceptions: source nesting is not the entire contract

A faithful exception implementation needs protected regions, handler search, unwinding, `leave`, `finally`/`fault`, rethrow identity, and eventually filters. The current Unity IL2CPP limitations document explicitly acknowledges observable ordering differences for exception filters implemented using C++ exceptions. This is particularly relevant to a compiler promising semantic preservation. [S17]

Our MVP supports non-filter handlers with explicit continuation records. Filters are rejected rather than approximated. The planned full implementation adds a shadow managed call stack and a first search pass that can evaluate a caller's filter before unwinding a callee's `finally`. Reusing the host language's already-unwound stack cannot retroactively recover that ordering.

### Libraries and host capabilities

An external method is not implemented merely because its name resembles a host function. Bind by an exact managed signature and document nullability, exception behavior, numeric representation, culture, and allocation behavior. Native interop, files, sockets, GUI toolkits, threads, and reflection also depend on host capabilities or packaged implementations.

For this MVP, unsupported external calls are compile-time diagnostics. There is no hidden fallback to running a .NET subprocess. A future compatibility interpreter or CLR/Wasm bridge may be a useful **separate, opt-in profile**, but it must not silently replace standalone source translation.

## 5. Proposed compatibility profiles

| Profile | Intended guarantee | Runtime/host policy |
|---|---|---|
| `portable-mvp` | Current implemented subset, measured by differential tests | Bundled JS/Python semantic helpers; one assembly; no CLR at program execution |
| `managed-portable` | Broad verifiable CLI, generics, value types, reflection metadata, async/library closure | Explicit managed services implemented in the target language; host capability manifest |
| `managed-dynamic` | Dynamic assembly loading and generated code | Optional compiler/interpreter service with CSP, security, and deployment implications made visible |
| `native-std` | C++ standard-library-only restricted programs | Ownership/escape constraints and a documented native subset; no claim of full managed heap semantics |
| `cpp-managed` | Broad managed semantics with C++ output | Generated/support code, potentially including a tracing heap; “std-only dependency” is distinguished from “no runtime services” |
| `hosted-interop` | Maximal practical access to existing .NET facilities | Explicit CLR/Wasm/native bridge; not presented as pure standalone source output |

Only `portable-mvp` is implemented initially. The other rows are proposed product boundaries, not implemented targets.

## 6. Implementation strategy derived from the research

First prove shared semantics across the two most different initial targets. Keep one reachability/capability analysis and one normalized IL input. Emit a simple generated control-flow dispatcher as a reference backend. Then introduce explicit basic blocks, stack-to-SSA conversion, effect-aware optimization, and target-native source structuring while retaining the baseline as a differential oracle.

Expand by semantic vertical slices: define metadata, verification, lowering, runtime behavior, BCL surface, ABI behavior, and adversarial tests together. For example, “generics” is not complete when generic method syntax prints; it also needs constructed identity, static storage per instantiation, constraints, sharing, virtual/interface dispatch, and reflection behavior.

Use a conformance ledger rather than a single misleading percentage. Separate opcode coverage, valid operand/type combinations, metadata coverage, runtime services, library signatures, and host integrations. Track performance and output size independently from correctness.

## Sources

All sources accessed 2026-09-16. URLs without fixed versions can change. No third-party implementation code was copied into this repository during this work.

- **S1** ECMA-335 publication: https://ecma-international.org/publications-and-standards/standards/ecma-335/
- **S2** .NET ECMA-335 Augments: https://github.com/dotnet/runtime/blob/main/docs/design/specs/Ecma-335-Augments.md
- **S3** Roslyn compiler API model: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/compiler-api-model
- **S4** Compilation.Emit API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.compilation.emit
- **S5** SRM signature provider: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.isignaturetypeprovider-2?view=net-10.0 and PEReader metadata access: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.pereaderextensions.getmetadatareader?view=net-10.0
- **S6** Mono.Cecil: https://github.com/jbevain/cecil
- **S7** ILSpy: https://github.com/icsharpcode/ILSpy
- **S8** Fable target status: https://fable.io/docs/
- **S9** Fable 5 release-candidate article, 2026-02-27: https://fable.io/blog/2026/2026-02-27-Fable_5_release_candidate.html
- **S10** H5 successor notice: https://github.com/curiosity-ai/h5
- **S11** Transpose: https://github.com/curiosity-ai/transpose
- **S12** WebSharper documentation: https://docs.websharper.com/
- **S13** WebSharper compiler API: https://docs.websharper.com/metaprogramming/compiler
- **S14** JSIL: https://github.com/sq/JSIL
- **S15** Archived IL2JS: https://github.com/Reactive-Extensions/IL2JS
- **S16** Unity IL2CPP pipeline: https://docs.unity3d.com/6000.0/Documentation/Manual/scripting-backends-il2cpp.html
- **S17** Unity current IL2CPP limitations (resolved to Unity 6.6 documentation when retrieved): https://docs.unity3d.com/Manual/scripting-restrictions.html
- **S18** IL2C: https://github.com/kekyo/IL2C
- **S19** Native AOT: https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- **S20** .NET WebAssembly AOT: https://learn.microsoft.com/en-us/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0
- **S21** Python.NET: https://pythonnet.github.io/pythonnet/python.html
- **S22** ILGPU kernels and restrictions: https://ilgpu.net/docs/03-advanced/02-kernels/
- **S23** Burst C# language support: https://docs.unity3d.com/Packages/com.unity.burst@1.8/manual/csharp-language-support.html
- **S24** MLIR EmitC: https://mlir.llvm.org/docs/Dialects/EmitC/
- **S25** ECMAScript specification: https://tc39.es/ecma262/
- **S26** Python expression semantics: https://docs.python.org/3/reference/expressions.html
