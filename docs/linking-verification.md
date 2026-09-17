# Filters, structured identities and conservative verification

Implemented 2026-09-17. This batch adds the `two-pass-managed-v1` exception policy, structural rewriting of the existing type-name codec, explicit ExportedType forwarding, and managed-address/exception-local checks. It does not establish complete CLI verification or a general-purpose CLR loader.

## 1. Real search-before-unwind filters

A filter may observe locals and state which a callee's finally will later modify. It must therefore run during handler search, not as a predicate after the host exception stack has already unwound. The compiler now supports `Filter` regions and `endfilter` on both source targets and both dispatch modes.

When any reachable method contains a filter, every emitted managed method in that module uses a live activation. Its arguments and locals are shared with a reentrant filter entry point; evaluation stacks, program counters and temporary operands are not. Each frame records its current IL offset. Search walks active frames from the throw site outwards, preserving metadata order for sibling handlers. Only after selecting a handler does ordinary propagation run the applicable finally/fault blocks.

The search runtime isolates exceptions raised while evaluating a filter. A helper called from the filter can handle its own exception; an exception escaping that search domain makes the original filter reject. It is not propagated as a replacement for the original exception. A failure raised later during unwinding is different: it starts a new search and can replace the original propagation.

Type initialization has an explicit runtime interception boundary. Caller filters see the TypeInitializationException wrapper rather than the intercepted initializer exception. A caught exception removes its search plan; a retired activation releases its evaluator and obsolete search references. Runtime diagnostics expose `exceptionPolicy` and `activeFrames` for filter-enabled modules. Modules without reachable filters retain the lighter existing execution path.

### Example

```bash
dotnet build Transpiler.slnx -c Release
CLI=src/Transpiler.Cli/bin/Release/net10.0/Transpiler.Cli.dll

dotnet "$CLI" compile samples/ExceptionFilters.cs --dispatch block \
  --target js --out artifacts/filters.mjs
node artifacts/filters.mjs

dotnet "$CLI" compile samples/ExceptionFilters.cs --dispatch instruction \
  --target py --out artifacts/filters.py
python3 artifacts/filters.py
```

Expected output:

```text
filter sees
1
callee finally
catch sees
2
```

The C# sample deliberately gives the filter and catch different observations. The implementation does not achieve this by replaying the callee or snapshotting all application state; it evaluates the filter against its still-live managed activation.

### Region and raw-IL rules

The verifier seeds both filter and handler stacks with the exception object. `endfilter` requires one Int32 result and no extra stack values. A filter has one final endfilter immediately before its handler. It cannot contain an embedded try region or escape with ordinary branches/leave. Nested exception handling belongs in called helper methods.

The raw-IL test uses PersistedAssemblyBuilder in a trusted test process to emit fault handlers and a method without InitLocals. The same saved assembly is executed by CoreCLR and both targets. It proves normal exit does not execute fault, a caller's filter precedes fault unwinding, and filter mutation of an already-initialized local remains observable. This test producer is not part of the production importer and does not execute user input during compilation.

These are tested managed exception semantics, not native OS exception interoperability, exact CLR stack traces, Watson state or exhaustive certification of arbitrary invalid IL.

## 2. Structural type rewriting

`CliTypeIdentity` parses the compiler's type-name representation into named scopes/definitions, generic arguments/parameters, vector or rectangular array wrappers, managed references, pointers, pinned markers and represented custom modifiers. Parsing is bounded to 4,096 characters and depth 64. Equality and hashing use the ordinal canonical encoding; numeric generic-parameter and rank spellings are normalized.

`TypeRewriter` centralizes traversal of signatures, field types, locals, exception types, interface maps and type tokens. It never rewrites string literals. Portable BCL substitutions now require the exact assembly scope and definition name, or a nested-type boundary. A type whose name merely starts with Task is not silently changed into a framework Task.

This is deliberately a structural model of the **existing codec**, not a lossless reimplementation of every CLI signature. The importer still has legacy choices for optional modifiers and function-pointer signatures, and generic specialization still consumes encoded names. Full calling conventions, signature-scoped identity, loader contexts, constraints and complete modifier fidelity remain work items.

## 3. Explicit assembly forwarding

`ForwarderImporter` reads ExportedType forwarding rows, including nested exported types and target assembly name/version/culture/public-key-token identity. The linker resolves explicit scoped chains before specialization and token remapping. Nested types follow their declaring type's destination even where metadata supplies only the parent forwarder.

