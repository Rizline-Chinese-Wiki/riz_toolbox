#ifdef RIZ_ENGINE_WAIT_CONTEXT
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <limits>
#include <utility>
#include "EngineWaitContext.h"
#include "EngineNodeContext.h"
#include "EngineSchedulerContext.h"
#include "MidContext.h"

extern "C" void RizEngineWaitPhase_Entry();
namespace {
constexpr std::size_t limit = 32, threadLimit = 64;
constexpr std::uint32_t depthLimit = 128;
using RawOriginal = std::uint64_t(__cdecl*)(void*, void*, void*, void*);
struct State {
    RizEngineWaitConfigV1 config{};
    RizEngineWaitObserverV1 observer{};
    void* volatile publishedObserver = nullptr;
    std::uint64_t epoch = 0;
    volatile LONG configured = 0, attachAttempted = 0, unknown = 0;
    volatile LONG64 entered = 0, exited = 0, inFlight = 0, faults = 0, snapshotFaults = 0, phases = 0, unmatched = 0;
};
struct Thread { volatile LONG id = 0; void* volatile top = nullptr; };
struct alignas(16) Scope {
    State* state;
    const RizEngineWaitObserverV1* observer;
    Thread* thread;
    Scope* parent;
    const RizEngineNodeFrameV1* nodeFrame;
    const RizEngineSchedulerFrameV1* schedulerFrame;
    RizEngineWaitFrameV1 frame;
    RizEngineWaitPhaseFrameV1 operation;
    std::uintptr_t token;
    std::uint64_t sourceRsp, seen, phaseCount;
    std::uint32_t phase;
    bool unknown, observerActive, entered, completed, operationActive;
    bool callbackExpected, callbackClaimed, callbackCompleted;
    bool cleanupExpected, cleanupClaimed, cleanupCompleted;
};
static_assert(sizeof(Scope) <= sizeof(RizEngineWaitPrivate::BoundaryStorage));
static_assert(alignof(Scope) <= alignof(RizEngineWaitPrivate::BoundaryStorage));
State states[limit];
Thread threads[threadLimit];
SRWLOCK configurationLock = SRWLOCK_INIT;
volatile LONG globalUnknown = 0;
volatile LONG64 epochSource = 0, sequenceSource = 0, eventSource = 0;

std::uint64_t Next(volatile LONG64* source)
{
    auto current = InterlockedCompareExchange64(source, 0, 0);
    for (;;) {
        if (current < 0 || current == (std::numeric_limits<LONG64>::max)()) return 0;
        const auto next = current + 1;
        const auto found = InterlockedCompareExchange64(source, next, current);
        if (found == current) return static_cast<std::uint64_t>(next);
        current = found;
    }
}
std::uint64_t Count(volatile LONG64* value)
{ return static_cast<std::uint64_t>(InterlockedCompareExchange64(value, 0, 0)); }
void Fault(State* state, Scope* scope = nullptr)
{
    // Foreign physical calls remain masks without manufacturing an owned fault.
    // Malformed unscoped probes still fault the whole observation transport.
    if (state || !scope) InterlockedExchange(&globalUnknown, 1);
    if (state) { InterlockedExchange(&state->unknown, 1); InterlockedIncrement64(&state->faults); }
    if (scope) scope->unknown = true;
}
void SnapshotFault(Scope* scope)
{
    if (scope->state) InterlockedIncrement64(&scope->state->snapshotFaults);
    Fault(scope->state, scope);
}
Thread* FindThread(DWORD id)
{
    for (auto& thread : threads)
        if (static_cast<DWORD>(InterlockedCompareExchange(&thread.id, 0, 0)) == id) return &thread;
    return nullptr;
}
Thread* ClaimThread(DWORD id)
{
    if (auto* found = FindThread(id)) return found;
    for (auto& thread : threads)
        if (InterlockedCompareExchange(&thread.id, static_cast<LONG>(id), 0) == 0) return &thread;
    return nullptr;
}
Scope* Top(Thread* thread)
{ return thread ? static_cast<Scope*>(InterlockedCompareExchangePointer(&thread->top, nullptr, nullptr)) : nullptr; }
bool Current(const Scope* scope)
{ return scope && scope->thread && Top(scope->thread) == scope && scope->frame.threadId == GetCurrentThreadId(); }
void Push(Scope* scope)
{
    scope->thread = ClaimThread(scope->frame.threadId);
    if (!scope->thread) { Fault(scope->state, scope); return; }
    scope->parent = Top(scope->thread);
    scope->frame.depth = scope->parent ? scope->parent->frame.depth + 1u : 1u;
    scope->frame.parentSequence = scope->parent ? scope->parent->frame.sequence : 0;
    scope->frame.rootSequence = scope->parent ? scope->parent->frame.rootSequence : scope->frame.sequence;
    if (!scope->frame.depth || scope->frame.depth > depthLimit) Fault(scope->state, scope);
    InterlockedExchangePointer(&scope->thread->top, scope);
}
void PopScope(Scope* scope)
{
    if (!scope->thread) return;
    if (!Current(scope)) { Fault(scope->state, scope); return; }
    InterlockedExchangePointer(&scope->thread->top, scope->parent);
}
void* Pointer(const void* value, std::size_t offset)
{ return *reinterpret_cast<void* const volatile*>(static_cast<const unsigned char*>(value) + offset); }
std::uint32_t Word(const void* value, std::size_t offset)
{ return *reinterpret_cast<const volatile std::uint32_t*>(static_cast<const unsigned char*>(value) + offset); }
std::uint64_t Quad(const void* value, std::size_t offset)
{ return *reinterpret_cast<const volatile std::uint64_t*>(static_cast<const unsigned char*>(value) + offset); }
const void* Add(const void* value, std::size_t offset)
{ return reinterpret_cast<const void*>(reinterpret_cast<std::uintptr_t>(value) + offset); }
bool CopyTuple(void* wait, RizEngineWaitTupleV1* target)
{
    RizEngineWaitTupleV1 copy{};
    __try {
        copy.callback = Pointer(wait, 0x10); copy.cleanup = Pointer(wait, 0x18);
        copy.payload = Pointer(wait, 0x20); copy.ownerId = Word(wait, 0x28);
        *target = copy; return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool CopyNode(void* node, void** owner, std::uint64_t* handle, void** cache,
    std::uint32_t* type, std::int32_t* references)
{
    __try {
        const auto actualOwner = Pointer(node, 0x58); const auto actualHandle = Quad(node, 0x10);
        const auto actualCache = Pointer(node, 0x20); const auto actualType = Word(node, 0x18);
        const auto actualReferences = static_cast<std::int32_t>(Word(node, 0x60));
        *owner = actualOwner; *handle = actualHandle; *cache = actualCache;
        *type = actualType; *references = actualReferences; return true;
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
void SnapshotOperationNode(Scope* scope)
{
    auto* op = &scope->operation;
    op->nodeSnapshotFlags = RizEngineWaitSnapshotRequested;
    if (CopyNode(op->node, &op->nodeOwner, &op->enumHandle, &op->enumCache, &op->enumType, &op->referenceCount))
        op->nodeSnapshotFlags |= RizEngineWaitSnapshotValid;
    else { op->nodeSnapshotFlags |= RizEngineWaitSnapshotFaulted; SnapshotFault(scope); }
}
void SnapshotOperationTuple(Scope* scope, bool preserveNode = false)
{
    auto* op = &scope->operation;
    // A callback may change cleanup/payload. Never attach old node scalars to
    // that new payload. Commit/fields-cleared retain their Begin node identity.
    op->tuple = {};
    if (!preserveNode) {
        op->nodeOwner = nullptr; op->enumHandle = 0; op->enumCache = nullptr;
        op->enumType = 0; op->referenceCount = 0; op->nodeSnapshotFlags = 0;
    }
    op->flags &= ~(RizEngineWaitSnapshotRequested | RizEngineWaitSnapshotValid | RizEngineWaitSnapshotFaulted | RizEngineWaitKnownCallback);
    op->flags |= RizEngineWaitSnapshotRequested;
    if (CopyTuple(op->wait, &op->tuple)) {
        op->flags |= RizEngineWaitSnapshotValid;
        if (op->tuple.callback == scope->state->config.schedulerTargets[5]) op->flags |= RizEngineWaitKnownCallback;
    }
    else { op->flags |= RizEngineWaitSnapshotFaulted; SnapshotFault(scope); }
}
State* Select(std::uint32_t kind, const void* target)
{
    if (!target || kind >= 13) return nullptr;
    for (auto& state : states) {
        if (InterlockedCompareExchange(&state.configured, 0, 0) != 1) continue;
        const void* actual = kind < 3 ? state.config.targets[kind]
            : kind < 7 ? state.config.nodeTargets[kind - 3] : state.config.schedulerTargets[kind - 7];
        if (actual == target) return &state;
    }
    return nullptr;
}
void Init(Scope* scope, State* state, std::uint32_t kind, void* a0, void* a1, void* a2, void* a3,
    const void* caller, const void* target, std::uint64_t externalSequence)
{
    __stosb(reinterpret_cast<unsigned char*>(scope), 0, sizeof(*scope));
    scope->state = state;
    if (state) scope->observer = static_cast<const RizEngineWaitObserverV1*>(
        InterlockedCompareExchangePointer(&state->publishedObserver, nullptr, nullptr));
    auto* frame = &scope->frame;
    frame->abiVersion = 1; frame->byteSize = sizeof(*frame); frame->kind = kind;
    frame->slot = state ? static_cast<std::uint32_t>(state - states) : 0xffffffffu;
    frame->epoch = state ? state->epoch : 0; frame->sequence = Next(&sequenceSource);
    frame->externalSequence = externalSequence; frame->threadId = GetCurrentThreadId();
    frame->arg0 = a0; frame->arg1 = a1; frame->arg2 = a2; frame->arg3 = a3;
    frame->caller = caller; frame->target = target;
    if (kind >= RizEngineWaitNodeExecute && kind <= RizEngineWaitNodeDetach) frame->node = a0;
    else if (kind == RizEngineWaitSchedulerCallback) frame->node = a1;
    Push(scope);
    // An Attach during an older same-family physical call cannot lend its new
    // observer to descendants of that older call. Null is a frozen snapshot.
    if (scope->parent && scope->parent->state == state) scope->observer = scope->parent->observer;
    if (!frame->sequence) Fault(state, scope);
    if (Current(scope) && frame->depth && frame->depth <= depthLimit) frame->flags |= RizEngineWaitParentMatched;
    if (state) {
        InterlockedIncrement64(&state->entered); InterlockedIncrement64(&state->inFlight);
        if (frame->threadId == state->config.mainThreadId) frame->flags |= RizEngineWaitMainThread;
        else Fault(state, scope);
    }
}
void BeginOperation(Scope* scope, const RizEventMidFrameV1* machine, bool active)
{
    if (scope->operationActive) Fault(scope->state, scope);
    auto* op = &scope->operation;
    __stosb(reinterpret_cast<unsigned char*>(op), 0, sizeof(*op));
    const auto* frame = &scope->frame;
    op->abiVersion = 1; op->byteSize = sizeof(*op); op->slot = frame->slot;
    op->threadId = frame->threadId; op->depth = frame->depth; op->scopeKind = frame->kind;
    op->epoch = frame->epoch; op->sequence = frame->sequence; op->externalSequence = frame->externalSequence;
    op->parentSequence = frame->parentSequence; op->rootSequence = frame->rootSequence;
    op->operationSequence = Next(&sequenceSource); op->sourceSequence = frame->sourceSequence;
    op->flags = frame->flags & (RizEngineWaitMainThread | RizEngineWaitParentMatched);
    op->originalCaller = frame->caller;
    scope->sourceRsp = machine ? machine->gpr[RizEventMidRsp] : 0;
    scope->seen = 0; scope->operationActive = active;
    scope->callbackExpected = scope->callbackClaimed = scope->callbackCompleted = false;
    scope->cleanupExpected = scope->cleanupClaimed = scope->cleanupCompleted = false;
    if (!op->operationSequence) Fault(scope->state, scope);
}
void EmitPhase(Scope* scope, std::uint32_t kind, const void* relayReturnPc)
{
    RizEngineWaitPhaseFrameV1 event;
    __movsb(reinterpret_cast<unsigned char*>(&event), reinterpret_cast<const unsigned char*>(&scope->operation), sizeof(event));
    event.kind = kind; event.eventSequence = Next(&eventSource);
    event.sitePc = kind < 18 ? scope->state->config.sites[kind].sitePc : nullptr;
    event.relayReturnPc = relayReturnPc;
    if (!event.eventSequence) Fault(scope->state, scope);
    if (scope->unknown || InterlockedCompareExchange(&globalUnknown, 0, 0)) event.flags |= RizEngineWaitUnknown;
    ++scope->phaseCount; InterlockedIncrement64(&scope->state->phases);
    if (!scope->observer) return;
    scope->observerActive = true;
    __try { scope->observer->phase(&event, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(scope->state, scope); }
    scope->observerActive = false;
}
void MarkPhase(Scope* scope, std::uint32_t kind, const RizEventMidFrameV1* machine)
{
    if ((scope->seen & (std::uint64_t{1} << kind)) != 0) Fault(scope->state, scope);
    scope->seen |= std::uint64_t{1} << kind;
    EmitPhase(scope, kind, reinterpret_cast<const void*>(machine->returnPc));
}
bool Seen(const Scope* scope, std::uint32_t kind)
{ return (scope->seen & (std::uint64_t{1} << kind)) != 0; }
bool SameRsp(const Scope* scope, const RizEventMidFrameV1* machine)
{ return scope->sourceRsp == machine->gpr[RizEventMidRsp]; }

// Every actual boundary is a physical mask. A source may be claimed only from
// this exact direct original-phase parent, never by searching for a payload.
void ClaimSource(Scope* scope)
{
    auto* parent = scope->parent;
    if (!parent || !scope->state || parent->state != scope->state
        || parent->observer != scope->observer || parent->phase != 1 || parent->observerActive
        || !parent->operationActive || parent->unknown || !scope->frame.externalSequence) return;
    auto* frame = &scope->frame; auto* op = &parent->operation;
    const auto* config = &scope->state->config;
    bool valid = false;
    if (frame->kind == RizEngineWaitSchedulerCallback && parent->frame.kind == RizEngineWaitDispatch
        && parent->callbackExpected && !parent->callbackClaimed
        && frame->caller == Add(config->sites[RizEngineWaitDispatchCallbackArmed].sitePc, 0x0a)) {
        valid = frame->target == op->actualCallback && frame->arg0 == op->actualOwner && frame->arg1 == op->actualPayload;
        if (valid) { parent->callbackClaimed = true; op->stageFlags |= RizEngineWaitCallbackClaimed; }
    }
    else if (frame->kind == RizEngineWaitNodeRelease && parent->cleanupExpected && !parent->cleanupClaimed) {
        const auto* expectedCaller = parent->frame.kind == RizEngineWaitDispatch
            ? Add(config->sites[RizEngineWaitDispatchCleanupDecision].sitePc, 0x0b)
            : parent->frame.kind == RizEngineWaitBatch ? Add(config->sites[RizEngineWaitBatchObjectBegin].sitePc, 0x0d) : nullptr;
        if (expectedCaller && frame->caller == expectedCaller) {
            valid = frame->target == op->actualCleanup && frame->arg0 == op->actualPayload;
            if (valid) { parent->cleanupClaimed = true; op->stageFlags |= RizEngineWaitCleanupClaimed; }
        }
    }
    if (!valid) return;
    op->sourceExternalSequence = frame->externalSequence; op->originalCaller = frame->caller;
    frame->sourceSequence = parent->frame.sequence; frame->operationSequence = op->operationSequence;
    frame->wait = op->wait; frame->node = op->node; frame->tuple = op->tuple;
    frame->manager = op->manager; frame->index = op->index;
    frame->flags |= RizEngineWaitSnapshotCached
        | (op->flags & (RizEngineWaitSnapshotRequested | RizEngineWaitSnapshotValid | RizEngineWaitSnapshotFaulted
            | RizEngineWaitKnownCallback | RizEngineWaitCallbackExpected | RizEngineWaitCleanupExpected))
        | (parent->frame.kind == RizEngineWaitDispatch ? RizEngineWaitSourceDispatch : RizEngineWaitSourceBatch);
}
void FinishSource(Scope* scope, const RizEngineWaitCompletionV1* completion)
{
    auto* parent = scope->parent;
    if (!scope->frame.sourceSequence) return;
    if (!parent || parent->frame.sequence != scope->frame.sourceSequence || parent->state != scope->state
        || parent->phase != 1 || parent->observerActive || !parent->operationActive
        || parent->operation.operationSequence != scope->frame.operationSequence) { Fault(scope->state, scope); return; }
    const bool good = (completion->flags & (RizEngineWaitReturned | RizEngineWaitAbnormal | RizEngineWaitEnterFaulted
        | RizEngineWaitRawRaxValid | RizEngineWaitExitUnknown)) == (RizEngineWaitReturned | RizEngineWaitRawRaxValid);
    parent->operation.rawRax = completion->rawRax; parent->operation.completionFlags = completion->flags;
    if (scope->frame.kind == RizEngineWaitSchedulerCallback && parent->callbackClaimed && !parent->callbackCompleted) {
        if (good) { parent->callbackCompleted = true; parent->operation.stageFlags |= RizEngineWaitCallbackCompleted; }
        else Fault(parent->state, parent);
    }
    else if (scope->frame.kind == RizEngineWaitNodeRelease && parent->cleanupClaimed && !parent->cleanupCompleted) {
        if (good) { parent->cleanupCompleted = true; parent->operation.stageFlags |= RizEngineWaitCleanupCompleted; }
        else Fault(parent->state, parent);
    }
    else Fault(parent->state, parent);
}
void DeliverEnter(Scope* scope)
{
    if (!Current(scope) || scope->entered || scope->phase != 0) { Fault(scope->state, scope); return; }
    scope->entered = true;
    if (scope->nodeFrame) {
        const auto* source = scope->nodeFrame;
        scope->frame.node = source->node; scope->frame.nodeOwner = source->owner;
        scope->frame.enumHandle = source->enumHandle; scope->frame.enumCache = source->enumCache;
        scope->frame.enumType = source->enumType; scope->frame.referenceCount = source->referenceCount;
        scope->frame.nodeSnapshotFlags = source->snapshotFlags;
        if ((source->snapshotFlags & RizEngineNodeSnapshotFaulted) != 0) SnapshotFault(scope);
    }
    else if (scope->schedulerFrame) {
        const auto* source = scope->schedulerFrame;
        scope->frame.nodeOwner = source->nodeOwner; scope->frame.enumHandle = source->enumHandle;
        scope->frame.enumCache = source->enumCache; scope->frame.enumType = source->enumType;
        scope->frame.referenceCount = source->referenceCount; scope->frame.nodeSnapshotFlags = source->nodeSnapshotFlags;
        if ((source->flags & RizEngineSchedulerUnknown) || (source->nodeSnapshotFlags & RizEngineSchedulerSnapshotFaulted)) Fault(scope->state, scope);
    }
    if (scope->unknown || (scope->state && InterlockedCompareExchange(&globalUnknown, 0, 0))) scope->frame.flags |= RizEngineWaitUnknown;
    if (!scope->observer) return;
    scope->observerActive = true;
    __try { scope->token = scope->observer->enter(&scope->frame, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(scope->state, scope); }
    scope->observerActive = false;
}
void FinishOperation(Scope* scope, RizEngineWaitCompletionV1* completion)
{
    if (scope->operationActive || (scope->callbackExpected && !scope->callbackCompleted) || (scope->cleanupExpected && !scope->cleanupCompleted)) {
        Fault(scope->state, scope); completion->flags |= RizEngineWaitMissingPhase;
        if (scope->state) {
            InterlockedIncrement64(&scope->state->unmatched); scope->operation.stageFlags |= RizEngineWaitMissingRequiredPhase;
            EmitPhase(scope, RizEngineWaitPhaseAbnormal, nullptr);
        }
        scope->operationActive = false;
    }
    completion->phases = scope->phaseCount;
}
void DeliverExit(Scope* scope, RizEngineWaitCompletionV1* completion)
{
    if (!Current(scope) || !scope->entered || scope->completed || scope->phase != 2) { Fault(scope->state, scope); return; }
    scope->completed = true;
    if ((completion->flags & RizEngineWaitAbnormal) != 0) Fault(scope->state, scope);
    FinishOperation(scope, completion);
    if (scope->unknown || (scope->state && InterlockedCompareExchange(&globalUnknown, 0, 0))) completion->flags |= RizEngineWaitExitUnknown;
    FinishSource(scope, completion);
    if (!scope->observer) return;
    scope->observerActive = true;
    __try { scope->observer->exit(&scope->frame, scope->token, completion, scope->observer->user); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(scope->state, scope); }
    scope->observerActive = false;
}
void Retire(Scope* scope)
{
    if (!scope->completed) Fault(scope->state, scope);
    PopScope(scope);
    if (scope->state) { InterlockedIncrement64(&scope->state->exited); InterlockedDecrement64(&scope->state->inFlight); }
}
void ReadWaitInput(Scope* scope)
{
    auto* frame = &scope->frame;
    if (frame->kind == RizEngineWaitYield) {
        frame->node = frame->arg0; frame->nodeSnapshotFlags = RizEngineWaitSnapshotRequested;
        if (CopyNode(frame->node, &frame->nodeOwner, &frame->enumHandle, &frame->enumCache, &frame->enumType, &frame->referenceCount)) frame->nodeSnapshotFlags |= RizEngineWaitSnapshotValid;
        else { frame->nodeSnapshotFlags |= RizEngineWaitSnapshotFaulted; SnapshotFault(scope); }
    }
    else if (frame->kind == RizEngineWaitDispatch) {
        frame->wait = frame->arg0; frame->flags |= RizEngineWaitSnapshotRequested;
        if (CopyTuple(frame->wait, &frame->tuple)) frame->flags |= RizEngineWaitSnapshotValid;
        else { frame->flags |= RizEngineWaitSnapshotFaulted; SnapshotFault(scope); }
        // A null actual callback skips the complete owner/callback/cleanup block.
        BeginOperation(scope, nullptr, frame->tuple.callback != nullptr);
        frame->operationSequence = scope->operation.operationSequence;
        scope->operation.wait = frame->wait; scope->operation.tuple = frame->tuple;
        scope->operation.flags |= frame->flags & (RizEngineWaitSnapshotRequested | RizEngineWaitSnapshotValid | RizEngineWaitSnapshotFaulted);
    }
    else frame->manager = frame->arg0;
}
__declspec(noinline) std::uint64_t Dispatch(State* state, std::uint32_t kind, void* a0, void* a1, void* a2, void* a3,
    const void* caller, DWORD incomingError)
{
    Scope scope; Init(&scope, state, kind, a0, a1, a2, a3, caller, state->config.targets[kind], 0);
    ReadWaitInput(&scope); SetLastError(incomingError); DeliverEnter(&scope);
    RizEngineWaitCompletionV1 completion{}; completion.abiVersion = 1; completion.byteSize = sizeof(completion);
    if (scope.unknown) completion.flags |= RizEngineWaitEnterFaulted;
    std::uint64_t raw = 0; bool returned = false; DWORD outgoingError = incomingError;
    scope.phase = 1; SetLastError(incomingError);
    __try {
        raw = reinterpret_cast<RawOriginal>(state->config.originals[kind])(a0, a1, a2, a3);
        outgoingError = GetLastError(); returned = true;
    }
    __finally {
        if (!returned) outgoingError = GetLastError();
        scope.phase = 2;
        completion.flags |= returned ? RizEngineWaitReturned | RizEngineWaitRawRaxValid : RizEngineWaitAbnormal;
        if (returned) completion.rawRax = raw;
        SetLastError(outgoingError); DeliverExit(&scope, &completion); Retire(&scope); SetLastError(outgoingError);
    }
    return raw;
}
template<std::size_t Kind, std::size_t Slot>
__declspec(noinline) std::uint64_t __cdecl Entry(void* a0, void* a1, void* a2, void* a3)
{
    const DWORD incomingError = GetLastError(); auto* state = &states[Slot];
    if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) RaiseFailFastException(nullptr, nullptr, 0);
    return Dispatch(state, static_cast<std::uint32_t>(Kind), a0, a1, a2, a3, _ReturnAddress(), incomingError);
}
template<std::size_t Kind, std::size_t... Slots> constexpr auto Entries(std::index_sequence<Slots...>)
{ return std::array<RawOriginal, sizeof...(Slots)>{ &Entry<Kind, Slots>... }; }
constexpr auto yieldEntries = Entries<RizEngineWaitYield>(std::make_index_sequence<limit>{});
constexpr auto dispatchEntries = Entries<RizEngineWaitDispatch>(std::make_index_sequence<limit>{});
constexpr auto batchEntries = Entries<RizEngineWaitBatch>(std::make_index_sequence<limit>{});
bool PhaseKind(std::uint32_t kind, std::uint32_t site)
{
    if (site < 4) return kind == RizEngineWaitYield;
    if (site < 6) return kind == RizEngineWaitNodeDetach;
    if (site < 9) return kind == RizEngineWaitNodeRelease;
    if (site < 12) return kind == RizEngineWaitNodeStart;
    if (site < 16) return kind == RizEngineWaitDispatch;
    return kind == RizEngineWaitBatch;
}
void ApplyPhase(Scope* scope, std::uint32_t site, const RizEventMidFrameV1* machine)
{
    auto* op = &scope->operation;
    auto reg = [machine](RizEventMidGprV1 index) { return reinterpret_cast<void*>(machine->gpr[index]); };
    switch (site) {
    case RizEngineWaitPublishBegin:
        BeginOperation(scope, machine, true); op->wait = reg(RizEventMidRdi); op->node = reg(RizEventMidR14);
        if (!op->wait || op->node != scope->frame.arg0) Fault(scope->state, scope);
        SnapshotOperationTuple(scope); SnapshotOperationNode(scope); op->stageFlags = RizEngineWaitBeforeWrite;
        op->actualCallback = scope->state->config.schedulerTargets[5]; op->actualCleanup = scope->state->config.nodeTargets[2];
        op->actualPayload = op->node; op->rawRax = static_cast<std::uint32_t>(machine->gpr[RizEventMidRbx]);
        break;
    case RizEngineWaitOldReferenceDecision:
        if (!Seen(scope, RizEngineWaitPublishBegin) || !SameRsp(scope, machine)) Fault(scope->state, scope);
        op->oldWait = reg(RizEventMidRcx);
        if ((op->oldWait == nullptr) != ((machine->rflags & 0x40) != 0)) Fault(scope->state, scope);
        if (!op->oldWait) op->stageFlags |= RizEngineWaitZeroOldReference;
        break;
    case RizEngineWaitOldReferenceReturned:
        if (!Seen(scope, RizEngineWaitOldReferenceDecision) || !SameRsp(scope, machine)
            || reg(RizEventMidRdi) != op->wait || reg(RizEventMidR14) != op->node) Fault(scope->state, scope);
        if (op->oldWait) op->stageFlags |= RizEngineWaitSharedReferenceReturned;
        break;
    case RizEngineWaitPublishCommitted:
        if (!Seen(scope, RizEngineWaitOldReferenceReturned) || !SameRsp(scope, machine)
            || reg(RizEventMidRdi) != op->wait || reg(RizEventMidR14) != op->node) Fault(scope->state, scope);
        // The old wait is never read here. The new wait remains retained and
        // these four publication writes have actually executed before this PC.
        SnapshotOperationTuple(scope, true);
        if (op->tuple.callback != scope->state->config.schedulerTargets[5]
            || op->tuple.cleanup != scope->state->config.nodeTargets[2] || op->tuple.payload != op->node) Fault(scope->state, scope);
        op->stageFlags |= RizEngineWaitFieldsWritten | RizEngineWaitCommitted; scope->operationActive = false;
        break;
    case RizEngineWaitDetachOwnerBegin: case RizEngineWaitDetachNestedOwnerBegin:
        BeginOperation(scope, machine, false); op->wait = reg(RizEventMidRax); op->node = reg(RizEventMidRdi);
        if (!op->wait || !op->node || machine->gpr[RizEventMidRbp] != 0
            || (site == RizEngineWaitDetachOwnerBegin && op->node != scope->frame.arg0)) Fault(scope->state, scope);
        SnapshotOperationTuple(scope); SnapshotOperationNode(scope); op->stageFlags = RizEngineWaitBeforeWrite;
        break;
    case RizEngineWaitReleaseClearBegin: case RizEngineWaitStartClearBegin:
        BeginOperation(scope, machine, true); op->wait = reg(RizEventMidRax); op->node = reg(RizEventMidRbx);
        if (!op->wait || !op->node || machine->gpr[RizEventMidRdi] != 0
            || (site == RizEngineWaitReleaseClearBegin && op->node != scope->frame.arg0)) Fault(scope->state, scope);
        SnapshotOperationTuple(scope); SnapshotOperationNode(scope); op->stageFlags = RizEngineWaitBeforeWrite;
        break;
    case RizEngineWaitReleaseFieldsCleared: case RizEngineWaitStartFieldsCleared:
        if (!Seen(scope, site - 1) || !SameRsp(scope, machine) || reg(RizEventMidRbx) != op->node
            || machine->gpr[RizEventMidRdi] != 0) Fault(scope->state, scope);
        // All four clears precede this PC; the shared-ref CALL has not run.
        SnapshotOperationTuple(scope, true);
        if (op->tuple.callback || op->tuple.cleanup || op->tuple.payload || op->tuple.ownerId) Fault(scope->state, scope);
        op->stageFlags |= RizEngineWaitFieldsWritten;
        break;
    case RizEngineWaitReleaseReferenceReturned: case RizEngineWaitStartReferenceReturned:
        if (!Seen(scope, site - 1) || !SameRsp(scope, machine) || reg(RizEventMidRbx) != op->node
            || machine->gpr[RizEventMidRdi] != 0) Fault(scope->state, scope);
        // The wait may be freed: keep only cached wait/tuple/node identity.
        op->flags |= RizEngineWaitSnapshotCached;
        op->stageFlags |= RizEngineWaitSharedReferenceReturned | RizEngineWaitBeforeWrite; scope->operationActive = false;
        break;
    case RizEngineWaitDispatchOwnerResult:
        if (!scope->operationActive || reg(RizEventMidRdi) != op->wait) Fault(scope->state, scope);
        scope->sourceRsp = machine->gpr[RizEventMidRsp]; op->actualOwner = reg(RizEventMidRax);
        if (!op->actualOwner) op->stageFlags |= RizEngineWaitOwnerNull;
        break;
    case RizEngineWaitDispatchCallbackArmed:
        if (!Seen(scope, RizEngineWaitDispatchOwnerResult) || !SameRsp(scope, machine)
            || !op->actualOwner || reg(RizEventMidRdi) != op->wait || reg(RizEventMidRax) != op->actualOwner) Fault(scope->state, scope);
        SnapshotOperationTuple(scope); op->actualCallback = op->tuple.callback; op->actualPayload = op->tuple.payload; op->node = op->actualPayload;
        if (op->actualCallback == scope->state->config.schedulerTargets[5]) {
            scope->callbackExpected = true; op->flags |= RizEngineWaitCallbackExpected; SnapshotOperationNode(scope);
        }
        break;
    case RizEngineWaitDispatchCleanupDecision:
        if (!Seen(scope, RizEngineWaitDispatchOwnerResult) || !SameRsp(scope, machine)
            || reg(RizEventMidRdi) != op->wait || (scope->callbackExpected && !scope->callbackCompleted)) Fault(scope->state, scope);
        SnapshotOperationTuple(scope); op->actualCleanup = reg(RizEventMidRax); op->actualPayload = op->tuple.payload;
        op->actualCallback = op->tuple.callback; op->node = op->actualPayload;
        if (op->tuple.cleanup != op->actualCleanup) Fault(scope->state, scope);
        if (op->actualCleanup && op->actualCleanup == scope->state->config.nodeTargets[2]) {
            scope->cleanupExpected = true; op->flags |= RizEngineWaitCleanupExpected; SnapshotOperationNode(scope);
        }
        break;
    case RizEngineWaitDispatchClearReached:
        if (!Seen(scope, RizEngineWaitDispatchCleanupDecision) || !SameRsp(scope, machine)
            || reg(RizEventMidRdi) != op->wait || (scope->cleanupExpected && !scope->cleanupCompleted)) Fault(scope->state, scope);
        // This is before the callback-slot store. Outer normal completion is
        // still required; cleanup may already have destroyed its payload node.
        op->flags |= RizEngineWaitSnapshotCached;
        op->stageFlags |= RizEngineWaitBeforeWrite | RizEngineWaitCallbackSlotClearReached; scope->operationActive = false;
        break;
    case RizEngineWaitBatchObjectBegin:
        BeginOperation(scope, machine, true); op->wait = reg(RizEventMidRbp); op->manager = reg(RizEventMidRsi);
        op->index = machine->gpr[RizEventMidRdi];
        if (!op->wait || op->manager != scope->frame.arg0 || machine->gpr[RizEventMidR14] != 0) Fault(scope->state, scope);
        SnapshotOperationTuple(scope); op->actualCleanup = op->tuple.cleanup; op->actualPayload = op->tuple.payload;
        op->actualCallback = op->tuple.callback; op->node = op->tuple.payload;
        if (op->tuple.callback && op->tuple.cleanup == scope->state->config.nodeTargets[2]) {
            scope->cleanupExpected = true; op->flags |= RizEngineWaitCleanupExpected; SnapshotOperationNode(scope);
        }
        break;
    case RizEngineWaitBatchObjectEnd:
        if (!Seen(scope, RizEngineWaitBatchObjectBegin) || !SameRsp(scope, machine)
            || reg(RizEventMidRbp) != op->wait || reg(RizEventMidRsi) != op->manager
            || machine->gpr[RizEventMidRdi] != op->index || machine->gpr[RizEventMidR14] != 0
            || (scope->cleanupExpected && !scope->cleanupCompleted)) Fault(scope->state, scope);
        op->flags |= RizEngineWaitSnapshotCached; op->stageFlags |= RizEngineWaitSharedReferenceReturned;
        if (op->tuple.callback) op->stageFlags |= RizEngineWaitCallbackSlotClearReached;
        scope->operationActive = false;
        break;
    default: Fault(scope->state, scope); return;
    }
    MarkPhase(scope, site, machine);
}
bool IsEntry(const void* value)
{
    if (value == reinterpret_cast<const void*>(&RizEngineWaitPhase_Entry)) return true;
    for (std::uint32_t slot = 0; slot < limit; ++slot)
        for (std::uint32_t kind = 0; kind < 3; ++kind) if (value == RizEngineWaitContext_GetEntry(kind, slot)) return true;
    return false;
}
Scope* From(RizEngineWaitPrivate::BoundaryStorage* storage) { return reinterpret_cast<Scope*>(storage); }
}

namespace RizEngineWaitPrivate {
void BeginNode(BoundaryStorage* storage, const RizEngineNodeFrameV1* frame, const void* target)
{
    const DWORD error = GetLastError(); auto* scope = From(storage); const auto kind = frame->kind + 3;
    Init(scope, Select(kind, target), kind, frame->arg0, frame->arg1, frame->arg2, frame->arg3, frame->caller, target, frame->sequence);
    scope->nodeFrame = frame; ClaimSource(scope); SetLastError(error);
}
void BeginScheduler(BoundaryStorage* storage, const RizEngineSchedulerFrameV1* frame, const void* target)
{
    const DWORD error = GetLastError(); auto* scope = From(storage); const auto kind = frame->kind + 7;
    Init(scope, Select(kind, target), kind, frame->arg0, frame->arg1, frame->arg2, frame->arg3, frame->caller, target, frame->sequence);
    scope->schedulerFrame = frame; ClaimSource(scope); SetLastError(error);
}
void Entered(BoundaryStorage* storage) { const DWORD error = GetLastError(); DeliverEnter(From(storage)); SetLastError(error); }
void Original(BoundaryStorage* storage)
{
    const DWORD error = GetLastError(); auto* scope = From(storage);
    if (!Current(scope) || !scope->entered || scope->phase != 0 || scope->observerActive) Fault(scope->state, scope);
    scope->phase = 1; SetLastError(error);
}
void Closing(BoundaryStorage* storage)
{
    const DWORD error = GetLastError(); auto* scope = From(storage);
    if (!Current(scope) || scope->phase == 2 || scope->completed) Fault(scope->state, scope);
    scope->phase = 2; SetLastError(error);
}
void CompleteNode(BoundaryStorage* storage, const RizEngineNodeCompletionV1* actual)
{
    const DWORD error = GetLastError(); auto* scope = From(storage);
    RizEngineWaitCompletionV1 completion{}; completion.abiVersion = 1; completion.byteSize = sizeof(completion);
    completion.flags = actual->flags & 15u; completion.outputFlags = actual->outputFlags;
    completion.rawRax = actual->returnByte; completion.externalFlags = actual->flags;
    completion.externalRawReturn = actual->returnByte; completion.outputNode = actual->startOutputNode;
    if ((actual->outputFlags & RizEngineNodeSnapshotFaulted) != 0) Fault(scope->state, scope);
    DeliverExit(scope, &completion); SetLastError(error);
}
void CompleteScheduler(BoundaryStorage* storage, const RizEngineSchedulerCompletionV1* actual)
{
    const DWORD error = GetLastError(); auto* scope = From(storage);
    RizEngineWaitCompletionV1 completion{}; completion.abiVersion = 1; completion.byteSize = sizeof(completion);
    completion.flags = actual->flags & 15u; completion.rawRax = actual->rawRax;
    completion.externalFlags = actual->flags; completion.externalRawReturn = static_cast<std::uint32_t>(actual->rawRax);
    if ((actual->flags & RizEngineSchedulerExitUnknown) != 0) Fault(scope->state, scope);
    DeliverExit(scope, &completion); SetLastError(error);
}
void Pop(BoundaryStorage* storage) { const DWORD error = GetLastError(); Retire(From(storage)); SetLastError(error); }
bool CanUseSchedulerSource(BoundaryStorage* storage, const RizEngineSchedulerFrameV1* actualSource, bool requireDirect)
{
    const DWORD error = GetLastError(); const auto* scope = From(storage); bool result = false;
    if (actualSource && Current(scope) && scope->schedulerFrame && !scope->observerActive && (scope->phase == 0 || scope->phase == 2)) {
        for (auto* at = scope->parent; at; at = at->parent) {
            if (!at->schedulerFrame || at->phase != 1 || at->observerActive) break;
            if (at->schedulerFrame == actualSource) { result = true; break; }
            if (requireDirect) break;
        }
    }
    SetLastError(error); return result;
}
}

extern "C" void __cdecl RizEngineWaitPhase_Dispatch(const RizEventMidFrameV1* machine)
{
    const DWORD incomingError = GetLastError(); auto* scope = Top(FindThread(GetCurrentThreadId()));
    State* selected = nullptr; std::uint32_t site = 18;
    __try {
        if (machine && machine->abiVersion == 1 && machine->byteSize == sizeof(*machine) && !machine->reserved0 && !machine->reserved1) {
            for (auto& state : states) {
                if (InterlockedCompareExchange(&state.configured, 0, 0) != 1) continue;
                for (std::uint32_t i = 0; i < 18; ++i)
                    if (state.config.sites[i].relayReturnPc == reinterpret_cast<const void*>(machine->returnPc)) { selected = &state; site = i; break; }
                if (selected) break;
            }
        }
        if (!selected || !scope || scope->state != selected || !Current(scope) || scope->phase != 1 || scope->observerActive
            || !scope->entered || scope->completed || !scope->frame.sequence || !scope->frame.depth || scope->frame.depth > depthLimit
            || !PhaseKind(scope->frame.kind, site)) {
            Fault(selected, scope); if (scope && scope->state != selected) Fault(scope->state, scope);
            if (selected) InterlockedIncrement64(&selected->unmatched);
        }
        else { SetLastError(incomingError); ApplyPhase(scope, site, machine); }
    }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(selected, scope); if (scope && scope->state != selected) Fault(scope->state, scope); }
    SetLastError(incomingError);
}

RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetAbiVersion() { return 1; }
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetLimit() { return static_cast<std::uint32_t>(limit); }
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetFrameSize() { return sizeof(RizEngineWaitFrameV1); }
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetCompletionSize() { return sizeof(RizEngineWaitCompletionV1); }
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetPhaseFrameSize() { return sizeof(RizEngineWaitPhaseFrameV1); }
RIZEWC_EXPORT void* __cdecl RizEngineWaitContext_GetEntry(std::uint32_t kind, std::uint32_t slot)
{
    if (kind >= 3 || slot >= limit) return nullptr;
    const RawOriginal entries[] = { yieldEntries[slot], dispatchEntries[slot], batchEntries[slot] }; return reinterpret_cast<void*>(entries[kind]);
}
RIZEWC_EXPORT void* __cdecl RizEngineWaitContext_GetPhaseEntry() { return reinterpret_cast<void*>(&RizEngineWaitPhase_Entry); }
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_Configure(std::uint32_t slot, const RizEngineWaitConfigV1* supplied)
{
    const DWORD error = GetLastError();
    RizEngineWaitConfigV1 config;
    __stosb(reinterpret_cast<unsigned char*>(&config), 0, sizeof(config));
    bool valid = false;
    __try { if (supplied) { config = *supplied; valid = true; } }
    __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (!valid || slot >= limit || config.abiVersion != 1 || config.byteSize != sizeof(config) || !config.mainThreadId || config.reserved) { SetLastError(error); return 1; }
    const void* identities[16]{};
    for (std::uint32_t i = 0; i < 3; ++i) { identities[i] = config.targets[i]; identities[i + 3] = config.originals[i]; }
    for (std::uint32_t i = 0; i < 4; ++i) identities[i + 6] = config.nodeTargets[i];
    for (std::uint32_t i = 0; i < 6; ++i) identities[i + 10] = config.schedulerTargets[i];
    for (std::uint32_t i = 0; i < 16; ++i) {
        if (!identities[i] || IsEntry(identities[i])) { SetLastError(error); return 1; }
        for (std::uint32_t j = 0; j < i; ++j) if (identities[i] == identities[j]) { SetLastError(error); return 1; }
    }
    for (std::uint32_t i = 0; i < 18; ++i) {
        if (!config.sites[i].sitePc || !config.sites[i].relayReturnPc) { SetLastError(error); return 1; }
        for (std::uint32_t j = 0; j < i; ++j)
            if (config.sites[i].sitePc == config.sites[j].sitePc || config.sites[i].relayReturnPc == config.sites[j].relayReturnPc) { SetLastError(error); return 1; }
    }
    AcquireSRWLockExclusive(&configurationLock); auto* state = &states[slot]; std::uint32_t result = 0;
    if (InterlockedCompareExchange(&state->configured, 0, 0)) result = 2;
    else {
        for (auto& other : states) {
            if (!InterlockedCompareExchange(&other.configured, 0, 0)) continue;
            const void* old[16]{};
            for (std::uint32_t i = 0; i < 3; ++i) { old[i] = other.config.targets[i]; old[i + 3] = other.config.originals[i]; }
            for (std::uint32_t i = 0; i < 4; ++i) old[i + 6] = other.config.nodeTargets[i];
            for (std::uint32_t i = 0; i < 6; ++i) old[i + 10] = other.config.schedulerTargets[i];
            for (const auto* item : old) for (const auto* identity : identities) if (item == identity) result = 3;
            for (const auto& oldSite : other.config.sites) for (const auto& newSite : config.sites)
                if (oldSite.sitePc == newSite.sitePc || oldSite.relayReturnPc == newSite.relayReturnPc) result = 3;
        }
        if (!result) {
            InterlockedExchange(&state->configured, -1); state->config = config; state->epoch = Next(&epochSource);
            if (!state->epoch) { Fault(state); InterlockedExchange(&state->configured, -2); result = 4; }
            else InterlockedExchange(&state->configured, 1);
        }
    }
    ReleaseSRWLockExclusive(&configurationLock); SetLastError(error); return result;
}
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_Attach(std::uint32_t slot, std::uint64_t epoch, const RizEngineWaitObserverV1* supplied)
{
    const DWORD error = GetLastError(); RizEngineWaitObserverV1 observer{}; bool valid = false;
    __try { if (supplied) { observer = *supplied; valid = true; } }
    __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (!valid || slot >= limit || observer.abiVersion != 1 || observer.byteSize != sizeof(observer) || !observer.enter || !observer.exit || !observer.phase || !epoch) { SetLastError(error); return 1; }
    auto* state = &states[slot]; std::uint32_t result = 0;
    if (InterlockedCompareExchange(&globalUnknown, 0, 0) || InterlockedCompareExchange(&state->unknown, 0, 0)) result = 5;
    else if (InterlockedCompareExchange(&state->configured, 0, 0) != 1) result = 2;
    else if (state->epoch != epoch) result = 3;
    else if (InterlockedCompareExchange(&state->attachAttempted, 1, 0)) result = 4;
    else { state->observer = observer; InterlockedExchangePointer(&state->publishedObserver, &state->observer); }
    SetLastError(error); return result;
}
RIZEWC_EXPORT std::uint32_t __cdecl RizEngineWaitContext_GetInfo(std::uint32_t slot, RizEngineWaitInfoV1* supplied)
{
    const DWORD error = GetLastError(); bool valid = false;
    __try { valid = supplied && supplied->abiVersion == 1 && supplied->byteSize == sizeof(*supplied); }
    __except (EXCEPTION_EXECUTE_HANDLER) { valid = false; }
    if (!valid || slot >= limit) { SetLastError(error); return 1; }
    auto* state = &states[slot]; const auto configured = InterlockedCompareExchange(&state->configured, 0, 0);
    RizEngineWaitInfoV1 info{}; info.abiVersion = 1; info.byteSize = sizeof(info); info.slot = slot;
    info.configured = configured == 1 ? 1u : 0u;
    info.attached = InterlockedCompareExchangePointer(&state->publishedObserver, nullptr, nullptr) ? 1u : 0u;
    info.unknown = configured < 0 || InterlockedCompareExchange(&globalUnknown, 0, 0) || InterlockedCompareExchange(&state->unknown, 0, 0) ? 1u : 0u;
    info.phaseEntry = RizEngineWaitContext_GetPhaseEntry();
    if (configured == 1) {
        info.epoch = state->epoch; info.mainThreadId = state->config.mainThreadId;
        for (std::uint32_t i = 0; i < 3; ++i) { info.targets[i] = state->config.targets[i]; info.originals[i] = state->config.originals[i]; }
    }
    for (std::uint32_t i = 0; i < 3; ++i) info.entries[i] = RizEngineWaitContext_GetEntry(i, slot);
    info.entered = Count(&state->entered); info.exited = Count(&state->exited); info.inFlight = Count(&state->inFlight);
    info.faults = Count(&state->faults); info.snapshotFaults = Count(&state->snapshotFaults); info.phases = Count(&state->phases); info.unmatched = Count(&state->unmatched);
    std::uint32_t result = configured == 0 ? 2u : configured != 1 ? 3u : 0u;
    __try { __movsb(reinterpret_cast<unsigned char*>(supplied), reinterpret_cast<const unsigned char*>(&info), sizeof(info)); }
    __except (EXCEPTION_EXECUTE_HANDLER) { Fault(state); result = 1; }
    SetLastError(error); return result;
}
#endif
