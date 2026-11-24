# RizToolbox.JudgeContext

Internal Windows x64 entry context helper for the exact IL2CPP signature
`void(receiver RCX, int32/enum32 EDX, float32 XMM2, MethodInfo* R9)`.
This directory contains C++/MASM code and a real native ABI fixture. The helper
does not resolve game symbols or install its own patch. `ExactHookBroker` owns
installation; the adapter's judgement module resolves note identities and
publishes the scoped SDK service. Full build/install now includes this DLL.

Build and run the fixture with the existing Visual Studio 2019 x64 toolchain:

```powershell
pwsh -NoProfile -File scripts/Build-JudgeContext.ps1
```

The script runs vcvars64, ml64, cl and dumpbin, writes only its chosen output
directory (default `native/RizToolbox.JudgeContext/out`), and runs the separate
fixture executable. It does not run dotnet, install anything, operate the game,
or copy a library into the game. See the committed
[validation report](../../docs/validation/judge-context-20261020/validation.md).

## Boundary and ownership

`JudgeEntry.asm` captures the 16 incoming GPRs and all 128 bits of XMM0..15 before
any C++/CLR code runs. The first instruction reserves stack space; no GPR or XMM
is overwritten before its value is saved. `gpr[Rsp]` and `entryStackPointer` are
the original entry RSP, and `returnAddress` is the original CALL return PC read
from that stack. RFLAGS, AVX/YMM upper halves, x87 state and MXCSR are not fields
in this version. The target must conform to the ordinary Windows x64 ABI.

The stack allocation is `0x1c8`: 32 bytes of shadow space, the aligned `0x1a0`
frame, and 8 bytes to keep RSP 16-byte aligned before CALL. Every saved
nonvolatile GPR and XMM6..15 has corresponding MASM unwind directives. It uses
ordinary CALL/RET; it does not synthesize return addresses or manipulate the
processor shadow stack. `/CETCOMPAT` and CFG metadata are emitted, but the
fixture does not claim that hardware CET was enabled on this machine.

The C++ dispatcher copies the four target arguments before invoking Enter,
calls the configured original exactly once, and calls Exit in a native
`__finally`. The original has no surrounding `__except` and its SEH/C++
exception continues to its caller. Enter/Exit observer faults are separately
contained; Exit failures do not replace an original exception. A frame is const
and stack-scoped: copy needed numbers inside the callback, never retain its
address. Each token returned by Enter receives one Exit, including token zero.
If Enter fails before returning its token, it is responsible for cleaning up
its own partial allocation; the helper calls Exit with zero and EnterFaulted.
Callbacks must catch their own managed exceptions before returning over the
interop boundary. The SEH fallback does not make corrupted memory recoverable.

Configuration is immutable and process lifetime. There is no unload/reset API.
The DLL, original trampoline, callback delegates and userContext must all remain
rooted until process exit. `SetEnabled(0)` blocks future observation acquisition
but is not a quiescence/unload barrier: an invocation that already acquired
observation still calls Exit, and a concurrent acquisition can still reach
Enter after SetEnabled returns. Callbacks must also check their module's active
state and registry retirement before acquiring roots. Stats are separately
atomic diagnostic counters, not a transaction or permission to free delegates.

**Configure must finish before Entry can execute.** Entry without a configured
original fails fast. The broker uses the pinned Dobby `Prepare` export to create
the trampoline while leaving the target bytes unchanged, configures and roots
this helper and its managed callbacks, then calls `Commit` to publish the patch.
The accepted Dobby SHA-256 is
`8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`.
Any uncertain native installation failure keeps the address, helper and callbacks
process-owned and rejects a second installation. Logical subscribers can detach;
the executable entry and callback roots are never unloaded while the process lives.

The publication fixture loads this actual Dobby DLL, verifies unchanged bytes
before Commit and patched bytes afterward, and runs four workers through the
prepared trampoline. It does not claim that prologue rewriting is atomic against
concurrent threads executing that prologue. Normal installation occurs on the
game main thread before gameplay calls. Static evidence is in
`docs/validation/judge-context-publication-20261020/dobby-static.json`;
the integrated full-build evidence is in
`docs/validation/runtime-judgement-identity-20261020/`.

## Exports and frame ABI v1

All exports and callbacks use the Windows x64 calling convention (C# Cdecl is
valid on Windows x64). Sizes and field offsets are compile-time asserted in
`JudgeContext.h` and checked by the fixture. Export names are undecorated:

| Export | Contract |
|---|---|
| `RizJudgeContext_GetEntry()` | Returns replacement entry address; does not configure or patch it |
| `RizJudgeContext_GetFrameSize()` | Returns `0x1a0` |
| `RizJudgeContext_Configure(ConfigV1*)` | Copies once; 0 success, 1 invalid config, 2 already configured |
| `RizJudgeContext_SetEnabled(uint)` | 0 success; 1 invalid value or unconfigured; values only 0/1 |
| `RizJudgeContext_GetStats(StatsV1*)` | Caller sets version=1,size=0x30; 0 success, 1 invalid header |

Enter is `uintptr_t Enter(const FrameV1*, void* user)`.
Exit is `void Exit(const FrameV1*, uintptr_t token, uint32 flags, void* user)`.
Exit flags are OriginalReturned=1, OriginalAbnormal=2, EnterFaulted=4.
OriginalReturned and OriginalAbnormal are mutually exclusive.

Frame byte layout (little endian, total `0x1a0`; frame address is 16-byte aligned):

| Offset | Type | Value |
|---|---|---|
| 0x000 | uint32 | ABI version 1 |
| 0x004 | uint32 | Byte size 0x1a0 |
| 0x008 | uint64 | Original caller return address |
| 0x010 | uint64 | Original entry RSP |
| 0x018 | uint64 | Reserved zero |
| 0x020 | 16 × uint64 | RAX, RCX, RDX, RBX, RSP, RBP, RSI, RDI, R8, R9, R10, R11, R12, R13, R14, R15 |
| 0x0a0 | 16 × 16 bytes | XMM0..15, each low uint64 then high uint64 |

Config byte layout (total `0x30`): version uint32@0, size uint32@4,
original pointer@8, enter pointer@0x10, exit pointer@0x18, userContext@0x20,
initiallyEnabled uint32@0x28, reserved uint32@0x2c=0.
Stats layout (total `0x30`): version uint32@0, size uint32@4, entered uint64@8,
exited@0x10, inFlight@0x18, enterFaults@0x20, exitFaults@0x28.

Exact C# layout examples for internal adapter/runtime integration only:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 0x1a0, Pack = 8)]
internal unsafe struct JudgeFrameV1
{
    [FieldOffset(0)] internal uint AbiVersion;
    [FieldOffset(4)] internal uint ByteSize;
    [FieldOffset(8)] internal ulong ReturnAddress;
    [FieldOffset(0x10)] internal ulong EntryStackPointer;
    [FieldOffset(0x18)] internal ulong Reserved;
    [FieldOffset(0x20)] internal fixed ulong Gpr[16];
    [FieldOffset(0xa0)] internal fixed ulong XmmQwords[32];
}
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 0x30)]
internal struct JudgeConfigV1
{
    internal uint AbiVersion, ByteSize;
    internal IntPtr Original, Enter, Exit, UserContext;
    internal uint InitiallyEnabled, Reserved;
}
[StructLayout(LayoutKind.Sequential, Pack = 8, Size = 0x30)]
internal struct JudgeStatsV1
{
    internal uint AbiVersion, ByteSize;
    internal ulong Entered, Exited, InFlight, EnterFaults, ExitFaults;
}
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate nuint JudgeEnter(IntPtr frame, IntPtr userContext);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void JudgeExit(IntPtr frame, nuint token, uint flags, IntPtr userContext);
```

In a copied frame, original enum32 bits are low 32 bits of Gpr[2]; original
float32 bits are low 32 bits of XmmQwords[4]. Do not numerically convert the
XMM low uint64 to float. Receiver is Gpr[1]; MethodInfo is Gpr[9]. Source owner
and NoteInformation extraction still require the pinned build's verified
return-RVA table, type/field validation and current run registry matching.
Unknown source must return zero/no semantic context while preserving the
native original call. No pointer/InstanceID/frame may escape to the public SDK.

## Adapter integration and remaining validation

The broker now shares a single patch per address and retains the caller context
through the actual inner NormalMode original call. The adapter validates the
twelve return addresses, actual note sources, object graph and current run.
An outer levelOver no-op produces no inner judgement event. Managed tests cover
unknown sources, reentry, withdrawal, abnormal originals, final-run origin and
failed post-original counter reads. Native fixtures separately cover ABI,
SEH/C++ unwind and prepared publication. These checks do not replace game
coverage of all twelve sources or calibration of measured delta units.