The resolver rejects cycles, over-budget chains, missing target assemblies, target identity mismatches, duplicate forwarders, a type simultaneously defined and forwarded, and a chain with no final definition. It does not probe the network or resolve by an unrelated simple type name. The one-version-per-assembly-name graph policy remains in force.

A forwarding into a normalized framework implementation must retain its resolved member owner even when the target's textual assembly prefix is removed. The test suite covers this distinction separately from user-assembly forwarding. Manifest `forwardings` entries record used source-to-target mappings alongside the existing assembly hashes and emitted-method origins.

### Unchanged-client regression

The end-to-end test first builds a Contracts implementation and an App referencing it. It then builds a Destination implementation and replaces Contracts with a forwarding facade. **App.dll is not recompiled.** CoreCLR, JavaScript and Python must all observe the moved generic class, nested class, struct, field, virtual override and generic helper correctly.

```bash
TRANSPILER_TEST_FILTER=linking/type-forwarding python3 tests/conformance.py
```

That filtered run is a development check, not a full-suite claim. It also reverses dependency argument order to check deterministic output and removes the destination to require a structured failure without a new target artifact.

| Diagnostic | Meaning |
|---|---|
| TR3010 | Duplicate or conflicting forwarded definition |
| TR3011 | Forwarding cycle or chain budget exceeded |
| TR3012 | Required forwarded implementation is absent |
| TR3013 | Chain does not end in a supplied type definition |
| TR3014 | Forwarded target identity differs from the supplied implementation |
| TR3020 | Type-codec length/nesting or syntax failure |

Explicit app forwarding is not arbitrary framework-facade normalization, binding redirects, multiple load contexts or package restore. Assembly identity checks detect binding conflicts; they do not authenticate publishers.

## 4. Managed-address provenance

`ByReferenceSafety` checks the origins of addresses returned by managed methods. Its abstract origins distinguish heap storage, caller-owned byrefs, this frame's local/argument slots and unknown storage. Stack/local/argument states join by union: one unsafe incoming path cannot be hidden by another safe path.

Addresses of local and argument slots are frame-owned. Static fields, array elements and boxed storage are heap-owned. A field address inside a value type inherits its containing storage's origin. A reference forwarded through a byref-returning call conservatively retains all incoming reference origins. Returning an address which may point into this frame or unknown storage fails with TR2120.

Separate checks reject byref-containing field/array/boxing storage outside the ref-like profile. Typed indirect accesses use TR2121 for incompatible widths or address/token types. Signed and unsigned storage of equal physical width can share compatible indirect operations; a typed ldobj/stobj requires its declared type rather than an arbitrary same-width reinterpretation.

This proof is conservative and incomplete as an overall verifier. In particular, it does not provide full scoped-ref/ref-struct semantics, interprocedural escape summaries, constructor initialization proofs or complete subtype verification. It can reject valid code whose safety would require a richer callee summary. A JavaScript/Python closure accidentally keeping a local alive is not accepted as a substitute for valid managed lifetime semantics.

## 5. Exceptional local initialization

The former blanket rejection of non-InitLocals methods with exception regions is replaced by a conservative must-assignment analysis. Every instruction in a protected region contributes its pre-instruction facts to reachable handler/filter entries. Facts intersect at joins. A store is credited only after it executes.

This allows values initialized before entering a protected region and values definitely assigned in a catch. It does not assume a throwing call returned, a filter accepted or a finally already ran during first-pass search. Assignments made only through cleanup/filter continuations and address-based first writes remain conservative exclusions. The raw-IL test exercises a legal nonzeroed exception-local path; rejection tests exercise missing assignment paths.

## 6. Reproducible gates and remaining work

The added fixtures include 25 structural identity/forwarding assertions, 22 managed-address/exception-local/filter-layout assertions, two-pass ordering across direct/async/delegate/iterator/library calls, frame retirement, unchanged-client forwarding, and persisted fault IL. Existing collection, source-backed ValueTask, native-host-stream and logical-heap tests remain enabled.

The configured full gate now contains 135 cases. A case can contain many compilations and assertions. `TRANSPILER_TEST_WORKERS` optionally runs independent case directories with one through eight workers; CI uses two. Results retain registration order. Reports identify the selected filter, registered and selected case counts and whether the run is complete. Empty selection refuses success and stale reports are removed before starting.

Still separate: full signature/loader fidelity, comprehensive byref and exceptional verification, member reflection/dynamic code, timers/contexts/threads, broader BCL and scalar/layout support, effect-aware SSA, ordinary-object collector integration, C++ targets and generalized host object/stream marshalling. This batch implements concrete foundations for those tasks rather than marking the entire universal compiler complete.
