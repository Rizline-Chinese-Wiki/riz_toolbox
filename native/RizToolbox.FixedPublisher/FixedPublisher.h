#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <cstddef>
#include <cstdint>
#include "FixedPublisherProfile.h"

// Windows x64 self-owned fixture component only. Fixed module/export/marker
// identity is mandatory. This is not a generic patch API or game credential.
// Default records remain version 1. RIZ_EARLY_ENGINE_KERNEL=1 selects the
// independently named Kernel publisher and version 2 with identical layouts.
inline constexpr std::uint32_t RizFixedVersion = RizFixedPublisherProfile::RecordVersion, RizFixedKinds = 4;
inline constexpr std::uint32_t RizFixedEventCapacity = 256;
inline constexpr std::uint32_t RizFixedHelperSlot = 28;
enum RizFixedStateV1 : std::uint32_t { RizFixedFresh, RizFixedPreparing, RizFixedPrepared,
    RizFixedBound, RizFixedPublishing, RizFixedFixtureReady, RizFixedDisabledNoSourceWrite, RizFixedUnknown };
enum RizFixedFailureV1 : std::uint32_t { RizFixedNoFailure, RizFixedInvalidRecord,
    RizFixedWrongState, RizFixedWrongOwner, RizFixedWrongModule, RizFixedWrongImage,
    RizFixedWrongHash, RizFixedWrongPrefix, RizFixedWrongMemory, RizFixedOverlappingRange,
    RizFixedInvalidEntry, RizFixedOriginalChanged, RizFixedFaultInjected,
    RizFixedProtectFailed, RizFixedWriteFailed, RizFixedReadbackFailed,
    RizFixedRestoreFailed, RizFixedFlushFailed, RizFixedLogOverflow, RizFixedNativeException };
enum RizFixedFaultStageV1 : std::uint32_t { RizFixedFaultNone,
    RizFixedBeforeProtect, RizFixedAfterProtect, RizFixedBeforeWrite, RizFixedAfterWrite,
    RizFixedBeforeReadback, RizFixedAfterReadback, RizFixedBeforeRestore, RizFixedAfterRestore,
    RizFixedBeforeFlush, RizFixedAfterFlush, RizFixedPartialWrite,
    RizFixedProtectApiFailure, RizFixedRestoreApiFailure, RizFixedFlushApiFailure };
enum RizFixedEventStageV1 : std::uint32_t { RizFixedPrepareBegin = 1, RizFixedPrepareValidated,
    RizFixedContinuationBound, RizFixedPrepareEnd, RizFixedBindBegin, RizFixedEntryBound, RizFixedBindEnd,
    RizFixedCommitBegin = 10, RizFixedProtectBegin, RizFixedProtectEnd,
    RizFixedSourceWriteBegin, RizFixedSourceWriteEnd, RizFixedReadbackBegin, RizFixedReadbackEnd,
    RizFixedRestoreBegin, RizFixedRestoreEnd, RizFixedFlushBegin, RizFixedFlushEnd,
    RizFixedCommitEnd, RizFixedFailureObserved, RizFixedFinalReadback, RizFixedReady };
enum RizFixedEventFlagsV1 : std::uint32_t { RizFixedEventInjected = 1, RizFixedEventCleanup = 2,
    RizFixedEventSucceeded = 4, RizFixedEventPartial = 8, RizFixedEventApiCalled = 16 };

struct RizFixedFaultV1 { std::uint32_t stage, kind, partialWriteBytes, reserved; };
struct RizFixedConfigV1 {
    std::uint32_t version, byteSize;
    void* module;
    void* targets[4];
    std::uint32_t imageSize, timeDateStamp;
    std::uint64_t fileBytes;
    unsigned char fileSha256[32];
    RizFixedFaultV1 fault;
    std::uint32_t reserved[4];
};
struct RizFixedBindingV1 {
    std::uint32_t version, byteSize;
    std::uint64_t epoch;
    void* helperModule;
    void* entries[4];
    std::uint64_t helperEpochs[4];
    std::uint32_t helperSlots[4];
    std::uint32_t reserved[4];
};
struct RizFixedEventV1 {
    std::uint64_t sequence;
    std::uint32_t stage, kind, state, failure, result, win32Error, flags, sourceBytesWritten;
    std::uint64_t target, original, entry, epoch;
    std::uint32_t originalProtection, observedProtection;
};
struct RizFixedReceiptV1 {
    std::uint32_t version, byteSize, kind, prefixBytes;
    std::uint64_t module, target, original, continuation, entry, epoch;
    std::uint32_t rva, originalProtection, observedProtection, flags;
    // flags: prepared1/bound2/committed4; facts below never inferred from flags.
    std::uint32_t protectAttempts, protectSucceeded, sourceWriteAttempts, sourceBytesWritten;
    std::uint32_t readbackAttempts, readbackVerified, restoreAttempts, restoreSucceeded;
    std::uint32_t flushAttempts, flushSucceeded, protectionOutstanding, lastWin32Error;
    std::uint64_t helperEpoch;
    std::uint32_t helperSlot, reserved;
    unsigned char originalBytes[32], patchBytes[32], observedBytes[32], moduleSha256[32];
};
struct RizFixedInfoV1 {
    std::uint32_t version, byteSize, state, failure;
    std::uint64_t epoch;
    std::uint32_t ownerThread, claimed, preparedCount, boundCount, nextCommit;
    std::uint32_t sourceWriteAttempts, sourceWriteCompletions, protectionOutstanding;
    std::uint32_t eventCount, overflow, reserved[2];
    RizFixedReceiptV1 receipts[4];
    RizFixedEventV1 events[RizFixedEventCapacity];
};

#ifdef RIZ_FIXED_PUBLISHER_BUILD
#define RIZ_FIXED_API extern "C" __declspec(dllexport)
#else
#define RIZ_FIXED_API extern "C" __declspec(dllimport)
#endif
// Return 0 success, 1 permanently failed, 2 busy or 3 invalid caller record.
// Prepare, Bind and Commit are single-owner operations; no reset/unbind/retry.
// GetInfo copies one bounded snapshot under a non-waiting exclusive gate.
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_Prepare(const RizFixedConfigV1* config);
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_Bind(const RizFixedBindingV1* binding);
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_Commit(std::uint32_t kind);
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_GetInfo(RizFixedInfoV1* information);
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_GetVersion();
RIZ_FIXED_API std::uint32_t __cdecl RizFixedPublisher_GetInfoSize();

// Four immutable-after-Prepare continuation pointers consumed only by static
// original replay stubs. Their nonzero values do not authorize a source write.
extern "C" void* RizFixedContinuation[4];
extern "C" void RizFixedOriginalStart();
extern "C" void RizFixedOriginalExecute();
extern "C" void RizFixedOriginalRelease();
extern "C" void RizFixedOriginalDetach();

static_assert(sizeof(void*) == 8);
static_assert(sizeof(RizFixedConfigV1) == 128);
static_assert(sizeof(RizFixedBindingV1) == 120);
static_assert(sizeof(RizFixedEventV1) == 80);
static_assert(sizeof(RizFixedReceiptV1) == 272);
static_assert(offsetof(RizFixedInfoV1, receipts) == 72);
static_assert(offsetof(RizFixedInfoV1, events) == 1160);
static_assert(sizeof(RizFixedInfoV1) == 21640);
