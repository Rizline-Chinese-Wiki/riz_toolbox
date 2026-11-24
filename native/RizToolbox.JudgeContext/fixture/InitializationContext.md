# Typed initialization transport and native unwind fixture

`InitializationContext.cpp` adds two independent typed entries to the existing `RizToolbox.JudgeContext.dll`: Start `void(self, MethodInfo*)` and getter `void*(self, information, MethodInfo*)`. It has its own per-kind configuration and counters; it does not reuse the Judge snapshot configuration, enable flag or callbacks.

The contract in `../InitializationContext.h` is version 1. Config is 0x28 bytes: version, size, original trampoline, Enter, Exit and user pointer. Stats is 0x38 bytes: version, size, then six UInt64 counters (`entered`, `exited`, `inFlight`, `enterFaults`, `exitFaults`, `skipped`). Kinds are Start=0/getter=1. Configure returns 0 on success, 1 for invalid input and 2 if that kind is already configured or publishing. `GetStats` returns 0 on success; invalid kind/config record sizes are rejected. Each configuration is copied and published once through interlocked release, before the native detour can be committed. DLL, original, callbacks and user owner must remain alive until process exit. Calling an unconfigured entry fails fast.

Enter receives `(self, information, method, uintptr_t* token, user)` and allows original only for the exact return value 1. Start supplies null information. A zero token is valid; a token already written before Enter faults is still passed to Exit. Exit receives `(self, information, method, actualReturnedReference, token, flags, user)`. Flags are Returned=1, Abnormal=2, EnterFaulted=4 and Skipped=8. Skipped calls have no Returned/Abnormal bit. Getter returns the Exit-validated reference after an original return; skipped/Enter-faulted calls always return null, even if Exit incorrectly supplies a non-null value. Exit faults also produce null. Start ignores Exit's reference return.

Enter and Exit each have their own native SEH containment. The original call is inside native `__try/__finally` without a catch, so original SEH/C++ exceptions reach the native caller unchanged. Exit is paired once even during that unwind, and an Exit callback fault cannot replace the original exception. Counters cover Enter through completed/contained Exit; the helper does not itself create Runtime TLS scopes, LOCAL identities, roots or initialization readiness.

Run the native-only evidence independently:

```powershell
./scripts/Test-InitializationContextNative.ps1 `
  -OutputDirectory D:/RizToolbox-build-cache/initialization-native-unwind-20261023/fixture `
  -HelperDirectory D:/RizToolbox-build-cache/initialization-native-unwind-20261023/helper
```

This builds the shared helper and preserves both existing Judge fixtures, then builds the new fixture executable. `-SkipHelperBuild` reuses an already built helper. No dotnet project, game, installation or UI is involved. The script verifies the inspected Dobby SHA-256 before loading it.

The new fixture uses genuine Dobby Prepare → Configure → Commit against distinct MASM-backed native targets. It verifies unchanged target bytes before Commit, copied immutable config, an eight-thread Configure race with exactly one winner per kind, full-width self/information/MethodInfo/reference values, null arguments and return validation, original once, nested same/cross-kind entries, Win32 workers and two simultaneous kind scopes. Actual original SEH code/parameters and original C++ exception type/payload are received by native callers, including when Exit itself raises SEH or throws C++. Enter/Exit callback faults, skip, non-one Enter refusal and final zero in-flight counts are also checked.

The recorded local run passed 1747 checks with zero failures. Both kinds ended with `entered=32`, `exited=32`, `inFlight=0`, `enterFaults=2`, `exitFaults=8`, `skipped=4`; the exact output and binary hashes are in the output directory's `fixture-result.json` and `fixture-manifest.json`. A polling-based concurrency check can vary the total check count across runs.

This is native transport/publication/unwind evidence. Production Runtime TLS claims, callback fail-closed behavior and Adapter root/session publication are exercised by their separate Runtime/AudioContent cases. This fixture does not establish game-image compatibility or live initialization correctness; `gameRuntimeVerified` remains false.
