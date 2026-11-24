#pragma once
#include <cstddef>
#include <cstdint>
#if defined(RIZ_SINGLE_RETURN_PROTOTYPE_BUILD)
#define RIZSR_EXPORT extern "C" __declspec(dllexport)
#else
#define RIZSR_EXPORT extern "C"
#endif
// Isolated internal Windows x64 prototype, never the frozen FirstInstall V7 ABI.
using RizSingleOriginal = float(__cdecl*)(void*, const void*);
enum RizClockPhase : std::uint32_t { RizClockPaused, RizClockPlaying, RizClockScheduled };
enum RizSingleFlags : std::uint32_t {
    RizSingleReturned = 1, RizSingleAbnormal = 2, RizSingleEnterFaulted = 4,
    RizSingleAuthorityCaptured = 8, RizSingleObserverCaptured = 16
};
enum RizSingleDisposition : std::uint32_t {
    RizSingleNoAuthority, RizSingleClockCandidate, RizSingleNonFiniteOriginal,
    RizSingleBadSample, RizSingleNonFiniteClock, RizSingleOverflow,
    RizSingleWithdrawn, RizSingleObserverFault, RizSingleWrongPhysicalIdentity,
    RizSingleWrongRunIdentity, RizSingleSourceFault, RizSingleUnknown
};
struct RizSingleFrameV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t sequence;
    void* self;
    const void* method;
    const void* caller;
    std::uint64_t runHigh, runLow, controllerGeneration, policyGeneration;
    std::uint32_t depth, threadId;
};
// This reports the original completion. candidateBits is provisional: a late
// observer fault/withdrawal revokes it. Observers have no return-value authority.
struct RizSingleCompletionV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t sequence;
    std::uint32_t flags, originalBits, candidateBits, disposition;
    std::uint64_t policyGeneration;
    std::uintptr_t observerToken;
};
using RizSingleEnter = std::uintptr_t(__cdecl*)(const RizSingleFrameV1*, void*);
using RizSingleExit = void(__cdecl*)(const RizSingleFrameV1*, const RizSingleCompletionV1*, void*);
struct RizSingleConfigV1 {
    std::uint32_t abiVersion, byteSize;
    RizSingleOriginal original;
    RizSingleEnter enter;
    RizSingleExit exit;
    void* user;
};
// The internal clock owner issues a current sample, never a replacement result.
// Its actual owner/run/generation/phase must match the immutable clock policy.
struct RizMusicClockSampleV1 {
    std::uint32_t abiVersion, byteSize;
    void* self;
    const void* method;
    std::uint64_t runHigh, runLow, controllerGeneration, policyGeneration;
    RizClockPhase phase;
    std::uint32_t reserved;
    double dspNow;
};
using RizMusicClockRead = std::uint32_t(__cdecl*)(RizMusicClockSampleV1*, void*);
struct RizMusicClockPolicyV1 {
    std::uint32_t abiVersion, byteSize;
    void* self;
    const void* method;
    std::uint64_t runHigh, runLow, controllerGeneration, policyGeneration;
    RizClockPhase phase;
    std::uint32_t reserved;
    double dspAnchor, mediaAnchor, rate, startDsp, clipLength;
    RizMusicClockRead readClock;
    void* clockOwner;
    // Null incoming MethodInfo needs one separately authorized exact caller.
    // method above stays the nonnull verified metadata identity.
    const void* permittedNullCaller;
};
struct RizSingleStatsV1 {
    std::uint32_t abiVersion, byteSize;
    std::uint64_t entered, exited, inFlight, originalCalls, enterFaults, exitFaults,
        sourceFaults, applied, declined, withdrawals;
    std::uint32_t unknown, configured, observerEnabled, authorityActive;
    std::uint64_t policyGeneration;
};
enum RizMusicClockBootstrapPhase : std::uint32_t {
    RizMusicClockDormant = 1, RizMusicClockArmed = 2, RizMusicClockCancelled = 3
};
// A readback is ordinary POD, never a receipt. receipt is the exact native-owned
// registry object, retained for process lifetime and validated before dereference.
struct RizMusicClockBootstrapInfoV1 {
    std::uint32_t abiVersion, byteSize, slot, phase;
    std::uint64_t epoch, initialGeneration, currentGeneration, inFlight;
    void* receipt;
    std::uint32_t unknown, reserved;
    RizMusicClockPolicyV1 policy;
};
// Configure and Claim are one-shot/process-lifetime. No slot reuse, arbitrary
// return replacement callback, unloading, or automatic production admission.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_GetLimit();
RIZSR_EXPORT void* __cdecl RizSingleReturnPrototype_GetEntry(std::uint32_t slot);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_Configure(std::uint32_t slot, const RizSingleConfigV1*);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ClaimMusicClock(std::uint32_t slot, const RizMusicClockPolicyV1*);
// scope=1 revoke authority, 2 stop new observers, 3 both. Already captured Exit
// duty survives withdrawal. inFlight must separately drain before root release.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_Withdraw(std::uint32_t slot, std::uint32_t scope);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_GetStats(std::uint32_t slot, RizSingleStatsV1*);
// New controlled-prototype publication surface. Each successful successor is
// immutable and permanently retained; expected-generation CAS never overwrites
// a successor. A closed/faulted binding cannot be reopened.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PublishMusicClock(std::uint32_t slot, std::uint64_t expectedGeneration, const RizMusicClockPolicyV1*);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadClockPolicy(std::uint32_t slot, RizMusicClockPolicyV1*, std::uint32_t* active);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_WithdrawMusicClock(std::uint32_t slot, std::uint64_t expectedGeneration);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_MarkFault(std::uint32_t slot);
// New isolated bootstrap surface; never FirstInstall V7. Prepare only admits a
// configured Known/open/unclaimed slot, expectedGeneration=0 and actual drain.
// The immutable initial policy is published dormant (authorityActive=0).
// Status: 0 success, 1 arguments/ABI/unregistered receipt, 2 claimed/phase,
// 3 unconfigured/Unknown, 4 closed, 5 generation/publication mismatch,
// 6 identity mismatch, 7 retained capacity/epoch exhaustion, 8 real inFlight.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PrepareDormantMusicClock(std::uint32_t slot,
    std::uint64_t expectedGeneration, const RizMusicClockPolicyV1*, void** receipt);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ArmDormantMusicClock(void* receipt);
