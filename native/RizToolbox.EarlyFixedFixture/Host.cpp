#include "EarlyFixedShared.h"
#include "ImporterApi.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include <cstdio>
#include <cstring>

extern "C" IMAGE_DOS_HEADER __ImageBase;
namespace {
constexpr std::uintptr_t TokenMask = 0x9123456789abcdefull;
volatile LONG64 hostChecks{}, hostFailures{}, observerEnters[4]{}, observerExits[4]{};
struct ObserverOwner { std::uint32_t kind; } owners[4]{};
struct Observation {
    OwnedEarlyCallV2 expectation;
    RizEngineNodeFrameV1 frame;
    std::uintptr_t token;
};
struct ThreadObservations { unsigned depth; Observation stack[8]; };
__declspec(thread) ThreadObservations observations;
struct WorkerPlan { HANDLE reached, release; bool blockAfterCalls; };

std::uint64_t Module() { return reinterpret_cast<std::uint64_t>(&__ImageBase); }
std::uint64_t AtomicRead(volatile LONG64* source)
{ return static_cast<std::uint64_t>(InterlockedCompareExchange64(source, 0, 0)); }
void Check(bool okay, const char* expression, unsigned line)
{
    InterlockedIncrement64(&hostChecks);
    if (okay) return;
    InterlockedIncrement64(&hostFailures);
    std::fprintf(stderr, "EARLY FIXED HOST FAILED line %u: %s\n", line, expression);
    RizEarlyRecorder_Event(RizEarlyPhaseEnd, RizEarlyHostPhase, 4, line, 1, 0,
        Module(), 0, 0, 0, 0, AtomicRead(&hostChecks), AtomicRead(&hostFailures), 0);
    RizEarlyRecorder_Fail(RizEarlyHostAssertion, 1);
}
#define CHECK(value) Check(!!(value), #value, __LINE__)
void RecordObserver(std::uint32_t type, const OwnedEarlyCallV2& call,
    const RizEngineNodeFrameV1* frame, std::uint32_t result, std::uint32_t flags,
    std::uint64_t value0, std::uint64_t value1)
{
    const auto* tuple = RizEarlyProxy_Inspect(call.kind);
    CHECK(tuple != nullptr);
    RizEarlyRecorder_Event(type, call.phase, call.kind, call.nested, result, flags,
        Module(), tuple->target, tuple->original, tuple->entry, call.callId,
        value0, value1, reinterpret_cast<std::uint64_t>(frame->caller));
}
std::uintptr_t __cdecl ObserveEnter(const RizEngineNodeFrameV1* frame, void* user)
{
    const auto* call = OwnedEarlyGetCurrent();
    CHECK(call && call->version == RizEarlyWorkloadVersion && call->byteSize == sizeof(*call));
    CHECK(frame && frame->abiVersion == 1 && frame->byteSize == sizeof(*frame));
    CHECK(call->kind < 4 && user == &owners[call->kind] && owners[call->kind].kind == call->kind);
    CHECK(frame->kind == call->kind && frame->arg0 == call->args[0] && frame->arg1 == call->args[1]
        && frame->arg2 == call->args[2] && frame->arg3 == call->args[3]);
    CHECK(frame->sequence != 0 && frame->caller && frame->reserved0 == 0 && frame->reserved1 == 0);
    if (call->kind == 0) {
        CHECK(frame->snapshotFlags == 0 && !frame->node && !frame->owner && !frame->enumHandle
            && !frame->enumCache && frame->enumType == 0 && frame->referenceCount == 0);
    } else {
        CHECK(frame->snapshotFlags == 3 && frame->node == call->node && frame->owner == call->owner
            && frame->enumHandle == call->enumHandle && frame->enumCache == call->enumCache
            && frame->enumType == call->enumType && frame->referenceCount == call->referenceCount);
    }
    CHECK(observations.depth < 8);
    auto& observation = observations.stack[observations.depth++];
    observation.expectation = *call; observation.frame = *frame;
    observation.token = static_cast<std::uintptr_t>(call->callId) ^ TokenMask;
    CHECK(observation.token != 0);
    InterlockedIncrement64(&observerEnters[call->kind]);
    RecordObserver(RizEarlyObserverEnter, *call, frame, frame->snapshotFlags, call->faultMode,
        reinterpret_cast<std::uint64_t>(frame->arg0), reinterpret_cast<std::uint64_t>(frame->arg1));
    RecordObserver(RizEarlyObserverArguments, *call, frame, 0, call->faultMode,
        reinterpret_cast<std::uint64_t>(frame->arg2), reinterpret_cast<std::uint64_t>(frame->arg3));
    RecordObserver(RizEarlyObserverSnapshot, *call, frame, static_cast<std::uint32_t>(frame->referenceCount),
        frame->snapshotFlags, reinterpret_cast<std::uint64_t>(frame->owner), frame->enumHandle);
    RecordObserver(RizEarlyObserverIdentity, *call, frame, 0, call->faultMode,
        frame->sequence, observation.token);
    return observation.token;
}
void __cdecl ObserveExit(const RizEngineNodeFrameV1* frame, std::uintptr_t token,
    const RizEngineNodeCompletionV1* completion, void* user)
{
    CHECK(observations.depth != 0);
    auto& observation = observations.stack[observations.depth - 1];
    const auto& call = observation.expectation;
    CHECK(OwnedEarlyGetCurrent() && OwnedEarlyGetCurrent()->callId == call.callId);
    CHECK(frame && std::memcmp(frame, &observation.frame, sizeof(*frame)) == 0);
    CHECK(user == &owners[call.kind] && token == observation.token);
    CHECK(completion && completion->abiVersion == 1 && completion->byteSize == sizeof(*completion)
        && completion->reserved == 0);
    const auto expectedFlags = call.faultMode ? 2u : call.kind == 3 ? 1u : 9u;
    CHECK(completion->flags == expectedFlags);
    CHECK(completion->returnByte == (!call.faultMode && call.kind != 3 ? call.rawReturn : 0u));
    if (!call.faultMode && call.kind == 0)
        CHECK(completion->outputFlags == 3 && completion->startOutputNode == call.node);
    else CHECK(completion->outputFlags == 0 && completion->startOutputNode == nullptr);
    // Only the saved scalar expectation/frame is examined here. No node or
    // output slot is dereferenced by the observer after the original returns.
    RecordObserver(RizEarlyObserverExit, call, frame, completion->flags, call.faultMode,
        completion->returnByte, reinterpret_cast<std::uint64_t>(completion->startOutputNode));
    RecordObserver(RizEarlyObserverCompletion, call, frame, 0, completion->outputFlags,
        frame->sequence, token);
    InterlockedIncrement64(&observerExits[call.kind]); --observations.depth;
}
OwnedEarlySnapshotV2 Snapshot()
{
    OwnedEarlySnapshotV2 snapshot{RizEarlyWorkloadVersion, sizeof(snapshot)};
    CHECK(OwnedEarlyGetSnapshot(&snapshot) == 0);
    CHECK(snapshot.initializerMask == 0x1e && snapshot.initializedPhases == 4 && snapshot.failures == 0);
    return snapshot;
}
void CheckInfo(bool attached, bool originalExceptions, const OwnedEarlySnapshotV2& snapshot)
{
    for (unsigned kind = 0; kind != 4; ++kind) {
        const auto* tuple = RizEarlyProxy_Inspect(kind); CHECK(tuple != nullptr);
        RizEngineNodeInfoV1 information{1, sizeof(information)};
        CHECK(RizEngineNodeContext_GetInfo(kind, RizEarlySlot, &information) == 0);
        CHECK(information.kind == kind && information.slot == RizEarlySlot
            && information.target == OwnedEarlyGetTarget(kind)
            && reinterpret_cast<std::uint64_t>(information.target) == tuple->target
            && reinterpret_cast<std::uint64_t>(information.original) == tuple->original
            && reinterpret_cast<std::uint64_t>(information.entry) == tuple->entry
            && information.epoch == tuple->epoch && information.epoch != 0);
        CHECK(information.attached == (attached ? 1u : 0u));
        CHECK(information.unknown == (originalExceptions ? 1u : 0u));
        CHECK(information.entered == snapshot.originals[kind] && information.exited == information.entered
            && information.inFlight == 0 && information.enterFaults == 0 && information.exitFaults == 0
            && information.snapshotFaults == 0 && information.outputFaults == 0);
        CHECK(AtomicRead(&observerEnters[kind]) == AtomicRead(&observerExits[kind]));
        if (!attached) CHECK(AtomicRead(&observerEnters[kind]) == 0);
    }
}
void Phase(std::uint32_t phase, std::uint32_t faultKind = 4, std::uint32_t faultMode = 0)
{
    OwnedEarlyPhaseReportV2 report{RizEarlyWorkloadVersion, sizeof(report)};
    CHECK(OwnedEarlyRunPhase(phase, faultKind, faultMode, &report) == 0);
    CHECK(report.phase == phase && report.faultKind == faultKind && report.faultMode == faultMode
        && report.phaseId != 0 && report.checks != 0 && report.failures == 0);
    CHECK(report.originals[0] == 1 && report.originals[3] == 1);
    const auto nested = faultMode && faultKind == 0 ? 0u : 1u;
    CHECK(report.originals[1] == 1 + nested && report.originals[2] == 1 + nested
        && report.nested[0] == 0 && report.nested[1] == nested && report.nested[2] == nested && report.nested[3] == 0);
    for (unsigned kind = 0; kind != 4; ++kind) {
        const auto caught = faultMode && kind == faultKind ? 1u : 0u;
        CHECK(report.caught[kind] == caught && report.returned[kind] == report.originals[kind] - caught);
    }
    CHECK(observations.depth == 0);
}
DWORD WINAPI Worker(void* parameter)
{
    auto* plan = static_cast<WorkerPlan*>(parameter);
    RizEarlyRecorder_Event(RizEarlyWorkerStarted, RizEarlyWorkerPhase, 4, plan->blockAfterCalls ? 1u : 0u,
        0, 0, Module(), 0, 0, 0, 0, 0, 0, 0);
    Phase(RizEarlyWorkerPhase);
    if (plan->blockAfterCalls) {
        CHECK(plan->reached && plan->release && SetEvent(plan->reached));
        CHECK(WaitForSingleObject(plan->release, 15000) == WAIT_OBJECT_0);
    }
    RizEarlyRecorder_Event(RizEarlyWorkerFinished, RizEarlyWorkerPhase, 4, plan->blockAfterCalls ? 1u : 0u,
        0, 0, Module(), 0, 0, 0, 0, 0, 0, 0);
    return 0;
}
HANDLE StartWorker(WorkerPlan* plan)
{
    RizEarlyRecorder_Event(RizEarlyWorkerRequested, RizEarlyHostPhase, 4, plan->blockAfterCalls ? 1u : 0u,
        0, 0, Module(), 0, 0, 0, 0, 0, 0, 0);
    const auto worker = CreateThread(nullptr, 0, Worker, plan, 0, nullptr); CHECK(worker != nullptr); return worker;
}
void Join(HANDLE worker)
{
    CHECK(worker != nullptr && WaitForSingleObject(worker, 15000) == WAIT_OBJECT_0);
    DWORD exitCode = 0xffffffffu; CHECK(GetExitCodeThread(worker, &exitCode) && exitCode == 0);
    CHECK(CloseHandle(worker));
}
int Complete(RizEarlySharedV1* shared, const OwnedEarlySnapshotV2& final)
{
    const auto mode = shared->header.config.mode;
    CHECK(observations.depth == 0 && shared->header.failure == 0 && shared->header.overflow == 0);
    for (unsigned kind = 0; kind != 4; ++kind)
        CHECK(static_cast<std::uint64_t>(shared->header.targetCalls[kind]) == final.originals[kind]);
    CHECK(RizEarlyRecorder_Capture() == 0);
    CHECK(shared->publisherSnapshot.infoResult == 0 && shared->publisherSnapshot.memoryResult == 0
        && (shared->publisherSnapshot.flags & (RizEarlyInfoAvailable | RizEarlyMemoryAvailable))
            == (RizEarlyInfoAvailable | RizEarlyMemoryAvailable));
    if (mode == RizEarlyBaselineOnly)
        CHECK(shared->publisherInfo.state == RizFixedFresh && shared->publisherInfo.sourceWriteAttempts == 0);
    else CHECK(shared->publisherInfo.state == RizFixedFixtureReady && shared->publisherInfo.sourceWriteAttempts == 4);
    RizEarlyRecorder_Event(RizEarlySnapshotCaptured, RizEarlyHostPhase, 4, 0,
        shared->publisherSnapshot.infoResult, shared->publisherSnapshot.flags,
        Module(), 0, 0, 0, 0, shared->publisherSnapshot.captureCalls,
        shared->publisherSnapshot.publisherState, 0);
    const auto totalChecks = AtomicRead(&hostChecks) + final.checks;
    const auto totalFailures = AtomicRead(&hostFailures) + final.failures;
    RizEarlyRecorder_Event(RizEarlyHostCompleted, RizEarlyHostPhase, 4, 0, 0, mode,
        Module(), 0, 0, 0, 0, totalChecks, totalFailures, 0);
    // Only this owned child completed. BaselineOnly explicitly has no Ready;
    // successful early publication is still not a game/V4 credential.
    InterlockedExchange(&shared->header.state, RizEarlyCompleted);
    std::printf("owned %s fixture mode=%u checks=%llu failures=%llu originals=%llu/%llu/%llu/%llu\n",
        RizEarlyFixtureLabel, mode, static_cast<unsigned long long>(totalChecks), static_cast<unsigned long long>(totalFailures),
        static_cast<unsigned long long>(final.originals[0]), static_cast<unsigned long long>(final.originals[1]),
        static_cast<unsigned long long>(final.originals[2]), static_cast<unsigned long long>(final.originals[3]));
    return totalFailures ? 1 : 0;
}
int Run()
{
    auto* shared = RizEarlyRecorder_GetShared(); CHECK(shared != nullptr);
    auto& header = shared->header; const auto mode = header.config.mode;
    CHECK(header.importerInitializers == 4 && header.overflow == 0 && header.failure == 0);
    if (mode == RizEarlyBaselineOnly) {
        CHECK(header.readySequence == 0 && header.writeAttempts == 0 && header.writeCompletions == 0
            && header.producerClaim == 0 && OwnedEarlyGetInitializerWorker() == nullptr);
        // A separate child exercises the SAME actual source exports while no
        // publisher Prepare, helper Configure, Bind or Commit has occurred.
        // Parent compares stable raw exception fields across same-binary child
        // processes; it does not claim the same object, address or run.
        for (unsigned kind = 0; kind != 4; ++kind) Phase(RizEarlyBaselineSehPhase, kind, 1);
        for (unsigned kind = 0; kind != 4; ++kind) Phase(RizEarlyBaselineCppPhase, kind, 2);
        const auto final = Snapshot();
        CHECK(final.originals[0] == 12 && final.originals[1] == 22
            && final.originals[2] == 22 && final.originals[3] == 12);
        for (unsigned kind = 0; kind != 4; ++kind) {
            RizEngineNodeInfoV1 information{1, sizeof(information)};
            CHECK(RizEngineNodeContext_GetInfo(kind, RizEarlySlot, &information) == 2);
            CHECK(information.attached == 0 && information.unknown == 0 && information.epoch == 0
                && information.entered == 0 && information.exited == 0 && information.inFlight == 0);
            CHECK(AtomicRead(&observerEnters[kind]) == 0 && AtomicRead(&observerExits[kind]) == 0);
        }
        CHECK(header.readySequence == 0 && header.writeAttempts == 0 && header.writeCompletions == 0
            && header.producerClaim == 0);
        return Complete(shared, final);
    }
    if (mode == RizEarlyPriorCall) {
        CHECK(header.writeAttempts == 0 && header.readySequence == 0);
        Phase(RizEarlyHostPhase); // An additional real target-call history.
        RizEarlyProxy_RunInstall(); CHECK(false);
    }
    if (mode == RizEarlyLate) {
        CHECK(header.writeAttempts == 0 && header.readySequence == 0);
        WorkerPlan plan{CreateEventW(nullptr, TRUE, FALSE, nullptr), CreateEventW(nullptr, TRUE, FALSE, nullptr), true};
        CHECK(plan.reached && plan.release);
        const auto worker = StartWorker(&plan); CHECK(worker != nullptr);
        CHECK(WaitForSingleObject(plan.reached, 15000) == WAIT_OBJECT_0);
        // The worker is genuinely running and has executed all four targets;
        // it is still retained/blocked when the late producer must refuse.
        CHECK(WaitForSingleObject(worker, 0) == WAIT_TIMEOUT);
        RizEarlyProxy_RunInstall(); CHECK(false);
    }
    if (mode == RizEarlyDuplicate) { RizEarlyProxy_RunInstall(); CHECK(false); }
    CHECK(mode == RizEarlyNormal || mode == RizEarlyWorker);
    CHECK(header.state == RizEarlyFixtureReady && header.readySequence != 0
        && header.writeAttempts == 4 && header.writeCompletions == 4);
    CHECK(RizEngineNodeContext_GetAbiVersion() == 2 && RizEngineNodeContext_GetLimit() == 32);
    if (mode == RizEarlyWorker) Join(static_cast<HANDLE>(OwnedEarlyGetInitializerWorker()));
    else CHECK(OwnedEarlyGetInitializerWorker() == nullptr);
    const auto baseline = Snapshot();
    const auto initializerExtra = mode == RizEarlyWorker ? 1u : 0u;
    CHECK(baseline.originals[0] == 4 + initializerExtra && baseline.originals[1] == 8 + initializerExtra * 2
        && baseline.originals[2] == 8 + initializerExtra * 2 && baseline.originals[3] == 4 + initializerExtra);
    CHECK(baseline.workerRequests == initializerExtra && baseline.workerStarts == initializerExtra
        && baseline.workerFinishes == initializerExtra);
    CheckInfo(false, false, baseline);
    Phase(RizEarlyHostPhase); CheckInfo(false, false, Snapshot());
    for (unsigned kind = 0; kind != 4; ++kind) {
        owners[kind].kind = kind; const auto* tuple = RizEarlyProxy_Inspect(kind);
        RizEngineNodeObserverV1 observer{1, sizeof(observer), ObserveEnter, ObserveExit, &owners[kind]};
        const auto result = RizEngineNodeContext_Attach(kind, RizEarlySlot, tuple->epoch, &observer);
        RizEarlyRecorder_Event(RizEarlyObserverAttached, RizEarlyHostPhase, kind, 0, result, 0,
            Module(), tuple->target, tuple->original, tuple->entry, tuple->epoch, 0, 0, 0);
        CHECK(result == 0);
    }
    Phase(RizEarlyHostAfterAttachPhase);
    WorkerPlan workerPlan{}; Join(StartWorker(&workerPlan));
    CheckInfo(true, false, Snapshot());
    // Every actual original exception stays native, after loader return and
    // after Attach. Unknown is sticky thereafter; this fixture does not clear
    // it or reinterpret a native original fault as successful cancellation.
    for (unsigned kind = 0; kind != 4; ++kind) Phase(RizEarlySehPhase, kind, 1);
    for (unsigned kind = 0; kind != 4; ++kind) Phase(RizEarlyCppExceptionPhase, kind, 2);
    const auto final = Snapshot(); CheckInfo(true, true, final);
    CHECK(observations.depth == 0);
    for (unsigned kind = 0; kind != 4; ++kind)
        CHECK(AtomicRead(&observerEnters[kind]) == (kind == 0 || kind == 3 ? 10u : 18u));
    CHECK(header.state == RizEarlyFixtureReady && header.failure == 0 && header.overflow == 0
        && header.importerInitializers == 4 && header.writeAttempts == 4 && header.writeCompletions == 4);
    return Complete(shared, final);
}
}
int main()
{
    __try { return Run(); }
    __except (EXCEPTION_EXECUTE_HANDLER) { RizEarlyRecorder_Fail(RizEarlyUnexpectedException, 1); }
}
