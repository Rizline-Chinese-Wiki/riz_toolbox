#pragma once
#include <cstddef>
#include <cstdint>
#if defined(RIZ_JUDGE_CONTEXT_BUILD) && defined(RIZ_ENGINE_INLINE_PROBE)
#define RIZEIC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZEIC_EXPORT extern "C"
#endif

// Observation-only side ABI 1. Instruction sites are not function ABIs. A
// separately proved permanent relay CALLs GetInlineEntry, replays the two Begin
// instructions, or JMPs to the original End epilogue. This API patches nothing.
enum RizEngineInlineKindV1 : std::uint32_t {
    RizEngineInlineStartEntered = 0, RizEngineInlineBegin = 1,
    RizEngineInlineEndFree = 2, RizEngineInlineEndRetained = 3,
    RizEngineInlineStartExited = 4
};
enum RizEngineInlineFlagsV1 : std::uint32_t {
    RizEngineInlineSnapshotRequested = 1, RizEngineInlineSnapshotValid = 2,
    RizEngineInlineSnapshotFaulted = 4, RizEngineInlineParentMatched = 8,
    RizEngineInlineUnknown = 16, RizEngineInlineMissingEnd = 32,
    RizEngineInlineAbnormal = 64, RizEngineInlineObserverFaulted = 128,
    RizEngineInlineOutputRequested = 256, RizEngineInlineOutputRead = 512,
    RizEngineInlineOutputFaulted = 1024
};
struct RizEngineInlineFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, flags;
    std::uint32_t startSlot, probeSlot, threadId, depth;
    std::uint64_t epoch, startSequence, parentStartSequence, eventSequence;
    void* node;
    void* owner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType;
    std::int32_t referenceCount;
    const void* sitePc;
    const void* relayReturnPc;
    void* outputSlot;
    std::uint32_t startExitFlags, rawStartReturn;
    void* startOutputNode;
};
// Snapshot flags always describe the cached Begin node scalars, including at
// End/StartExited. StartEntered has no node snapshot and copies actual Start
// owner/Enumerator/outputSlot inputs. StartExited's separate Output* flags
// describe the actual Start output-slot copy; startExitFlags is the unchanged
// typed Start completion flags. rawStartReturn is zero for StartEntered/Begin,
// the actual saved RAX low byte for End, and the original typed completion's AL
// for StartExited; native code checks End and completion agree. probeSlot is
// always the configured inline family slot, never a site index/sentinel. Kind
// maps Begin/EndFree/EndRetained to the family's three configured site pairs.
struct RizEngineInlineSiteV1 { const void* sitePc; const void* relayReturnPc; };
struct RizEngineInlineConfigV1 {
    std::uint32_t abiVersion, byteSize, startSlot, mainThreadId;
    std::uint64_t expectedStartEpoch;
    void* expectedStartTarget;
    std::uint64_t reserved;
    // Ordered Begin, EndFree, EndRetained. Actual CALL return PCs select the
    // site. No caller-supplied Start sequence, node or event kind is accepted.
    RizEngineInlineSiteV1 sites[3];
};
using RizEngineInlineCallbackV1 = void(__cdecl*)(const RizEngineInlineFrameV1*, void*);
struct RizEngineInlineObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizEngineInlineCallbackV1 callback;
    void* user;
};
struct RizEngineInlineInfoV1 {
    std::uint32_t abiVersion, byteSize, slot, configured, attached, unknown, mainThreadId, reserved;
    std::uint64_t epoch, startEpoch;
    void* startTarget;
    void* entry;
    std::uint64_t startsEntered, startsExited, startsInFlight, begins, endsFree, endsRetained,
        probesInFlight, faults, snapshotFaults, orphanProbes, unmatchedStarts, events;
};

