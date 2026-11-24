#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZVC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZVC_EXPORT extern "C"
#endif

// Windows x64 instance void(self, MethodInfo*) transport. This is independent
// of the coroutine kind table: it has no enumerator, result or return override.
enum RizVoidExitV1 : std::uint32_t {
    RizVoidReturned = 1,
    RizVoidAbnormal = 2,
    RizVoidEnterFaulted = 4
};
struct RizVoidFrameV1 {
    std::uint32_t abiVersion, byteSize, reserved0, reserved1;
    void* self;
    const void* method;
    const void* caller; // Actual native return address captured by the entry.
};
using RizVoidOriginalV1 = void(__cdecl*)(void* self, const void* method);
using RizVoidEnterV1 = std::uintptr_t(__cdecl*)(const RizVoidFrameV1*, void* user);
using RizVoidExitCallbackV1 = void(__cdecl*)(const RizVoidFrameV1*, std::uintptr_t token,
    std::uint32_t flags, void* user);
struct RizVoidConfigV1 {
    std::uint32_t abiVersion, byteSize;
    RizVoidOriginalV1 original;
    RizVoidEnterV1 enter;
    RizVoidExitCallbackV1 exit;
    void* user;
};
struct RizVoidStatsV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t entered, exited, inFlight, enterFaults, exitFaults;
};

// Appended admission ABI. Closing is only a quiescence boundary for calls that
// already enter this safely installed parent transport. It does not make the
// first detour installation or an arbitrary machine-code publication safe.
enum RizVoidGatePhaseV1 : std::uint32_t { RizVoidGateOpen = 0, RizVoidGateClosing = 1, RizVoidGateClosed = 2 };
struct RizVoidGateReceiptV1 {
    std::uint32_t abiVersion, byteSize, slot, ownerThreadId;
    std::uint64_t epoch, reserved;
};
struct RizVoidGateStatsV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t active, parked, epoch;
    std::uint32_t phase, unknown;
};

// Configure each of the 32 process-lifetime slots exactly once before a detour
// can publish its entry. Config, callbacks, user and original must stay alive.
// 0 = configured, 1 = invalid config, 2 = already claimed (including publishing).
// Never retry an unknown publication or reuse a claimed slot.
// GetEntry returns a stable address for a valid slot. Calling it unconfigured
// fails fast; observer faults cannot suppress the original or its native unwind.
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetLimit();
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetFrameSize();
RIZVC_EXPORT void* __cdecl RizVoidContext_GetEntry(std::uint32_t slot);
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Configure(std::uint32_t slot, const RizVoidConfigV1* config);
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetStats(std::uint32_t slot, RizVoidStatsV1* statistics);

// Close accepts a header-initialized receipt (reserved=0) and timeout <=10000ms.
// 0 issued; 1 invalid; 2 this thread is admitted to ANY void slot; 3 gate busy;
// 4 timeout (gate reopened); 5 sticky unknown (unissued closure reopened).
// An issued receipt is tied to its slot, globally unique epoch and OS thread.
// Verify/Release: 0 valid; 1 invalid ABI/reserved; 2 bad slot; 3 wrong OS thread;
// 4 stale epoch; 5 not an issued closed gate; 6 active calls; 7 sticky unknown.
// Release errors never open a gate. Dispose must not release after a write or
// uncertain publication. Parked calls enter no CLR callback until released;
// GetStats.inFlight continues to count them. Gate active includes Enter through
// Exit callback completion. Already-admitted same-slot recursion bypasses close.
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Close(std::uint32_t slot, std::uint32_t timeoutMs, RizVoidGateReceiptV1* receipt);
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Verify(const RizVoidGateReceiptV1* receipt);
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_Release(const RizVoidGateReceiptV1* receipt);
RIZVC_EXPORT std::uint32_t __cdecl RizVoidContext_GetGateStats(std::uint32_t slot, RizVoidGateStatsV1* statistics);

static_assert(sizeof(void*) == 8, "Windows x64 required");
static_assert(sizeof(RizVoidFrameV1) == 0x28);
static_assert(offsetof(RizVoidFrameV1, self) == 0x10);
static_assert(offsetof(RizVoidFrameV1, method) == 0x18);
static_assert(offsetof(RizVoidFrameV1, caller) == 0x20);
static_assert(sizeof(RizVoidConfigV1) == 0x28);
static_assert(offsetof(RizVoidConfigV1, original) == 0x08);
static_assert(offsetof(RizVoidConfigV1, enter) == 0x10);
static_assert(offsetof(RizVoidConfigV1, exit) == 0x18);
static_assert(offsetof(RizVoidConfigV1, user) == 0x20);
static_assert(sizeof(RizVoidStatsV1) == 0x30);
static_assert(offsetof(RizVoidStatsV1, entered) == 0x08);
static_assert(offsetof(RizVoidStatsV1, exitFaults) == 0x28);
static_assert(sizeof(RizVoidGateReceiptV1) == 0x20);
static_assert(offsetof(RizVoidGateReceiptV1, slot) == 0x08);
static_assert(offsetof(RizVoidGateReceiptV1, ownerThreadId) == 0x0c);
static_assert(offsetof(RizVoidGateReceiptV1, epoch) == 0x10);
static_assert(offsetof(RizVoidGateReceiptV1, reserved) == 0x18);
static_assert(sizeof(RizVoidGateStatsV1) == 0x28);
static_assert(offsetof(RizVoidGateStatsV1, active) == 0x08);
static_assert(offsetof(RizVoidGateStatsV1, parked) == 0x10);
static_assert(offsetof(RizVoidGateStatsV1, epoch) == 0x18);
static_assert(offsetof(RizVoidGateStatsV1, phase) == 0x20);
static_assert(offsetof(RizVoidGateStatsV1, unknown) == 0x24);
