#pragma once
#include <cstddef>
#include <cstdint>

#ifdef RIZ_JUDGE_CONTEXT_BUILD
#define RIZTA_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZTA_EXPORT extern "C"
#endif

enum RizTweenAdmissionPhaseV1 : std::uint32_t {
    RizTweenEmpty = 0, RizTweenOpen = 1, RizTweenClosing = 2,
    RizTweenSealed = 3, RizTweenUnknown = 4, RizTweenRetired = 5
};
struct RizTweenAdmissionConfigV1 {
    std::uint32_t abiVersion, byteSize;
    const void* exactTweenClass;
    const void* cachedPointerField;
    std::uint32_t cachedPointerOffset, reserved;
};
struct RizTweenAdmissionReceiptV1 {
    std::uint32_t abiVersion, byteSize, slot, phase;
    std::uint64_t epoch;
    const void* object;
    const void* actualClass;
    const void* cachedNative;
    std::uint64_t issuersInFlight, activeEntries, admittedIssuers, deniedIssuers, deniedDead;
    std::uint32_t unknown, reserved;
};
struct RizTweenAdmissionInfoV1 {
    std::uint32_t abiVersion, byteSize, configured, unknown;
    const void* exactTweenClass;
    const void* cachedPointerField;
    std::uint32_t cachedPointerOffset, capacity;
    std::uint64_t configurationEpoch, calls, originalCalls, skippedDead, skippedSealed, unknownReads, claims, activeOwners;
};

// This is a private fixed-method admission policy, not effects completion.
// Configure independently verifies official metadata and the actual current
// pre-init producer. The caller must keep an independent strong root through
// Claim, every failure, Seal, and the successful Unregister receipt.
// 0 success; 1 invalid; 2 producer/not-configured/epoch mismatch;
// 3 duplicate Claim or pending Seal/Unregister; 4 capacity/second Configure;
// 5 sticky Unknown; 6 Claim's physical lanes busy (no mutation, retryable).
// Seal never waits and never reopens a closing owner.
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Configure(const RizTweenAdmissionConfigV1* config);
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Claim(void* actualTween, RizTweenAdmissionReceiptV1* receipt);
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Read(std::uint32_t slot, std::uint64_t epoch, RizTweenAdmissionReceiptV1* receipt);
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Seal(std::uint32_t slot, std::uint64_t epoch, RizTweenAdmissionReceiptV1* receipt);
// Caller must first establish the actual complete queue-capacity exit and
// destruction authority in its private typed owner. No caller boolean is
// accepted here. Native code additionally requires the sealed actual object
// to have cached pointer zero and all five physical lanes to be idle.
// A successful Retired receipt retains the exact former object identity only
// in that returned copy; native registry storage then forgets its raw address.
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_Unregister(std::uint32_t slot, std::uint64_t epoch, RizTweenAdmissionReceiptV1* receipt);
RIZTA_EXPORT std::uint32_t __cdecl RizTweenAdmission_GetInfo(RizTweenAdmissionInfoV1* info);

// Internal stack responsibility used only by OperationContext's actual dispatch.
// Every relevant entry reserves its owner record under the same SRW lock as
// Seal. No callback, game call, root operation or GC API runs under that lock.
struct RizTweenAdmissionTicket {
    std::uint32_t slot = 0xffffffffu, reserved = 0;
    std::uint64_t epoch = 0;
    bool relevant = false, issuer = false, skipped = false, registered = false;
};
RizTweenAdmissionTicket RizTweenAdmissionEnter(std::uint32_t kind, std::uint32_t slot, void* self, void* argument);
void RizTweenAdmissionExit(const RizTweenAdmissionTicket& ticket, bool originalCalled, bool returned, bool observerFault);

static_assert(sizeof(RizTweenAdmissionConfigV1) == 32);
static_assert(sizeof(RizTweenAdmissionReceiptV1) == 96);
static_assert(offsetof(RizTweenAdmissionReceiptV1, epoch) == 16);
static_assert(offsetof(RizTweenAdmissionReceiptV1, object) == 24);
static_assert(offsetof(RizTweenAdmissionReceiptV1, cachedNative) == 40);
static_assert(offsetof(RizTweenAdmissionReceiptV1, issuersInFlight) == 48);
static_assert(offsetof(RizTweenAdmissionReceiptV1, unknown) == 88);
static_assert(sizeof(RizTweenAdmissionInfoV1) == 104);
