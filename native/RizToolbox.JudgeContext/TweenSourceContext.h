#pragma once
#include <cstddef>
#include <cstdint>

#if defined(RIZ_JUDGE_CONTEXT_BUILD) && defined(RIZ_TWEEN_SOURCE_CONTEXT_BUILD)
#define RIZTS_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZTS_EXPORT extern "C"
#endif

// Source instructions are not function calls. The publisher supplies permanent
// replay relays; this side ABI never patches code or declares startup Ready.
// Every pointer is a borrowed identity. A caller must retain actual manager and
// Tween roots before registration and through every Unknown/unfinished record.
enum RizTweenSourceSiteV1 : std::uint32_t {
    RizTweenReadyPopFront = 0, RizTweenReadyPopAppend = 1,
    RizTweenPlayingInsertFront = 2, RizTweenPlayingInsertAppend = 3,
    RizTweenPlayingMoveWrite = 4, RizTweenPlayingMoveClear = 5, RizTweenPlayingTailClear = 6,
    RizTweenScheduledPushFront = 7, RizTweenScheduledPushTail = 8, RizTweenScheduledSetItem = 9,
    RizTweenScheduledRemoveAdvance = 10, RizTweenScheduledRemoveTail = 11, RizTweenScheduledRemoveCurrent = 12,
    RizTweenTailDeadDecision = 13,
    RizTweenPlayingExecuteFront = 14, RizTweenPlayingExecuteExisting = 15, RizTweenPlayingExecuteAppend = 16,
    RizTweenUpdateOnStartFront = 17, RizTweenUpdateOnStartAppend = 18,
    RizTweenPlayOnStart = 19, RizTweenPlayOnComplete = 20, RizTweenExecuteOnComplete = 21,
    RizTweenSourceSiteCount = 22
};
enum RizTweenSourceOperationV1 : std::uint32_t { RizTweenSourceUpdate = 0, RizTweenSourcePlay = 1, RizTweenSourceExecute = 2 };
enum RizTweenSourceStageV1 : std::uint32_t { RizTweenSourceBefore = 0, RizTweenSourceAfter = 1, RizTweenSourceDecision = 2 };
enum RizTweenSourceFlagsV1 : std::uint32_t {
    RizTweenSourceKnown = 1, RizTweenSourcePopSucceeded = 2, RizTweenSourceExactDead = 4,
    RizTweenSourceRedirected = 8, RizTweenSourceParentMatched = 16, RizTweenSourceUnknown = 32,
    RizTweenSourceAbnormal = 64, RizTweenSourceReadFault = 128, RizTweenSourcePaired = 256
};
// Exactly one compound source operation. A raw store and its original GC write
// barrier are one replay unit: no managed callback/GC operation may occur inside.
struct RizTweenSourceRecordV1 {
    std::uint32_t abiVersion, byteSize, site, stage, flags, threadId, depth, operation;
    std::uint64_t epoch, updateSequence, parentUpdateSequence, eventSequence, transaction;
    const void* sourcePc;
    const void* relayReturnPc;
    void* manager;
    void* container;
    void* arrayBefore;
    void* arrayAfter;
    void* tween;
    void* prior;
    std::int32_t sourceIndex, destinationIndex, countBefore, countAfter, capacityBefore, capacityAfter, versionBefore, versionAfter;
    std::uint64_t rawResult, occurrence, reserved1;
};
struct RizTweenSourceSiteBindingV1 {
    std::uint32_t site, stage;
    const void* sourcePc;
    const void* relayReturnPc;
    const void* deadContinuation;
};
struct RizTweenSourceConfigV1 {
    std::uint32_t abiVersion, byteSize;
    const void* moduleBase;
    const void* reservedTweenClass;
    const void* reservedManagerClass;
    std::uint32_t siteBindingCount, reserved;
    const RizTweenSourceSiteBindingV1* sites;
};
struct RizTweenSourceMetadataV1 {
    std::uint32_t abiVersion, byteSize;
    const void* exactTweenClass;
    const void* exactManagerClass;
    const void* cachedPointerField;
};
struct RizTweenSourceOperationTicketV1 {
    std::uint32_t abiVersion, byteSize, threadId, depth, operation, reserved;
    std::uint64_t epoch, operationSequence, parentOperationSequence;
    void* receiver;
};
struct RizTweenSourceUpdateTicketV1 {
    std::uint32_t abiVersion, byteSize, threadId, depth;
    std::uint64_t epoch, updateSequence, parentUpdateSequence;
    void* manager;
};
struct RizTweenSourceInfoV1 {
    std::uint32_t abiVersion, byteSize, configured, unknown, bindingCount, journalCapacity, activeUpdates, metadataBound;
    std::uint64_t epoch, events, transactions, completedTransactions, redirects, faults, pendingTransactions, retainedRecords;
};

