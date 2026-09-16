# Transpiler

A Roslyn-fronted, CLI-assembly-based multi-target compiler. First targets: standalone JavaScript and Python; C++ is a planned backend.

## Design contract

```text
C# source --Roslyn--> PE / CIL + metadata
                              ^
existing managed assemblies --|
                              v
          metadata import -> normalized CIL -> verification
                              v
                 target capability validation
                              v
           JavaScript source | Python source | future C++
```

The durable input is a real ECMA-335 assembly, not C# syntax rewritten as another language. Generated programs must not need Roslyn, CoreCLR, pythonnet, or a .NET process at execution time. Small, explicit target-language semantic helpers are allowed and bundled with generated output. A future strict standard-C++ profile will distinguish native/std-only lowering from managed compatibility services.

Universal means an extensible architecture and a conformance roadmap, **not a claim that this initial MVP implements every CLI feature or the entire .NET library ecosystem**. Unsupported reachable operations must produce actionable diagnostics rather than plausible but incorrect code.

The implementation, research snapshot dated 2026-09-16, architecture, specification, compatibility matrix, and differential tests are being introduced in granular commits directly on `main`.
