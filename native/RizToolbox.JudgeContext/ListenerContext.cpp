#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <limits>
#include "ListenerContext.h"

extern "C" void RizListenerContext_PreEntry();
extern "C" void RizListenerContext_PostEntry();

namespace {
constexpr std::size_t slotLimit = RizListenerSlotLimitV1, childLimit = 256;
struct State {
    RizListenerConfigV1 config{};
    RizListenerObserverV1 observer{};
    volatile LONG configured = 0, attaching = 0, unknown = 0;
    PVOID volatile published = nullptr;
    std::uint64_t epoch = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, faults = 0;
    volatile LONG64 abnormal = 0, overflow = 0, missingPost = 0;
};
struct Child {
    State* state = nullptr;
    const RizListenerObserverV1* observer = nullptr;
    RizListenerParentCapture* parent = nullptr;
    RizListenerFrameV1 frame{};
    std::uintptr_t managedToken = 0;
};
State states[slotLimit];
SRWLOCK configurationLock = SRWLOCK_INIT;
volatile LONG64 epochSource = 0, parentSequenceSource = 0, childSequenceSource = 0;
thread_local Child children[childLimit]{};
thread_local std::size_t childDepth = 0;
thread_local std::uint64_t maskDepth = 0, masks[slotLimit]{};
thread_local bool tlsUnknown = false;

[[noreturn]] void FailFast()
{
    RaiseFailFastException(nullptr, nullptr, 0);
    __assume(0);
}
std::uint32_t Slot(const State* state) { return static_cast<std::uint32_t>(state - states); }
bool Known(State* state) { return InterlockedCompareExchange(&state->unknown, 0, 0) == 0; }
void Fault(State* state)
{
    InterlockedExchange(&state->unknown, 1);
    InterlockedIncrement64(&state->faults);
    RizVoidBootstrap_ListenerFault(Slot(state), state->config.parentEpoch);
}
const RizListenerObserverV1* ReadObserver(State* state)
{
    return static_cast<const RizListenerObserverV1*>(
        InterlockedCompareExchangePointer(&state->published, nullptr, nullptr));
}
std::uint64_t Next(volatile LONG64* source)
{
    const auto next = InterlockedIncrement64(source);
    // Never reuse a sequence even after the signed process-lifetime range.
    if (next <= 0) FailFast();
    return static_cast<std::uint64_t>(next);
}
bool MachineValid(const RizEventMidFrameV1* frame)
{
    return frame && frame->abiVersion == 1 && frame->byteSize == sizeof(*frame)
        && frame->reserved0 == 0 && frame->reserved1 == 0
        && frame->entryRsp != 0 && frame->gpr[RizEventMidRsp] == frame->entryRsp + 8;
}
State* Select(const RizEventMidFrameV1* frame, bool post, std::uint32_t* site)
{
    if (!MachineValid(frame)) FailFast();
    for (auto& state : states) {
        if (InterlockedCompareExchange(&state.configured, 0, 0) != 1) continue;
        for (std::uint32_t index = 0; index != 3; ++index) {
            const auto pc = post ? state.config.postTransportReturnPc[index] : state.config.sourceReturnPc[index];
            if (pc == reinterpret_cast<const void*>(frame->returnPc)) {
                *site = index;
                return &state;
            }
        }
    }
    // A pre with no configured source has no recoverable original target.
    // Unknown posts also cannot be allowed to complete an unrelated child.
    FailFast();
}
void Begin(State* state)
{
    InterlockedIncrement64(&state->entered);
    InterlockedIncrement64(&state->inFlight);
}
void End(State* state, bool abnormal)
{
    if (abnormal) InterlockedIncrement64(&state->abnormal);
    InterlockedIncrement64(&state->exited);
    InterlockedDecrement64(&state->inFlight);
}
void ObserveBefore(Child* child)
{
    if (!child->observer) return;
    __try { child->managedToken = child->observer->before(&child->frame, child->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        child->managedToken = 0;
        child->frame.flags |= RizListenerBeforeFaulted;
        Fault(child->state);
    }
}
void ObserveAfter(Child* child)
{
    if (!child->observer) return;
    __try { child->observer->after(&child->frame, child->managedToken, child->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(child->state); }
}
void Pop(Child* child, bool abnormal)
{
    auto* state = child->state;
    __try { ObserveAfter(child); }
    __finally {
        if (childDepth == 0 || &children[childDepth - 1] != child) {
            tlsUnknown = true;
            Fault(state); // Retain unproved responsibility rather than pop another generation.
        } else {
            *child = Child{};
            --childDepth;
            End(state, abnormal);
        }
    }
}
void MaskBegin(State* state)
{
    Fault(state);
    if (maskDepth == (std::numeric_limits<std::uint64_t>::max)()
        || masks[Slot(state)] == static_cast<std::uint64_t>((std::numeric_limits<LONG64>::max)())) FailFast();
    ++maskDepth;
    ++masks[Slot(state)];
    InterlockedIncrement64(&state->overflow);
}
// This is intentionally outside any observer __try/__except. A bad virtual
// table read is an original native fault, unwinds the pre ASM frame and reaches
// the parent finally. Its ExceptionAddress is this real read, not the old RVA.
const void* Target(State* state, std::uint32_t site, const RizEventMidFrameV1* frame)
{
    if (site < 2) return state->config.directTarget[site];
    const auto address = static_cast<std::uintptr_t>(frame->gpr[RizEventMidRax]) + 0x178;
    return *reinterpret_cast<const void* const volatile*>(address);
}
bool ParentMatches(const RizListenerCurrentParent& parent, State* state, const RizEventMidFrameV1* frame)
{
    return parent.slot == Slot(state) && parent.epoch == state->config.parentEpoch
        && parent.capture && parent.capture->state == state && parent.capture->sequence != 0
        && parent.self == reinterpret_cast<const void*>(frame->gpr[RizEventMidRsi]);
}
void SetFrame(Child* child, State* state, std::uint32_t site, const RizEventMidFrameV1* frame,
    const RizListenerCurrentParent* parent)
{
    auto& output = child->frame;
    output.abiVersion = 1; output.byteSize = sizeof(output);
    output.slot = Slot(state); output.site = site;
    output.stage = RizListenerBefore; output.osThreadId = GetCurrentThreadId();
    output.epoch = state->epoch; output.childSequence = Next(&childSequenceSource);
    output.parentSequence = parent && parent->capture ? parent->capture->sequence : 0;
    output.parentManagedToken = parent ? parent->managedToken : 0;
    output.event = reinterpret_cast<const void*>(frame->gpr[RizEventMidRsi]);
    output.list = reinterpret_cast<const void*>(frame->gpr[RizEventMidRbp]);
    output.invokable = reinterpret_cast<const void*>(frame->gpr[RizEventMidRcx]);
    output.argRcx = output.invokable;
    output.argRdx = reinterpret_cast<const void*>(frame->gpr[RizEventMidRdx]);
    output.argR8 = reinterpret_cast<const void*>(frame->gpr[RizEventMidR8]);
    output.argR9 = reinterpret_cast<const void*>(frame->gpr[RizEventMidR9]);
    output.sourceReturnPc = state->config.sourceReturnPc[site];
    output.transportReturnPc = reinterpret_cast<const void*>(frame->returnPc);
    output.sourceRsp = frame->gpr[RizEventMidRsp];
    output.index = frame->gpr[RizEventMidRbx];
    output.machineFrame = frame;
}
bool ConfigValid(const RizListenerConfigV1* config)
{
    if (!config || config->abiVersion != 1 || config->byteSize != sizeof(*config)
        || config->parentEpoch == 0 || !config->directTarget[0] || !config->directTarget[1]) return false;
    const void* pcs[6]{ config->sourceReturnPc[0], config->sourceReturnPc[1], config->sourceReturnPc[2],
        config->postTransportReturnPc[0], config->postTransportReturnPc[1], config->postTransportReturnPc[2] };
    for (std::size_t i = 0; i != 6; ++i) {
        if (!pcs[i] || pcs[i] == reinterpret_cast<const void*>(&RizListenerContext_PreEntry)
            || pcs[i] == reinterpret_cast<const void*>(&RizListenerContext_PostEntry)) return false;
    }
    for (const auto target : config->directTarget)
        if (target == reinterpret_cast<const void*>(&RizListenerContext_PreEntry)
            || target == reinterpret_cast<const void*>(&RizListenerContext_PostEntry)) return false;
    return true;
}
}

// Pre ASM stores this return value only in the original callee's home[0].
extern "C" const void* __cdecl RizListenerContext_PreDispatch(const RizEventMidFrameV1* frame)
{
    std::uint32_t site = 0;
    auto* state = Select(frame, false, &site);
    Begin(state);
    if (tlsUnknown || maskDepth != 0 || childDepth == childLimit) {
        MaskBegin(state);
        return Target(state, site, frame);
    }
    RizListenerCurrentParent parent{};
    const bool hasParent = RizVoidBootstrap_CurrentListenerParent(&parent);
    const bool matched = hasParent && ParentMatches(parent, state, frame);
    auto* child = &children[childDepth++];
    *child = Child{};
    child->state = state;
    child->parent = hasParent ? parent.capture : nullptr;
    SetFrame(child, state, site, frame, hasParent ? &parent : nullptr);
    if (matched) child->observer = static_cast<const RizListenerObserverV1*>(parent.capture->observer);
    else Fault(state); // This frame masks all older listener children.
    // Retain a cleanup record before the risky read. No Before callback occurs
    // if the actual virtual target load faults.
    child->frame.flags |= RizListenerTargetReadFaulted;
    child->frame.actualTarget = Target(state, site, frame);
    child->frame.flags &= ~RizListenerTargetReadFaulted;
    ObserveBefore(child);
    // Do not retain a pointer to the pre helper's expired stack snapshot.
    child->frame.machineFrame = nullptr;
    return child->frame.actualTarget;
}

extern "C" void __cdecl RizListenerContext_PostDispatch(const RizEventMidFrameV1* frame)
{
    std::uint32_t site = 0;
    auto* state = Select(frame, true, &site);
    if (maskDepth != 0) {
        if (masks[Slot(state)] == 0) { tlsUnknown = true; Fault(state); return; }
        --masks[Slot(state)]; --maskDepth;
        End(state, false);
        return;
    }
    if (tlsUnknown || childDepth == 0) { Fault(state); return; }
    auto* child = &children[childDepth - 1];
    if (child->state != state || child->frame.site != site
        || child->frame.sourceRsp != frame->gpr[RizEventMidRsp]
        || child->frame.event != reinterpret_cast<const void*>(frame->gpr[RizEventMidRsi])
        || child->frame.list != reinterpret_cast<const void*>(frame->gpr[RizEventMidRbp])
        || child->frame.index != frame->gpr[RizEventMidRbx]) {
        tlsUnknown = true; Fault(state); return;
    }
    RizListenerCurrentParent parent{};
    const bool matched = RizVoidBootstrap_CurrentListenerParent(&parent)
        && ParentMatches(parent, state, frame) && parent.capture == child->parent
        && parent.capture->sequence == child->frame.parentSequence;
    if (!matched && child->observer) {
        // An unexpected post cannot mint a successful After or borrow a parent.
        Fault(state);
        child->frame.stage = RizListenerAbnormal;
        child->frame.flags |= RizListenerMissingPost;
        InterlockedIncrement64(&state->missingPost);
    } else child->frame.stage = RizListenerReturned;
    child->frame.transportReturnPc = reinterpret_cast<const void*>(frame->returnPc);
    child->frame.machineFrame = frame;
    Pop(child, !matched && child->observer);
}

void RizListenerContext_CaptureParent(std::uint32_t slot, std::uint64_t parentEpoch, RizListenerParentCapture* capture)
{
    *capture = RizListenerParentCapture{};
    capture->childBaseline = childDepth;
    capture->maskBaseline = maskDepth;
    for (std::size_t i = 0; i != slotLimit; ++i) capture->masks[i] = masks[i];
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1
        || state->config.parentEpoch != parentEpoch) return;
    capture->state = state;
    capture->sequence = Next(&parentSequenceSource);
    capture->observer = ReadObserver(state); // Exactly once, before managed Enter.
}

void RizListenerContext_CloseParent(RizListenerParentCapture* capture, std::uint32_t parentFlags)
{
    auto* parentState = const_cast<State*>(static_cast<const State*>(capture->state));
    const bool returned = (parentFlags & RizVoidReturned) != 0 && (parentFlags & RizVoidAbnormal) == 0;
    if (childDepth < capture->childBaseline || maskDepth < capture->maskBaseline) {
        tlsUnknown = true; if (parentState) Fault(parentState); return;
    }
    // Unwind overflow masks owned by this parent without revealing older children.
    for (std::size_t i = 0; i != slotLimit; ++i) {
        if (masks[i] < capture->masks[i]) { tlsUnknown = true; if (parentState) Fault(parentState); return; }
        const auto count = masks[i] - capture->masks[i];
        if (count != 0) {
            auto* state = &states[i];
            Fault(state);
            InterlockedExchangeAdd64(&state->exited, static_cast<LONG64>(count));
            InterlockedExchangeAdd64(&state->abnormal, static_cast<LONG64>(count));
            InterlockedExchangeAdd64(&state->inFlight, -static_cast<LONG64>(count));
            masks[i] = capture->masks[i];
        }
    }
    maskDepth = capture->maskBaseline;
    while (childDepth > capture->childBaseline) {
        auto* child = &children[childDepth - 1];
        auto* state = child->state;
        if (child->parent != capture || child->frame.parentSequence != capture->sequence) {
            // Never deliver another parent's context. Retain unknown status,
            // but remove the stray top record from this exiting native window.
            Fault(state); child->observer = nullptr;
        }
        child->frame.stage = RizListenerAbnormal;
        child->frame.machineFrame = nullptr;
        child->frame.transportReturnPc = nullptr;
        if (returned) {
            child->frame.flags |= RizListenerMissingPost;
            InterlockedIncrement64(&state->missingPost); Fault(state);
        } else child->frame.flags |= RizListenerParentAbnormal;
        const auto before = childDepth;
        Pop(child, true);
        if (childDepth >= before) break; // Callback corruption cannot create an infinite cleanup loop.
    }
}

RIZVC_EXPORT void* __cdecl RizListenerContext_GetPreEntry()
{ return reinterpret_cast<void*>(&RizListenerContext_PreEntry); }
RIZVC_EXPORT void* __cdecl RizListenerContext_GetPostEntry()
{ return reinterpret_cast<void*>(&RizListenerContext_PostEntry); }

RIZVC_EXPORT std::uint32_t __cdecl RizListenerContext_Configure(std::uint32_t slot, const RizListenerConfigV1* config)
{
    if (slot >= slotLimit || !ConfigValid(config)) return 1;
    const void* pcs[6]{ config->sourceReturnPc[0], config->sourceReturnPc[1], config->sourceReturnPc[2],
        config->postTransportReturnPc[0], config->postTransportReturnPc[1], config->postTransportReturnPc[2] };
    for (std::size_t i = 0; i != 6; ++i)
        for (std::size_t j = 0; j != i; ++j)
            if (pcs[i] == pcs[j]) return 3;
    RizVoidBootstrapInfoV1 parent{ 1, sizeof(parent), 0, 0, nullptr, nullptr, nullptr, 0, 0, 0 };
    if (RizVoidBootstrap_GetInfo(slot, &parent) != 0) return 4;
    if (parent.epoch != config->parentEpoch) return 5;
    if (parent.unknown != 0) return 6;
    AcquireSRWLockExclusive(&configurationLock);
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 0) {
        ReleaseSRWLockExclusive(&configurationLock); return 2;
    }
    for (auto& candidate : states) {
        if (InterlockedCompareExchange(&candidate.configured, 0, 0) == 0) continue;
        for (std::size_t i = 0; i != 3; ++i) {
            for (std::size_t j = 0; j != 3; ++j) {
                if (config->sourceReturnPc[i] == candidate.config.sourceReturnPc[j]
                    || config->sourceReturnPc[i] == candidate.config.postTransportReturnPc[j]
                    || config->postTransportReturnPc[i] == candidate.config.sourceReturnPc[j]
                    || config->postTransportReturnPc[i] == candidate.config.postTransportReturnPc[j]) {
                    ReleaseSRWLockExclusive(&configurationLock); return 3;
                }
            }
        }
    }
    InterlockedExchange(&state->configured, -1);
    state->config = *config;
    state->epoch = Next(&epochSource);
    if (!RizVoidBootstrap_ListenerParentKnown(slot, config->parentEpoch)) {
        Fault(state); InterlockedExchange(&state->configured, -2);
        ReleaseSRWLockExclusive(&configurationLock); return 6;
    }
    InterlockedExchange(&state->configured, 1);
    ReleaseSRWLockExclusive(&configurationLock);
    return 0;
}
RIZVC_EXPORT std::uint32_t __cdecl RizListenerContext_Attach(std::uint32_t slot, std::uint64_t epoch,
    const RizListenerObserverV1* observer)
{
    if (slot >= slotLimit || !observer || observer->abiVersion != 1 || observer->byteSize != sizeof(*observer)
        || !observer->before || !observer->after) return 1;
    auto* state = &states[slot];
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) return Known(state) ? 2u : 5u;
    if (epoch == 0 || state->epoch != epoch) return 3;
    if (!RizVoidBootstrap_ListenerParentKnown(slot, state->config.parentEpoch)) Fault(state);
    if (!Known(state)) return 5;
    if (InterlockedCompareExchange(&state->attaching, -1, 0) != 0) return 4;
    if (!RizVoidBootstrap_ListenerParentKnown(slot, state->config.parentEpoch)) Fault(state);
    if (!Known(state)) { InterlockedExchange(&state->attaching, -2); return 5; }
    state->observer = *observer;
    InterlockedExchangePointer(&state->published, &state->observer);
    InterlockedExchange(&state->attaching, 1);
    return 0;
}
RIZVC_EXPORT std::uint32_t __cdecl RizListenerContext_GetInfo(std::uint32_t slot, RizListenerInfoV1* info)
{
    if (slot >= slotLimit || !info || info->abiVersion != 1 || info->byteSize != sizeof(*info) || info->reserved != 0) return 1;
    auto* state = &states[slot];
    const auto phase = InterlockedCompareExchange(&state->configured, 0, 0);
    *info = RizListenerInfoV1{};
    info->abiVersion = 1; info->byteSize = sizeof(*info); info->slot = slot;
    info->preEntry = RizListenerContext_GetPreEntry(); info->postEntry = RizListenerContext_GetPostEntry();
    if (phase == 1 || phase == -2) {
        info->epoch = state->epoch; info->parentEpoch = state->config.parentEpoch;
        for (std::size_t i = 0; i != 3; ++i) {
            info->sourceReturnPc[i] = state->config.sourceReturnPc[i];
            info->postTransportReturnPc[i] = state->config.postTransportReturnPc[i];
        }
        for (std::size_t i = 0; i != 2; ++i) info->directTarget[i] = state->config.directTarget[i];
    }
    if (phase == 1) {
        if (!RizVoidBootstrap_ListenerParentKnown(slot, state->config.parentEpoch)) InterlockedExchange(&state->unknown, 1);
        info->attached = ReadObserver(state) ? 1u : 0u;
    }
    info->entered = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->entered, 0, 0));
    info->exited = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->exited, 0, 0));
    info->inFlight = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->inFlight, 0, 0));
    info->faults = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->faults, 0, 0));
    info->abnormal = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->abnormal, 0, 0));
    info->overflow = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->overflow, 0, 0));
    info->missingPost = static_cast<std::uint64_t>(InterlockedCompareExchange64(&state->missingPost, 0, 0));
    info->unknown = Known(state) ? 0u : 1u;
    return phase == 1 ? 0u : 2u;
}
