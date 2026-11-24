# Windows x64 initialization ABI fixture

This fixture executes the production `ExactHookBroker` replacements through actual native callers and the inspected Dobby DLL. All eight target entries are distinct MASM stubs with 32 patchable NOP bytes followed by a tail jump to typed C++ originals. The native DLL is loaded for process lifetime; it must not be unloaded after patching.

Build the DLL and run its finite native-only probe:

```powershell
./scripts/Build-InitializationAbiFixture.ps1
```

The default output is `artifacts/native-initialization-abi`. `native-probe-result.json` explicitly reports `managedTransportVerified=false`: this first probe checks the C++ harness, not the production managed replacements.

After the ordinary production build has produced the new Runtime assembly, run the production fixture independently:

```powershell
./scripts/Test-InitializationAbiFixture.ps1 `
  -RuntimeAssembly ./src/RizToolbox.Runtime/bin/Release/net6.0/RizToolbox.Runtime.dll `
  -SkipNativeBuild
```

The runner references the already built Runtime and SDK DLLs. It creates its own unique project, `bin`, and `obj` beneath `artifacts`; it does not build project references or contend with the solution's intermediates. A missing library or old Runtime API fails explicitly. The fixture checks the inspected Dobby SHA before installation.

The reusable C# API is `NativeInitializationAbiFixtureRegression.Run(nativeDllPath, dobbyDllPath)`. Its registered Runtime test reads `RIZ_INITIALIZATION_ABI_FIXTURE` and `RIZ_INITIALIZATION_ABI_DOBBY`; `scripts/build.ps1` builds this fixture, supplies both variables and restores their previous values afterwards. Running Runtime tests directly requires these explicit paths; a missing fixture fails. The C# test constructs sealed target tokens by reflection solely to assign the fixture's actual native addresses. All replacements, dispatch, callbacks, counters, Boolean marshalling, suppression and trampolines use the production Runtime implementation.

The DLL's C API is defined in `Fixture.h`:

| Export | Purpose |
| --- | --- |
| `RtInitTarget(kind)` | Gets an actual native Start/getter/Play/Tooltip/Progress/Mode/Instantaneous/EndRange target. |
| `RtInitInvoke(request, result)` | Calls the exact typed target on the caller's native thread and records any SEH exception. |
| `RtInitInvokeWorker(request, result)` | Uses Win32 `CreateThread`, invokes the same typed caller, then joins before returning. |
| `RtInitRead(kind, observation)` | Copies native original counts, opaque pointer arguments, raw float/Boolean bits and native thread ID. |
| `RtInitSetReference(reference)` | Configures the getter original's exact RAX reference return. |
| `RtInitPlayInputBits()` | Reads the actual EDX immediately before the MASM Play caller's CALL; tests supply nonzero high 24 bits and DL=0/1. |
| `RtInitSetObserver(callback, context)` | Calls a retained observer from the genuine C++ original, enabling synchronous native recursion and nested getter/tail scenarios. The observer must catch all its own exceptions. |
| `RtInitReset()` | Clears observations only when every native original has exited. It does not reset production policy state or unknown evidence. |

Requests are 48 bytes, results 24 bytes, and observations 64 bytes on Windows x64. Start is `void(self, method)`, getter is `ref(self, information, method)`, Play is `void(self, uint8, method)`, and Tooltip is `void(string, float, method)` with the float in XMM1. Pointers are opaque and never dereferenced by the fixture.

Production checks cover original once, full-width pointer and MethodInfo preservation, reference/null returns, both Boolean values, nullable strings, positive and negative zero, a negative float, positive/negative quiet NaNs and a signaling NaN bit pattern. The Play caller uses MASM to preserve a full `0xA5B6C700 | value` in EDX; a four-byte Boolean read would mistake the false case for true. They include native recursive entry counts, genuine Win32 worker calls, LOCAL parent claims and ordinary child masking, independent LOCAL Play, Before/After/Fault callback failures returning native null, and logical detach plus GC during the native Start stack. C++ SEH status must remain zero; a CLR exception that terminates the process also fails the runner.

This is finite offline ABI and dispatch evidence. It does not validate IL2CPP metadata resolution, game object fields, a game image's actual prologues, initialization graph ownership or a live LOCAL run. `gameRuntimeVerified` remains false in every manifest.
