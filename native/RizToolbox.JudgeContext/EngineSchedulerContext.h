#pragma once
#include <cstddef>
#include <cstdint>
#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZESC_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZESC_EXPORT extern "C"
#endif

// Independent observation ABI 1. No patch, cancellation or release authority.
// All entries preserve four actual opaque integer-register inputs and the
// original full RAX through a uint64 machine carrier (not a source bool/void
// declaration). Register's observed result is an output-slot pointer. Callback
// semantically consumes uint32 EAX; no AL normalization is allowed. Remove,
// Consume, Pump and Drain have no proved source-language return type.
enum RizEngineSchedulerKindV1 : std::uint32_t {
    RizEngineSchedulerRegister = 0, RizEngineSchedulerRemove = 1,
    RizEngineSchedulerConsume = 2, RizEngineSchedulerPump = 3,
    RizEngineSchedulerDrain = 4, RizEngineSchedulerCallback = 5
};
enum RizEngineSchedulerFlagsV1 : std::uint32_t {
    RizEngineSchedulerSnapshotRequested = 1, RizEngineSchedulerSnapshotValid = 2,
    RizEngineSchedulerSnapshotFaulted = 4, RizEngineSchedulerUnknown = 8,
    RizEngineSchedulerMainThread = 16, RizEngineSchedulerParentMatched = 32,
    RizEngineSchedulerSourceDrainRow = 512, RizEngineSchedulerSourceConsume = 1024,
    RizEngineSchedulerSnapshotCached = 2048
};
enum RizEngineSchedulerExitFlagsV1 : std::uint32_t {
    RizEngineSchedulerReturned = 1, RizEngineSchedulerAbnormal = 2,
    RizEngineSchedulerEnterFaulted = 4, RizEngineSchedulerRawRaxValid = 8,
    RizEngineSchedulerMissingRow = 16, RizEngineSchedulerExitUnknown = 32
};
// Snapshot bit values also apply to completion.outputFlags/nodeSnapshotFlags.
struct RizEngineSchedulerRecordV1 {
    std::uint64_t dueTimeBits;
    std::int64_t frame;
    std::uint32_t intervalBits, repeat;
    void* payload;
    void* callback;
    void* cleanup;
    std::uint32_t ownerId, category, creationTag, reserved;
};
struct RizEngineSchedulerFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, flags;
    std::uint32_t slot, threadId, depth, reserved;
    std::uint64_t epoch, sequence, parentSequence, rootSequence;
    // Native-issued source: nearest active Drain row, or the immediately
    // pending Consume of the actual parent Pump for a Callback. Its flags say
    // which. Zero means no such source, never an ancestor guessed by payload.
    std::uint64_t sourceSequence;
    void* arg0; void* arg1; void* arg2; void* arg3;
    const void* caller;
    void* tree; void* manager; void* entry; void* sourceRecord; void* outputSlot;
    RizEngineSchedulerRecordV1 record;
    void* nodeOwner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType;
    std::int32_t referenceCount;
    std::uint32_t nodeSnapshotFlags, reserved1;
};
struct RizEngineSchedulerCompletionV1 {
    std::uint32_t abiVersion, byteSize, flags, outputFlags;
    std::uint64_t rawRax;
    void* outputEntry;
    std::uint64_t rowBegins, rowEnds;
};
enum RizEngineSchedulerRowKindV1 : std::uint32_t {
    RizEngineSchedulerRowBegin = 0, RizEngineSchedulerRowEnd = 1,
    RizEngineSchedulerRowAbnormal = 2
};
struct RizEngineSchedulerRowFrameV1 {
    std::uint32_t abiVersion, byteSize, kind, flags;
    std::uint32_t slot, threadId, depth, reserved;
    std::uint64_t epoch, dispatchSequence, parentSequence, rootSequence;
    std::uint64_t rowSequence, eventSequence;
    void* tree; void* manager; void* entry;
    const void* sitePc; const void* relayReturnPc;
    RizEngineSchedulerRecordV1 record;
    void* nodeOwner;
    std::uint64_t enumHandle;
    void* enumCache;
    std::uint32_t enumType;
    std::int32_t referenceCount;
    std::uint32_t nodeSnapshotFlags, reserved1;
};
struct RizEngineSchedulerSiteV1 { const void* sitePc; const void* relayReturnPc; };
struct RizEngineSchedulerConfigV1 {
    std::uint32_t abiVersion, byteSize;
    void* target;
    void* original;
    std::uint32_t mainThreadId, reserved;
    // Drain only: ordered Begin at 447787, End at 447866. Both are required.
    // Other kinds must have two zero sites. No patch is performed here.
    RizEngineSchedulerSiteV1 sites[2];
};
using RizEngineSchedulerEnterV1 = std::uintptr_t(__cdecl*)(const RizEngineSchedulerFrameV1*, void*);
using RizEngineSchedulerExitV1 = void(__cdecl*)(const RizEngineSchedulerFrameV1*, std::uintptr_t,
    const RizEngineSchedulerCompletionV1*, void*);
