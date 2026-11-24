#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZIC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZIC_EXPORT extern "C"
#endif

// Internal Windows x64 transport. Config, trampoline, callbacks, user owner and
// DLL are immutable and retained for process lifetime after native publication.
enum RizInitializationKindV1 : std::uint32_t { RizInitializationStart = 0, RizInitializationGetter = 1 };
enum RizInitializationExitFlagsV1 : std::uint32_t {
    RizInitializationReturned = 1,
    RizInitializationAbnormal = 2,
    RizInitializationEnterFaulted = 4,
    RizInitializationSkipped = 8
};
using RizInitializationStartV1 = void(__cdecl*)(void* self, const void* method);
using RizInitializationGetterV1 = void*(__cdecl*)(void* self, void* information, const void* method);
using RizInitializationEnterV1 = std::uint32_t(__cdecl*)(void* self, void* information, const void* method,
    std::uintptr_t* token, void* user);
using RizInitializationExitV1 = void*(__cdecl*)(void* self, void* information, const void* method,
    void* returnedReference, std::uintptr_t token, std::uint32_t flags, void* user);
struct RizInitializationConfigV1 {
    std::uint32_t abiVersion;
    std::uint32_t byteSize;
    void* original;
    RizInitializationEnterV1 enter;
    RizInitializationExitV1 exit;
    void* userContext;
};
struct RizInitializationStatsV1 {
    std::uint32_t abiVersion;
    std::uint32_t byteSize;
    std::uint64_t entered;
    std::uint64_t exited;
    std::uint64_t inFlight;
    std::uint64_t enterFaults;
    std::uint64_t exitFaults;
    std::uint64_t skipped;
};

// GetStats accepts either the original V1 statistics packet or this V2
// publication packet. A V2 read copies the one actual ready slot; an empty
// or publishing slot returns 2 and leaves the caller's packet unchanged.
struct RizInitializationPublicationV2 {
    std::uint32_t abiVersion;
    std::uint32_t byteSize;
    std::uint32_t configured;
    std::uint32_t kind;
    void* entry;
    const void* slotIdentity;
    RizInitializationConfigV1 config;
};

// Configure result: 0 success; 1 invalid kind/ABI/config; 2 already configured
// (including concurrent publication). Configure each kind once before Commit.
RIZIC_EXPORT void* __cdecl RizInitializationContext_GetEntry(std::uint32_t kind);
RIZIC_EXPORT std::uint32_t __cdecl RizInitializationContext_GetConfigSize();
RIZIC_EXPORT std::uint32_t __cdecl RizInitializationContext_Configure(std::uint32_t kind, const RizInitializationConfigV1* config);
RIZIC_EXPORT std::uint32_t __cdecl RizInitializationContext_GetStats(std::uint32_t kind, void* statisticsOrPublication);

static_assert(sizeof(void*) == 8, "Windows x64 required");
static_assert(sizeof(RizInitializationConfigV1) == 0x28);
static_assert(offsetof(RizInitializationConfigV1, original) == 8);
static_assert(offsetof(RizInitializationConfigV1, userContext) == 0x20);
static_assert(sizeof(RizInitializationStatsV1) == 0x38);
static_assert(sizeof(RizInitializationPublicationV2) == 0x48);
static_assert(offsetof(RizInitializationPublicationV2, config) == 0x20);
