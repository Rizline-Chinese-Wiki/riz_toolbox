#pragma once
#include <cstddef>
#include <cstdint>
#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZCC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZCC_EXPORT extern "C"
#endif

enum RizCoroutineKindV1 : std::uint32_t { RizCoroutineStart = 0, RizCoroutineMoveNext = 1, RizCoroutineFactory = 2 };
enum RizCoroutineExitV1 : std::uint32_t { RizCoroutineReturned = 1, RizCoroutineAbnormal = 2, RizCoroutineEnterFaulted = 4 };
struct RizCoroutineFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, reserved;
    void* self;
    void* enumerator; // Start's reference argument (IEnumerator or method name); zero for Move/Factory.
    const void* method;
    const void* caller; // Actual native return address, never inferred from TLS.
};
using RizCoroutineEnterV1 = std::uintptr_t(__cdecl*)(const RizCoroutineFrameV1*, void* user);
using RizCoroutineExitCallbackV1 = void(__cdecl*)(const RizCoroutineFrameV1*, std::uintptr_t token,
    std::uintptr_t result, std::uint32_t flags, void* user);
struct RizCoroutineConfigV1 {
    std::uint32_t abiVersion, byteSize;
    void* original;
    RizCoroutineEnterV1 enter;
    RizCoroutineExitCallbackV1 exit;
    void* user;
};
struct RizCoroutineStatsV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t entered, exited, inFlight, enterFaults, exitFaults;
};
struct RizCoroutineConfigurationV1 {
    std::uint32_t abiVersion, byteSize, configured, kind, slot, reserved;
    void* entry;
    RizCoroutineConfigV1 configuration;
};
// Immutable process slots. Native original always executes exactly once;
// observers cannot suppress calls, replace returns or swallow original unwind.
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_GetLimit();
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_GetFrameSize();
RIZCC_EXPORT void* __cdecl RizCoroutineContext_GetEntry(std::uint32_t kind, std::uint32_t slot);
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_Configure(std::uint32_t kind, std::uint32_t slot, const RizCoroutineConfigV1* config);
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_GetStats(std::uint32_t kind, std::uint32_t slot, RizCoroutineStatsV1* statistics);
// Read-only actual immutable state. Returns 0 for a stable configured slot,
// 1 for invalid arguments/header, or 2 before publication/during a changed read.
RIZCC_EXPORT std::uint32_t __cdecl RizCoroutineContext_ReadConfigurationV1(std::uint32_t kind, std::uint32_t slot, RizCoroutineConfigurationV1* value);
static_assert(sizeof(void*) == 8);
static_assert(sizeof(RizCoroutineFrameV1) == 0x30 && offsetof(RizCoroutineFrameV1, caller) == 0x28);
static_assert(sizeof(RizCoroutineConfigV1) == 0x28 && sizeof(RizCoroutineStatsV1) == 0x30);
static_assert(sizeof(RizCoroutineConfigurationV1) == 0x48
    && offsetof(RizCoroutineConfigurationV1, entry) == 0x18
    && offsetof(RizCoroutineConfigurationV1, configuration) == 0x20);
