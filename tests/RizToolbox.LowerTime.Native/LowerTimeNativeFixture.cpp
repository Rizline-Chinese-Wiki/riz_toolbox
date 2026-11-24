#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "PlaybackSourceContext.h"
#include "SingleReturnContext.h"

extern "C" {
void* WrapperEntry{}; void* LowerEntry{}; void* ClipEntry{}; void* StartEntry{};
void* PauseEntry{}; void* PauseLeafEntry{}; void* PitchEntry{};
void __cdecl FixtureWrapperTarget(void*, float, void*);
void __cdecl FixtureWrapperOriginal(void*, float, void*);
void __cdecl FixtureBadWrapperOriginal(void*, float, void*);
void __cdecl FixtureNoChildOriginal(void*, float, void*);
void __cdecl FixtureDuplicateOriginal(void*, float, void*);
void __cdecl FixtureWrongBitsOriginal(void*, float, void*);
void __cdecl FixtureWrongSelfOriginal(void*, float, void*);
void __cdecl FixtureLowerTarget(void*, float);
void __cdecl FixtureClipTarget(void*, void*, void*);
void __cdecl FixtureCtorTarget();
void __cdecl FixtureStartTarget();
std::uint8_t __cdecl FixtureStartOriginal(void*, void*);
extern const unsigned char FixtureClipReturned;
void __cdecl FixturePauseTarget(void*, void*);
void __cdecl FixturePauseOriginal(void*, void*);
void __cdecl FixturePauseLeafTarget();
void __cdecl FixturePitchTarget(void*, float, void*);
__declspec(dllimport) int __cdecl RtPlaybackSourceConfigureInitialization(void*, void*);
__declspec(dllimport) int __cdecl RtPlaybackSourceReadInitialization(void*, RizPlaybackInitializationInfoV2*);
__declspec(dllimport) int __cdecl RtPlaybackSourceEnterFresh(void*);
__declspec(dllimport) void __cdecl RtPlaybackSourceLeaveFresh(void*);
std::uint32_t __cdecl RizSingleReturnPrototype_BindFreshSource(std::uint32_t, void*, void*);
std::uint32_t __cdecl RizSingleReturnPrototype_BindInitialCaptureSource(std::uint32_t, void*, void*);
std::uint32_t __cdecl RizSingleReturnPrototype_BindLogicalSource(std::uint32_t, void*, void*);
std::uint32_t __cdecl RizSingleReturnPrototype_BeginInitialCapture(std::uint32_t, void*, void**);
std::uint32_t __cdecl RizSingleReturnPrototype_ReadInitialCapture(void*, RizPlaybackInitialCaptureInfoV1*);
std::uint32_t __cdecl RizSingleReturnPrototype_ActInitialCapture(void*, std::uint32_t, void*, std::uint64_t, std::uint64_t, std::uint64_t);
std::uint32_t __cdecl RizSingleReturnPrototype_BeginInitializationHandoff(std::uint32_t, std::uint64_t, void*, void**);
std::uint32_t __cdecl RizSingleReturnPrototype_AdmitLogicalHandoff(void*);
std::uint32_t __cdecl RizSingleReturnPrototype_VerifyInitializationCapture(void*, void*, std::uint64_t, std::uint64_t, std::uint64_t);
std::uint32_t __cdecl RizSingleReturnPrototype_OpenTransferredSource(void*);
std::uint32_t __cdecl RizSingleReturnPrototype_PrepareTransferredClock(void*, const RizMusicClockPolicyV1*);
std::uint32_t __cdecl RizSingleReturnPrototype_ArmTransferredClock(void*);
std::uint32_t __cdecl RizSingleReturnPrototype_ReadLogicalHandoff(void*, void*);
}

