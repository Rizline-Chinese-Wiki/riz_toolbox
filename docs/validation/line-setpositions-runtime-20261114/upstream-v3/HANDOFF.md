# Current root v3 offline progress (2026-11-14)

The current five-file candidate is candidate-root-v3.json SHA `c33320d763d346311f424d5e0de2bfa22cabbf41d65566a9072332a23752f41a`. A further root review found that enumerated MethodInfo must be memory-admitted before invoking method metadata exports. The v3 M wrapper checks the whole MethodInfo2022 range immediately inside each guarded export callback, and the invoke preparation guard checks the expected MethodInfo range too.

A new unreadable-method regression returns pointer1 from the actual class-method export and asserts refusal before any method-name export or invocation. Root-r2 fresh strict build W0/E0 and all eight Runtime seam cases pass. Actual root session39193 exit0/chunk378c8d; result `4d53d7e0401f924c0538fa4f1347f939646b8534808ea3568e52f18ae80047e4`, path D:/riz-build-cache-next/line-setpositions-profile-20261114/root-r2/. root-shadow-exit-v3.json records the terminal. Root v2 source/tests/control/receipt are preserved in iteration/root-v2-tested, and its actual root-r1 result is unchanged.

Main remains 909 during its separate validation; v3 is not applied or accepted. Real loaded GameAssembly/body admission and Unity external effects remain untested. Next integrate this exact v3 payload against accepted main909 with fresh tests; do not apply the older v1/v2 payload or cite seven old cases as evidence for v3.

---

# Root v2 offline progress (2026-11-14)

This isolated candidate now has a fresh strict Release build and seven synthetic-image Runtime seam tests, natural root session62175 exit0/chunk78a391. Result `6fc7f32c86ca50545c72cdefcd64af04a985120ca0c0d5e6e0dfa4e4a1fd1dbb` at D:/riz-build-cache-next/line-setpositions-profile-20261114/root-r1/result.json; exact source/test pins in candidate-root-v2.json. It is not applied to main or accepted as real profile admission.

Root preserved the agent's original version in iteration/static-agent-v1, then moved beforeEntry admission ahead of final profile/source/root/class/storage checks. This prevents an admission callback's own disposal/class drift from bypassing verification. Pin-acquisition target callbacks now have admission guards before the next native export. VerifyCurrent rejects workers before running caller guard. Runtime attaches original roots/count/entered/returned across native exception-out and cleanup failures.

The seven tests reuse only the existing synthetic SEC_IMAGE mapper, never execute its mapped code, and invoke production runtime_invoke transport through unmanaged fake IL2CPP exports. They cover canonical signature/explicit shorter count and same array identity, wrong scalar or overload RVA, capacity bounds before admission, callback receiver drift after admission, storing array/count before IL2CPP exception-out, worker zero callbacks/exports, and exact temporary pin cleanup failure after successful field publication. The real loaded GameAssembly body/hash profile is compiled but not instantiated/tested; its real admission and Unity renderer effects remain unknown.

Next: independently review v2, integrate against an accepted main graph and run fresh main validation. Then wire exact dispatch into the same retained reader/endpoint scratch owner, add saved _positionsPlan/_positionCountPlan and nested plan observations, and exercise two distinct Lines sharing the same original array. Preserve entered/unknown journals and shared tail; do not treat the wrapper alone as renderer or complete Seek closure.

---

# Isolated exact SetPositions profile and invocation seam

Candidate source only:

- `src/RizToolbox.Runtime/NativeRuntime.LineSetPositions.cs` adds a guarded fresh signature inspector and an original-root/pinned temporary invocation seam.
- `src/RizToolbox.Adapter/Gameplay/NativeSeekLineSetPositionsNativeProfile.cs` pins the current mapped `GameAssembly.dll`, exact `MethodInfo2022`/entry, and the complete 288-byte method body before dispatch.

The input is `work/line-renderer-next-20261114/observed-metadata.json` and the existing frozen `work/line-ring-target-static-20261113/LineInformation.SetPositions-buffer.asm.txt` (ASM text SHA-256 `ee0d24a8f8c9bd2618860f68b9643e9943423f46e5515f4c76c13a869c01c48d`). The accepted pinned image fingerprint is `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`. The exact two-argument body is RVA `0x3fb160`, length `288`, SHA-256 `e948e88fdbba2a25281ae2fb8ced224a3b9197915714dfef210fdc19e86fb8e6`. The one-argument overload at `0x3fb280` is never selected.

The generated method signature is checked as `void(self: InLevel.LineInformation, positions: UnityEngine.Vector3[], positionCount: System.Int32, MethodInfo*)`. The inspector enumerates the two-parameter declaration, verifies its nonstatic/nonabstract/non-generic MethodInfo, canonical `LineInformation`, canonical `Vector3` fields and 12-byte value layout, canonical rank-one `Vector3[]` class/element/stride, exact non-byref Int32, exact Void return, module entry and RVA. The profile hashes the complete mapped body at every `VerifyCurrent`, using `ReadProcessMemory` so an unreadable body rejects without an unmanaged copy fault.

`InvokeExact` accepts the **original caller-owned line and shared scratch roots**, an actual count, caller guard, source-association verifier, and a `beforeEntry` journal callback. Runtime registers temporary pinned handles before post-export guards, rechecks the original identities and exact classes, checks the line's official object size and writable saved-field slots, checks the array's official header/length/byte length/object size and `count <= actual capacity`, then uses `InvokeCore`/`il2cpp_runtime_invoke` with `Arg.Ref(array)` and `Arg.Value(count)`. The last `beforeEntry` callback precedes `InvokeRaw` without another managed callback; page access is checked once more after that callback without invoking user code. If invocation or cleanup throws, the outer exception carries the original line/array roots, count, native-entry flag, and successful-return flag; temporary cleanup failure retains `NativePublicationCleanupOwner`. The caller must publish its command owner and journal before allowing entry. The callback should only journal and must not mutate source or native code.

This verifies the generated ABI; it **does not call the raw generated function pointer**. The existing runtime-invoke transport returns an IL2CPP exception through its exception out pointer, which is needed because frozen disassembly shows the method can save `_positionsPlan` at `0x88` and `_positionCountPlan` at `0x90` before a later exception. An entered exceptional call is a potentially published write. The profile does not read back those fields or the nested `LineRenderer` plan, close Unity `set_positionCount`/`SetPositions` effects, make the native call atomic, implement the dynamic producer, or authorize complete Seek.

`LineSetPositionsNativeEntered` means the `beforeEntry` callback completed and dispatch was attempted; it is conservative, not proof that the generated body reached its first instruction. The mapped-body hash is likewise an observation, not an atomic lock against a later native code change.

The profile constructor takes the canonical `LineClass` from the retained original line source profile; the later command owner must supply `verifySource` against that same source and original shared scratch. It must also preserve occurrence order, distinguish cull from zero-count calls, read back current line/plan fields after each entry, and journal uncertain effects without blind retry. The candidate deliberately has no owner orchestration.

No binary or game was opened for this preparation; no network access, build, native dispatch, or main-source edit was performed. The frozen ASM text provides disassembly and a recorded body hash, not raw executable bytes in this workspace. Actual admission occurs only when the profile checks the loaded pinned module, current MethodInfo and mapped 288 bytes in the intended environment. Until that check and a scoped build/backend validation, this remains an unaccepted candidate.