// Cancel only owns the dormant initial policy. An armed successor must use the
// existing exact-generation WithdrawMusicClock; old bootstrap Cancel cannot revoke it.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_CancelDormantMusicClock(void* receipt);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadDormantMusicClock(void* receipt,
    RizMusicClockBootstrapInfoV1*);
// Orthogonal certified retry for a staged Unity backing that died before
// source-open. Existing action12/13 executes only through the bound source's
// backing proof; resource invalidation and physical Unknown are not fabricated.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryBackingInitialCapture(
    void* priorCapture, void* freshInitializer, void** output);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryBackingInitializationHandoff(
    void* priorHandoff, void* freshInitializer, void** output);
// Independent direct-lower interruption proof. Fresh actual Start completion
// must be certified by the bound source's private lower-pitch badge (14/15).
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryLowerPitchInitialCapture(
    void* priorCapture, void* freshInitializer, void** output);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryLowerPitchInitializationHandoff(
    void* priorHandoff, void* freshInitializer, void** output);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture(
    void* priorCapture, void* freshInitializer, void** output);
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff(
    void* priorHandoff, void* freshInitializer, void** output);
// Check the already bound source, atomically closing only its old logical
// publication on a recognized backing boundary. 1 fresh, 0 decline; no caller
// identity/epoch/bool can initialize or restore authority. Physical faults stay
// sticky. A managed reader uses this only after its callback released locks.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PollFreshSource(std::uint32_t slot);
static_assert(sizeof(void*) == 8);
static_assert(sizeof(RizSingleFrameV1) == 80);
static_assert(offsetof(RizSingleFrameV1, self) == 16);
static_assert(offsetof(RizSingleFrameV1, method) == 24);
static_assert(sizeof(RizSingleCompletionV1) == 48);
static_assert(offsetof(RizSingleCompletionV1, originalBits) == 20);
static_assert(sizeof(RizSingleConfigV1) == 40);
static_assert(sizeof(RizMusicClockSampleV1) == 72);
static_assert(sizeof(RizMusicClockPolicyV1) == 128);
static_assert(sizeof(RizSingleStatsV1) == 112);
static_assert(sizeof(RizMusicClockBootstrapInfoV1) == 192);
static_assert(offsetof(RizMusicClockBootstrapInfoV1, epoch) == 16);
static_assert(offsetof(RizMusicClockBootstrapInfoV1, receipt) == 48);
static_assert(offsetof(RizMusicClockBootstrapInfoV1, unknown) == 56);
static_assert(offsetof(RizMusicClockBootstrapInfoV1, policy) == 64);
