#pragma once
#include <cstddef>
#include <cstdint>
#include "TweenAdmission.h"

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZTR_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZTR_EXPORT extern "C"
#endif

enum RizTweenRetirementDomain : std::uint32_t {
    RizTweenRetirementPlay, RizTweenRetirementAdd, RizTweenRetirementExecute,
    RizTweenRetirementUpdate, RizTweenRetirementAwake, RizTweenRetirementSingle,
    RizTweenRetirementSourceOperation, RizTweenRetirementSourcePending,
    RizTweenRetirementSourceRelay, RizTweenRetirementReferenceScope,
    RizTweenRetirementReferenceNotify, RizTweenRetirementDomainCount
};
struct RizTweenRetirementReservation {
    std::uint64_t serial{}, generation{};
    std::uint32_t domain{}, slot{};
};
// Enter is called before taking an observer snapshot or publishing a borrower.
// The gate never covers a callback, original, GC/root operation or native call.
RizTweenRetirementReservation RizTweenRetirementEnter(std::uint32_t domain);
void RizTweenRetirementExit(RizTweenRetirementReservation& reservation, bool failed = false);
void RizTweenRetirementFault();
class RizTweenRetirementScope {
    RizTweenRetirementReservation value_;
public:
    explicit RizTweenRetirementScope(std::uint32_t domain) : value_(RizTweenRetirementEnter(domain)) {}
    ~RizTweenRetirementScope() { RizTweenRetirementExit(value_); }
    RizTweenRetirementScope(const RizTweenRetirementScope&) = delete;
    RizTweenRetirementScope& operator=(const RizTweenRetirementScope&) = delete;
    RizTweenRetirementReservation Take() { auto value=value_; value_={}; return value; }
};
std::uint32_t RizTweenRetirementOperationDomain(std::uint32_t kind, std::uint32_t slot);

struct RizTweenRetirementClaimV1 {
    std::uint32_t slot, reserved;
    std::uint64_t epoch;
    const void* object;
    const void* actualClass;
};
struct RizTweenRetirementRequestV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t configurationEpoch, sourceEpoch, journalCutoff;
    std::uint64_t referenceObserverEpoch, referenceDeliveryCutoff, referenceReaderGeneration;
};
struct RizTweenRetirementCutV1 {
    std::uint32_t abiVersion, byteSize, threadId, phase;
    std::uint64_t token, generation, configurationEpoch, sourceEpoch, journalCutoff;
    std::uint64_t referenceObserverEpoch, referenceDeliveryCutoff, referenceReaderGeneration;
    std::uint32_t claimCount, reserved;
};
struct RizTweenRetirementInfoV1 {
    std::uint32_t abiVersion, byteSize, unknown, activeCut;
    std::uint64_t generation, reservations, entered, exited, cuts, commits;
};
// Acquired means all prior reservations and native source/reference scopes were
// drained atomically. Future foreign operations use a NEW generation and keep
// executing. The managed caller must have irreversibly withdrawn its exact run
// enrollment and root borrowers before Acquire. This is not global suspension.
// 0 success, 1 invalid, 2 foreign/stale, 3 genuinely pending (no mutation),
// 5 sticky Unknown, 6 duplicate commit. Cuts are same-thread synchronous loans.
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_Acquire(const RizTweenRetirementRequestV1* request,
    std::uint32_t claimCount, const RizTweenRetirementClaimV1* claims, RizTweenRetirementCutV1* cut);
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_Verify(const RizTweenRetirementCutV1* cut);
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_VerifyReferenceRoots(const RizTweenRetirementCutV1* cut);
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_UnregisterBatch(const RizTweenRetirementCutV1* cut,
    std::uint32_t claimCount, const RizTweenRetirementClaimV1* claims, RizTweenAdmissionReceiptV1* receipts);
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_Release(const RizTweenRetirementCutV1* cut);
RIZTR_EXPORT std::uint32_t __cdecl RizTweenRetirement_GetInfo(RizTweenRetirementInfoV1* info);

// Called only while the retirement gate owns its short atomic data transaction.
// Admission validates the WHOLE set before mutating ANY owner. No managed or
// game callback and no root operation can be invoked by this function.
std::uint32_t RizTweenAdmissionRetirementBatch(std::uint64_t configurationEpoch,
    std::uint32_t count, const RizTweenRetirementClaimV1* claims,
    RizTweenAdmissionReceiptV1* receipts, bool commit);

static_assert(sizeof(RizTweenRetirementClaimV1) == 32);
static_assert(sizeof(RizTweenRetirementRequestV1) == 56);
static_assert(sizeof(RizTweenRetirementCutV1) == 88);
static_assert(sizeof(RizTweenRetirementInfoV1) == 64);
