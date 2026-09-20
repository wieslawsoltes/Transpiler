# Validation evidence — offline dependency closure and input locks

Recorded 2026-09-20. The preceding [generalized stream-export ledger](history/validation-stream-exports-2026-09-20.md) is preserved unchanged with its own revision and evidence. Earlier milestones are not reclassified as current input-lock evidence.

## Exact implementation and CI provenance

Validated implementation commit: **`293a12bb2dcfd79f04ba180222362c468a141003`**.
Git tree: **`b5c6a48028ffc89ec5975328d35194957309721b`**.

[Workflow 35497517271](https://github.com/wieslawsoltes/Transpiler/actions/runs/35497517271) completed successfully. All four jobs passed: **conformance**, **input-contracts**, **stream-lifecycle** and **clock-lifecycle**. The unfiltered conformance result is separate from the targeted input/stream/clock reports. CI build and source/compiler artifact packaging also succeeded.

The downloaded full artifact **10601452632**, `transpiler-build-and-conformance`, has verified SHA-256 **`565c987a0ab3a3d0c33ebe62788da4983f419ccbedb35b0bbc1e5ae0fb7c0c99`**. Its nested source ZIP comment identifies the exact implementation commit above. All **216 compiler, test and workflow files** were compared byte-for-byte against the local implementation: **zero mismatches**. The solution, Directory.Build.props and global.json were additionally checked unchanged.

The focused input artifact **10601616666**, `transpiler-input-contract-regressions`, has verified SHA-256 `623ab2b12236fa0256d10395c8862270896d709c978c205124355e4ec5e594bb`. Its five selected end-to-end groups passed with `complete: false` and `filter: inputs/`; the direct 52-invariant executable ran separately in that job. It is not substituted for the complete suite.

## Two observed complete runs

| Environment | Registered / selected | Passed | Failed | Complete / filter |
|---|---|---|---|---|
| Local Linux x64, SDK 10.0.100, Node 22.16.0, Python 3.13.5 | 274 / 274 | 274 | 0 | true / empty |
| GitHub Linux x64, SDK 10.0.401, Node 22.23.2, Python 3.13.15 | 274 / 274 | 274 | 0 | true / empty |

Both reports used two workers. The local Release build reported zero warnings and errors. Counts are registered harness groups, not universal CLI/BCL coverage percentages.

Local full-report SHA-256: `02c94251ff03286bdc9a5fc38a1d88de3b4a215fb18a651721b0d59fe8206978`.

CI full-report SHA-256: `05bf8530aad55a0e35471d1a8d7a03886e942339130942be976ac278b3634d97`.

The local report identifies Linux 6.18.44/glibc 2.41; CI identifies Linux 6.17.0-1022-azure/glibc 2.39. These are two observed environments, not a claim of Windows/macOS/ARM64 qualification.

## New input-contract evidence

The six new registered groups include **52 direct invariants**, real cyclic assembly closure/replay on both hosted targets, C# source/BCL lock replay on both targets, and unchanged forwarded-consumer resolution. They are part of the 274-case total.

Tests verify exact identity/content matching; duplicate identical copies; rejection of byte-distinct same-identity candidates, wrong versions, reference-only images and malformed matching filenames; cycle termination; explicit-reference equivalence; and deterministic instruction/SSA output after moving the implementation graph and changing reference-directory order.

The source/BCL fixtures each pin **167 reference-pack images** and **seven compiler/toolchain images** in the observed SDK environments. Changed root/library/reference-pack/compiler bytes and emission options fail lock validation. The snapshot checks cover stable cached bytes, independent mutable copies, fresh-session changes, exact byte/file quotas, pre-canceled work and mid-import cancellation. Resolver policy checks include framework-token classification, unsafe path names, directory/edge limits and self-references.

Failure-path tests preserve pre-existing target output and prevent diagnostics from overwriting an explicit or already-captured input. Malformed lock schemas, missing sections, duplicate or unknown JSON properties, duplicate identities and invalid hashes are rejected rather than tolerated silently.

## Sample and independent generated-code replay

`samples/input-locks/build.py` was executed after the local build. It built real Arithmetic.dll and Api.dll dependencies plus Program.dll, recorded JavaScript/Python locks, relocated all three assemblies, verified locked replay, compared target bytes and executed both outputs. Both runs printed **42**.

Four generated modules from the downloaded focused CI artifact were executed unchanged with local Node/Python: the JavaScript/Python SSA cyclic-closure modules printed **42**, and the JavaScript/Python forwarded-consumer modules printed **73**. This execution-only replay did not launch the CLR. It supplements the two full compiler runs; it is not another full-suite claim.

## Retained coverage and scope

All prior ordinary/BCL and SSA differential tests, instruction/block comparisons, native C++ checks, filters, byref/local verification, logical collector tests, time-service ownership and generalized stream-factory lifecycle groups remain passing in the complete suite. This continuation did not replace existing negative cases with blanket acceptance or weaken the unfiltered gate.

The delivered scope is **explicit-directory-closure-v1**, **compilation-input-lock-v1**, shared bounded snapshots and cooperative cancellation checkpoints. Read [input-locks.md](input-locks.md) for exact APIs, quota defaults and limitations. Input locks identify captured managed/reference/toolchain bytes; they do not provide a package store, network restore, publisher authentication, full environment/source/PDB locking or preemptive CPU/heap isolation.

Lossless CLI signatures, deeper exceptional/byref verification, broader BCL/reflection, contexts/threads/I/O, ordinary-object collector integration, expanded optimization and managed-object native backends remain in the [implementation plan](implementation-plan.md). Passing this milestone does not certify those separate capabilities.

The final documentation-only successor leaves all 216 validated compiler/test/workflow files and the tested input-lock sample unchanged. The successful CI evidence belongs to the implementation commit above; the documentation successor does not claim an independent suite rerun.
