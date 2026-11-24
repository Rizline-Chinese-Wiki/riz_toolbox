// Owned Source-helper checks only. Stock/Dobby and managed Clock publication
// have separate acceptance; this executable loads no game image or game code.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "PlaybackSourceContext.h"
#include "../PlaybackInitialization.h"
#include "../PlaybackLogicalHandoff.h"
extern "C" {
void __cdecl ResourceClipLeaf(void*, void*, void*);
void __cdecl ResourceSetClipTarget(void*, void*, void*);
std::uint8_t __cdecl ResourceStartOriginal(void*, void*);
extern const unsigned char ResourceClipReturned;
void* ResourceSetClipEntry{};
}
namespace {
unsigned checks{}, failures{};
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; std::printf("FAIL %d %s\n", __LINE__, #x); } } while (0)
struct Audio { void* Class; void* Clip; float Pitch; };
struct Player { void* Class; void* Monitor; Audio* Source; };
struct Reference { void* Clip; unsigned Calls; };
struct Progress { void* Class; void* Monitor; unsigned char Padding[24]; Audio* Source; Reference* Clip; Player* MusicPlayer; };
struct Start { void* Class; void* Monitor; int State; int Reserved; void* Current; Progress* Host; };
static_assert(offsetof(Progress, Clip) == 0x30 && offsetof(Progress, MusicPlayer) == 0x38);
static_assert(offsetof(Start, Host) == 0x20);
int audioClass{}, playerClass{}, progressClass{}, startClass{}, otherClass{}, clipA{}, clipB{};
Audio audio{&audioClass, &clipA, 1.0f};
Player player{&playerClass, nullptr, &audio};
Reference referenceA{&clipA, 0}, referenceB{&clipB, 0};
Progress progress{&progressClass, nullptr, {}, &audio, &referenceA, &player};
Progress foreign{&progressClass, nullptr, {}, &audio, &referenceA, &player};
Progress unrelated{&otherClass, nullptr, {}, &audio, &referenceA, &player};
Start start{&startClass, nullptr, 0, 0, nullptr, &progress};
using RefEntry = void(__cdecl*)(void*, void*, void*);
using BoolEntry = void(__cdecl*)(void*, unsigned char, void*);
using StartEntry = std::uint8_t(__cdecl*)(void*, void*);
using PitchEntry = void(__cdecl*)(void*, float, void*);
using BindGate = void*(__cdecl*)(void*, RizLogicalCommit, void*);
using LogicalAction = std::uint32_t(__cdecl*)(void*, std::uint32_t, void*);
using ConfigureInitialization = int(__cdecl*)(void*, void*);
using ReadInitialization = int(__cdecl*)(void*, RizPlaybackInitializationInfoV2*);
using BeginCapture = std::uint32_t(__cdecl*)(void*, void*, void*);
HMODULE library{};
void* owner{}; void* gate{}; void* progressReceipt{}; void* hostReceipt{}; void* playerReceipt{};
void* futureTicket{}; void* initializationTicket{};
RefEntry clipEntry{}; BoolEntry playEntry{}; StartEntry startEntry{}; PitchEntry pitchEntry{};
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceSetArmed) setArmed;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadCurrent) readCurrent;
decltype(&RtPlaybackSourceVerifyCurrent) verifyCurrent;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
decltype(&RtPlaybackSourceReadProgressResource) readResource;
decltype(&RtPlaybackSourceStageProgressResource) stageResource;
decltype(&RtPlaybackSourceReadProgressResourceTicket) readTicket;
decltype(&RtPlaybackSourceBindInitializationResource) bindResource;
decltype(&RtPlaybackSourceRetireProgressResource) retireResource;
decltype(&RtPlaybackSourceStagePlayerWatch) stagePlayer;
decltype(&RtPlaybackSourceReadPlayerWatch) readPlayer;
decltype(&RtPlaybackSourceRetirePlayerWatch) retirePlayer;
decltype(&RtPlaybackSourceStageProgressWatch) stageProgress;
decltype(&RtPlaybackSourceReadProgressWatch) readProgress;
decltype(&RtPlaybackSourceRetireProgressWatch) retireProgress;
decltype(&RtPlaybackSourceRetryResourceInitialization) retryResource;
ReadInitialization readInitialization;
BeginCapture beginCapture;
BeginCapture beginHandoff;
LogicalAction logicalAction;
unsigned barriers{}, observed{}, returned{}, abnormal{}, setClips{}, pitches{};
unsigned startCallbacks{}, resourceCallbacks{}, commitCalls{};
unsigned retryAttempts{}, retryAccepted{}, staleRejections{}, pendingReplacements{}, unknownRootRefusals{}, inFlightRetryRejections{};
unsigned withdrawRetired{}, withdrawInFlightRejections{};
std::uint64_t priorOrdinal{}, freshOrdinal{};
std::uint64_t expectedRetryInitializer{};
void* retryPriorForCallback{};
std::uint32_t retryActionForCallback{12};
bool rejectRetryCommit{};
void* lastField{}; void* lastValue{}; void* lastMethod{};
bool throwBarrier{}, bindOnStart{}, interruptStart{}, nestedPlay{}, nestedForeignChild{}, nestedForeignThrow{};
bool withdrawDuringStart{};
bool responsibilityChecks{};
unsigned responsibilityBefore{}, responsibilityReturned{}, responsibilityReceipts{}, responsibilityNoJournal{};
unsigned responsibilityTlsChecks{}, responsibilityWorkerReceipts{}, responsibilityAllocationFailures{};
std::uint64_t lastObservedToken{};
std::uint64_t playSequence{};
RtPlaybackSourceFrame frames[272]{}; unsigned frameCount{};
unsigned char targets[7]{};
RtPlaybackSourceInfo Health() { RtPlaybackSourceInfo info{}; CHECK(readOwner(owner, &info) == 1); return info; }
RtPlaybackProgressResourceInfoV1 Resource(void* receipt = nullptr) {
    RtPlaybackProgressResourceInfoV1 info{1, sizeof(info)};
    CHECK(readResource(owner, receipt ? receipt : progressReceipt, &info) == 1); return info;
}
RtPlaybackProgressResourceInfoV1 Ticket(void* receipt) {
    RtPlaybackProgressResourceInfoV1 info{1, sizeof(info)};
    CHECK(readTicket(owner, receipt, &info) == 1); return info;
}
RizPlaybackInitializationInfoV2 Initialization() {
    RizPlaybackInitializationInfoV2 info{2, sizeof(info)};
    CHECK(readInitialization(owner, &info) == 1); return info;
}
RtPlaybackPlayerWatchInfoV2 PlayerWatch(void* receipt) {
    RtPlaybackPlayerWatchInfoV2 info{2, sizeof(info)};
    CHECK(readPlayer(owner, receipt, &info) == 1); return info;
}
RtPlaybackProgressWatchInfoV1 ProgressWatch(void* receipt) {
    RtPlaybackProgressWatchInfoV1 info{1, sizeof(info)};
    CHECK(readProgress(owner, receipt, &info) == 1); return info;
}
void* Stage(void* receipt = nullptr) {
    auto info = Resource(receipt); void* ticket{};
    CHECK(stageResource(owner, receipt ? receipt : progressReceipt, info.Revision, reinterpret_cast<void*>(info.Reference), &ticket) == 1);
    CHECK(ticket != nullptr); return ticket;
}
bool FencesSame(const RtPlaybackSourceInfo& a, const RtPlaybackSourceInfo& b) {
    return a.WriterSequence == b.WriterSequence && a.SourceSequence == b.SourceSequence && a.SchedulingSequence == b.SchedulingSequence;
}
bool Same(const RtPlaybackSourceInfo& a, const RtPlaybackSourceInfo& b) {
    return FencesSame(a, b) && a.Entered == b.Entered && a.Exited == b.Exited && a.InFlight == b.InFlight
        && a.Faults == b.Faults && a.Flags == b.Flags;
}
template<class T> T Symbol(const char* name) {
    const auto value = GetProcAddress(library, name); CHECK(value != nullptr); return reinterpret_cast<T>(value);
}
bool CallClipCatch(Progress* self, void* value, void* method) {
    __try { clipEntry(self, value, method); return false; }
    __except (GetExceptionCode() == 0xe0423020u ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) { return true; }
}
void __cdecl OriginalConstructor(void* self, void* value, void*) { static_cast<Player*>(self)->Source = static_cast<Audio*>(value); }
void __cdecl OriginalMusicPlayer(void* self, void* value, void*) { static_cast<Progress*>(self)->MusicPlayer = static_cast<Player*>(value); }
void __cdecl OriginalSetClip(void* self, void* value, void* method) {
    ++setClips; CHECK(self == &audio && method == nullptr);
    static_cast<Audio*>(self)->Clip = value;
}
void __cdecl OriginalPitch(void* self, float value, void*) { ++pitches; static_cast<Audio*>(self)->Pitch = value; }
void __cdecl OriginalPlay(void*, unsigned char, void*) {
    if (!nestedPlay) return;
    const auto previous = Health();
    if (nestedForeignThrow) CHECK(CallClipCatch(&foreign, &referenceB, reinterpret_cast<void*>(0x9009)));
    else clipEntry(&foreign, &referenceB, reinterpret_cast<void*>(0x9009));
    if (!nestedForeignChild) CHECK(Health().Entered == previous.Entered);
    // The foreign finally must restore the surrounding current parent.
    clipEntry(&progress, &referenceA, nullptr);
}
void Observe(void*, const RtPlaybackSourceFrame* frame) {
    CHECK(frame && frame->Version == 4 && frame->Size == 168);
    if (!frame) return;
    if (responsibilityChecks) {
        RtPlaybackSourceFrame current{};
        CHECK(readCurrent(frame->Kind, frame->Slot, &current) == 1);
        CHECK(std::memcmp(&current, frame, sizeof(current)) == 0 && verifyCurrent(owner, frame) == 1);
        auto forged = *frame; forged.Flags ^= RtSourceInitializationResponsibilityFlag;
        CHECK(verifyCurrent(owner, &forged) == 0); ++responsibilityTlsChecks;
        CHECK(!(Health().Flags & RtSourceInitializationResponsibilityFlag));
        if (frame->Flags & RtSourceInitializationResponsibilityFlag) {
            CHECK((frame->Flags & RtSourceDormant) && !(frame->Flags & RtSourceArmed));
            CHECK(frame->Role == RtSourceProgressStartMoveNext || frame->Role == RtSourceSetClip || frame->Role == RtSourceProgressClip);
            if (frame->Stage == RtSourceBefore) {
                CHECK(Health().Flags == RtSourceArmed && !Health().Faults);
                RtPlaybackSourceFrame pending{};
                CHECK(readReceipt(owner, frame->Sequence, frame->Token, &pending) == 0);
                ++responsibilityBefore;
            } else if (frame->Stage == RtSourceOriginalReturned) ++responsibilityReturned;
        }
    }
    if (frame->Stage == RtSourceBefore) {
        lastObservedToken = frame->Token;
        ++observed; CHECK(frameCount < 272); if (frameCount < 272) frames[frameCount++] = *frame;
        if (frame->Role == RtSourceProgressClip) {
            ++resourceCallbacks;
            CHECK(frame->Self == reinterpret_cast<std::uint64_t>(&progress));
            if (!(frame->Flags & RtSourceUnknown)) {
                const auto info = Resource();
                CHECK(info.Reference == reinterpret_cast<std::uint64_t>(progress.Clip));
                if (futureTicket) {
                    const auto ticket = Ticket(futureTicket);
                    CHECK(!ticket.Current && info.Reference == ticket.Reference && info.Revision == ticket.Revision + 1);
                }
            }
        }
        if (frame->Role == RtSourceProgressPlay) playSequence = frame->Sequence;
        if (frame->Role == RtSourceProgressStartMoveNext) {
            ++startCallbacks;
            if (start.State == 0 && bindOnStart) {
                initializationTicket = Stage();
                const auto info = Ticket(initializationTicket);
                CHECK(info.Phase == 2 && info.Current == 1 && info.Initializer && info.InitializerOrdinal);
                CHECK(bindResource(owner, initializationTicket) == 1);
                CHECK(retireResource(owner, initializationTicket) == 0);
                if (interruptStart) {
                    clipEntry(&progress, &referenceB, nullptr);
                    CHECK(Ticket(initializationTicket).Phase == 4 && Ticket(initializationTicket).Current == 0);
                    CHECK(retireResource(owner, initializationTicket) == 0);
                }
                if (withdrawDuringStart) {
                    CHECK(setArmed(owner, Health().OwnerEpoch, 0) == 1);
                    CHECK((Health().Flags & RtSourceWithdrawn) && !(Health().Flags & RtSourceUnknown) && Health().InFlight != 0);
                    CHECK(retireResource(owner, initializationTicket) == 0); ++withdrawInFlightRejections;
                    CHECK(Ticket(initializationTicket).Phase == 2);
                }
            }
            if (retryPriorForCallback) {
                const auto calls = commitCalls;
                CHECK(Health().InFlight != 0);
                CHECK(retryResource(gate, retryPriorForCallback,
                    reinterpret_cast<void*>(Ticket(initializationTicket).Initializer), retryActionForCallback, &clipA) == 2);
                CHECK(commitCalls == calls);
                ++inFlightRetryRejections;
            }
        }
    } else if (frame->Stage == RtSourceOriginalReturned) ++returned;
    else if (frame->Stage == RtSourceAbnormal) ++abnormal;
}
std::uint32_t __cdecl Commit(void*, void*, std::uint32_t action, const RizLogicalSourceSnapshot* source, void*) {
    ++commitCalls;
    CHECK(source && !source->inFlight && source->owner == reinterpret_cast<std::uint64_t>(owner));
    if (action == 5 || (action >= 7 && action <= 10) || action == 12 || action == 13)
        CHECK(source->initializer && source->initializedPlayer == reinterpret_cast<std::uint64_t>(&player)
        && source->initializedSource == reinterpret_cast<std::uint64_t>(&audio) && source->initializedClip == reinterpret_cast<std::uint64_t>(audio.Clip));
    if (action == 12 || action == 13) {
        ++retryAttempts;
        CHECK(action == retryActionForCallback && source->initializer == expectedRetryInitializer);
        if (rejectRetryCommit) return 6;
        ++retryAccepted;
    }
    return 0;
}
void DrainJournal() {
    for (unsigned i = 0; i < frameCount; ++i) {
        RtPlaybackSourceFrame receipt{};
        CHECK(readReceipt(owner, frames[i].Sequence, frames[i].Token, &receipt) == 1);
        CHECK((receipt.Flags & RtSourcePhysicalExitedFlag) != 0);
        if (responsibilityChecks) {
            CHECK(receipt.Stage == RtSourcePhysicalExit && receipt.Version == 4 && receipt.Size == 168);
            CHECK((receipt.Flags & RtSourceInitializationResponsibilityFlag) == (frames[i].Flags & RtSourceInitializationResponsibilityFlag));
            if (receipt.Flags & RtSourceInitializationResponsibilityFlag) ++responsibilityReceipts;
        }
        CHECK(releaseReceipt(owner, frames[i].Sequence, frames[i].Token) == 1);
    }
    frameCount = 0;
}
void Setup(bool host) {
    readOwner = Symbol<decltype(readOwner)>("RtPlaybackSourceReadOwner");
    readReceipt = Symbol<decltype(readReceipt)>("RtPlaybackSourceReadReceipt");
    releaseReceipt = Symbol<decltype(releaseReceipt)>("RtPlaybackSourceReleaseReceipt");
    configure = Symbol<decltype(configure)>("RtPlaybackSourceConfigure");
    setArmed = Symbol<decltype(setArmed)>("RtPlaybackSourceSetArmed");
    readResource = Symbol<decltype(readResource)>("RtPlaybackSourceReadProgressResource");
    stageResource = Symbol<decltype(stageResource)>("RtPlaybackSourceStageProgressResource");
    readTicket = Symbol<decltype(readTicket)>("RtPlaybackSourceReadProgressResourceTicket");
    bindResource = Symbol<decltype(bindResource)>("RtPlaybackSourceBindInitializationResource");
    retireResource = Symbol<decltype(retireResource)>("RtPlaybackSourceRetireProgressResource");
    stagePlayer = Symbol<decltype(stagePlayer)>("RtPlaybackSourceStagePlayerWatch");
    readPlayer = Symbol<decltype(readPlayer)>("RtPlaybackSourceReadPlayerWatch");
    retirePlayer = Symbol<decltype(retirePlayer)>("RtPlaybackSourceRetirePlayerWatch");
    stageProgress = Symbol<decltype(stageProgress)>("RtPlaybackSourceStageProgressWatch");
    readProgress = Symbol<decltype(readProgress)>("RtPlaybackSourceReadProgressWatch");
    retireProgress = Symbol<decltype(retireProgress)>("RtPlaybackSourceRetireProgressWatch");
    retryResource = Symbol<decltype(retryResource)>("RtPlaybackSourceRetryResourceInitialization");
    readInitialization = Symbol<ReadInitialization>("RtPlaybackSourceReadInitialization");
    beginCapture = Symbol<BeginCapture>("RtPlaybackSourceBeginInitialCapture");
    beginHandoff = Symbol<BeginCapture>("RtPlaybackSourceBeginInitializationHandoff");
    logicalAction = Symbol<LogicalAction>("RtPlaybackSourceLogicalAction");
    CHECK(Symbol<decltype(&RtPlaybackProgressResourceVersion)>("RtPlaybackProgressResourceVersion")() == 1);
    owner = Symbol<decltype(&RtPlaybackSourceCreateOwner)>("RtPlaybackSourceCreateOwner")(Observe, nullptr); CHECK(owner);
    const std::uint32_t kinds[] = {RtSourceRef1, RtSourceBool1, RtSourceRef1, RtSourceBooleanReturn0, RtSourceRef1, RtSourceRef1, RtSourceSingle1};
    const std::uint32_t roles[] = {RtSourcePlayerConstructor, RtSourceProgressPlay, RtSourceProgressMusicPlayer, RtSourceProgressStartMoveNext,
        RtSourceProgressClip, RtSourceSetClip, RtSourceSetPitch};
    void* originals[] = {reinterpret_cast<void*>(&OriginalConstructor), reinterpret_cast<void*>(&OriginalPlay), reinterpret_cast<void*>(&OriginalMusicPlayer),
        reinterpret_cast<void*>(&ResourceStartOriginal), reinterpret_cast<void*>(&ResourceClipLeaf), reinterpret_cast<void*>(&OriginalSetClip), reinterpret_cast<void*>(&OriginalPitch)};
    const auto entry = Symbol<decltype(&RtPlaybackSourceGetEntry)>("RtPlaybackSourceGetEntry");
    for (unsigned i = 0; i < 7; ++i) CHECK(configure(owner, kinds[i], i, roles[i],
        i == 5 ? reinterpret_cast<void*>(&ResourceSetClipTarget) : &targets[i], originals[i]) == 1);
    clipEntry = reinterpret_cast<RefEntry>(entry(RtSourceRef1, 4));
    playEntry = reinterpret_cast<BoolEntry>(entry(RtSourceBool1, 1));
    startEntry = reinterpret_cast<StartEntry>(entry(RtSourceBooleanReturn0, 3));
    pitchEntry = reinterpret_cast<PitchEntry>(entry(RtSourceSingle1, 6));
    ResourceSetClipEntry = entry(RtSourceRef1, 5);
    CHECK(setArmed(owner, Health().OwnerEpoch, 1) == 1);
    CHECK(Symbol<decltype(&RtPlaybackSourceConfigurePlayerWatch)>("RtPlaybackSourceConfigurePlayerWatch")(owner, &playerClass, &audioClass) == 1);
    CHECK(Symbol<decltype(&RtPlaybackSourceConfigureProgressWatch)>("RtPlaybackSourceConfigureProgressWatch")(owner, &progressClass, &startClass) == 1);
    CHECK(Symbol<ConfigureInitialization>("RtPlaybackSourceConfigureInitialization")(owner, const_cast<unsigned char*>(&ResourceClipReturned)) == 1);
    if (host) {
        CHECK(Symbol<decltype(&RtPlaybackSourceBindHostProgressWatch)>("RtPlaybackSourceBindHostProgressWatch")(owner, &progress, &progressReceipt) == 1);
        hostReceipt = progressReceipt;
    } else {
        CHECK(stagePlayer(owner, &player, &audio, &playerReceipt) == 1);
        CHECK(stageProgress(owner, &progress, playerReceipt, &progressReceipt) == 1);
        CHECK(Symbol<decltype(&RtPlaybackSourceActivateInitialPlayerWatch)>("RtPlaybackSourceActivateInitialPlayerWatch")(owner, playerReceipt) == 1);
    }
    const unsigned char prefix[] = {0x48, 0x89, 0x51, 0x30, 0x48, 0x83, 0xc1, 0x30, 0xe9};
    CHECK(std::memcmp(reinterpret_cast<const void*>(&ResourceClipLeaf), prefix, sizeof(prefix)) == 0);
}
DWORD WINAPI ForeignWorker(void*) {
    for (unsigned i = 0; i < 32; ++i) {
        clipEntry(&foreign, &referenceB, nullptr); clipEntry(&unrelated, nullptr, reinterpret_cast<void*>(0x12345678));
    }
    return 0;
}
DWORD WINAPI CurrentWorker(void*) { clipEntry(&progress, &referenceB, nullptr); return 0; }
void RunWorker(LPTHREAD_START_ROUTINE work) {
    const auto thread = CreateThread(nullptr, 0, work, nullptr, 0, nullptr); CHECK(thread);
    CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
}
void RevisionChecks() {
    const auto baseline = Health();
    void* values[] = {&referenceA, &referenceB, &referenceA, nullptr, nullptr, &referenceB};
    void* methods[] = {nullptr, &startClass, reinterpret_cast<void*>(0x12345678), nullptr, &otherClass, reinterpret_cast<void*>(0x23456789)};
    for (unsigned i = 0; i < 6; ++i) {
        futureTicket = Stage(); const auto before = Resource(); const auto old = progress.Clip;
        clipEntry(&progress, values[i], methods[i]);
        const auto after = Resource(); const auto ticket = Ticket(futureTicket);
        CHECK(after.Revision == before.Revision + 1 && after.ContinuityIdentity == before.ContinuityIdentity);
        CHECK(progress.Clip == values[i] && ticket.Phase == 4 && !ticket.Current);
        CHECK(lastField == &progress.Clip && lastValue == values[i] && lastMethod == methods[i]);
        CHECK(frames[frameCount - 1].ArgBits == reinterpret_cast<std::uint64_t>(values[i]));
        CHECK(old == reinterpret_cast<Reference*>(before.Reference));
        CHECK(retireResource(owner, futureTicket) == 1 && Ticket(futureTicket).Phase == 5);
        futureTicket = nullptr; DrainJournal();
    }
    const auto final = Health();
    CHECK(FencesSame(baseline, final) && final.Entered == baseline.Entered + 6 && final.Exited == baseline.Exited + 6);
    CHECK(barriers == 6 && resourceCallbacks == 6 && returned == 6 && final.Flags == RtSourceArmed);
    RtPlaybackProgressResourceInfoV1 bad{7, sizeof(bad)}; const auto saved = bad;
    CHECK(readResource(owner, progressReceipt, &bad) == 0 && std::memcmp(&bad, &saved, sizeof(bad)) == 0);
    CHECK(bindResource(owner, reinterpret_cast<void*>(0x12345678)) == 0);
}
void ForeignChecks() {
    const auto baseline = Health(); const auto resource = Resource();
    clipEntry(&foreign, &referenceB, &otherClass); clipEntry(&unrelated, nullptr, nullptr);
    RunWorker(ForeignWorker);
    CHECK(Same(baseline, Health()) && Resource().Revision == resource.Revision);
    CHECK(barriers == 66 && observed == 0 && frameCount == 0);
    nestedPlay = true; playEntry(&progress, 1, nullptr);
    CHECK(frameCount == 2 && frames[1].ParentSequence == playSequence && frames[1].Depth == 2);
    CHECK(Health().SchedulingSequence == baseline.SchedulingSequence + 1 && Resource().Revision == resource.Revision + 1);
    DrainJournal();
    nestedForeignChild = true; playEntry(&progress, 1, nullptr);
    CHECK(frameCount == 3 && frames[1].ParentSequence == 0 && frames[1].Depth == 1
        && frames[2].ParentSequence == playSequence && frames[2].Depth == 2);
    DrainJournal();
    nestedForeignChild = false; nestedForeignThrow = true; playEntry(&progress, 1, nullptr);
    CHECK(frameCount == 2 && frames[1].ParentSequence == playSequence && frames[1].Depth == 2);
    CHECK(Health().Flags == RtSourceArmed); DrainJournal();
}
void StartChecks(bool interrupted, bool lateInterrupted) {
    gate = Symbol<BindGate>("RtPlaybackSourceBindLogicalGate")(owner, Commit, &clipB); CHECK(gate);
    futureTicket = Stage(); const auto initialFences = Health(); clipEntry(&progress, &referenceB, nullptr);
    CHECK(FencesSame(initialFences, Health()) && audio.Clip == &clipA && Ticket(futureTicket).Current == 0);
    CHECK(retireResource(owner, futureTicket) == 1); futureTicket = nullptr; DrainJournal();
    bindOnStart = true; interruptStart = interrupted;
    CHECK(startEntry(&start, nullptr) == 1);
    if (interrupted) {
        auto stale = initializationTicket; CHECK(Ticket(stale).Phase == 4 && retireResource(owner, stale) == 1);
        RizPlaybackInitializationInfoV2 invalid{2, sizeof(invalid)};
        CHECK(readInitialization(owner, &invalid) == 1 && invalid.phase == 5 && !invalid.receipt && invalid.blocked);
        interruptStart = false; start.State = 0; DrainJournal(); CHECK(startEntry(&start, nullptr) == 1);
    }
    CHECK(startEntry(&start, nullptr) == 0);
    CHECK(referenceB.Calls == 1 && referenceA.Calls == 0 && setClips == 1 && audio.Clip == &clipB);
    RizPlaybackInitializationInfoV2 candidate{2, sizeof(candidate)};
    CHECK(readInitialization(owner, &candidate) == 1 && candidate.phase == 4 && candidate.receipt);
    CHECK(Ticket(initializationTicket).Phase == 2 && Ticket(initializationTicket).Current == 1);
    if (lateInterrupted) {
        auto old = initializationTicket; clipEntry(&progress, &referenceA, nullptr);
        CHECK(readInitialization(owner, &candidate) == 1 && candidate.phase == 5 && !candidate.receipt);
        CHECK(beginCapture(gate, reinterpret_cast<void*>(Ticket(old).Initializer), &clipA) != 0 && commitCalls == 0);
        CHECK(retireResource(owner, old) == 1); DrainJournal();
        start.State = 0; CHECK(startEntry(&start, nullptr) == 1); CHECK(startEntry(&start, nullptr) == 0);
        CHECK(referenceA.Calls == 1 && setClips == 2 && audio.Clip == &clipA);
        CHECK(readInitialization(owner, &candidate) == 1 && candidate.phase == 4 && candidate.receipt);
    }
    CHECK(beginCapture(gate, reinterpret_cast<void*>(candidate.receipt), &clipA) == 0);
    CHECK(logicalAction(gate, 8, &clipA) == 0);
    const auto continuity = Ticket(initializationTicket).ContinuityIdentity;
    const auto oldWatch = progressReceipt;
    CHECK(stagePlayer(owner, &player, &audio, &playerReceipt) == 1);
    CHECK(stageProgress(owner, &progress, playerReceipt, &progressReceipt) == 1);
    CHECK(Resource().ContinuityIdentity == continuity && Ticket(initializationTicket).Current == 1);
    CHECK(logicalAction(gate, 9, &clipA) == 0 && logicalAction(gate, 10, &clipA) == 0);
    CHECK(Ticket(initializationTicket).Phase == 3 && Ticket(initializationTicket).Current == 0);
    CHECK(retireProgress(owner, oldWatch) == 1 && Resource().ContinuityIdentity == continuity);
    const auto opened = Health(); const auto currentClip = audio.Clip;
    clipEntry(&progress, &referenceB, nullptr);
    CHECK(FencesSame(opened, Health()) && audio.Clip == currentClip && Ticket(initializationTicket).Phase == 3);
    CHECK(logicalAction(gate, 11, &clipA) == 0);
    CHECK(retireResource(owner, initializationTicket) == 1);
    CHECK(readInitialization(owner, &candidate) == 1 && candidate.phase == 7 && !candidate.blocked);
    DrainJournal();
}
RizPlaybackInitializationInfoV2 CompleteActualStart() {
    start.State = 0; bindOnStart = true;
    CHECK(startEntry(&start, nullptr) == 1);
    const auto yielded = Initialization();
    CHECK(yielded.phase == 2 && yielded.blocked && !yielded.receipt && Health().InFlight == 0);
    CHECK(Ticket(initializationTicket).Phase == 2 && Ticket(initializationTicket).Current == 1);
    CHECK(startEntry(&start, nullptr) == 0);
    const auto candidate = Initialization();
    CHECK(candidate.phase == 4 && candidate.receipt && candidate.blocked && Health().InFlight == 0);
    CHECK(candidate.player == reinterpret_cast<std::uint64_t>(&player)
        && candidate.source == reinterpret_cast<std::uint64_t>(&audio)
        && candidate.clip == reinterpret_cast<std::uint64_t>(audio.Clip));
    CHECK(candidate.receipt == Ticket(initializationTicket).Initializer);
    // Each original has really returned and its native physical exit journal
    // is checked before any replacement/capture operation is attempted.
    DrainJournal();
    return candidate;
}
void CheckNoJournalFrames() {
    for (unsigned i = 0; i < frameCount; ++i) {
        CHECK((frames[i].Flags & RtSourceDormant) && !(frames[i].Flags & RtSourceInitializationResponsibilityFlag));
        RtPlaybackSourceFrame receipt{}; std::memset(&receipt, 0xa5, sizeof(receipt));
        const auto untouched = receipt;
        CHECK(readReceipt(owner, frames[i].Sequence, frames[i].Token, &receipt) == 0);
        CHECK(std::memcmp(&receipt, &untouched, sizeof(receipt)) == 0);
        CHECK(releaseReceipt(owner, frames[i].Sequence, frames[i].Token) == 0);
        ++responsibilityNoJournal;
    }
    frameCount = 0;
}
void EnableResponsibilityChecks() {
    readCurrent = Symbol<decltype(readCurrent)>("RtPlaybackSourceReadCurrent");
    verifyCurrent = Symbol<decltype(verifyCurrent)>("RtPlaybackSourceVerifyCurrent");
    responsibilityChecks = true;
}
void ResponsibilityChecks(const char* mode) {
    gate = Symbol<BindGate>("RtPlaybackSourceBindLogicalGate")(owner, Commit, &clipB); CHECK(gate);
    if (std::strstr(mode, "closed")) {
        EnableResponsibilityChecks();
        CHECK(logicalAction(gate, 0, &clipA) == 0);
        // FreshBlocked is still false: even resource roles have no completion
        // row here. Ordinary non-initializer Dormant roles stay unqualified.
        clipEntry(&progress, &referenceA, nullptr);
        playEntry(&progress, 0, nullptr);
        pitchEntry(&audio, 1.0f, nullptr);
        reinterpret_cast<RefEntry>(ResourceSetClipEntry)(&audio, &clipA, nullptr);
        // A host watch has no player/audio roots before the actual Start;
        // those two originals execute transparently with no source frame.
        const unsigned ordinaryFrames = hostReceipt ? 2u : 4u;
        CHECK(pitches == 1 && setClips == 1);
        CHECK(frameCount == ordinaryFrames && responsibilityBefore == 0); CheckNoJournalFrames();
        bindOnStart = true; start.State = 0;
        CHECK(startEntry(&start, nullptr) == 1);
        CHECK(Initialization().phase == 2 && Initialization().blocked);
        CHECK(frameCount == 1 && responsibilityBefore == 0); CheckNoJournalFrames();
        // The real yielded initializer now holds FreshBlocked. Its final
        // parent, exact set_clip child and resource setter each own a row.
        CHECK(startEntry(&start, nullptr) == 0);
        CHECK(Initialization().phase == 4 && responsibilityBefore == 2);
        clipEntry(&progress, &referenceB, nullptr);
        CHECK(Initialization().phase == 5 && Ticket(initializationTicket).Phase == 4);
        CHECK(frameCount == 3 && responsibilityBefore == 3 && responsibilityReturned == 3);
        DrainJournal();
        CHECK(responsibilityReceipts == 3 && responsibilityNoJournal == ordinaryFrames + 1);
        CHECK(Health().Flags == RtSourceArmed && !Health().Faults && !Health().InFlight);
        CHECK(retireResource(owner, initializationTicket) == 1);
        return;
    }
    const auto first = CompleteActualStart();
    CHECK(beginCapture(gate, reinterpret_cast<void*>(first.receipt), &clipA) == 0);
    CHECK(logicalAction(gate, 8, &clipA) == 0);
    EnableResponsibilityChecks();
    if (std::strstr(mode, "worker")) {
        const auto before = Health(); const auto callbacks = observed;
        // This owned process has one owner and no intervening native entry.
        // The previous actual callback token locates the next diagnostic row;
        // it grants no TLS, ticket, root retirement or source authority.
        const auto token = lastObservedToken + 1;
        RunWorker(CurrentWorker);
        const auto after = Health(); RtPlaybackSourceFrame receipt{};
        CHECK(observed == callbacks && frameCount == 0 && responsibilityTlsChecks == 0);
        CHECK(after.Entered == before.Entered + 1 && after.Exited == before.Exited + 1 && !after.InFlight);
        CHECK((after.Flags & RtSourceUnknown) && !(after.Flags & RtSourceInitializationResponsibilityFlag));
        CHECK(readReceipt(owner, after.Entered, token, &receipt) == 1);
        CHECK(receipt.Role == RtSourceProgressClip && receipt.Self == reinterpret_cast<std::uint64_t>(&progress)
            && receipt.OwnerEpoch == after.OwnerEpoch && receipt.Stage == RtSourcePhysicalExit);
        CHECK((receipt.Flags & (RtSourceUnknown | RtSourceDormant | RtSourceReturnedFlag | RtSourcePhysicalExitedFlag))
            == (RtSourceUnknown | RtSourceDormant | RtSourceReturnedFlag | RtSourcePhysicalExitedFlag));
        CHECK(!(receipt.Flags & RtSourceInitializationResponsibilityFlag));
        CHECK(verifyCurrent(owner, &receipt) == 0 && retireResource(owner, initializationTicket) == 0);
        CHECK(releaseReceipt(owner, receipt.Sequence, receipt.Token) == 1); ++responsibilityWorkerReceipts;
        CHECK(Ticket(initializationTicket).Phase == 4 && Initialization().phase == 5);
    } else if (std::strstr(mode, "saturation")) {
        for (unsigned i = 0; i < 256; ++i) clipEntry(&progress, &referenceA, nullptr);
        CHECK(responsibilityBefore == 256 && Health().Flags == RtSourceArmed);
        clipEntry(&progress, &referenceB, nullptr);
        CHECK(frameCount == 257 && responsibilityBefore == 256 && (Health().Flags & RtSourceUnknown));
        const auto failed = frames[256];
        CHECK((failed.Flags & RtSourceUnknown) && !(failed.Flags & RtSourceInitializationResponsibilityFlag));
        frameCount = 256; DrainJournal();
        frames[0] = failed; frameCount = 1; CheckNoJournalFrames(); ++responsibilityAllocationFailures;
        CHECK(responsibilityReceipts == 256 && responsibilityNoJournal == 1);
        CHECK(retireResource(owner, initializationTicket) == 0);
    } else {
        const bool seh = std::strstr(mode, "seh") != nullptr;
        if (seh) { throwBarrier = true; CHECK(CallClipCatch(&progress, &referenceB, nullptr)); }
        else { CHECK(std::strstr(mode, "class")); progress.Class = &otherClass; clipEntry(&progress, &referenceB, nullptr); }
        CHECK((Health().Flags & RtSourceUnknown) && !Health().InFlight && frameCount == 1);
        RtPlaybackSourceFrame receipt{};
        CHECK(readReceipt(owner, frames[0].Sequence, frames[0].Token, &receipt) == 1);
        CHECK((receipt.Flags & (RtSourceUnknown | RtSourcePhysicalExitedFlag)) == (RtSourceUnknown | RtSourcePhysicalExitedFlag));
        CHECK((receipt.Flags & RtSourceInitializationResponsibilityFlag) == (seh ? RtSourceInitializationResponsibilityFlag : 0u));
        CHECK(responsibilityBefore == (seh ? 1u : 0u) && retireResource(owner, initializationTicket) == 0);
        if (seh) CHECK((receipt.Flags & RtSourceAbnormalFlag) && !(receipt.Flags & RtSourceReturnedFlag));
        DrainJournal(); CHECK(responsibilityReceipts == (seh ? 1u : 0u));
    }
    CHECK(!(Health().Flags & RtSourceInitializationResponsibilityFlag));
}
void StagePendingAndRetirePredecessor() {
    const auto previousProgress = progressReceipt;
    const auto previousPlayer = playerReceipt;
    CHECK(stagePlayer(owner, &player, &audio, &playerReceipt) == 1);
    CHECK(stageProgress(owner, &progress, playerReceipt, &progressReceipt) == 1);
    CHECK(PlayerWatch(playerReceipt).Phase == 1 && ProgressWatch(progressReceipt).Phase == 1);
    CHECK(retireProgress(owner, previousProgress) == 1 && ProgressWatch(previousProgress).Phase == 3);
    if (previousPlayer) CHECK(retirePlayer(owner, previousPlayer) == 1 && PlayerWatch(previousPlayer).Phase == 3);
    CHECK(Resource().Current == 1 && Ticket(initializationTicket).Current == 1);
}
void CheckStaleInitializer(void* prior, void* fresh, std::uint32_t action) {
    const auto before = commitCalls;
    CHECK(beginCapture(gate, prior, &clipA) == 2); ++staleRejections;
    CHECK(beginHandoff(gate, prior, &clipA) == 2); ++staleRejections;
    CHECK(retryResource(gate, prior, prior, action, &clipA) == 2); ++staleRejections;
    if (fresh) { CHECK(retryResource(gate, reinterpret_cast<void*>(0x12345678), fresh, action, &clipA) == 2); ++staleRejections; }
    CHECK(commitCalls == before);
}
void Phase6RetryChecks(bool pending, bool handoff, bool same, bool aba) {
    gate = Symbol<BindGate>("RtPlaybackSourceBindLogicalGate")(owner, Commit, &clipB); CHECK(gate);
    const auto originalProgress = progressReceipt;
    const auto originalPlayer = playerReceipt;
    const auto first = CompleteActualStart();
    const auto prior = reinterpret_cast<void*>(first.receipt);
    const auto priorTicket = initializationTicket;
    const auto priorResource = Ticket(priorTicket);
    priorOrdinal = first.ordinal;
    CHECK(referenceA.Calls == 1 && referenceB.Calls == 0 && setClips == 1 && audio.Clip == &clipA);
    CHECK((handoff ? beginHandoff : beginCapture)(gate, prior, &clipA) == 0);
    CHECK(Initialization().phase == 6 && !Initialization().receipt && Initialization().blocked);
    CHECK(logicalAction(gate, 8, &clipA) == 0);
    if (pending) StagePendingAndRetirePredecessor();
    const auto oldPlayer = pending ? playerReceipt : nullptr;
    const auto oldProgress = pending ? progressReceipt : nullptr;
    const auto beforeWrite = Health();
    clipEntry(&progress, same ? &referenceA : &referenceB, nullptr);
    if (aba) clipEntry(&progress, &referenceA, nullptr);
    const auto resource = Resource();
    CHECK(FencesSame(beforeWrite, Health()) && audio.Clip == &clipA && setClips == 1
        && referenceA.Calls == 1 && referenceB.Calls == 0);
    CHECK(resource.Revision == priorResource.Revision + (aba ? 2u : 1u)
        && resource.ContinuityIdentity == priorResource.ContinuityIdentity);
    CHECK(Ticket(priorTicket).Phase == 4 && !Ticket(priorTicket).Current);
    CHECK(Initialization().phase == 5 && Initialization().blocked && !Initialization().receipt);
    const auto calls = commitCalls;
    CHECK(logicalAction(gate, 8, &clipA) == 8 && logicalAction(gate, 9, &clipA) == 8
        && logicalAction(gate, 10, &clipA) == 8 && commitCalls == calls);
    CheckStaleInitializer(prior, nullptr, handoff ? 13u : 12u);
    if (pending) {
        // The old seven-root pending watch is now the sole Progress identity:
        // the host/active predecessor was retired, but no pending root is cut.
        CHECK(ProgressWatch(originalProgress).Phase == 3 && ProgressWatch(oldProgress).Phase == 1
            && !ProgressWatch(oldProgress).Active && PlayerWatch(oldPlayer).Phase == 1);
    }
    DrainJournal();
    retryPriorForCallback = prior; retryActionForCallback = handoff ? 13u : 12u;
    const auto fresh = CompleteActualStart();
    retryPriorForCallback = nullptr;
    const auto candidate = reinterpret_cast<void*>(fresh.receipt);
    freshOrdinal = fresh.ordinal; expectedRetryInitializer = fresh.receipt;
    CHECK(candidate != prior && fresh.ordinal > first.ordinal && fresh.sourceFence > first.sourceFence);
    CHECK(Ticket(priorTicket).Initializer == first.receipt && Ticket(priorTicket).InitializerOrdinal == first.ordinal
        && Ticket(priorTicket).Phase == 4 && !Ticket(priorTicket).Current);
    CHECK(Ticket(initializationTicket).ContinuityIdentity == priorResource.ContinuityIdentity
        && Ticket(initializationTicket).Revision == resource.Revision && Ticket(initializationTicket).Current == 1);
    CHECK(audio.Clip == ((same || aba) ? &clipA : &clipB) && setClips == 2
        && referenceA.Calls == ((same || aba) ? 2u : 1u) && referenceB.Calls == ((same || aba) ? 0u : 1u));
    CheckStaleInitializer(prior, candidate, retryActionForCallback);
    const auto beforeBad = commitCalls;
    CHECK(retryResource(gate, prior, candidate, 11, &clipA) == 1 && commitCalls == beforeBad);
    rejectRetryCommit = true;
    CHECK(retryResource(gate, prior, candidate, retryActionForCallback, &clipA) == 6);
    CHECK(Initialization().phase == 4 && Initialization().receipt == fresh.receipt
        && Ticket(initializationTicket).Current == 1 && Ticket(priorTicket).Current == 0);
    if (pending) CHECK(PlayerWatch(oldPlayer).Phase == 1 && ProgressWatch(oldProgress).Phase == 1);
    rejectRetryCommit = false;
    CHECK(retryResource(gate, prior, candidate, retryActionForCallback, &clipA) == 0);
    CHECK(Initialization().phase == 6 && !Initialization().receipt && Initialization().blocked);
    CHECK(retryAttempts == 2 && retryAccepted == 1 && inFlightRetryRejections == 2);
    CHECK(retryResource(gate, prior, candidate, retryActionForCallback, &clipA) == 2); ++staleRejections;
    CHECK(retireResource(owner, priorTicket) == 1 && Ticket(priorTicket).Phase == 5
        && Ticket(initializationTicket).Current == 1 && Initialization().phase == 6);
    CheckStaleInitializer(prior, candidate, retryActionForCallback);
    CHECK(logicalAction(gate, 8, &clipA) == 0);
    CHECK(stagePlayer(owner, &player, &audio, &playerReceipt) == 1);
    if (pending) {
        CHECK(playerReceipt != oldPlayer && PlayerWatch(oldPlayer).Phase == 3
            && PlayerWatch(playerReceipt).Phase == 1 && ProgressWatch(oldProgress).Phase == 1);
        // Player replacement must leave the unique old Progress identity alive
        // until the exact Progress watch is replaced under the same native lock.
        CHECK(Resource().ContinuityIdentity == priorResource.ContinuityIdentity && Ticket(initializationTicket).Current == 1);
        CHECK(retirePlayer(owner, oldPlayer) == 1 && PlayerWatch(playerReceipt).Phase == 1);
        void* invalid{};
        CHECK(stageProgress(owner, &foreign, playerReceipt, &invalid) == 0 && !invalid);
        CHECK(ProgressWatch(oldProgress).Phase == 1 && Ticket(initializationTicket).Current == 1);
    }
    CHECK(stageProgress(owner, &progress, playerReceipt, &progressReceipt) == 1);
    CHECK(Resource().ContinuityIdentity == priorResource.ContinuityIdentity && Ticket(initializationTicket).Current == 1);
    if (pending) {
        CHECK(progressReceipt != oldProgress && ProgressWatch(oldProgress).Phase == 3);
        CHECK(retireProgress(owner, oldProgress) == 1 && retirePlayer(owner, oldPlayer) == 1);
        CHECK(ProgressWatch(progressReceipt).Phase == 1 && PlayerWatch(playerReceipt).Phase == 1
            && Ticket(initializationTicket).Current == 1); ++pendingReplacements;
    }
    CHECK(retireProgress(owner, originalProgress) == 1 && Ticket(initializationTicket).Current == 1);
    if (originalPlayer) CHECK(retirePlayer(owner, originalPlayer) == 1 && PlayerWatch(playerReceipt).Phase == 1);
    CHECK(logicalAction(gate, 9, &clipA) == 0 && logicalAction(gate, 10, &clipA) == 0);
    CHECK(Initialization().phase == 7 && !Initialization().blocked && Ticket(initializationTicket).Phase == 3);
    CHECK(ProgressWatch(progressReceipt).Phase == 2 && ProgressWatch(progressReceipt).Active
        && PlayerWatch(playerReceipt).Phase == 2 && PlayerWatch(playerReceipt).Active);
    if (pending) {
        CHECK(retireProgress(owner, oldProgress) == 1 && retirePlayer(owner, oldPlayer) == 1);
        CHECK(ProgressWatch(progressReceipt).Active && PlayerWatch(playerReceipt).Active);
    }
    CHECK(retireResource(owner, priorTicket) == 1 && Ticket(priorTicket).Phase == 5);
    CheckStaleInitializer(prior, candidate, retryActionForCallback);
    CHECK(logicalAction(gate, 11, &clipA) == 0 && retireResource(owner, initializationTicket) == 1);
    CHECK(Health().Flags == RtSourceArmed && !Health().Faults && !Health().InFlight);
}
void PendingUnknownChecks(const char* mode) {
    gate = Symbol<BindGate>("RtPlaybackSourceBindLogicalGate")(owner, Commit, &clipB); CHECK(gate);
    const auto first = CompleteActualStart();
    priorOrdinal = first.ordinal;
    const auto prior = reinterpret_cast<void*>(first.receipt);
    CHECK(beginCapture(gate, prior, &clipA) == 0 && logicalAction(gate, 8, &clipA) == 0);
    StagePendingAndRetirePredecessor();
    const auto retainedPlayer = playerReceipt;
    const auto retainedProgress = progressReceipt;
    const auto retainedTicket = initializationTicket;
    if (std::strstr(mode, "worker")) RunWorker(CurrentWorker);
    else if (std::strstr(mode, "seh")) {
        throwBarrier = true; CHECK(CallClipCatch(&progress, &referenceB, nullptr));
    } else {
        CHECK(std::strstr(mode, "class") != nullptr);
        progress.Class = &otherClass; clipEntry(&progress, &referenceB, nullptr);
    }
    CHECK((Health().Flags & RtSourceUnknown) && !Health().InFlight && Health().Entered == Health().Exited);
    CHECK(Initialization().phase == 5 && Initialization().blocked && !Initialization().receipt);
    CHECK(Ticket(retainedTicket).Phase == 4 && !Ticket(retainedTicket).Current);
    if (std::strstr(mode, "withdraw")) {
        CHECK(setArmed(owner, Health().OwnerEpoch, 0) == 1);
        CHECK((Health().Flags & (RtSourceUnknown | RtSourceWithdrawn)) == (RtSourceUnknown | RtSourceWithdrawn));
    }
    const auto before = commitCalls;
    CHECK(retryResource(gate, prior, prior, 12, &clipA) == 3 && logicalAction(gate, 8, &clipA) == 3);
    for (unsigned i = 0; i < 2; ++i) {
        CHECK(retireResource(owner, retainedTicket) == 0); ++unknownRootRefusals;
        CHECK(retireProgress(owner, retainedProgress) == 0); ++unknownRootRefusals;
        CHECK(retirePlayer(owner, retainedPlayer) == 0); ++unknownRootRefusals;
        CHECK(Ticket(retainedTicket).Phase == 4 && ProgressWatch(retainedProgress).Phase == 1
            && PlayerWatch(retainedPlayer).Phase == 1);
    }
    void* rejected{};
    CHECK(stagePlayer(owner, &player, &audio, &rejected) == 0 && !rejected);
    CHECK(stageResource(owner, retainedProgress, Ticket(retainedTicket).Revision + 1, &referenceB, &rejected) == 0 && !rejected);
    CHECK(commitCalls == before && !retryAttempts && unknownRootRefusals == 6);
    // Static owned objects remain rooted for process lifetime. A native known
    // physical exit does not authorize root retirement once owner is Unknown.
    DrainJournal();
}
void WithdrawChecks(const char* mode) {
    const bool future = std::strstr(mode, "future") != nullptr;
    const bool yielded = std::strstr(mode, "yield") != nullptr;
    const bool physical = std::strstr(mode, "inflight") != nullptr;
    const bool phase6 = std::strstr(mode, "phase6") != nullptr;
    const bool consumed = std::strstr(mode, "consumed") != nullptr;
    if (phase6 || consumed) {
        gate = Symbol<BindGate>("RtPlaybackSourceBindLogicalGate")(owner, Commit, &clipB); CHECK(gate);
    }
    if (future) initializationTicket = Stage();
    else if (yielded || physical) {
        bindOnStart = true; withdrawDuringStart = physical;
        CHECK(startEntry(&start, nullptr) == 1);
        CHECK(Initialization().phase == (physical ? 5u : 2u));
        CHECK(withdrawInFlightRejections == (physical ? 1u : 0u));
        DrainJournal();
    } else {
        const auto candidate = CompleteActualStart();
        if (phase6 || consumed) CHECK(beginCapture(gate, reinterpret_cast<void*>(candidate.receipt), &clipA) == 0);
        if (consumed) {
            CHECK(logicalAction(gate, 8, &clipA) == 0);
            StagePendingAndRetirePredecessor();
            CHECK(logicalAction(gate, 9, &clipA) == 0 && logicalAction(gate, 10, &clipA) == 0
                && logicalAction(gate, 11, &clipA) == 0);
        }
    }
    const auto retained = Ticket(initializationTicket);
    CHECK(retained.Phase == (future ? 1u : consumed ? 3u : 2u));
    CHECK(!Health().InFlight && !Health().Faults && !(Health().Flags & RtSourceUnknown));
    CHECK(setArmed(owner, Health().OwnerEpoch, 0) == 1);
    CHECK((Health().Flags & RtSourceWithdrawn) && !(Health().Flags & (RtSourceArmed | RtSourceUnknown)));
    CHECK(!Ticket(initializationTicket).Current && Ticket(initializationTicket).Phase == retained.Phase);
    auto status = retireResource(owner, initializationTicket);
    CHECK(status == 1); if (status == 1) ++withdrawRetired;
    CHECK(Ticket(initializationTicket).Phase == 5 && !Ticket(initializationTicket).Current);
    CHECK(Ticket(initializationTicket).Initializer == retained.Initializer
        && Ticket(initializationTicket).InitializerOrdinal == retained.InitializerOrdinal);
    if (retained.Phase == 2) CHECK(Initialization().phase == 5 && Initialization().blocked && !Initialization().receipt);
    if (consumed) CHECK(Initialization().phase == 7 && !Initialization().blocked);
    status = retireResource(owner, initializationTicket);
    CHECK(status == 1); if (status == 1) ++withdrawRetired;
    void* rejected{};
    CHECK(stageResource(owner, progressReceipt, retained.Revision, reinterpret_cast<void*>(retained.Reference), &rejected) == 0 && !rejected);
    CHECK(setArmed(owner, Health().OwnerEpoch, 1) == 0 && !(Health().Flags & RtSourceArmed));
    CHECK(retireProgress(owner, progressReceipt) == 1);
    if (playerReceipt) CHECK(retirePlayer(owner, playerReceipt) == 1);
    CHECK(withdrawRetired == 2 && !(Health().Flags & RtSourceUnknown) && !Health().InFlight && !Health().Faults);
}
}
extern "C" void __cdecl ResourceBarrierOriginal(void* field, void* value, void* method) {
    ++barriers; lastField = field; lastValue = value; lastMethod = method;
    CHECK(*static_cast<void**>(field) == value);
    if (field == &foreign.Clip && nestedForeignChild) {
        nestedForeignChild = false; clipEntry(&progress, &referenceB, nullptr); nestedForeignChild = true;
    }
    if (throwBarrier || (field == &foreign.Clip && nestedForeignThrow)) RaiseException(0xe0423020u, 0, 0, nullptr);
}
extern "C" void* __cdecl ResourceResolveReference(void* raw) {
    auto* reference = static_cast<Reference*>(raw); CHECK(reference == progress.Clip && reference != nullptr);
    if (!reference) return nullptr; ++reference->Calls; return reference->Clip;
}
int main(int argc, char** argv) {
    if (argc != 3) return 2;
    library = LoadLibraryA(argv[1]); if (!library) return 3;
    const bool host = std::strncmp(argv[2], "host-", 5) == 0;
    Setup(host);
    if (std::strstr(argv[2], "responsibility")) ResponsibilityChecks(argv[2]);
    else if (std::strstr(argv[2], "pending-unknown")) PendingUnknownChecks(argv[2]);
    else if (std::strstr(argv[2], "withdraw")) WithdrawChecks(argv[2]);
    else if (std::strstr(argv[2], "phase6")) Phase6RetryChecks(std::strstr(argv[2], "pending") != nullptr,
        std::strstr(argv[2], "handoff") != nullptr, std::strstr(argv[2], "same") != nullptr,
        std::strstr(argv[2], "aba") != nullptr);
    else if (std::strstr(argv[2], "revision")) RevisionChecks();
    else if (std::strstr(argv[2], "foreign")) ForeignChecks();
    else if (std::strstr(argv[2], "start")) StartChecks(std::strstr(argv[2], "interrupt") != nullptr,
        std::strstr(argv[2], "late") != nullptr);
    else if (std::strcmp(argv[2], "worker") == 0) {
        futureTicket = Stage(); const auto before = Health(); RunWorker(CurrentWorker); const auto final = Health();
        CHECK(FencesSame(before, final) && final.Entered == before.Entered + 1 && final.Exited == before.Exited + 1 && !final.InFlight);
        CHECK(final.Flags & RtSourceUnknown); CHECK(!observed && barriers == 1 && Ticket(futureTicket).Current == 0);
        CHECK(retireResource(owner, futureTicket) == 0);
    } else if (std::strcmp(argv[2], "seh") == 0) {
        futureTicket = Stage(); throwBarrier = true; CHECK(CallClipCatch(&progress, &referenceB, &otherClass));
        CHECK(barriers == 1 && abnormal == 1 && returned == 0 && progress.Clip == &referenceB && (Health().Flags & RtSourceUnknown));
        CHECK(Ticket(futureTicket).Phase == 4 && !Ticket(futureTicket).Current && retireResource(owner, futureTicket) == 0); DrainJournal();
    } else if (std::strcmp(argv[2], "class") == 0) {
        futureTicket = Stage(); progress.Class = &otherClass; clipEntry(&progress, &referenceB, nullptr);
        CHECK(Health().Flags & RtSourceUnknown); CHECK(barriers == 1 && observed == 1 && Ticket(futureTicket).Current == 0);
        CHECK(retireResource(owner, futureTicket) == 0); DrainJournal();
    } else return 4;
    std::printf("{\"kind\":\"owned-progress-resource-source-helper\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"barriers\":%u,\"resourceBefore\":%u,\"actualSetClip\":%u,\"actualResolveA\":%u,\"actualResolveB\":%u,\"retryAttempts\":%u,\"retryAccepted\":%u,\"staleRejections\":%u,\"pendingReplacements\":%u,\"unknownRootRefusals\":%u,\"inFlightRetryRejections\":%u,\"withdrawRetired\":%u,\"withdrawInFlightRejections\":%u,\"priorOrdinal\":%llu,\"freshOrdinal\":%llu,\"sourceWireBytes\":%zu,\"resourceWireBytes\":%zu,\"responsibilityBefore\":%u,\"responsibilityReturned\":%u,\"responsibilityReceipts\":%u,\"responsibilityNoJournal\":%u,\"responsibilityTlsChecks\":%u,\"responsibilityWorkerReceipts\":%u,\"responsibilityAllocationFailures\":%u,\"gameExecuted\":false}\n",
        argv[2], failures ? "false" : "true", checks, failures, barriers, resourceCallbacks, setClips, referenceA.Calls, referenceB.Calls,
        retryAttempts, retryAccepted, staleRejections, pendingReplacements, unknownRootRefusals, inFlightRetryRejections,
        withdrawRetired, withdrawInFlightRejections, priorOrdinal, freshOrdinal,
        sizeof(RtPlaybackSourceFrame), sizeof(RtPlaybackProgressResourceInfoV1), responsibilityBefore, responsibilityReturned,
        responsibilityReceipts, responsibilityNoJournal, responsibilityTlsChecks, responsibilityWorkerReceipts, responsibilityAllocationFailures);
    return failures ? 1 : 0;
}
