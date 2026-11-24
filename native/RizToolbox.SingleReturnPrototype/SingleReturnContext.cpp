#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <cmath>
#include <cstring>
#include <limits>
#include <utility>
#include "SingleReturnContext.h"
#include "../PlaybackLogicalHandoff.h"
#include "../PlaybackInitialization.h"
#include "../PlaybackFreshSource.h"
#include "../PlaybackFreshClock.h"
namespace {
constexpr std::size_t limit = 32;
struct BootstrapReceipt;
struct LogicalSession;
struct HandoffReceipt;
struct InitialCaptureReceipt;
struct FreshSourceReceipt;
struct FreshClockReceipt;
using ResourceInitializationRetry = std::uint32_t(__cdecl*)(void*, void*, void*, std::uint32_t, void*);
struct State {
    RizSingleConfigV1 config{};
    RizMusicClockPolicyV1 policy{};
    struct PolicyNode { RizMusicClockPolicyV1 value; PolicyNode* previous; };
    PolicyNode* retainedPolicies = nullptr;
    void* freshOwner = nullptr;
    RizFreshSourceCoordinate freshSourceCoordinate = nullptr;
    RizFreshSourceCoordinateWithDiagnostic freshSourceCoordinateWithDiagnostic = nullptr;
    FreshSourceReceipt* freshSourceCapture = nullptr;
    FreshClockReceipt* freshClock = nullptr;
    RizFreshClockEnter freshClockEnter = nullptr;
    RizPlaybackFreshEnter freshEnter = nullptr;
    RizPlaybackFreshLeave freshLeave = nullptr;
    int(__cdecl* activateInitialWatch)(void*, void*) = nullptr;
    void* sourceGate = nullptr;
    RizLogicalAction sourceAction = nullptr;
    RizPlaybackInitializationBegin initializationBegin = nullptr;
    RizPlaybackInitializationBegin initialCaptureBegin = nullptr;
    ResourceInitializationRetry resourceInitializationRetry = nullptr;
    ResourceInitializationRetry backingInitializationRetry = nullptr;
    ResourceInitializationRetry lowerPitchInitializationRetry = nullptr;
    ResourceInitializationRetry audioInterruptionRetry = nullptr;
    InitialCaptureReceipt* initialCapture = nullptr;
    LogicalSession* logical = nullptr;
    HandoffReceipt* handoff = nullptr;
    volatile LONG physicalClosed = 0, logicalBlocked = 0;
    void* volatile publishedPolicy = nullptr;
    BootstrapReceipt* bootstrap = nullptr;
    SRWLOCK authorityGate = SRWLOCK_INIT;
    volatile LONG configured = 0, policyClaimed = 0, observerEnabled = 0, authorityActive = 0, authorityClosed = 0, unknown = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, originalCalls = 0,
        enterFaults = 0, exitFaults = 0, sourceFaults = 0, applied = 0, declined = 0, withdrawals = 0;
};
struct BootstrapReceipt {
    State* state;
    std::uint32_t slot, phase;
    std::uint64_t epoch, initialGeneration;
    const RizMusicClockPolicyV1* initialPolicy;
};
State states[limit];
// Registration is append-only. Successful receipts and policies are never freed,
// overwritten or reused, even after logical closure. Input addresses are compared
// to this native registry before any access through the supplied receipt pointer.
void* volatile bootstrapRegistry[limit]{};
volatile LONG64 bootstrapEpochSource = 0;
volatile LONG64 sequenceSource = 0;
__declspec(thread) std::uint32_t physicalDepth = 0;
LONG Read(volatile LONG* value) { return InterlockedCompareExchange(value, 0, 0); }
std::uint64_t Read64(volatile LONG64* value) { return static_cast<std::uint64_t>(InterlockedCompareExchange64(value, 0, 0)); }
const RizMusicClockPolicyV1* Published(State* state)
{ return static_cast<const RizMusicClockPolicyV1*>(InterlockedCompareExchangePointer(&state->publishedPolicy,nullptr,nullptr)); }
// The caller holds authorityGate before entering the source leaf. Status 2 is
// a recognized Unity lifetime boundary, 3 is physical Unknown. Published policy
// and session identities are retained so a completed actual Start can transfer.
bool FreshClockFresh(State* state);
bool FreshClockTransferred(State* state);
bool SourceFresh(State* state) {
    if (state->freshClock && !FreshClockTransferred(state)) return FreshClockFresh(state);
    if (!state->freshEnter) return true;
    const auto result = state->freshEnter(state->freshOwner);
    if (result == 2 || result == 3) {
        if (Published(state)) {
            InterlockedExchange(&state->authorityActive, 0);
            InterlockedExchange(&state->authorityClosed, 1);
            InterlockedExchange(&state->logicalBlocked, 1);
        }
        if (result == 3) InterlockedExchange(&state->unknown, 1);
    }
    return result == 1;
}
bool PolicyReceiver(const RizSingleFrameV1* frame, const RizMusicClockPolicyV1* policy) {
    return policy && frame->self == policy->self && (frame->method == policy->method
        || !frame->method && policy->permittedNullCaller && frame->caller == policy->permittedNullCaller);
}
bool FreshBeforeCandidate(State* state, const RizSingleFrameV1* frame, const RizMusicClockPolicyV1* policy) {
    if (!PolicyReceiver(frame, policy)) return true; // foreign original is isolated
    AcquireSRWLockShared(&state->authorityGate);
    const bool fresh = SourceFresh(state);
    if (fresh && state->freshLeave) state->freshLeave(state->freshOwner);
    ReleaseSRWLockShared(&state->authorityGate); return fresh;
}
BootstrapReceipt* RegisteredReceipt(const void* pointer)
{
    if (!pointer) return nullptr;
    for (std::size_t slot = 0; slot < limit; ++slot) {
        auto* registered = static_cast<BootstrapReceipt*>(InterlockedCompareExchangePointer(&bootstrapRegistry[slot], nullptr, nullptr));
        if (registered == pointer) return registered;
    }
    return nullptr;
}
std::uint64_t NextBootstrapEpoch()
{
    for (;;) {
        const auto current = InterlockedCompareExchange64(&bootstrapEpochSource, 0, 0);
        if (current == (std::numeric_limits<LONG64>::max)()) return 0;
        const auto next = current + 1;
        if (InterlockedCompareExchange64(&bootstrapEpochSource, next, current) == current) return static_cast<std::uint64_t>(next);
    }
}
bool ValidInitialPolicy(const RizMusicClockPolicyV1& policy)
{
    return policy.abiVersion == 1 && policy.byteSize == sizeof(policy) && policy.self && policy.method
        && (policy.runHigh != 0 || policy.runLow != 0) && policy.controllerGeneration != 0 && policy.policyGeneration != 0
        && policy.phase <= RizClockScheduled && policy.reserved == 0 && policy.readClock && policy.clockOwner
        && std::isfinite(policy.dspAnchor) && std::isfinite(policy.mediaAnchor) && std::isfinite(policy.startDsp)
        && std::isfinite(policy.clipLength) && policy.clipLength > 0 && policy.mediaAnchor >= 0 && policy.mediaAnchor <= policy.clipLength
        && std::isfinite(policy.rate) && policy.rate > 0;
}
bool BootstrapInfoHeader(const RizMusicClockBootstrapInfoV1* info)
{
    __try { return info && info->abiVersion == 1 && info->byteSize == sizeof(*info); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool WriteBootstrapInfo(RizMusicClockBootstrapInfoV1* destination, const RizMusicClockBootstrapInfoV1* source)
{
    __try { *destination = *source; return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool FiniteBits(std::uint32_t bits) { return (bits & 0x7f800000u) != 0x7f800000u; }
float FromBits(std::uint32_t bits) { float value; std::memcpy(&value, &bits, sizeof(value)); return value; }
std::uint32_t Bits(float value) { std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits)); return bits; }
std::uintptr_t Enter(State* state, const RizSingleFrameV1* frame, bool* faulted)
{
    __try { return state->config.enter(frame, state->config.user); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *faulted = true; InterlockedIncrement64(&state->enterFaults); InterlockedExchange(&state->unknown, 1); return 0;
    }
}
bool Exit(State* state, const RizSingleFrameV1* frame, const RizSingleCompletionV1* completion)
{
    __try { state->config.exit(frame, completion, state->config.user); return true; }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedIncrement64(&state->exitFaults); InterlockedExchange(&state->unknown, 1); return false;
    }
}
bool ReadClock(State* state, const RizMusicClockPolicyV1* policy, RizMusicClockSampleV1* sample)
{
    __try { return policy->readClock(sample, policy->clockOwner) == 0; }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        InterlockedIncrement64(&state->sourceFaults); InterlockedExchange(&state->unknown, 1); return false;
    }
}
std::uint32_t Fault(State* state, std::uint32_t disposition)
{ InterlockedExchange(&state->unknown, 1); return disposition; }
std::uint32_t Candidate(State* state, const RizSingleFrameV1* frame, const RizMusicClockPolicyV1* policy,
    std::uint32_t raw, std::uint32_t* candidate)
{
    *candidate = raw;
    if (!policy) return RizSingleNoAuthority;
    if (Read(&state->unknown)) return RizSingleUnknown;
    if (!Read(&state->authorityActive)) return RizSingleWithdrawn;
    const bool exactMethod = frame->method == policy->method;
    const bool authorizedNullMethod = frame->method == nullptr && policy->permittedNullCaller != nullptr
        && frame->caller == policy->permittedNullCaller;
    if (frame->self != policy->self || (!exactMethod && !authorizedNullMethod)) return RizSingleWrongPhysicalIdentity;
    if (!FiniteBits(raw)) return Fault(state, RizSingleNonFiniteOriginal);
    RizMusicClockSampleV1 sample{1, sizeof(sample)};
    if (!ReadClock(state, policy, &sample)) {
        // A logical owner may withdraw while its already captured reader is in
        // flight. That is a declined old policy, not a new physical source fault.
        // A real reader SEH has already set Unknown and is never hidden here.
        if (!Read(&state->authorityActive) && !Read(&state->unknown)) return RizSingleWithdrawn;
        return Fault(state, Read64(&state->sourceFaults) ? RizSingleSourceFault : RizSingleBadSample);
    }
    if (sample.abiVersion != 1 || sample.byteSize != sizeof(sample) || sample.reserved != 0
        || sample.self != frame->self || sample.method != policy->method) return Fault(state, RizSingleBadSample);
    if (sample.runHigh != policy->runHigh || sample.runLow != policy->runLow
        || sample.controllerGeneration != policy->controllerGeneration || sample.policyGeneration != policy->policyGeneration
        || sample.phase != policy->phase) return Fault(state, RizSingleWrongRunIdentity);
    if (!std::isfinite(sample.dspNow)) return Fault(state, RizSingleNonFiniteClock);
    double media{};
    if (policy->phase == RizClockPaused) media = policy->mediaAnchor;
    else if (policy->phase == RizClockScheduled) {
        const auto delta = sample.dspNow - policy->startDsp;
        // The independently confirmed scheduled start has unscaled wall-time
        // preroll. Only actual playback after start-DSP is multiplied by rate.
        media = policy->mediaAnchor + delta * (delta < 0 ? 1.0 : policy->rate);
    } else {
        if (sample.dspNow < policy->dspAnchor) return Fault(state, RizSingleNonFiniteClock);
        media = policy->mediaAnchor + (sample.dspNow - policy->dspAnchor) * policy->rate;
    }
    // Match managed float.IsFinite((float)value) before the clip clamp. A
    // double immediately above float.MaxValue can still round to finite Single.
    // Clamping must not hide a cast that would return infinity or fault.
    if (!std::isfinite(media)) return Fault(state, RizSingleOverflow);
    const auto unboundedSingle = static_cast<float>(media);
    if (!FiniteBits(Bits(unboundedSingle))) return Fault(state, RizSingleOverflow);
    if (media > policy->clipLength) media = policy->clipLength;
    const auto converted = static_cast<float>(media);
    if (!FiniteBits(Bits(converted))) return Fault(state, RizSingleOverflow);
    *candidate = Bits(converted);
    return RizSingleClockCandidate;
}
std::uint32_t SafeCandidate(State* state, const RizSingleFrameV1* frame, const RizMusicClockPolicyV1* policy,
    std::uint32_t raw, std::uint32_t* candidate)
{
    // Only authority evaluation is caught. The exact original remains outside
    // this __except, so its native exception still unwinds to its caller.
    __try { return Candidate(state, frame, policy, raw, candidate); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        *candidate = raw;
        InterlockedIncrement64(&state->sourceFaults);
        return Fault(state, RizSingleSourceFault);
    }
}
// RCX=self, RDX=MethodInfo, XMM0.low32=System.Single. Original is outside
// every __except: exactly one typed call, native SEH finally on unwind.
float Dispatch(State* state, const RizSingleFrameV1* frame, const RizMusicClockPolicyV1* policy, bool observe)
{
    if (Read(&state->configured) != 1) RaiseFailFastException(nullptr, nullptr, 0);
    std::uintptr_t token = 0;
    std::uint32_t raw = 0, candidate = 0, disposition = RizSingleNoAuthority;
    bool enterFaulted = false, exitSucceeded = true, returned = false;
    __try {
        if (observe) token = Enter(state, frame, &enterFaulted);
        InterlockedIncrement64(&state->originalCalls);
        const float original = state->config.original(frame->self, frame->method); // EXACTLY ONCE
        raw = Bits(original); candidate = raw; returned = true;
        // The original/enter observer may replace a backing object. Detect it
        // before readClock, so a normal lifetime change cannot masquerade as a
        // bad old sample and poison the physical owner permanently.
        disposition = enterFaulted ? RizSingleObserverFault
            : !FreshBeforeCandidate(state, frame, policy) ? RizSingleWithdrawn
            : SafeCandidate(state, frame, policy, raw, &candidate);
    }
    __finally {
        const RizSingleCompletionV1 completion{1, sizeof(completion), frame->sequence,
            (returned ? RizSingleReturned : RizSingleAbnormal) | (enterFaulted ? RizSingleEnterFaulted : 0u)
                | (policy ? RizSingleAuthorityCaptured : 0u) | (observe ? RizSingleObserverCaptured : 0u),
            raw, candidate, disposition, frame->policyGeneration, token};
        if (observe) exitSucceeded = Exit(state, frame, &completion);
    }
    // Withdrawal and the final commit have a native linearization gate.
    // No result is ever taken from an observer callback or its token.
    AcquireSRWLockShared(&state->authorityGate);
    const bool needsFresh = PolicyReceiver(frame, policy);
    const bool fresh = !needsFresh || SourceFresh(state);
    const bool apply = fresh && returned && !enterFaulted && exitSucceeded && disposition == RizSingleClockCandidate
        && Read(&state->unknown) == 0 && Read(&state->authorityActive) == 1
        && policy == Published(state) && policy->policyGeneration == frame->policyGeneration;
    const auto result = apply ? candidate : raw;
    if (apply) InterlockedIncrement64(&state->applied);
    else InterlockedIncrement64(&state->declined);
    if (needsFresh && fresh && state->freshLeave) state->freshLeave(state->freshOwner);
    ReleaseSRWLockShared(&state->authorityGate);
    return FromBits(result);
}
template<std::size_t Slot> __declspec(noinline) float __cdecl Entry(void* self, const void* method)
{
    auto* state = &states[Slot];
    AcquireSRWLockShared(&state->authorityGate);
    // Count BEFORE snapshot publication can be revoked: withdraw+drain cannot
    // miss an old observer/policy borrower between capture and dispatch.
    InterlockedIncrement64(&state->inFlight); InterlockedIncrement64(&state->entered);
    const auto* policy = Read(&state->authorityActive) == 1 ? Published(state) : nullptr;
    const bool observe = Read(&state->observerEnabled) == 1;
    ReleaseSRWLockShared(&state->authorityGate);
    const auto sequence = InterlockedIncrement64(&sequenceSource);
    if (sequence <= 0) { InterlockedExchange(&state->unknown, 1); RaiseFailFastException(nullptr, nullptr, 0); }
    const RizSingleFrameV1 frame{1, sizeof(frame), static_cast<std::uint64_t>(sequence), self, method, _ReturnAddress(),
        policy ? policy->runHigh : 0, policy ? policy->runLow : 0, policy ? policy->controllerGeneration : 0,
        policy ? policy->policyGeneration : 0, ++physicalDepth, GetCurrentThreadId()};
    float result{};
    __try { result = Dispatch(state, &frame, policy, observe); }
    __finally {
        --physicalDepth; InterlockedIncrement64(&state->exited); InterlockedDecrement64(&state->inFlight);
    }
    return result;
}
template<std::size_t... Slots> auto Entries(std::index_sequence<Slots...>)
{ return std::array<void*, sizeof...(Slots)>{reinterpret_cast<void*>(&Entry<Slots>)...}; }
const auto entries = Entries(std::make_index_sequence<limit>{});
bool IsEntry(const void* pointer) { for (const auto entry : entries) if (entry == pointer) return true; return false; }
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_GetLimit() { return static_cast<std::uint32_t>(limit); }
RIZSR_EXPORT void* __cdecl RizSingleReturnPrototype_GetEntry(std::uint32_t slot) { return slot < limit ? entries[slot] : nullptr; }
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_Configure(std::uint32_t slot, const RizSingleConfigV1* config)
{
    if (slot >= limit || !config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || !config->original || !config->enter || !config->exit || IsEntry(reinterpret_cast<void*>(config->original))) return 1;
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, -1, 0) != 0) return 2;
    state->config = *config;
    InterlockedExchange(&state->observerEnabled, 1);
    InterlockedExchange(&state->configured, 1); return 0;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ClaimMusicClock(std::uint32_t slot, const RizMusicClockPolicyV1* policy)
{
    if (slot >= limit || !policy || policy->abiVersion != 1 || policy->byteSize != sizeof(*policy) || !policy->self || !policy->method
        || (policy->runHigh == 0 && policy->runLow == 0) || policy->controllerGeneration == 0 || policy->policyGeneration == 0
        || policy->phase > RizClockScheduled || policy->reserved != 0 || !policy->readClock || !policy->clockOwner
        || !std::isfinite(policy->dspAnchor) || !std::isfinite(policy->mediaAnchor) || !std::isfinite(policy->startDsp)
        || !std::isfinite(policy->clipLength) || policy->clipLength <= 0 || policy->mediaAnchor < 0 || policy->mediaAnchor > policy->clipLength
        || !std::isfinite(policy->rate) || policy->rate <= 0) return 1;
    auto* state = &states[slot];
    AcquireSRWLockExclusive(&state->authorityGate);
    if (state->freshSourceCapture) { ReleaseSRWLockExclusive(&state->authorityGate); return 2; }
    if (Read(&state->configured) != 1 || Read(&state->unknown) != 0) { ReleaseSRWLockExclusive(&state->authorityGate); return 3; }
    if (Read(&state->authorityClosed) != 0) { ReleaseSRWLockExclusive(&state->authorityGate); return 4; }
    if (Read64(&state->inFlight) != 0) { ReleaseSRWLockExclusive(&state->authorityGate); return 8; }
    if (InterlockedCompareExchange(&state->policyClaimed, -1, 0) != 0) { ReleaseSRWLockExclusive(&state->authorityGate); return 2; }
    const bool fresh = SourceFresh(state);
    if (!fresh) { InterlockedExchange(&state->policyClaimed, 0); ReleaseSRWLockExclusive(&state->authorityGate); return 4; }
    state->policy = *policy;
    InterlockedExchangePointer(&state->publishedPolicy,&state->policy);
    InterlockedExchange(&state->policyClaimed, 1); InterlockedExchange(&state->authorityActive, 1);
    if (state->freshLeave) state->freshLeave(state->freshOwner);
    ReleaseSRWLockExclusive(&state->authorityGate); return 0;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_Withdraw(std::uint32_t slot, std::uint32_t scope)
{
    if (slot >= limit || scope == 0 || scope > 3 || Read(&states[slot].configured) != 1) return 1;
    auto* state = &states[slot];
    AcquireSRWLockExclusive(&state->authorityGate);
    if ((scope & 1) != 0) { InterlockedExchange(&state->physicalClosed, 1); InterlockedExchange(&state->authorityActive, 0); InterlockedExchange(&state->authorityClosed, 1); }
    if ((scope & 2) != 0) InterlockedExchange(&state->observerEnabled, 0);
    InterlockedIncrement64(&state->withdrawals);
    ReleaseSRWLockExclusive(&state->authorityGate); return 0;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_GetStats(std::uint32_t slot, RizSingleStatsV1* stats)
{
    if (slot >= limit || !stats || stats->abiVersion != 1 || stats->byteSize != sizeof(*stats)) return 1;
    auto* state = &states[slot];
    AcquireSRWLockShared(&state->authorityGate);
    const RizSingleStatsV1 result{1, sizeof(result), Read64(&state->entered), Read64(&state->exited), Read64(&state->inFlight),
        Read64(&state->originalCalls), Read64(&state->enterFaults), Read64(&state->exitFaults), Read64(&state->sourceFaults),
        Read64(&state->applied), Read64(&state->declined), Read64(&state->withdrawals),
        static_cast<std::uint32_t>(Read(&state->unknown)), static_cast<std::uint32_t>(Read(&state->configured)),
        static_cast<std::uint32_t>(Read(&state->observerEnabled)), static_cast<std::uint32_t>(Read(&state->authorityActive)),
        Published(state) ? Published(state)->policyGeneration : 0};
    *stats = result;
    ReleaseSRWLockShared(&state->authorityGate); return 0;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PublishMusicClock(std::uint32_t slot, std::uint64_t expected, const RizMusicClockPolicyV1* policy)
{
    if (expected == 0) return RizSingleReturnPrototype_ClaimMusicClock(slot,policy);
    if (slot >= limit || !policy || policy->abiVersion != 1 || policy->byteSize != sizeof(*policy)
        || !policy->self || !policy->method || !policy->readClock || !policy->clockOwner || policy->reserved
        || policy->phase > RizClockScheduled || policy->policyGeneration <= expected
        || !std::isfinite(policy->dspAnchor) || !std::isfinite(policy->mediaAnchor) || !std::isfinite(policy->startDsp)
        || !std::isfinite(policy->rate) || policy->rate <= 0 || !std::isfinite(policy->clipLength)
        || policy->clipLength <= 0 || policy->mediaAnchor < 0 || policy->mediaAnchor > policy->clipLength) return 1;
    auto* state=&states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    if (state->freshSourceCapture && !FreshClockTransferred(state)) { ReleaseSRWLockExclusive(&state->authorityGate); return 2; }
    const auto* current=Published(state);
    const bool fresh = SourceFresh(state);
    std::uint32_t result=0;
    if (!fresh) result = 4;
    else if (Read(&state->unknown) || Read(&state->configured)!=1) result=3;
    else if (Read(&state->authorityClosed) || Read(&state->logicalBlocked)) result=4;
    else if (state->bootstrap && state->bootstrap->phase == RizMusicClockDormant) result=2;
    else if (!current || current->policyGeneration!=expected) result=5;
    else if (Read64(&state->inFlight)!=0) result=8;
    else if (current->self!=policy->self || current->method!=policy->method || current->runHigh!=policy->runHigh
        || current->runLow!=policy->runLow || current->controllerGeneration!=policy->controllerGeneration
        || current->readClock!=policy->readClock || current->clockOwner!=policy->clockOwner
        || current->permittedNullCaller!=policy->permittedNullCaller) result=6;
    else {
        auto* node = static_cast<State::PolicyNode*>(HeapAlloc(GetProcessHeap(), 0, sizeof(State::PolicyNode)));
        if (!node) result = 7;
        else {
            node->value = *policy; node->previous = state->retainedPolicies; state->retainedPolicies = node;
            InterlockedExchangePointer(&state->publishedPolicy, &node->value); InterlockedExchange(&state->authorityActive,1);
        }
    }
    if (fresh && state->freshLeave) state->freshLeave(state->freshOwner);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadClockPolicy(std::uint32_t slot, RizMusicClockPolicyV1* policy, std::uint32_t* active)
{
    if (slot>=limit || !policy || !active || policy->abiVersion!=1 || policy->byteSize!=sizeof(*policy)) return 1;
    auto* state=&states[slot]; AcquireSRWLockShared(&state->authorityGate); const auto* current=Published(state);
    if (current) *policy=*current;
    *active=static_cast<std::uint32_t>(Read(&state->authorityActive));
    ReleaseSRWLockShared(&state->authorityGate); return current?0u:2u;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_WithdrawMusicClock(std::uint32_t slot, std::uint64_t expected)
{
    if (slot>=limit || expected==0) return 1;
    auto* state=&states[slot]; AcquireSRWLockExclusive(&state->authorityGate); const auto* current=Published(state);
    if (state->freshSourceCapture && !FreshClockTransferred(state)) { ReleaseSRWLockExclusive(&state->authorityGate); return 2; }
    const bool matches=current && current->policyGeneration==expected;
    if (matches) {
        InterlockedExchange(&state->authorityActive,0); InterlockedExchange(&state->authorityClosed,1);
        InterlockedIncrement64(&state->withdrawals);
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return matches?0u:5u;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_MarkFault(std::uint32_t slot)
{
    if (slot>=limit || Read(&states[slot].configured)!=1) return 1;
    auto* state = &states[slot];
    AcquireSRWLockExclusive(&state->authorityGate);
    InterlockedExchange(&state->unknown,1);
    ReleaseSRWLockExclusive(&state->authorityGate); return 0;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PollFreshSource(std::uint32_t slot)
{
    if (slot >= limit) return 0;
    auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    // This bridge never resolves a caller-supplied owner or library. Its only
    // source leaf was permanently bound by the physical installation group.
    const bool bound = Read(&state->configured) == 1 && state->freshOwner && state->freshEnter && state->freshLeave;
    const bool fresh = bound && SourceFresh(state);
    if (fresh) state->freshLeave(state->freshOwner);
    const bool usable = fresh && !Read(&state->unknown) && !Read(&state->physicalClosed);
    ReleaseSRWLockExclusive(&state->authorityGate); return usable ? 1u : 0u;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_PrepareDormantMusicClock(std::uint32_t slot,
    std::uint64_t expected, const RizMusicClockPolicyV1* policy, void** receipt)
{
    if (slot >= limit || !policy || !receipt) return 1;
    RizMusicClockPolicyV1 initial{};
    __try { *receipt = nullptr; initial = *policy; }
    __except (EXCEPTION_EXECUTE_HANDLER) { return 1; }
    if (!ValidInitialPolicy(initial)) return 1;
    if (expected != 0) return 5;
    auto* state = &states[slot];
    AcquireSRWLockExclusive(&state->authorityGate);
    if (state->freshSourceCapture) { ReleaseSRWLockExclusive(&state->authorityGate); return 2; }
    const bool fresh = SourceFresh(state);
    std::uint32_t result = 0;
    if (!fresh) result = 4;
    else if (Read(&state->configured) != 1 || Read(&state->unknown)) result = 3;
    else if (Read(&state->authorityClosed) || Read(&state->observerEnabled) != 1) result = 4;
    else if (Read(&state->policyClaimed) != 0 || Published(state) || state->bootstrap) result = 2;
    else if (Read64(&state->inFlight) != 0) result = 8;
    else {
        auto* created = static_cast<BootstrapReceipt*>(HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, sizeof(BootstrapReceipt)));
        if (!created) result = 7;
        else {
            const auto epoch = NextBootstrapEpoch();
            // Even an exhausted allocation is retained; a receipt address can
            // never later become a different successful receipt.
            if (epoch == 0) result = 7;
            else {
                state->policy = initial;
                *created = BootstrapReceipt{state, slot, RizMusicClockDormant, epoch, initial.policyGeneration, &state->policy};
                state->bootstrap = created;
                InterlockedExchangePointer(&state->publishedPolicy, &state->policy);
                InterlockedExchange(&state->policyClaimed, 1);
                InterlockedExchange(&state->authorityActive, 0);
                InterlockedExchangePointer(&bootstrapRegistry[slot], created);
                // The caller's writable output was checked before admission.
                // A concurrent external revocation of that memory is caller error.
                *receipt = created;
            }
        }
    }
    if (fresh && state->freshLeave) state->freshLeave(state->freshOwner);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ArmDormantMusicClock(void* pointer)
{
    auto* receipt = RegisteredReceipt(pointer);
    if (!receipt) return 1;
    auto* state = receipt->state;
    AcquireSRWLockExclusive(&state->authorityGate);
    const auto* current = Published(state);
    const bool fresh = SourceFresh(state);
    std::uint32_t result = 0;
    if (!fresh) result = 4;
    else if (Read(&state->configured) != 1 || Read(&state->unknown)) result = 3;
    else if (Read(&state->authorityClosed)) result = 4;
    else if (state->bootstrap != receipt || receipt->phase != RizMusicClockDormant) result = 2;
    else if (current != receipt->initialPolicy || !current || current->policyGeneration != receipt->initialGeneration
        || Read(&state->policyClaimed) != 1 || Read(&state->authorityActive) != 0) result = 5;
    else if (Read64(&state->inFlight) != 0) result = 8;
    else {
        InterlockedExchange(&state->authorityActive, 1);
        receipt->phase = RizMusicClockArmed;
    }
    if (fresh && state->freshLeave) state->freshLeave(state->freshOwner);
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_CancelDormantMusicClock(void* pointer)
{
    auto* receipt = RegisteredReceipt(pointer);
    if (!receipt) return 1;
    auto* state = receipt->state;
    AcquireSRWLockExclusive(&state->authorityGate);
    const auto* current = Published(state);
    std::uint32_t result = 0;
    if (state->bootstrap != receipt || receipt->phase != RizMusicClockDormant) result = 2;
    else if (Read(&state->authorityClosed)) result = 4;
    else if (current != receipt->initialPolicy || !current || current->policyGeneration != receipt->initialGeneration) result = 5;
    else {
        InterlockedExchange(&state->authorityActive, 0);
        InterlockedExchange(&state->authorityClosed, 1);
        receipt->phase = RizMusicClockCancelled;
        InterlockedIncrement64(&state->withdrawals);
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ReadDormantMusicClock(void* pointer,
    RizMusicClockBootstrapInfoV1* info)
{
    auto* receipt = RegisteredReceipt(pointer);
    if (!receipt || !BootstrapInfoHeader(info)) return 1;
    auto* state = receipt->state;
    AcquireSRWLockShared(&state->authorityGate);
    const auto* current = Published(state);
    const RizMusicClockBootstrapInfoV1 result{1, sizeof(*info), receipt->slot,
        Read(&state->authorityClosed) ? RizMusicClockCancelled : receipt->phase,
        receipt->epoch, receipt->initialGeneration, current ? current->policyGeneration : 0,
        Read64(&state->inFlight), receipt, static_cast<std::uint32_t>(Read(&state->unknown)), 0,
        current ? *current : RizMusicClockPolicyV1{}};
    const bool written = WriteBootstrapInfo(info, &result);
    ReleaseSRWLockShared(&state->authorityGate); return written ? 0u : 1u;
}

#include "SingleReturnHandoff.inc"
#include "SingleReturnInitialCapture.inc"
#include "SingleReturnLowerPitch.inc"
#include "SingleReturnAudioInterruption.inc"
#include "SingleReturnFreshSource.inc"
#include "SingleReturnFreshClock.inc"

RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_BindFreshSource(std::uint32_t slot, void* library, void* owner) {
    if (slot >= limit || !library || !owner) return 1;
    auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    if (Read(&state->configured) != 1 || Read(&state->unknown) || state->freshOwner || Published(state)) result = 2;
    else {
        const auto enter = reinterpret_cast<RizPlaybackFreshEnter>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceEnterFresh"));
        const auto leave = reinterpret_cast<RizPlaybackFreshLeave>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceLeaveFresh"));
        const auto version = reinterpret_cast<std::uint32_t(__cdecl*)()>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackInitializationVersion"));
        if (!enter || !leave || !version || version() != 2 || enter(owner) != 1) result = 3;
        else {
            state->freshOwner = owner; state->freshEnter = enter; state->freshLeave = leave;
            const auto freshVersion = reinterpret_cast<std::uint32_t(__cdecl*)()>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackFreshSourceVersion"));
            if (freshVersion && freshVersion() == 1)
                state->freshSourceCoordinate = reinterpret_cast<RizFreshSourceCoordinate>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceCoordinateFresh"));
            const auto freshDiagnosticVersion = reinterpret_cast<std::uint32_t(__cdecl*)()>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackFreshSourceDiagnosticVersion"));
            if (freshDiagnosticVersion && freshDiagnosticVersion() == 2)
                state->freshSourceCoordinateWithDiagnostic = reinterpret_cast<RizFreshSourceCoordinateWithDiagnostic>(GetProcAddress(
                    static_cast<HMODULE>(library), "RtPlaybackSourceCoordinateFreshWithDiagnostic"));
            const auto clockVersion = reinterpret_cast<std::uint32_t(__cdecl*)()>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackFreshClockVersion"));
            if (clockVersion && clockVersion() == 2) {
                state->freshClockEnter = reinterpret_cast<RizFreshClockEnter>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceEnterFreshClock"));
                state->sourceAction = reinterpret_cast<RizLogicalAction>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceLogicalAction"));
                state->initializationBegin = reinterpret_cast<RizPlaybackInitializationBegin>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceBeginInitializationHandoff"));
                state->resourceInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryResourceInitialization"));
                state->backingInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryBackingInitialization"));
                state->lowerPitchInitializationRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryLowerPitchInitialization"));
                state->audioInterruptionRetry = reinterpret_cast<ResourceInitializationRetry>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceRetryAudioInterruptedInitialization"));
            }
            state->activateInitialWatch = reinterpret_cast<int(__cdecl*)(void*, void*)>(GetProcAddress(static_cast<HMODULE>(library), "RtPlaybackSourceActivateInitialPlayerWatch"));
            leave(owner);
        }
    }
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}

// Orthogonal PlayerWatch V1. Both actual gates remain exclusive at initial
// source-open, while GetTime has never published any policy or bootstrap.
RIZSR_EXPORT std::uint32_t __cdecl RizSingleReturnPrototype_ActivateInitialPlayerWatch(
    std::uint32_t slot, void* sourceOwner, void* receipt) {
    if (slot >= limit || !sourceOwner || !receipt) return 1;
    auto* state = &states[slot]; AcquireSRWLockExclusive(&state->authorityGate);
    std::uint32_t result = 0;
    if (Read(&state->configured) != 1 || Read(&state->unknown) || Read(&state->physicalClosed)) result = 3;
    else if (state->freshOwner != sourceOwner || !state->activateInitialWatch) result = 6;
    else if (Published(state) || state->bootstrap || state->initialCapture || state->logical || state->handoff
        || Read(&state->authorityActive)) result = 2;
    else if (Read64(&state->inFlight)) result = 8;
    else if (state->activateInitialWatch(sourceOwner, receipt) != 1) result = 6;
    ReleaseSRWLockExclusive(&state->authorityGate); return result;
}