using RizEngineSchedulerRowV1 = void(__cdecl*)(const RizEngineSchedulerRowFrameV1*, void*);
struct RizEngineSchedulerObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizEngineSchedulerEnterV1 enter;
    RizEngineSchedulerExitV1 exit;
    RizEngineSchedulerRowV1 row;
    void* user;
};
struct RizEngineSchedulerInfoV1 {
    std::uint32_t abiVersion, byteSize, kind, slot;
    std::uint32_t configured, attached, unknown, mainThreadId;
    void* target; void* original; void* entry; void* rowEntry;
    std::uint64_t epoch, entered, exited, inFlight, faults, snapshotFaults;
    std::uint64_t rowBegins, rowEnds, rowsInFlight, unmatchedRows;
};

// Six independent tables of 32 permanent descriptors. Each actual configured
// call occupies a bounded native CAS thread stack, including foreign/unattached
// calls. Snapshot observer once before Enter, and retain it through Exit/rows.
// No heap, CRT/PE TLS, detach, replacement, game hook or startup permission.
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetAbiVersion();
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetLimit();
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetFrameSize();
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetCompletionSize();
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetRowFrameSize();
RIZESC_EXPORT void* __cdecl RizEngineSchedulerContext_GetEntry(std::uint32_t kind, std::uint32_t slot);
RIZESC_EXPORT void* __cdecl RizEngineSchedulerContext_GetDrainRowEntry();
// Configure: 0 ready, 1 invalid, 2 claimed, 3 duplicate site/source, 4 epoch exhausted.
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_Configure(std::uint32_t kind,
    std::uint32_t slot, const RizEngineSchedulerConfigV1*);
// Attach: 0 ready, 1 invalid, 2 unconfigured, 3 epoch mismatch, 4 attempted, 5 Unknown.
// Drain requires row callback; others require null row. Roots are permanent.
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_Attach(std::uint32_t kind,
    std::uint32_t slot, std::uint64_t epoch, const RizEngineSchedulerObserverV1*);
// GetInfo: 0 configured, 1 invalid, 2 unconfigured, 3 permanently failed claim.
RIZESC_EXPORT std::uint32_t __cdecl RizEngineSchedulerContext_GetInfo(std::uint32_t kind,
    std::uint32_t slot, RizEngineSchedulerInfoV1*);

// Record reads only: Register sourceRecord=arg2; Remove arg1 with entry=arg2;
// Consume entry=arg2 and sourceRecord=entry+20. Callback copies exact node
// +58/+10/+18/+20/+60 before observers (not after original). Pump/Drain input
// shapes have no row snapshot. Register normal Exit reads only its actual arg1
// output slot once; it never dereferences the returned entry. All other Exit
// paths read no original object. Original exceptions unwind through __finally.
// Row payload node scalars are copied only when record.callback equals an
// actual configured Callback.target (never arbitrary foreign payloads). A raw
// helper GetEntry value is not a replacement for that configured source fact.
// Drain probe requires actual current native Drain original phase, saved RBP
// equals actual arg0 manager and saved RBX equals cached entry. Begin reads row;
// End uses only cached fields, register values and real CALL return PC, never
// the recycled entry/node. Missing End emits Abnormal before the typed Exit.

static_assert(sizeof(RizEngineSchedulerRecordV1) == 64);
static_assert(offsetof(RizEngineSchedulerRecordV1, payload) == 24);
static_assert(offsetof(RizEngineSchedulerRecordV1, ownerId) == 48);
static_assert(sizeof(RizEngineSchedulerFrameV1) == 256);
static_assert(offsetof(RizEngineSchedulerFrameV1, epoch) == 32);
static_assert(offsetof(RizEngineSchedulerFrameV1, sourceSequence) == 64);
static_assert(offsetof(RizEngineSchedulerFrameV1, arg0) == 72);
static_assert(offsetof(RizEngineSchedulerFrameV1, caller) == 104);
static_assert(offsetof(RizEngineSchedulerFrameV1, tree) == 112);
static_assert(offsetof(RizEngineSchedulerFrameV1, record) == 152);
static_assert(offsetof(RizEngineSchedulerFrameV1, nodeOwner) == 216);
static_assert(offsetof(RizEngineSchedulerFrameV1, nodeSnapshotFlags) == 248);
static_assert(sizeof(RizEngineSchedulerCompletionV1) == 48);
static_assert(offsetof(RizEngineSchedulerCompletionV1, rawRax) == 16);
static_assert(offsetof(RizEngineSchedulerCompletionV1, outputEntry) == 24);
static_assert(sizeof(RizEngineSchedulerRowFrameV1) == 224);
static_assert(offsetof(RizEngineSchedulerRowFrameV1, dispatchSequence) == 40);
static_assert(offsetof(RizEngineSchedulerRowFrameV1, rowSequence) == 64);
static_assert(offsetof(RizEngineSchedulerRowFrameV1, tree) == 80);
static_assert(offsetof(RizEngineSchedulerRowFrameV1, record) == 120);
static_assert(offsetof(RizEngineSchedulerRowFrameV1, nodeOwner) == 184);
static_assert(sizeof(RizEngineSchedulerConfigV1) == 64);
static_assert(offsetof(RizEngineSchedulerConfigV1, sites) == 32);
static_assert(sizeof(RizEngineSchedulerObserverV1) == 40);
static_assert(offsetof(RizEngineSchedulerObserverV1, row) == 24);
static_assert(sizeof(RizEngineSchedulerInfoV1) == 144);
static_assert(offsetof(RizEngineSchedulerInfoV1, epoch) == 64);
static_assert(offsetof(RizEngineSchedulerInfoV1, rowBegins) == 112);