// 32 permanent inline families, each bound to one already configured exact
// Start slot/epoch/target. All callbacks and code remain rooted until exit.
// Configure 0 success, 1 invalid, 2 slot claimed, 3 duplicate source/return PC
// or Start binding, 4 epoch exhaustion. Attach: 0 success, 1 invalid, 2 not
// configured, 3 epoch mismatch, 4 already attempted, 5 sticky Unknown.
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineAbiVersion();
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineLimit();
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineFrameSize();
RIZEIC_EXPORT void* __cdecl RizEngineNodeContext_GetInlineEntry();
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_ConfigureInline(std::uint32_t slot, const RizEngineInlineConfigV1*);
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_AttachInline(std::uint32_t slot, std::uint64_t epoch, const RizEngineInlineObserverV1*);
RIZEIC_EXPORT std::uint32_t __cdecl RizEngineNodeContext_GetInlineInfo(std::uint32_t slot, RizEngineInlineInfoV1*);

static_assert(sizeof(RizEngineInlineFrameV1) == 144);
static_assert(offsetof(RizEngineInlineFrameV1, kind) == 8);
static_assert(offsetof(RizEngineInlineFrameV1, flags) == 12);
static_assert(offsetof(RizEngineInlineFrameV1, startSlot) == 16);
static_assert(offsetof(RizEngineInlineFrameV1, probeSlot) == 20);
static_assert(offsetof(RizEngineInlineFrameV1, threadId) == 24);
static_assert(offsetof(RizEngineInlineFrameV1, depth) == 28);
static_assert(offsetof(RizEngineInlineFrameV1, epoch) == 32);
static_assert(offsetof(RizEngineInlineFrameV1, startSequence) == 40);
static_assert(offsetof(RizEngineInlineFrameV1, parentStartSequence) == 48);
static_assert(offsetof(RizEngineInlineFrameV1, eventSequence) == 56);
static_assert(offsetof(RizEngineInlineFrameV1, node) == 64);
static_assert(offsetof(RizEngineInlineFrameV1, owner) == 72);
static_assert(offsetof(RizEngineInlineFrameV1, enumHandle) == 80);
static_assert(offsetof(RizEngineInlineFrameV1, enumCache) == 88);
static_assert(offsetof(RizEngineInlineFrameV1, enumType) == 96);
static_assert(offsetof(RizEngineInlineFrameV1, referenceCount) == 100);
static_assert(offsetof(RizEngineInlineFrameV1, sitePc) == 104);
static_assert(offsetof(RizEngineInlineFrameV1, relayReturnPc) == 112);
static_assert(offsetof(RizEngineInlineFrameV1, outputSlot) == 120);
static_assert(offsetof(RizEngineInlineFrameV1, startExitFlags) == 128);
static_assert(offsetof(RizEngineInlineFrameV1, rawStartReturn) == 132);
static_assert(offsetof(RizEngineInlineFrameV1, startOutputNode) == 136);
static_assert(sizeof(RizEngineInlineSiteV1) == 16);
static_assert(sizeof(RizEngineInlineConfigV1) == 88);
static_assert(offsetof(RizEngineInlineConfigV1, expectedStartEpoch) == 16);
static_assert(offsetof(RizEngineInlineConfigV1, startSlot) == 8);
static_assert(offsetof(RizEngineInlineConfigV1, mainThreadId) == 12);
static_assert(offsetof(RizEngineInlineConfigV1, expectedStartTarget) == 24);
static_assert(offsetof(RizEngineInlineConfigV1, reserved) == 32);
static_assert(offsetof(RizEngineInlineConfigV1, sites) == 40);
static_assert(sizeof(RizEngineInlineObserverV1) == 24);
static_assert(sizeof(RizEngineInlineInfoV1) == 160);
static_assert(offsetof(RizEngineInlineInfoV1, epoch) == 32);
static_assert(offsetof(RizEngineInlineInfoV1, startsEntered) == 64);
static_assert(offsetof(RizEngineInlineInfoV1, events) == 152);
