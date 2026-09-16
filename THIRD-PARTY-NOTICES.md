# Third-party notices and implementation provenance

`Transpiler.Bcl` and `Transpiler.Runtime.Managed` contain original portable C# implementations, not copied upstream collection/task/collector code. Standard abstract interface declarations are read from a selected .NET reference pack as metadata contracts.

The optional portable BCL policy imports a reviewed set of **21 original CoreLib integer Math method bodies**, enumerated exactly by `UpstreamBclCatalog` (`corelib-integer-v1`). They are consumed from the selected real implementation DLL, whose identity and SHA-256 appear in the compilation manifest. No CoreCLR/SGen/Boehm/MMTk native collector implementation is vendored or linked by this batch.

For adopted .NET implementation material, preserve the full MIT notice in `src/Transpiler.Backends/Runtime/NOTICE.txt`. The emitter includes it as comments when generated source contains framework implementation bodies. Retain the compilation manifest with generated releases. Additional upstream library adoption requires its own license/provenance review; this file is not blanket permission for arbitrary referenced dependencies.

The compiler build uses the SDK's Roslyn binaries. CI binary bundles copy the selected SDK's LICENSE.txt and ThirdPartyNotices.txt into their licenses directory, alongside this document and the .NET runtime notice. Review redistribution requirements for the actual packaged dependencies and versions.

Upstream sources:

- .NET runtime license: https://github.com/dotnet/runtime/blob/main/LICENSE.TXT
- .NET runtime notices: https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT
- Roslyn license: https://github.com/dotnet/roslyn/blob/main/License.txt
