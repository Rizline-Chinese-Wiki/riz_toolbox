#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZJC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZJC_EXPORT extern "C"
#endif

// Windows x64 only. No pointer in this internal contract may be published to plugins.
// The snapshot is immutable, stack-scoped, and invalid as soon as Exit returns.
struct alignas(16) RizJudgeXmmV1 { std::uint64_t low; std::uint64_t high; };
enum RizJudgeGprV1 : std::uint32_t {
    RizRax, RizRcx, RizRdx, RizRbx, RizRsp, RizRbp, RizRsi, RizRdi,
    RizR8, RizR9, RizR10, RizR11, RizR12, RizR13, RizR14, RizR15
};
struct alignas(16) RizJudgeFrameV1 {
    std::uint32_t abiVersion;             // 0x000: 1
    std::uint32_t byteSize;               // 0x004: 0x1a0
    std::uint64_t returnAddress;          // 0x008: caller's CALL return PC
    std::uint64_t entryStackPointer;      // 0x010: RSP before the bridge prologue
    std::uint64_t reserved;               // 0x018: zero; not RFLAGS
    std::uint64_t gpr[16];                // 0x020: order RizJudgeGprV1
    RizJudgeXmmV1 xmm[16];                // 0x0a0: entire 128 bits, XMM0..15
};
enum RizJudgeExitFlagsV1 : std::uint32_t {
    RizOriginalReturned = 1,
    RizOriginalAbnormal = 2,
    RizEnterFaulted = 4
};
using RizJudgeOriginalV1 = void(__fastcall*)(void*, std::int32_t, float, const void*);
using RizJudgeEnterV1 = std::uintptr_t(__fastcall*)(const RizJudgeFrameV1*, void*);
using RizJudgeExitV1 = void(__fastcall*)(const RizJudgeFrameV1*, std::uintptr_t, std::uint32_t, void*);
struct RizJudgeConfigV1 {
    std::uint32_t abiVersion;             // 0x00: 1
    std::uint32_t byteSize;               // 0x04: 0x30
    RizJudgeOriginalV1 original;          // 0x08: detour owner's original trampoline
    RizJudgeEnterV1 enter;                // 0x10: observer, zero token allowed
    RizJudgeExitV1 exit;                  // 0x18: paired observer; called even for token zero
    void* userContext;                    // 0x20: process-lifetime owner
    std::uint32_t initiallyEnabled;       // 0x28: 0 or 1
    std::uint32_t reserved;               // 0x2c: zero
};
struct RizJudgeStatsV1 {
    std::uint32_t abiVersion;
    std::uint32_t byteSize;
    std::uint64_t entered;
    std::uint64_t exited;
    std::uint64_t inFlight;
    std::uint64_t enterFaults;
    std::uint64_t exitFaults;
};
// Read-only current native configuration. This is not an installation issuer;
// only the retained bridge can compare it with its actual Configure arguments.
struct RizJudgeConfigurationV1 {
    std::uint32_t abiVersion;
    std::uint32_t byteSize;
    std::uint32_t configured;
    std::uint32_t enabled;
    void* entry;
    RizJudgeConfigV1 configuration;
};

// Configure exactly once, BEFORE a detour can publish Entry. It copies the config;
// callbacks, userContext, original trampoline, and this DLL must live until process exit.
// Result: 0 success, 1 invalid ABI/config, 2 already configured. No hot reconfiguration.
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_Configure(const RizJudgeConfigV1*);
RIZJC_EXPORT void* __cdecl RizJudgeContext_GetEntry();
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_GetFrameSize();
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_SetEnabled(std::uint32_t);
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_GetStats(RizJudgeStatsV1*);
// 0 copied, 1 invalid output ABI, 2 not ready or changed during this read.
RIZJC_EXPORT std::uint32_t __cdecl RizJudgeContext_ReadConfigurationV1(RizJudgeConfigurationV1*);

static_assert(sizeof(void*) == 8, "Windows x64 required");
static_assert(sizeof(RizJudgeFrameV1) == 0x1a0);
static_assert(offsetof(RizJudgeFrameV1, gpr) == 0x20);
static_assert(offsetof(RizJudgeFrameV1, xmm) == 0xa0);
static_assert(sizeof(RizJudgeConfigV1) == 0x30);
static_assert(offsetof(RizJudgeConfigV1, initiallyEnabled) == 0x28);
static_assert(sizeof(RizJudgeStatsV1) == 0x30);
static_assert(sizeof(RizJudgeConfigurationV1) == 0x48);
static_assert(offsetof(RizJudgeConfigurationV1, configuration) == 0x18);