// Detail of PublishedTweenSource in the existing V7 failure word. The high
// byte is 1 and the next byte is the first failed guard. The low bytes are
// stage and site, each plus one when known.
enum class RizTweenSourceFault : std::uint32_t {
    SequenceExhausted = 1,
    JournalPendingEviction = 2,
    JournalRecordMismatch = 3,
    ReferencesParentChanged = 4,
    ReferencesScopeRejected = 5,
    ReferencesDeliveryRejected = 6,
    ReferencesNoticeRejected = 7,
    ReferencesCloseRejected = 8,
    OperationDepthLimit = 9,
    OperationReceiverMismatch = 10,
    OperationManagerMismatch = 11,
    OperationSequenceReused = 12,
    OperationCapacity = 13,
    OperationTicketMismatch = 14,
    OperationAbnormalExit = 15,
    OperationUnfinishedSource = 16,
    OperationUnfinishedMove = 17,
    OperationUnfinishedPop = 18,
    OperationPendingMismatch = 19,
    InvalidSourceFrame = 20,
    UnboundSourceReturn = 21,
    SourceParentMissing = 22,
    SourceOperationMismatch = 23,
    SourceDepthLimit = 24,
    SourceCapacity = 25,
    SourceBeforeMismatch = 26,
    SourceBeforeChanged = 27,
    SourceAfterMissingBefore = 28,
    SourceAfterPairMismatch = 29,
    SourceAfterStackMismatch = 30,
    SourceAfterMismatch = 31,
    SourceAfterChanged = 32
};
std::uint32_t RizTweenSourceFirstFaultDetail();

// No exported Configure bypass. The actual publisher must call this private
// initializer after proving source spans, replay continuations and registered
// source-context unwind identities. Both reserved class pointers must be null:
// official IL2CPP metadata is unavailable at pre-init publication time.
std::uint32_t RizTweenSourceConfigure(const RizTweenSourceConfigV1* config);
// Post-init only. Independent metadata binding is separate from pre-init code
// publication. It requires the actual admission policy's verified Tween class.
std::uint32_t RizTweenSourceBindMetadata(const RizTweenSourceMetadataV1* metadata);
// receiver is the actual TweenInfo for Play/Execute, and the actual manager for
// Update. actualManager is required for Update/Execute; Play supplies null and
// borrows its nearest retained source parent's manager, when one exists. This
// is an internal physical-operation bridge, not caller-issued source authority.
std::uint32_t RizTweenSourceOperationEnter(std::uint32_t operation, void* receiver,
    void* actualManager, std::uint64_t actualOperationSequence, RizTweenSourceOperationTicketV1* ticket);
std::uint32_t RizTweenSourceOperationExit(const RizTweenSourceOperationTicketV1* ticket, bool returned);
// Actual OperationContext Update entry/finally will call these after publisher
// integration. Standalone source fixtures call the same functions on owned data.
std::uint32_t RizTweenSourceUpdateEnter(void* manager, std::uint64_t actualOperationSequence, RizTweenSourceUpdateTicketV1* ticket);
std::uint32_t RizTweenSourceUpdateExit(const RizTweenSourceUpdateTicketV1* ticket, bool returned);
// A real relay CALL enters the preserving ASM entry. Dispatch derives the site
// from its real return PC and returns only that site's registered continuation.
RIZTS_EXPORT void* __cdecl RizTweenSourceContext_GetEntry();
RIZTS_EXPORT std::uint32_t __cdecl RizTweenSourceContext_GetInfo(RizTweenSourceInfoV1* info);
// Non-consuming ring-window read. Event sequences are monotonic; completed old
// rows may leave the 8192-record window. Status 3 rejects a missing/expired row
// without copying a reused cell. An unfinished Before cannot be overwritten:
// that condition retains its native pending responsibility and becomes Unknown.
RIZTS_EXPORT std::uint32_t __cdecl RizTweenSourceContext_ReadRecord(std::uint64_t epoch, std::uint64_t eventSequence, RizTweenSourceRecordV1* record);

static_assert(sizeof(RizTweenSourceRecordV1) == 192);
static_assert(offsetof(RizTweenSourceRecordV1, epoch) == 32);
static_assert(offsetof(RizTweenSourceRecordV1, sourcePc) == 72);
static_assert(offsetof(RizTweenSourceRecordV1, manager) == 88);
static_assert(offsetof(RizTweenSourceRecordV1, sourceIndex) == 136);
static_assert(offsetof(RizTweenSourceRecordV1, rawResult) == 168);
static_assert(sizeof(RizTweenSourceSiteBindingV1) == 32);
static_assert(sizeof(RizTweenSourceConfigV1) == 48);
static_assert(sizeof(RizTweenSourceUpdateTicketV1) == 48);
static_assert(sizeof(RizTweenSourceMetadataV1) == 32);
static_assert(sizeof(RizTweenSourceOperationTicketV1) == 56);
static_assert(sizeof(RizTweenSourceInfoV1) == 96);
