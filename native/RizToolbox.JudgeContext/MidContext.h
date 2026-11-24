#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZEMC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZEMC_EXPORT extern "C"
#endif

// Internal Windows x64 observe-only snapshot. This ABI owns no game patch and
// grants no invocation, return replacement, metadata or resource-release right.
struct alignas(16) RizEventMidXmmV1 { std::uint64_t low, high; };
enum RizEventMidGprV1 : std::uint32_t {
    RizEventMidRax, RizEventMidRcx, RizEventMidRdx, RizEventMidRbx,
    RizEventMidRsp, RizEventMidRbp, RizEventMidRsi, RizEventMidRdi,
    RizEventMidR8, RizEventMidR9, RizEventMidR10, RizEventMidR11,
    RizEventMidR12, RizEventMidR13, RizEventMidR14, RizEventMidR15
};
struct alignas(16) RizEventMidFrameV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t returnPc;     // The actual CALL's return slot, never inferred.
    std::uint64_t entryRsp;     // Native bridge entry RSP, pointing at returnPc.
    std::uint64_t rflags;       // Captured before any flag-changing instruction.
    std::uint64_t gpr[16];      // RSP is the source RSP before CALL: entryRsp + 8.
    RizEventMidXmmV1 xmm[16];
    std::uint32_t mxcsr;
    std::uint16_t fpcsr;
    std::uint16_t reserved0;
    std::uint64_t reserved1;
};
using RizEventMidCallbackV1 = void(__cdecl*)(const RizEventMidFrameV1*, void* user);
struct RizEventMidConfigV1 {
    std::uint32_t abiVersion, byteSize;
    const void* expectedReturnPc;
    RizEventMidCallbackV1 callback;
    void* user;
    std::uint64_t reserved;
};
struct RizEventMidStatsV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t entered, exited, inFlight, faults;
};

// The fixed ASM entry identifies one of 32 permanent slots by its actual return
// PC. Unknown return PCs are observed by no slot and return unchanged. A slot
// and its return PC may be configured only once; each return PC must be unique.
// Callbacks must treat the entire stack-scoped snapshot as immutable. No DLL,
// callback, user state or configured relay may unload after native publication.
// Configure: 0 success, 1 invalid ABI/config, 2 slot claimed, 3 duplicate return PC.
RIZEMC_EXPORT void* __cdecl RizEventMidContext_GetEntry();
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_GetLimit();
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_GetFrameSize();
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_Configure(std::uint32_t slot, const RizEventMidConfigV1* config);
RIZEMC_EXPORT std::uint32_t __cdecl RizEventMidContext_GetStats(std::uint32_t slot, RizEventMidStatsV1* statistics);

static_assert(sizeof(void*) == 8, "Windows x64 required");
static_assert(alignof(RizEventMidFrameV1) == 16 && sizeof(RizEventMidFrameV1) == 0x1b0);
static_assert(offsetof(RizEventMidFrameV1, returnPc) == 0x08);
static_assert(offsetof(RizEventMidFrameV1, entryRsp) == 0x10);
static_assert(offsetof(RizEventMidFrameV1, rflags) == 0x18);
static_assert(offsetof(RizEventMidFrameV1, gpr) == 0x20);
static_assert(offsetof(RizEventMidFrameV1, xmm) == 0xa0);
static_assert(offsetof(RizEventMidFrameV1, mxcsr) == 0x1a0);
static_assert(offsetof(RizEventMidFrameV1, fpcsr) == 0x1a4);
static_assert(offsetof(RizEventMidFrameV1, reserved0) == 0x1a6);
static_assert(offsetof(RizEventMidFrameV1, reserved1) == 0x1a8);
static_assert(sizeof(RizEventMidConfigV1) == 0x28);
static_assert(offsetof(RizEventMidConfigV1, expectedReturnPc) == 0x08);
static_assert(offsetof(RizEventMidConfigV1, callback) == 0x10);
static_assert(offsetof(RizEventMidConfigV1, user) == 0x18);
static_assert(offsetof(RizEventMidConfigV1, reserved) == 0x20);
static_assert(sizeof(RizEventMidStatsV1) == 0x28);
static_assert(offsetof(RizEventMidStatsV1, entered) == 0x08);
static_assert(offsetof(RizEventMidStatsV1, faults) == 0x20);
