# Value semantics and collection expansion

Research date: 2026-09-16. Primary sources below inform the next implementation batch; implemented surfaces and measured results are recorded separately in the compatibility ledger.

## Equality is a runtime/type-system contract

`EqualityComparer<T>.Default` selects IEquatable<T> when the declared T implements it, and otherwise uses Object.Equals/GetHashCode. This is not equivalent to asking whether a particular runtime subtype happens to implement IEquatable<T>. An interface dispatch table must prefer the exact Equals(T) signature over an Equals(object) overload. Explicit implementations also need reachability roots when invoked from a runtime helper rather than a visible IL call.

Keep reference identity, virtual Object equality, generic equality, primitive equality and structural ValueType equality distinct. Direct `base.Equals` and `base.ToString` must not redispatch into the override. Console/object string conversion must invoke the override. Boxed primitives require their own value equality even when the call was declared against Object. Floating-point Equals treats NaNs as equal and distinguishes that contract from IL floating comparisons.

Hash values need consistency with the selected equality, not byte-for-byte equality with another runtime process. String and default object/value-type hash algorithms are not stable cross-process serialization formats. Preserve explicit user GetHashCode overrides; document host-local hashing for built-in/default representations. Tests should check equal values have equal hashes, dispatch and collision behavior rather than snapshot randomized hashes.

## Nullable representation

A nullable value has managed has-value and underlying-value storage. Boxing an empty nullable yields null; boxing a populated nullable yields a box of its underlying T, not a Nullable<T> box. Unboxing null to Nullable<T> gives an empty nullable; unboxing a compatible T box gives a populated nullable; an incompatible boxed type fails. These rules belong in runtime lowering even when the nullable API methods themselves are translated managed C#.

## Comparers and order

Comparer<T>.Default uses the corresponding comparable contracts. Primitive numeric order must include the .NET NaN ordering convention. String default ordering is culture-sensitive; substituting ordinal host string order would be silently incorrect. Provide an explicit ordinal comparer and reject unsupported culture-dependent bindings instead of pretending they are equivalent. Custom comparison delegates and interfaces should execute their real translated methods.

## Dictionary and set storage

Implement managed bucket/entry storage with collision chains, stored hashes, reusable free entries, typed copy semantics and bounded corruption detection. Do not replace a dictionary with a JS Map or Python dict: key equality, custom comparers, null-key policy, struct copying, and enumeration effects differ.

Current .NET Dictionary.Remove and Clear do not invalidate active enumerators. Successful insertion and capacity changes do. Existing-key replacement behavior should be checked against the selected implementation as documentation is not an exhaustive mutation matrix. Enumerable ordering is not a universal dictionary API guarantee; tests that compare the selected implementation's order must identify that scope.

Sets permit a null element where dictionaries reject a null key. Set algebra must handle duplicates, custom equality, self-operations and repeated/lazy enumeration. A Dictionary-backed set can share storage machinery but must separately implement the set null policy and exposed enumerator/version semantics.

## Verification strategy

Use the same emitted DLL for the CoreCLR oracle and both target compilers. Add Release/Debug coverage for overridden/base Object calls, IEquatable versus runtime-subtype selection, explicit implementations, plain/custom structs, nullable box/unbox, NaN/signed zero, custom comparers, collision-heavy mutation, enumeration state and self operations. Keep method-origin manifests so managed algorithms and fundamental runtime hooks are distinguishable.

## Primary sources

- EqualityComparer<T>.Default selection: https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.equalitycomparer-1.default?view=net-10.0
- Comparer<T>.Default selection: https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.comparer-1.default?view=net-10.0
- Dictionary.Remove enumeration behavior: https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.remove?view=net-10.0
- Dictionary enumerator contract: https://learn.microsoft.com/en-us/dotnet/api/system.collections.generic.dictionary-2.getenumerator?view=net-10.0
- Selected implementation research, Dictionary: https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Collections/Generic/Dictionary.cs
- Nullable boxing and unboxing: https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-nullable
- Nullable implementation: https://source.dot.net/system.private.corelib/src/runtime/src/libraries/System.Private.CoreLib/src/System/Nullable.cs.html
- Single floating-point contract: https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-single

These unpinned documents may change. Compiled input manifests identify consumed implementation binaries. This research is not a claim of exhaustive implementation or third-party performance testing.
