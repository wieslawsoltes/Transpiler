# Filters, forwarding and managed lifetime research

Checked 2026-09-17. The implementation contract is [linking-verification.md](../linking-verification.md). This note separates source requirements from the repository's tested subset. No third-party implementation code was copied for these additions.

## Search precedes unwind

Microsoft documents that exception filters run before the stack is unwound, retaining access to original local state. Evaluating a filter only after a host-language exception reaches the caller can therefore change program behavior. [R1]

The chosen source-emission design retains a live managed activation for every method in a module requiring filters. An activation exposes a reentrant filter entry point sharing arguments and locals, but not operand stacks or temporary operands. The runtime searches live frames, then unwinds the selected path. This is a managed source-code protocol; it does not reuse CoreCLR native funclet layout or claim native ABI compatibility.

The endfilter contract constrains layout and behavior: one final endfilter terminates the filter, an embedded try is not permitted, and an exception escaping the filter is treated as rejection. These constraints justify explicit region checks rather than support for arbitrary invented control-flow shapes. Called helper methods may still use their own exception handling. [R2]

Tests distinguish rejecting filters, filters throwing from helpers, replacement exceptions during finally, rethrow identity and initializer wrapping. An initializer is an interception boundary because caller filters must not observe the exception that the runtime catches and wraps internally. The implementation makes that boundary visible in its search stack instead of relying on already-unwound host state.

This is not a claim that every CLR runtime-internal interception, corrupted-state exception or native stack/debugging behavior is implemented. Existing runtime services and supported library bodies still define the compatibility boundary.

## Forward types without recompiling the consumer

.NET type forwarding is intended to move a type between assemblies while keeping existing clients usable without recompilation. A correct regression must therefore run an unchanged client, not merely recompile its source against the destination assembly. [R3]

The test builds the old contract implementation and client first, then replaces the contract with a TypeForwardedTo facade and supplies a destination assembly. It verifies class/struct/generic/nested identities, virtual calls and fields against CoreCLR and both source backends. Missing destinations must fail rather than falling back to a similarly named type.

Metadata contains ExportedType entries and an IsForwarder indication. Importing their target assembly identities and nested resolution chains lets the existing explicit linker resolve the move before closed specialization. The implementation records used forwarding bindings in its manifest. [R4]

This does not solve arbitrary loader contexts or framework facade normalization. The repository still has an explicit framework-name policy and one supplied version per assembly simple name. Full signature identity, modifier fidelity, forwarding through every host framework and dynamic loading need distinct work.

## Structural identities before richer optimizations

Replacing substrings in a type name can accidentally rewrite a different definition, especially where generated/nested names share prefixes. The new model recursively rewrites named scope/definition nodes and wrappers, while ordinary string constants remain untouched. Its canonical form supports predictable identity equality and tracing.

The model parses the repository's existing metadata-name codec. It is not presented as lossless CLI signature import: the current importer has legacy choices for function pointers and optional modifiers. A future canonical signature model must retain calling convention, custom modifiers, scope and generic context before optimization can rely on all those distinctions. This is an engineering recommendation derived from the observed importer boundaries, not a claim that the entire migration is implemented.

## Managed-reference safety is not host-object longevity

C# ref-safety rules distinguish the lifetime of the referenced storage from the lifetime of an object which happens to represent that reference. A returned ref cannot point to a callee's retired local storage merely because a target-language closure keeps an implementation cell reachable. [R5]

The compiler now uses conservative address-origin propagation for byref-returning methods and validates typed indirect accesses. It does not infer an arbitrary callee's precise ref-return dependency; all incoming byref origins are retained. This choice can reject code that a more precise interprocedural proof would accept, but avoids claiming validity from the host collector's behavior.

Exceptional definite assignment similarly uses pre-instruction facts at handler/filter entries. It avoids crediting a store that has not completed or a finally that will run only after the search pass. Full exception-sensitive summaries and address-first initialization are future precision work, not silently assumed guarantees.

## Independent raw-IL evidence

A C#-only corpus cannot directly express every useful CLI handler shape. The tests therefore use PersistedAssemblyBuilder and a PE builder to save a trusted test program containing fault clauses and nonzeroed locals. The production importer still reads without loading/executing user assemblies. Persisted emission is a test dependency, not a new dynamic-code service in generated applications. [R6]

A fault block is entered for exceptional unwind, not normal exit; the fixture combines both paths with a caller filter. The expected event ordering is checked first by CoreCLR, then against both generated targets and dispatch modes. This independently exercises the runtime's fault path and the exceptional local-initialization checks. [R7]

## Primary sources

Accessed 2026-09-17. Mutable documentation may change. These references describe contracts; exact consumed assemblies remain identified by build manifests.

- R1: C# exception-handling statements and filter timing: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/statements/exception-handling-statements
- R2: CLI endfilter instruction: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.endfilter?view=net-10.0
- R3: Type forwarding in .NET: https://learn.microsoft.com/en-us/dotnet/standard/assembly/type-forwarding
- R4: ExportedType.IsForwarder: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.exportedtype.isforwarder?view=net-10.0
- R5: C# ref safety: https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/keywords/ref
- R6: PersistedAssemblyBuilder: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.persistedassemblybuilder?view=net-10.0
- R7: ILGenerator.BeginFaultBlock: https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.ilgenerator.beginfaultblock?view=net-10.0