namespace {
struct alignas(8) Storage { unsigned char Data[96]; };
Storage source{}, foreignSource{}, player{}, progress{}, oldStart{}, freshStart{};
std::uint64_t audioClass{}, playerClass{}, methodIdentity{}, clipIdentity{};
void* owner{}; void* sourceLibrary{}; void* pending{}; void* capture{};
const wchar_t* mode{};
RtPlaybackSourceInfo initial{};
RtPlaybackSourceFrame observed[256]{};
unsigned observedCount{}, beforeCount{}, returnedCount{}, abnormalCount{}, childCount{};
unsigned checks{}, failures{};
volatile LONG64 originalCalls{}, stores{}, pitchCalls{}, clockOriginalCalls{};
std::uint64_t lastSelf{}, lastThread{};
std::uint32_t lastBits{};
bool clockBound{}, clockPublished{}, probe{}, reentered{}, withdrawBefore{};
bool phaseTest{}, workerCase{}, throwBefore{}, throwAfter{};
bool retryWithinClock{}, nestedOriginal{}, lastLowerWasChild{};
void* retryFresh{}; void* priorInitializer{};
std::uint64_t oldYield{};
RtPlaybackAudioInterruptionInfoV1 boundaryBadge{};
RtPlaybackWriterInfo writer{};
RizMusicClockPolicyV1 policy{};

bool Is(const wchar_t* value) { return std::wcscmp(mode, value) == 0; }
bool Starts(const wchar_t* value) { return std::wcsncmp(mode, value, std::wcslen(value)) == 0; }
bool Transfer() { return Starts(L"transfer"); }
RizPlaybackInitializationInfoV2 CompleteStart(Storage& enumerator);
void Check(bool condition, const char* expression, unsigned line) {
    ++checks;
    if (!condition) { ++failures; std::printf("FAIL %u %s\n", line, expression); }
}
#define CHECK(condition) Check((condition), #condition, __LINE__)
void PutPointer(Storage& object, unsigned offset, void* value) { std::memcpy(object.Data + offset, &value, sizeof(value)); }
std::uint64_t Address(const void* value) { return reinterpret_cast<std::uint64_t>(value); }
float Float(std::uint32_t bits) { float value{}; std::memcpy(&value, &bits, sizeof(value)); return value; }
RtPlaybackSourceInfo State() {
    RtPlaybackSourceInfo info{}; CHECK(RtPlaybackSourceReadOwner(owner, &info) == 1); return info;
}
int Fresh() {
    const auto result = RtPlaybackSourceEnterFresh(owner);
    if (result == 1) RtPlaybackSourceLeaveFresh(owner);
    return result;
}
RtPlaybackAudioInterruptionInfoV1 Badge() {
    RtPlaybackAudioInterruptionInfoV1 info{}; info.Version = 1; info.Size = sizeof(info);
    CHECK(RtPlaybackSourceReadAudioInterruption(owner, &oldStart, oldYield, &info) == 1); return info;
}
void ProbeRevoked() {
    CHECK(Fresh() == (workerCase ? 3 : 2));
    if (clockBound) {
        CHECK(RizSingleReturnPrototype_PollFreshSource(0) == 0);
        if (clockPublished) {
            RizMusicClockPolicyV1 actual{}; actual.abiVersion = 1; actual.byteSize = sizeof(actual); std::uint32_t active = 9;
            CHECK(RizSingleReturnPrototype_ReadClockPolicy(0, &actual, &active) == 0 && active == 0);
        }
    }
    if (writer.Reservation) {
        RtPlaybackWriterInfo actual{}; actual.Version = 1; actual.Size = sizeof(actual);
        const auto calls = InterlockedCompareExchange64(&pitchCalls, 0, 0);
        CHECK(RtPlaybackWriterRun(reinterpret_cast<void*>(writer.Reservation), writer.Nonce, &actual) == 0);
        CHECK(InterlockedCompareExchange64(&pitchCalls, 0, 0) == calls);
    }
}
void __cdecl Observer(void*, const RtPlaybackSourceFrame* frame) {
    CHECK(frame && RtPlaybackSourceVerifyCurrent(owner, frame) == 1);
    if (!frame) return;
    CHECK(observedCount < 256);
    if (observedCount < 256) observed[observedCount++] = *frame;
    if (frame->Stage == RtSourceBefore) {
        ++beforeCount;
        RtPlaybackSourceFrame changed = *frame; changed.Flags ^= RtSourceLowerTimeChildFlag;
        CHECK(RtPlaybackSourceVerifyCurrent(owner, &changed) == 0);
        changed = *frame; changed.Caller ^= 1;
        CHECK(RtPlaybackSourceVerifyCurrent(owner, &changed) == 0);
        CHECK(RtPlaybackSourceReleaseReceipt(owner, frame->Sequence, frame->Token) == 0);
        if (frame->Role == RtSourceLowerSetTime) {
            CHECK(frame->IncomingMethod == 0 && frame->Kind == RtSourceIcallSingle1 && frame->ArgBits <= UINT32_MAX);
            lastLowerWasChild = (frame->Flags & RtSourceLowerTimeChildFlag) != 0;
            if (frame->Flags & RtSourceLowerTimeChildFlag) ++childCount;
            else if (probe) ProbeRevoked();
        }
        if (Is(L"reentry") && frame->Role == RtSourceSetTime && !reentered) {
            reentered = true; FixtureLowerTarget(&source, Float(static_cast<std::uint32_t>(frame->ArgBits)));
        }
        if (withdrawBefore && frame->Role == RtSourceLowerSetTime)
            CHECK(RtPlaybackSourceSetArmed(owner, initial.OwnerEpoch, 0) == 1);
    } else if (frame->Stage == RtSourceOriginalReturned) ++returnedCount;
    else if (frame->Stage == RtSourceAbnormal) ++abnormalCount;
}
void __cdecl LowerOriginal(void* self, float value) {
    std::uint32_t bits{}; std::memcpy(&bits, &value, sizeof(bits));
    InterlockedIncrement64(&originalCalls); lastSelf = Address(self); lastBits = bits; lastThread = GetCurrentThreadId();
    if (probe && self == &source && !lastLowerWasChild) ProbeRevoked();
    if (phaseTest && self == &source) {
        boundaryBadge = Badge();
        CHECK(boundaryBadge.Invalidated == 1 && boundaryBadge.FenceKind == 2 && boundaryBadge.ActualStamp == boundaryBadge.Scheduling);
        CHECK((boundaryBadge.ActualFlags & RtSourcePhysicalExitedFlag) == 0);
        RtPlaybackSourceFrame untouched{}; untouched.Version = 0x11223344u;
        CHECK(RtPlaybackSourceReadReceipt(owner, boundaryBadge.AttemptSequence, boundaryBadge.AttemptToken, &untouched) == 0
            && untouched.Version == 0x11223344u);
        if (Is(L"first-inflight") || Is(L"transfer-inflight")) {
            CHECK(State().InFlight == 1);
            RizPlaybackInitializationInfoV2 retained{}; retained.version = 2; retained.size = sizeof(retained);
            CHECK(RtPlaybackSourceReadInitialization(owner, &retained) == 1 && retained.phase == 5 && retained.receipt == 0);
            void* rejected{};
            CHECK((Transfer() ? RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff(capture, priorInitializer, &rejected)
                : RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture(capture, priorInitializer, &rejected)) != 0 && !rejected);
        }
    }
    if (Is(L"duplicate-child") && !nestedOriginal) {
        nestedOriginal = true; FixtureLowerTarget(self, value);
    }
    if (throwBefore) RaiseException(0xe2450022u, 0, 0, nullptr);
    if (self == &source || self == &foreignSource) std::memcpy(static_cast<Storage*>(self)->Data + 0x20, &bits, sizeof(bits));
    InterlockedIncrement64(&stores);
    if (throwAfter) RaiseException(0xe2450023u, 0, 0, nullptr);
}
void __cdecl CtorOriginal(void*, void*, void*) { }
void __cdecl ClipOriginal(void* self, void* clip, void*) { std::memcpy(static_cast<Storage*>(self)->Data + 0x30, &clip, sizeof(clip)); }
void __cdecl PauseLeafOriginal(void*, void*) { }
void __cdecl PitchOriginal(void*, float, void*) { InterlockedIncrement64(&pitchCalls); }
float __cdecl ClockOriginal(void*, const void*) {
    InterlockedIncrement64(&clockOriginalCalls);
    if (retryWithinClock) {
        void* rejected{};
        CHECK((Transfer() ? RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff(capture, retryFresh, &rejected)
            : RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture(capture, retryFresh, &rejected)) == 8 && !rejected);
    }
    return 9.0f;
}
std::uintptr_t __cdecl ClockEnter(const RizSingleFrameV1*, void*) { return 1; }
void __cdecl ClockExit(const RizSingleFrameV1*, const RizSingleCompletionV1*, void*) { }
std::uint32_t __cdecl ReadClock(RizMusicClockSampleV1* sample, void*) {
    sample->abiVersion = 1; sample->byteSize = sizeof(*sample); sample->self = policy.self; sample->method = policy.method;
    sample->runHigh = policy.runHigh; sample->runLow = policy.runLow; sample->controllerGeneration = policy.controllerGeneration;
    sample->policyGeneration = policy.policyGeneration; sample->phase = policy.phase; sample->reserved = 0; sample->dspNow = 11;
    return 0;
}
void PublishClock() {
    policy.abiVersion = 1; policy.byteSize = sizeof(policy); policy.self = &player; policy.method = &methodIdentity;
    policy.runHigh = 17; policy.runLow = 19; policy.controllerGeneration = 7; policy.policyGeneration = 1;
    policy.phase = RizClockPlaying; policy.dspAnchor = 10; policy.mediaAnchor = 1; policy.rate = 1;
    policy.startDsp = 10; policy.clipLength = 100; policy.readClock = ReadClock; policy.clockOwner = &policy;
    void* receipt{};
    CHECK(RizSingleReturnPrototype_PrepareDormantMusicClock(0, 0, &policy, &receipt) == 0 && receipt);
    CHECK(RizSingleReturnPrototype_ArmDormantMusicClock(receipt) == 0); clockPublished = true;
    const auto entry = reinterpret_cast<RizSingleOriginal>(RizSingleReturnPrototype_GetEntry(0));
    CHECK(entry(&player, &methodIdentity) == 2.0f);
}
void ConfigureSingle(bool publish) {
    const RizSingleConfigV1 config{1, sizeof(config), ClockOriginal, ClockEnter, ClockExit, nullptr};
    CHECK(RizSingleReturnPrototype_Configure(0, &config) == 0);
    CHECK(RizSingleReturnPrototype_BindFreshSource(0, sourceLibrary, owner) == 0); clockBound = true;
    if (publish) PublishClock();
}
void Setup(bool activeWatch, bool bindLower = true) {
    PutPointer(source, 0, &audioClass); PutPointer(foreignSource, 0, &audioClass); PutPointer(player, 0, &playerClass);
    PutPointer(player, 0x10, &source); PutPointer(progress, 0x28, &source); PutPointer(progress, 0x38, &player);
    owner = RtPlaybackSourceCreateOwner(Observer, nullptr); CHECK(owner != nullptr); initial = State();
    CHECK(RtPlaybackSourceVersion() == 4 && sizeof(RtPlaybackSourceFrame) == 168 && sizeof(RtPlaybackSourceInfo) == 136);
    CHECK(RtPlaybackLowerTimeVersion() == 1 && RtPlaybackAudioInterruptionVersion() == 1 && sizeof(RtPlaybackAudioInterruptionInfoV1) == 144);
    WrapperEntry = RtPlaybackSourceGetEntry(RtSourceSingle1, 0); LowerEntry = RtPlaybackSourceGetEntry(RtSourceIcallSingle1, 1);
    ClipEntry = RtPlaybackSourceGetEntry(RtSourceRef1, 3); StartEntry = RtPlaybackSourceGetEntry(RtSourceBooleanReturn0, 4);
    PauseEntry = RtPlaybackSourceGetEntry(RtSourceVoid0, 6); PauseLeafEntry = RtPlaybackSourceGetEntry(RtSourceVoid0, 7);
    PitchEntry = RtPlaybackSourceGetEntry(RtSourceSingle1, 8);
    auto wrapperOriginal = Is(L"wrong-caller") ? FixtureBadWrapperOriginal : FixtureWrapperOriginal;
    if (Is(L"missing-child")) wrapperOriginal = FixtureNoChildOriginal;
    if (Is(L"duplicate-wrapper")) wrapperOriginal = FixtureDuplicateOriginal;
    if (Is(L"wrong-bits")) wrapperOriginal = FixtureWrongBitsOriginal;
    if (Is(L"wrong-self")) wrapperOriginal = FixtureWrongSelfOriginal;
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceSingle1, 0, RtSourceSetTime, reinterpret_cast<void*>(FixtureWrapperTarget), reinterpret_cast<void*>(wrapperOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceIcallSingle1, 1, RtSourceLowerSetTime, reinterpret_cast<void*>(FixtureLowerTarget), reinterpret_cast<void*>(LowerOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceRef1, 2, RtSourcePlayerConstructor, reinterpret_cast<void*>(FixtureCtorTarget), reinterpret_cast<void*>(CtorOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceRef1, 3, RtSourceSetClip, reinterpret_cast<void*>(FixtureClipTarget), reinterpret_cast<void*>(ClipOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceBooleanReturn0, 4, RtSourceProgressStartMoveNext, reinterpret_cast<void*>(FixtureStartTarget), reinterpret_cast<void*>(FixtureStartOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceVoid0, 6, RtSourcePause, reinterpret_cast<void*>(FixturePauseTarget), reinterpret_cast<void*>(FixturePauseOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceVoid0, 7, RtSourcePauseLeaf, reinterpret_cast<void*>(FixturePauseLeafTarget), reinterpret_cast<void*>(PauseLeafOriginal)) == 1);
    CHECK(RtPlaybackSourceConfigure(owner, RtSourceSingle1, 8, RtSourceSetPitch, reinterpret_cast<void*>(FixturePitchTarget), reinterpret_cast<void*>(PitchOriginal)) == 1);
    if (Is(L"configure")) {
        CHECK(RtPlaybackSourceConfigureLowerTime(reinterpret_cast<unsigned char*>(owner) + 8, 0, 1) == 0);
        CHECK(RtPlaybackSourceConfigureLowerTime(owner, 1, 0) == 0 && RtPlaybackSourceConfigureLowerTime(owner, 0, 0) == 0);
        CHECK(RtPlaybackSourceConfigureLowerTime(owner, 8, 1) == 0 && RtPlaybackSourceConfigureLowerTime(owner, 0, 31) == 0);
        void* other = RtPlaybackSourceCreateOwner(Observer, nullptr);
        CHECK(other && RtPlaybackSourceConfigureLowerTime(other, 0, 1) == 0);
    }
    if (bindLower) CHECK(RtPlaybackSourceConfigureLowerTime(owner, 0, 1) == 1);
    CHECK(RtPlaybackSourceSetArmed(owner, initial.OwnerEpoch, 1) == (bindLower ? 1 : 0));
    if (!bindLower) return;
    CHECK(RtPlaybackSourceConfigureLowerTime(owner, 0, 1) == 0);
    CHECK(RtPlaybackSourceConfigurePlayerWatch(owner, &playerClass, &audioClass) == 1);
    CHECK(RtPlaybackSourceConfigureInitialization(owner, const_cast<unsigned char*>(&FixtureClipReturned)) == 1);
    if (activeWatch) {
        CHECK(RtPlaybackSourceStagePlayerWatch(owner, &player, &source, &pending) == 1);
        CHECK(RtPlaybackSourceActivateInitialPlayerWatch(owner, pending) == 1); pending = nullptr;
    }
    CHECK(Fresh() == 1);
}
void ReserveWriter() {
    FixturePauseTarget(&player, nullptr);
    RtPlaybackSourceFrame root{};
    for (unsigned i = 0; i < observedCount; ++i)
        if (observed[i].Role == RtSourcePause && observed[i].Stage == RtSourceBefore) root = observed[i];
    const auto health = State();
    writer.Version = 1; writer.Size = sizeof(writer); writer.RootSequence = root.Sequence; writer.RootToken = root.Token;
    writer.Source = Address(&source); writer.Target = Address(reinterpret_cast<void*>(FixturePitchTarget));
    writer.IncomingMethod = Address(&methodIdentity); writer.ArgBits = 0x40000000u;
    writer.ExpectedWriter = health.WriterSequence; writer.ExpectedSource = health.SourceSequence; writer.ExpectedScheduling = health.SchedulingSequence;
    CHECK(RtPlaybackWriterReserve(owner, &writer) == 1 && writer.Reservation && writer.Nonce);
}
void Receipts() {
    for (unsigned i = 0; i < observedCount; ++i) {
        const auto& before = observed[i];
        if (before.Stage != RtSourceBefore || !(before.Flags & (RtSourceArmed | RtSourceInitializationResponsibilityFlag))) continue;
        RtPlaybackSourceFrame actual{};
        CHECK(RtPlaybackSourceReadReceipt(owner, before.Sequence, before.Token, &actual) == 1);
        CHECK(actual.Stage == RtSourcePhysicalExit && (actual.Flags & RtSourcePhysicalExitedFlag) && actual.Sequence == before.Sequence);
        CHECK(RtPlaybackSourceReleaseReceipt(owner, before.Sequence, before.Token) == 1);
        CHECK(RtPlaybackSourceReleaseReceipt(owner, before.Sequence, before.Token) == 0);
        CHECK(RtPlaybackSourceVerifyCurrent(owner, &before) == 0);
    }
}
DWORD WINAPI Worker(void* self) { FixtureLowerTarget(self, Float(0x3fa00000u)); return 0; }
void CallWorker(void* self) {
    const auto thread = CreateThread(nullptr, 0, Worker, self, 0, nullptr); CHECK(thread != nullptr);
    CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
}
RizPlaybackInitializationInfoV2 CompleteStart(Storage& enumerator) {
    PutPointer(enumerator, 0x20, &progress); PutPointer(enumerator, 0x38, &clipIdentity);
    auto entry = reinterpret_cast<std::uint8_t(__cdecl*)(void*, void*)>(StartEntry);
    CHECK(entry(&enumerator, nullptr) == 1 && entry(&enumerator, nullptr) == 0);
    RizPlaybackInitializationInfoV2 info{}; info.version = 2; info.size = sizeof(info);
    CHECK(RtPlaybackSourceReadInitialization(owner, &info) == 1 && info.phase == 4 && info.receipt);
    return info;
}
void Phase6() {
    const bool transfer = Transfer();
    const bool withPending = Is(L"first-pending") || Is(L"transfer-pending");
    workerCase = Is(L"first-worker");
    Setup(transfer); ConfigureSingle(transfer);
    CHECK((transfer ? RizSingleReturnPrototype_BindLogicalSource(0, sourceLibrary, owner)
        : RizSingleReturnPrototype_BindInitialCaptureSource(0, sourceLibrary, owner)) == 0);
    const auto prior = CompleteStart(oldStart); oldYield = prior.yieldSequence;
    priorInitializer = reinterpret_cast<void*>(prior.receipt);
    CHECK((transfer ? RizSingleReturnPrototype_BeginInitializationHandoff(0, 1, reinterpret_cast<void*>(prior.receipt), &capture)
        : RizSingleReturnPrototype_BeginInitialCapture(0, reinterpret_cast<void*>(prior.receipt), &capture)) == 0 && capture);
    if (withPending) CHECK(RtPlaybackSourceStagePlayerWatch(owner, &player, &source, &pending) == 1 && pending);
    const auto before = State(); phaseTest = true; probe = true;
    if (Is(L"first-foreign") || Is(L"transfer-foreign")) {
        phaseTest = false; probe = false; FixtureLowerTarget(&foreignSource, Float(0x7fc12345u));
        const auto after = State(); const auto untouched = Badge();
        CHECK(untouched.Invalidated == 0 && untouched.Reason == 0 && untouched.AttemptSequence == 0
            && after.WriterSequence == before.WriterSequence && after.SourceSequence == before.SourceSequence
            && after.SchedulingSequence == before.SchedulingSequence && after.Entered == before.Entered);
        return;
    }
    if (workerCase) CallWorker(&source);
    else if (Is(L"first-throw-before") || Is(L"first-throw-after")) {
        throwBefore = Is(L"first-throw-before"); throwAfter = Is(L"first-throw-after"); bool caught = false;
        __try { FixtureLowerTarget(&source, Float(0x3fa00000u)); }
        __except(EXCEPTION_EXECUTE_HANDLER) { caught = true; }
        CHECK(caught);
    }
    else if (Is(L"first-wrapper")) FixtureWrapperTarget(&source, Float(0x3fa00000u), &methodIdentity);
    else FixtureLowerTarget(&source, Float(0x3fa00000u));
    phaseTest = false; probe = false;
    const auto badge = Badge();
    CHECK(badge.Invalidated == 1 && badge.FenceKind == 2 && badge.Writer == before.WriterSequence && badge.Source == before.SourceSequence
        && badge.Scheduling == before.SchedulingSequence + 1 && badge.ActualStamp == badge.Scheduling
        && badge.PlayerWatchReceipt == Address(pending) && (badge.ActualFlags & RtSourcePhysicalExitedFlag));
    CHECK(badge.Reason == (Is(L"first-wrapper") ? 1u : 2u));
    RtPlaybackLowerPitchInitializationInfoV1 pitch{}; pitch.Version = 1; pitch.Size = sizeof(pitch);
    CHECK(RtPlaybackSourceReadLowerPitchInitializationInvalidation(owner, &oldStart, oldYield, &pitch) == 1
        && pitch.Invalidated == 0 && pitch.Reason == 0 && pitch.InvalidationWriter == 0);
    RtPlaybackSourceFrame actual{};
    CHECK(RtPlaybackSourceReadReceipt(owner, badge.AttemptSequence, badge.AttemptToken, &actual) == 1 && actual.Stage == RtSourcePhysicalExit);
    RtPlaybackAudioInterruptionInfoV1 copied = badge; copied.ActualStamp = 1; copied.FenceKind = 1;
    CHECK(RtPlaybackSourceRetryAudioInterruptedInitialization(&copied, reinterpret_cast<void*>(prior.receipt), &freshStart, 16, &copied) == 1);
    void* rejected{};
    const auto stale = transfer ? RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff(capture, reinterpret_cast<void*>(prior.receipt), &rejected)
        : RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture(capture, reinterpret_cast<void*>(prior.receipt), &rejected);
    CHECK(stale != 0 && !rejected);
    if (workerCase || throwBefore || throwAfter) { CHECK(State().Flags & RtSourceUnknown); return; }
    const auto fresh = CompleteStart(freshStart); void* next{};
    if (Is(L"first-clock-inflight") || Is(L"transfer-clock-inflight")) {
        retryFresh = reinterpret_cast<void*>(fresh.receipt); retryWithinClock = true;
        CHECK(reinterpret_cast<RizSingleOriginal>(RizSingleReturnPrototype_GetEntry(0))(&player, &methodIdentity) == 9.0f);
        retryWithinClock = false;
    }
    CHECK((transfer ? RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff(capture, reinterpret_cast<void*>(fresh.receipt), &next)
        : RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture(capture, reinterpret_cast<void*>(fresh.receipt), &next)) == 0 && next && next != capture);
    CHECK((transfer ? RizSingleReturnPrototype_RetryAudioInterruptedInitializationHandoff(capture, reinterpret_cast<void*>(fresh.receipt), &rejected)
        : RizSingleReturnPrototype_RetryAudioInterruptedInitialCapture(capture, reinterpret_cast<void*>(fresh.receipt), &rejected)) != 0 && !rejected);
    if (transfer) CHECK(RizSingleReturnPrototype_AdmitLogicalHandoff(next) == 0);
    else CHECK(RizSingleReturnPrototype_ActInitialCapture(next, 8, owner, 0, 0, 0) == 0);
    void* nextWatch{};
    CHECK(RtPlaybackSourceStagePlayerWatch(owner, &player, &source, &nextWatch) == 1 && nextWatch && nextWatch != pending);
    if (transfer) {
        CHECK(RizSingleReturnPrototype_VerifyInitializationCapture(next, owner, Address(&player), Address(&source), Address(&clipIdentity)) == 0);
        CHECK(RizSingleReturnPrototype_OpenTransferredSource(next) == 0);
        policy.policyGeneration = 2;
        CHECK(RizSingleReturnPrototype_PrepareTransferredClock(next, &policy) == 0);
        CHECK(RizSingleReturnPrototype_ArmTransferredClock(next) == 0);
    } else {
        CHECK(RizSingleReturnPrototype_ActInitialCapture(next, 9, owner, Address(&player), Address(&source), Address(&clipIdentity)) == 0);
        CHECK(RizSingleReturnPrototype_ActInitialCapture(next, 10, owner, 0, 0, 0) == 0);
        PublishClock(); CHECK(RizSingleReturnPrototype_BindLogicalSource(0, sourceLibrary, owner) == 0);
    }
    CHECK(Fresh() == 1 && RizSingleReturnPrototype_PollFreshSource(0) == 1 && !(State().Flags & RtSourceUnknown));
}
void Run() {
    if (Starts(L"first") || Starts(L"transfer")) { Phase6(); return; }
    Setup(true, !Is(L"unbound"));
    if (Is(L"configure") || Is(L"unbound")) return;
    if (Is(L"bits")) {
        const std::uint32_t bits[]{0, 0x80000000u, 1, 0x80000001u, 0x3fa00000u, 0x7f7fffffu, 0xff7fffffu,
            0x7f800000u, 0xff800000u, 0x7fc12345u, 0xffc12345u, 0x7f812345u, 0xff812345u};
        const auto before = State();
        for (const auto value : bits) { FixtureWrapperTarget(&source, Float(value), &methodIdentity); CHECK(lastBits == value && lastSelf == Address(&source)); }
        const auto after = State();
        CHECK(after.WriterSequence == before.WriterSequence && after.SourceSequence == before.SourceSequence
            && after.SchedulingSequence == before.SchedulingSequence + 13 && after.Entered == before.Entered + 26
            && childCount == 13 && originalCalls == 13 && stores == 13 && Fresh() == 1 && !(after.Flags & RtSourceUnknown));
        return;
    }
    ConfigureSingle(true); ReserveWriter();
    const auto before = State(); const auto callbacks = observedCount; probe = true;
    if (Is(L"same-value")) { const std::uint32_t bits = 0x3fa00000u; std::memcpy(source.Data + 0x20, &bits, sizeof(bits)); }
    if (Is(L"foreign") || Is(L"foreign-worker")) {
        if (Is(L"foreign-worker")) CallWorker(&foreignSource);
        else { FixtureLowerTarget(&foreignSource, Float(0x7fc12345u)); FixtureLowerTarget(reinterpret_cast<void*>(0x12345678u), Float(0x80000000u)); }
        const auto after = State();
        CHECK(observedCount == callbacks && after.Entered == before.Entered && after.WriterSequence == before.WriterSequence
            && after.SourceSequence == before.SourceSequence && after.SchedulingSequence == before.SchedulingSequence && Fresh() == 1);
        CHECK(RizSingleReturnPrototype_PollFreshSource(0) == 1);
        RtPlaybackWriterInfo actual{}; actual.Version = 1; actual.Size = sizeof(actual);
        CHECK(RtPlaybackWriterRun(reinterpret_cast<void*>(writer.Reservation), writer.Nonce, &actual) == 1 && pitchCalls == 1);
        return;
    }
    if (Is(L"worker")) { workerCase = true; CallWorker(&source); CHECK(observedCount == callbacks); }
    else if (Is(L"reentry")) FixtureWrapperTarget(&source, Float(0x3fa00000u), &methodIdentity);
    else if (Is(L"wrong-caller")) FixtureWrapperTarget(&source, Float(0x3fa00000u), &methodIdentity);
    else if (Is(L"missing-child") || Is(L"duplicate-wrapper") || Is(L"duplicate-child") || Is(L"wrong-bits") || Is(L"wrong-self"))
        FixtureWrapperTarget(&source, Float(0x3fa00000u), &foreignSource);
    else if (Is(L"withdraw")) { withdrawBefore = true; FixtureLowerTarget(&source, Float(0x3fa00000u)); }
    else if (Is(L"throw-before") || Is(L"throw-after") || Is(L"throw-child-before") || Is(L"throw-child-after")) {
        throwBefore = Is(L"throw-before") || Is(L"throw-child-before"); throwAfter = !throwBefore;
        bool caught = false;
        __try {
            if (Starts(L"throw-child")) FixtureWrapperTarget(&source, Float(0x3fa00000u), &methodIdentity);
            else FixtureLowerTarget(&source, Float(0x3fa00000u));
        }
        __except (EXCEPTION_EXECUTE_HANDLER) { caught = true; }
        CHECK(caught && abnormalCount == (Starts(L"throw-child") ? 2u : 1u) && (State().Flags & RtSourceUnknown));
    } else {
        FixtureLowerTarget(&source, Float(0x3fa00000u));
        if (Is(L"aba")) { FixtureLowerTarget(&source, Float(0x40000000u)); FixtureLowerTarget(&source, Float(0x3fa00000u)); }
    }
    const auto after = State(); const auto count = Is(L"aba") || Is(L"duplicate-wrapper") ? 3u
        : Is(L"reentry") || Is(L"wrong-caller") || Is(L"wrong-bits") || Is(L"duplicate-child") ? 2u : 1u;
    CHECK(after.WriterSequence == before.WriterSequence && after.SourceSequence == before.SourceSequence
        && after.SchedulingSequence == before.SchedulingSequence + count && after.InFlight == 0 && after.Entered == after.Exited);
    CHECK(originalCalls == (Is(L"missing-child") ? 0 : Is(L"reentry") || Is(L"duplicate-wrapper") || Is(L"duplicate-child") ? 2 : 1) + (Is(L"aba") ? 2 : 0));
    CHECK(stores == originalCalls - (throwBefore ? 1 : 0));
    if (Is(L"reentry")) CHECK(childCount == 1 && !(after.Flags & RtSourceUnknown));
    if (Is(L"wrong-caller")) CHECK(childCount == 0 && (after.Flags & RtSourceUnknown));
    if (Is(L"wrong-self") || Is(L"wrong-bits") || Is(L"missing-child") || Is(L"duplicate-wrapper")) CHECK(childCount == 0 && (after.Flags & RtSourceUnknown));
    if (Is(L"duplicate-child")) CHECK(childCount == 1 && (after.Flags & RtSourceUnknown));
    if (Is(L"worker")) CHECK(after.Flags & RtSourceUnknown);
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    mode = argv[1]; sourceLibrary = GetModuleHandleW(L"LowerTimeSourceHelper.dll");
    CHECK(sourceLibrary != nullptr);
    Run();
    if (owner) {
        Receipts(); const auto final = State();
        CHECK(final.Entered == final.Exited && final.InFlight == 0);
        std::printf("{\"mode\":\"%ls\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"before\":%u,\"returned\":%u,\"abnormal\":%u,\"exactChildren\":%u,\"nativeEntered\":%llu,\"nativeExited\":%llu,\"nativeInFlight\":%llu,\"writer\":%llu,\"source\":%llu,\"scheduling\":%llu,\"originalCalls\":%lld,\"stores\":%lld,\"actualDobbyPrepared\":0,\"actualDobbyCommitted\":0,\"ownedEntryProtocolOnly\":true,\"threeMemberDobbyAcceptance\":\"deferred\",\"gameExecuted\":false,\"productionAdmission\":false}\n",
            mode, failures ? "false" : "true", checks, failures, beforeCount, returnedCount, abnormalCount, childCount,
            final.Entered, final.Exited, final.InFlight, final.WriterSequence, final.SourceSequence, final.SchedulingSequence, originalCalls, stores);
    }
    return failures ? 1 : 0;
}
