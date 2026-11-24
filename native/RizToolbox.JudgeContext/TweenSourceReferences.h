#pragma once
#include <cstddef>
#include <cstdint>

#if defined(RIZ_JUDGE_CONTEXT_BUILD) && defined(RIZ_TWEEN_SOURCE_REFERENCES_BUILD)
#define RIZTSR_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZTSR_EXPORT extern "C"
#endif

// This side ABI publishes borrowed references from an actual private source
// scope. It neither installs sources nor grants Ready, admission, cancellation,
// return replacement, object ownership, root release or completion authority.
enum RizTweenSourceReferenceRoleV1 : std::uint32_t {
    RizTweenReferenceManager = 1, RizTweenReferenceTween = 2,
    RizTweenReferenceContainer = 4, RizTweenReferenceArray = 8,
    RizTweenReferencePrior = 16, RizTweenReferenceEvent = 32
};
enum RizTweenSourceReferencePhaseV1 : std::uint32_t {
    RizTweenReferences = 0, RizTweenReferencesBeforeCommitted = 1,
    RizTweenReferencesAfterCommitted = 2, RizTweenReferencesAbnormal = 3
};

// Scalar-only origin. operation/site/stage use the existing source V1 enums.
// expected roles describe the source of a pointer, not an asserted object class.
// relayReturnPc is the actual initial Before (or Decision) binding anchor. It
// remains fixed when stage advances to After; the journal's After PC is separate.
struct RizTweenSourceReferenceOriginV1 {
    std::uint32_t abiVersion, byteSize, operation, threadId;
    std::uint32_t depth, site, stage, originRefRoles;
    std::uint64_t sourceEpoch, operationSequence, parentOperationSequence, transaction;
    const void* sourcePc;
    const void* relayReturnPc;
};
struct RizTweenSourceBorrowedReferenceV1 {
    void* value;
    std::uint32_t role;
    std::int32_t sourceIndex;
};
struct RizTweenSourceReferenceNoticeV1 {
    std::uint32_t abiVersion, byteSize, phase, referenceCount;
    std::uint64_t observerEpoch, scopeId, deliverySequence;
    const RizTweenSourceReferenceOriginV1* origin;
    const RizTweenSourceBorrowedReferenceV1* references;
};
// Zero acknowledges only synchronous observer delivery. Every other value is
// an observer failure. The value is never a source decision or game result.
using RizTweenSourceReferencesNotifyV1 = std::uint32_t(__cdecl*)(
    const RizTweenSourceReferenceNoticeV1*, void* user);
struct RizTweenSourceReferencesObserverV1 {
    std::uint32_t abiVersion, byteSize;
    RizTweenSourceReferencesNotifyV1 notify;
    void* user;
};
struct RizTweenSourceReferencesTicketV1 {
    std::uint32_t abiVersion, byteSize, threadId, reserved;
    std::uint64_t observerEpoch, scopeId, sourceEpoch, operationSequence, transaction;
};
struct RizTweenSourceReferencesInfoV1 {
    std::uint32_t abiVersion, byteSize, attached, unknown;
    std::uint32_t scopeCapacity, activeScopes, retainedScopes, callbackInFlight;
    std::uint64_t sourceEpoch, observerEpoch, scopesBegun, scopesEnded, scopesCompleted;
    std::uint64_t deliveryAttempts, deliveriesCompleted, callbackFaults, rejectedReads;
};

// PRIVATE same-helper integration. The actual FirstInstall Ready and official
// source/admission metadata gate must call Attach; a caller DTO cannot call an
// exported attach/create/deliver seam. Observer/helper/user live for the process.
// Attach: 0 attached, 1 invalid, 2 permanently attached/claimed. No replacement.
std::uint32_t RizTweenSourceReferencesAttach(std::uint64_t actualSourceEpoch,
    const RizTweenSourceReferencesObserverV1* observer);

// Caller must hold NO source SRW lock when using these private primitives.
// Begin reserves native responsibility and snapshots the immutable observer.
// References must be delivered immediately after their actual load, before any
// metadata/class inspection. Receiver records every independent root first;
// only then may it inspect that rooted object. No deferred/worker publication.
// Each before and after stage must publish at least one non-null reference
// before its matching committed notice; completion never accepts empty/all-null
// reference phases.
// Before/AfterCommitted follow complete source validation. Raw store + original
// GC barrier remain uninterrupted: After is emitted only after the whole replay.
std::uint32_t RizTweenSourceReferencesBeginFromSource(
    const RizTweenSourceReferenceOriginV1* origin, RizTweenSourceReferencesTicketV1* ticket);
std::uint32_t RizTweenSourceReferencesDeliverFromSource(
    const RizTweenSourceReferencesTicketV1* ticket, std::uint32_t phase,
    const RizTweenSourceReferenceOriginV1* origin,
    const RizTweenSourceBorrowedReferenceV1* references, std::uint32_t referenceCount);
// End(false) emits Abnormal if the caller has not already done so. A failed
// observer does not suppress real After/Abnormal delivery. End releases only a
// completely acknowledged native transport scope; failed/pending scopes remain
// retained and Unknown. It never frees the receiver's independent roots.
std::uint32_t RizTweenSourceReferencesEndFromSource(
    const RizTweenSourceReferencesTicketV1* ticket, bool returned);

RIZTSR_EXPORT std::uint32_t __cdecl RizTweenSourceReferences_GetInfo(RizTweenSourceReferencesInfoV1* info);
// Synchronous read only. The exact active notice ADDRESS and epoch/scope/delivery
// must match this OS thread's current callback TLS. Copies/stale DTOs/other
// threads are rejected before their reference pointers are read. Caller owns
// output storage; the returned notice points only into the supplied copies.
RIZTSR_EXPORT std::uint32_t __cdecl RizTweenSourceReferences_ReadCurrentNotice(
    const RizTweenSourceReferenceNoticeV1* actualNotice,
    std::uint64_t observerEpoch, std::uint64_t scopeId, std::uint64_t deliverySequence,
    RizTweenSourceReferenceNoticeV1* noticeCopy, RizTweenSourceReferenceOriginV1* originCopy,
    RizTweenSourceBorrowedReferenceV1* referencesCopy, std::uint32_t referenceCapacity);

static_assert(sizeof(RizTweenSourceReferenceOriginV1) == 80);
static_assert(sizeof(RizTweenSourceBorrowedReferenceV1) == 16);
static_assert(sizeof(RizTweenSourceReferenceNoticeV1) == 56);
static_assert(sizeof(RizTweenSourceReferencesObserverV1) == 24);
static_assert(sizeof(RizTweenSourceReferencesTicketV1) == 56);
static_assert(sizeof(RizTweenSourceReferencesInfoV1) == 104);
static_assert(offsetof(RizTweenSourceReferenceNoticeV1, observerEpoch) == 16);
static_assert(offsetof(RizTweenSourceReferenceNoticeV1, origin) == 40);
static_assert(offsetof(RizTweenSourceReferenceOriginV1, sourceEpoch) == 32);
static_assert(offsetof(RizTweenSourceReferenceOriginV1, sourcePc) == 64);
