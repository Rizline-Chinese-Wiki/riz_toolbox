#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <cstddef>
#include <string>
#include "PlaybackSourceContext.h"
#include "../PlaybackLogicalHandoff.h"
#include "../PlaybackInitialization.h"
#include "../RizToolbox.SingleReturnPrototype/SingleReturnContext.h"

extern "C" {
void* FixtureFirstSetClipEntry = nullptr;
void __cdecl FixtureFirstSetClipTarget(void*, void*, void*);
std::uint8_t __cdecl FixtureFirstStartOriginal(void*, void*);
extern const unsigned char FixtureFirstClipReturned;
}

// Direct helper-level adversarial check. The separate owned stock fixture
// proves actual Dobby relocation, original unwind and real shared body shape.
namespace {
struct Player { void* klass; std::uint64_t padding; void* source; };
static_assert(offsetof(Player, source) == 0x10);
struct FirstProgress {
    void* header[5]; void* source; void* unused; Player* player;
};
struct FirstStart {
    void* klass; void* monitor; std::int32_t state; std::uint32_t padding;
    void* current; FirstProgress* progress; void* unused0; void* unused1;
    void* clip; Player* candidate;
};
static_assert(offsetof(FirstProgress, source) == 0x28 && offsetof(FirstProgress, player) == 0x38);
static_assert(offsetof(FirstStart, state) == 0x10 && offsetof(FirstStart, progress) == 0x20);
static_assert(offsetof(FirstStart, clip) == 0x38 && offsetof(FirstStart, candidate) == 0x40);
struct WatchInfo {
    std::uint32_t version, size, phase, active;
    std::uint64_t owner, epoch, receipt, ordinal, player, klass, expected, cut, writer, source, scheduling, audioSource, audioClass;
};
static_assert(sizeof(WatchInfo) == 120);
using VoidRef = void(__cdecl*)(void*, void*, void*);
using Void0 = void(__cdecl*)(void*, void*);
using ConfigureWatch = int(__cdecl*)(void*, void*, void*);
using StageWatch = int(__cdecl*)(void*, void*, void*, void**);
using ReadWatch = int(__cdecl*)(void*, void*, WatchInfo*);
using ActivateWatch = int(__cdecl*)(void*, void*);
using RetireWatch = int(__cdecl*)(void*, void*);
using BindGate = void*(__cdecl*)(void*, RizLogicalCommit, void*);
using Action = std::uint32_t(__cdecl*)(void*, std::uint32_t, void*);
using ConfigureInitialization = int(__cdecl*)(void*, void*);
using ReadInitialization = int(__cdecl*)(void*, RizPlaybackInitializationInfoV2*);
using SingleConfigure = std::uint32_t(__cdecl*)(std::uint32_t, const RizSingleConfigV1*);
using SingleBind = std::uint32_t(__cdecl*)(std::uint32_t, void*, void*);
using SingleBegin = std::uint32_t(__cdecl*)(std::uint32_t, void*, void**);
using SingleAct = std::uint32_t(__cdecl*)(void*, std::uint32_t, void*, std::uint64_t, std::uint64_t, std::uint64_t);
using SingleRead = std::uint32_t(__cdecl*)(void*, RizPlaybackInitialCaptureInfoV1*);
using SingleActivate = std::uint32_t(__cdecl*)(std::uint32_t, void*, void*);
using SinglePrepare = std::uint32_t(__cdecl*)(std::uint32_t, std::uint64_t, const RizMusicClockPolicyV1*, void**);
using SingleArm = std::uint32_t(__cdecl*)(void*);

decltype(&RtPlaybackSourceCreateOwner) createOwner;
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceGetEntry) entry;
decltype(&RtPlaybackSourceSetArmed) arm;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadCurrent) readCurrent;
decltype(&RtPlaybackSourceVerifyCurrent) verifyCurrent;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
ConfigureWatch configureWatch;
StageWatch stageWatch;
ReadWatch readWatch;
ActivateWatch activateWatch;
RetireWatch retireWatch;
BindGate bindGate;
Action action;
ConfigureInitialization configureInitialization;
ReadInitialization readInitialization;
VoidRef ctorEntry;
Void0 pauseEntry;
using Bool0 = std::uint8_t(__cdecl*)(void*, void*);
Bool0 startEntry;
HMODULE singleLibrary;
SingleConfigure singleConfigure;
SingleBind bindFresh, bindInitialCapture, bindLogical;
SingleBegin beginInitialCapture;
SingleAct actInitialCapture;
SingleRead readInitialCapture;
SingleActivate singleActivate;
SinglePrepare singlePrepare;
SingleArm singleArm;
void* owner;
void* gate;
void* initialReceipt;
int mugClass, foreignClass, audioClass;
struct Audio { void* klass; std::uint64_t value; };
Audio sourceA{&audioClass, 0}, sourceB{&audioClass, 0};
int firstClip, clockMethod;
Player playerA{&mugClass, 0, &sourceA};
Player playerB{&mugClass, 0, &sourceB};
Player foreignPlayer{&foreignClass, 0, &sourceA};
Player pendingForeignPlayer{&mugClass, 0, &sourceA};
enum class PendingCase { None, Same, Aba, Foreign, Worker, Class, Normal };
bool inspectPendingCallbacks, pendingDormantOnly = true, pendingSummary, pendingFinalizeAttempted;
std::uint32_t pendingOpenStatus = UINT32_MAX, pendingExpectedStatus = UINT32_MAX;
std::uint64_t pendingSourceBefore, pendingSourceAfter, pendingEnteredBefore, pendingEnteredAfter;
unsigned pendingOriginals, pendingAttempts, pendingObserved;
PendingCase pendingScenario = PendingCase::None;
const char* PendingName(PendingCase value) {
    switch (value) {
    case PendingCase::Same: return "same"; case PendingCase::Aba: return "aba";
    case PendingCase::Foreign: return "foreign"; case PendingCase::Worker: return "worker";
    case PendingCase::Class: return "class"; case PendingCase::Normal: return "normal";
    default: return "none";
    }
}
PendingCase ParsePending(const wchar_t* suffix) {
    if (std::wcscmp(suffix, L"same") == 0) return PendingCase::Same;
    if (std::wcscmp(suffix, L"aba") == 0) return PendingCase::Aba;
    if (std::wcscmp(suffix, L"foreign") == 0) return PendingCase::Foreign;
    if (std::wcscmp(suffix, L"worker") == 0) return PendingCase::Worker;
    if (std::wcscmp(suffix, L"class") == 0) return PendingCase::Class;
    if (std::wcscmp(suffix, L"normal") == 0) return PendingCase::Normal;
    return PendingCase::None;
}
FirstProgress firstProgress{{}, &sourceB, nullptr, nullptr};
FirstStart firstStart{nullptr, nullptr, 0, 0, nullptr, &firstProgress, nullptr, nullptr, &firstClip, &playerB};
std::atomic<unsigned> originalCalls{}, pauseCalls{}, beforeCallbacks{}, returnedCallbacks{};
int directIndex=-1;bool directLate;
std::atomic<unsigned> directCalls{};
void* directEntries[6]{}; unsigned char directTargets[6]{};
const std::uint32_t directKinds[]={RtSourceUInt64_1,RtSourceDouble1,RtSourceVoid0,RtSourceVoid0,RtSourceBool1,RtSourceBool1};
const std::uint32_t directRoles[]={RtSourceAudioPlayHelper,RtSourceAudioPlayDelayed,RtSourceAudioPlay,RtSourceAudioStop,RtSourceAudioStopBoolean,RtSourceAudioSetLoop};
const std::uint32_t directSlots[]={16,17,18,29,30,31};
void __cdecl DirectStore64(void* self,std::uint64_t value,void*) {++directCalls;static_cast<Audio*>(self)->value=value;}
void __cdecl DirectStoreDouble(void* self,double value,void* method) {std::uint64_t bits;std::memcpy(&bits,&value,8);DirectStore64(self,bits,method);}
void __cdecl DirectStoreVoid(void* self,void* method) {DirectStore64(self,0,method);}
void __cdecl DirectStoreBool(void* self,unsigned char value,void* method) {DirectStore64(self,value,method);}
void* directOriginals[]={reinterpret_cast<void*>(&DirectStore64),reinterpret_cast<void*>(&DirectStoreDouble),reinterpret_cast<void*>(&DirectStoreVoid),
    reinterpret_cast<void*>(&DirectStoreVoid),reinterpret_cast<void*>(&DirectStoreBool),reinterpret_cast<void*>(&DirectStoreBool)};
void InvokeDirect() {
    auto* method=reinterpret_cast<void*>(std::uintptr_t{0xCAFEBABE});
    if(directIndex==0)reinterpret_cast<void(__cdecl*)(void*,std::uint64_t,void*)>(directEntries[0])(&sourceB,0xFEDCBA9876543210ull,method);
    else if(directIndex==1)reinterpret_cast<void(__cdecl*)(void*,double,void*)>(directEntries[1])(&sourceB,1.25,method);
    else if(directIndex<4)reinterpret_cast<void(__cdecl*)(void*,void*)>(directEntries[directIndex])(&sourceB,method);
    else reinterpret_cast<void(__cdecl*)(void*,unsigned char,void*)>(directEntries[directIndex])(&sourceB,0xFF,method);
}

std::atomic<unsigned> setClipCalls{};
std::atomic<unsigned> currentCallbacks{}, outsideCurrentProbes{};
RtPlaybackSourceFrame lastBefore{};
void* beforeStoreValue;
void* lastArg;
void* lastMethod;
void* lastSelf;
RtPlaybackSourceInfo originalHealth{};
std::atomic<unsigned> failures{}, checks{};
std::uint32_t firstInitializerPhase{}, firstActivationStatus{}, firstOpenStatus{}, firstFinalizeStatus{}, firstFinalPhase{};
#define CHECK(value) do { const auto checkNumber = checks.fetch_add(1) + 1; if (!(value)) { failures.fetch_add(1); std::fprintf(stderr, "check %u failed at line %d: %s\n", checkNumber, __LINE__, #value); } } while (0)

template<typename T> T Symbol(HMODULE module, const char* name) {
    auto* address = GetProcAddress(module, name);
    CHECK(address != nullptr);
    return reinterpret_cast<T>(address);
}
RtPlaybackSourceInfo Health() {
    RtPlaybackSourceInfo info{};
    CHECK(readOwner(owner, &info) == 1);
    return info;
}
WatchInfo Watch(void* receipt) {
    WatchInfo info{2, sizeof(WatchInfo)};
    CHECK(readWatch(owner, receipt, &info) == 1);
    return info;
}
void OutsideCurrent() {
    outsideCurrentProbes.fetch_add(1);
    RtPlaybackSourceFrame output{};
    std::memset(&output, 0xA5, sizeof(output));
    const auto unchanged = output;
    CHECK(readCurrent(RtSourceRef1, 0, &output) == 0);
    CHECK(readCurrent(RtSourceVoid0, 1, &output) == 0);
    CHECK(std::memcmp(&output, &unchanged, sizeof(output)) == 0);
    CHECK(verifyCurrent(owner, &lastBefore) == 0);
}
void __cdecl OriginalCtor(void* self, void* value, void* method) {
    originalCalls.fetch_add(1);
    OutsideCurrent();
    lastSelf = self; lastArg = value; lastMethod = method;
    beforeStoreValue = static_cast<Player*>(self)->source;
    originalHealth = Health();
    static_cast<Player*>(self)->source = value;
}
void __cdecl DummyCtor(void*, void*, void*) {}
void __cdecl OriginalPause(void*, void*) { pauseCalls.fetch_add(1); OutsideCurrent(); }
void __cdecl DummyPause(void*, void*) {}
void __cdecl OriginalSetClip(void* self, void* clip, void* method) {
    CHECK(self == &sourceB && clip == &firstClip && method == nullptr);
    setClipCalls.fetch_add(1); OutsideCurrent();
}
void __cdecl DummyStart(void*, void*) {}
float __cdecl OriginalClock(void*, const void*) { return 0.0f; }
std::uintptr_t __cdecl ClockEnter(const RizSingleFrameV1*, void*) { return 0; }
void __cdecl ClockExit(const RizSingleFrameV1*, const RizSingleCompletionV1*, void*) {}
std::uint32_t __cdecl ReadClock(RizMusicClockSampleV1*, void*) { return 0; }
void __cdecl Observe(void*, const RtPlaybackSourceFrame* frame) {
    if (inspectPendingCallbacks && frame->Role == RtSourcePlayerConstructor) {
        ++pendingObserved;
        const bool dormant = (frame->Flags & RtSourceDormant) && !(frame->Flags & RtSourceArmed);
        pendingDormantOnly = pendingDormantOnly && dormant;
        CHECK(dormant); // the existing managed bridge issues no ticket for this frame
    }
    RtPlaybackSourceFrame current{};
    CHECK(readCurrent(frame->Kind, frame->Slot, &current) == 1);
    CHECK(std::memcmp(&current, frame, sizeof(current)) == 0);
    CHECK(verifyCurrent(owner, &current) == 1);
    currentCallbacks.fetch_add(1);
    CHECK(readCurrent(frame->Kind, frame->Slot + 1, &current) == 0);
    CHECK(std::memcmp(&current, frame, sizeof(current)) == 0);
    CHECK(verifyCurrent(&foreignClass, frame) == 0 && verifyCurrent(owner, nullptr) == 0);
    auto fabricated = *frame;
    ++fabricated.Token;
    CHECK(verifyCurrent(owner, &fabricated) == 0);
    CHECK(readReceipt(owner, frame->Sequence, frame->Token, &current) == 0);
    if (frame->Stage == RtSourceBefore) {
        beforeCallbacks.fetch_add(1); lastBefore = *frame;
    } else if (frame->Stage == RtSourceOriginalReturned) returnedCallbacks.fetch_add(1);
}
std::uint32_t __cdecl Commit(void*, void*, std::uint32_t, const RizLogicalSourceSnapshot* snapshot, void*) {
    if (!snapshot || snapshot->owner != reinterpret_cast<std::uint64_t>(owner) || snapshot->inFlight) ++failures;
    return 0;
}
DWORD WINAPI ForeignWorker(void*) {
    for (unsigned i = 0; i < 300; ++i) ctorEntry(&foreignPlayer, reinterpret_cast<void*>(std::uintptr_t{0xA000u} + i), reinterpret_cast<void*>(std::uintptr_t{0xB000u} + i));
    return 0;
}
DWORD WINAPI RelevantWorker(void*) {
    ctorEntry(&playerA, &sourceB, nullptr);
    return 0;
}
bool Setup(HMODULE helper, bool firstCapture) {
    createOwner = Symbol<decltype(createOwner)>(helper, "RtPlaybackSourceCreateOwner");
    configure = Symbol<decltype(configure)>(helper, "RtPlaybackSourceConfigure");
    entry = Symbol<decltype(entry)>(helper, "RtPlaybackSourceGetEntry");
    arm = Symbol<decltype(arm)>(helper, "RtPlaybackSourceSetArmed");
    readOwner = Symbol<decltype(readOwner)>(helper, "RtPlaybackSourceReadOwner");
    readCurrent = Symbol<decltype(readCurrent)>(helper, "RtPlaybackSourceReadCurrent");
    verifyCurrent = Symbol<decltype(verifyCurrent)>(helper, "RtPlaybackSourceVerifyCurrent");
    readReceipt = Symbol<decltype(readReceipt)>(helper, "RtPlaybackSourceReadReceipt");
    releaseReceipt = Symbol<decltype(releaseReceipt)>(helper, "RtPlaybackSourceReleaseReceipt");
    configureWatch = Symbol<ConfigureWatch>(helper, "RtPlaybackSourceConfigurePlayerWatch");
    stageWatch = Symbol<StageWatch>(helper, "RtPlaybackSourceStagePlayerWatch");
    readWatch = Symbol<ReadWatch>(helper, "RtPlaybackSourceReadPlayerWatch");
    activateWatch = Symbol<ActivateWatch>(helper, "RtPlaybackSourceActivateInitialPlayerWatch");
    retireWatch = Symbol<RetireWatch>(helper, "RtPlaybackSourceRetirePlayerWatch");
    bindGate = Symbol<BindGate>(helper, "RtPlaybackSourceBindLogicalGate");
    action = Symbol<Action>(helper, "RtPlaybackSourceLogicalAction");
    configureInitialization = Symbol<ConfigureInitialization>(helper, "RtPlaybackSourceConfigureInitialization");
    readInitialization = Symbol<ReadInitialization>(helper, "RtPlaybackSourceReadInitialization");
    if (failures) return false;
    owner = createOwner(Observe, &foreignClass);
    CHECK(owner != nullptr);
    CHECK(configure(owner, RtSourceRef1, 0, RtSourcePlayerConstructor, reinterpret_cast<void*>(&DummyCtor), reinterpret_cast<void*>(&OriginalCtor)) == 1);
    CHECK(configure(owner, RtSourceVoid0, 1, RtSourcePause, reinterpret_cast<void*>(&DummyPause), reinterpret_cast<void*>(&OriginalPause)) == 1);
    if (firstCapture) {
        CHECK(configure(owner, RtSourceRef1, 2, RtSourceSetClip, reinterpret_cast<void*>(&FixtureFirstSetClipTarget), reinterpret_cast<void*>(&OriginalSetClip)) == 1);
        CHECK(configure(owner, RtSourceBooleanReturn0, 3, RtSourceProgressStartMoveNext,
            reinterpret_cast<void*>(&DummyStart), reinterpret_cast<void*>(&FixtureFirstStartOriginal)) == 1);
        FixtureFirstSetClipEntry = entry(RtSourceRef1, 2);
        startEntry = reinterpret_cast<Bool0>(entry(RtSourceBooleanReturn0, 3));
        CHECK(FixtureFirstSetClipEntry && startEntry);
    }
    ctorEntry = reinterpret_cast<VoidRef>(entry(RtSourceRef1, 0));
    pauseEntry = reinterpret_cast<Void0>(entry(RtSourceVoid0, 1));
    CHECK(ctorEntry && pauseEntry);
    if (directIndex>=0) for(unsigned i=0;i<6;++i) {
        CHECK(configure(owner,directKinds[i],directSlots[i],directRoles[i],&directTargets[i],directOriginals[i])==1);
        directEntries[i]=entry(directKinds[i],directSlots[i]);CHECK(directEntries[i]);
    }
    const auto initial = Health();
    CHECK(initial.OwnerEpoch != 0 && initial.ConfiguredSlots == (firstCapture ? 4u : 2u)+(directIndex>=0?6u:0u));
    CHECK(arm(owner, initial.OwnerEpoch, 1) == 1);
    CHECK(configureWatch(owner, &mugClass, &audioClass) == 1);
    if (firstCapture) {
        CHECK(configureInitialization(owner, const_cast<unsigned char*>(&FixtureFirstClipReturned)) == 1);
    } else {
        CHECK(stageWatch(owner, &playerA, (&playerA)->source, &initialReceipt) == 1 && initialReceipt);
        CHECK(activateWatch(owner, initialReceipt) == 1);
        CHECK(Watch(initialReceipt).active == 1);
    }
    OutsideCurrent();
    return !failures;
}
void Foreign() {
    const auto initial = Health();
    auto* thread = CreateThread(nullptr, 0, ForeignWorker, nullptr, 0, nullptr);
    CHECK(thread && WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0);
    if (thread) CloseHandle(thread);
    CHECK(originalCalls == 300 && beforeCallbacks == 0 && returnedCallbacks == 0);
    const auto after = Health();
    CHECK(after.Entered == initial.Entered && after.Exited == initial.Exited && after.InFlight == 0);
    CHECK(after.WriterSequence == initial.WriterSequence && after.SourceSequence == initial.SourceSequence
        && after.SchedulingSequence == initial.SchedulingSequence && after.Flags == initial.Flags && after.Faults == 0);
    CHECK(lastSelf == &foreignPlayer && lastArg == reinterpret_cast<void*>(0xA000u + 299)
        && lastMethod == reinterpret_cast<void*>(0xB000u + 299));
    // A live relevant attempt still gets a row after >256 foreign calls.
    ctorEntry(&playerA, &sourceA, nullptr);
    CHECK(originalCalls == 301 && beforeCallbacks == 1 && returnedCallbacks == 1);
    RtPlaybackSourceFrame completion{};
    CHECK(readReceipt(owner, lastBefore.Sequence, lastBefore.Token, &completion) == 1);
    CHECK((completion.Flags & RtSourcePhysicalExitedFlag) != 0 && releaseReceipt(owner, lastBefore.Sequence, lastBefore.Token) == 1);
    CHECK(!(Health().Flags & RtSourceUnknown));
}
void SameNullAba() {
    const auto initial = Health();
    ctorEntry(&playerA, &sourceA, reinterpret_cast<void*>(0xBEEF));
    CHECK(beforeStoreValue == &sourceA && lastArg == &sourceA && lastMethod == reinterpret_cast<void*>(0xBEEF));
    ctorEntry(&playerA, nullptr, nullptr);
    CHECK(beforeStoreValue == &sourceA && lastArg == nullptr && lastMethod == nullptr);
    ctorEntry(&playerA, &sourceA, reinterpret_cast<void*>(0xCAFE));
    CHECK(beforeStoreValue == nullptr && playerA.source == &sourceA && lastMethod == reinterpret_cast<void*>(0xCAFE));
    const auto after = Health();
    CHECK(originalCalls == 3 && beforeCallbacks == 3 && returnedCallbacks == 3);
    CHECK(after.Entered == initial.Entered + 3 && after.Exited == initial.Exited + 3 && after.InFlight == 0);
    CHECK(after.SourceSequence == initial.SourceSequence + 3 && after.Faults == 0);
}
void Worker() {
    const auto initial = Health();
    auto* thread = CreateThread(nullptr, 0, RelevantWorker, nullptr, 0, nullptr);
    CHECK(thread && WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0);
    if (thread) CloseHandle(thread);
    const auto after = Health();
    CHECK(originalCalls == 1 && beforeCallbacks == 0 && returnedCallbacks == 0);
    CHECK(beforeStoreValue == &sourceA && playerA.source == &sourceB);
    CHECK((originalHealth.Flags & RtSourceUnknown) && originalHealth.InFlight == 1
        && originalHealth.SourceSequence == initial.SourceSequence + 1);
    CHECK((after.Flags & RtSourceUnknown) && after.Entered == initial.Entered + 1
        && after.Exited == initial.Exited + 1 && after.InFlight == 0);
}
void CurrentReceiverClass() {
    const auto initial = Health();
    // An object with the right class is still foreign unless its exact rooted
    // receiver owns the active watch. Its original and all argument bits pass.
    ctorEntry(&playerB, nullptr, reinterpret_cast<void*>(0xACED));
    const auto foreign = Health();
    CHECK(lastSelf == &playerB && lastArg == nullptr && lastMethod == reinterpret_cast<void*>(0xACED));
    CHECK(playerB.source == nullptr && beforeCallbacks == 0 && returnedCallbacks == 0);
    CHECK(foreign.Entered == initial.Entered && foreign.Exited == initial.Exited && foreign.InFlight == 0);
    CHECK(foreign.WriterSequence == initial.WriterSequence && foreign.SourceSequence == initial.SourceSequence
        && foreign.SchedulingSequence == initial.SchedulingSequence && foreign.Flags == initial.Flags && foreign.Faults == 0);
    // The watched receiver remains relevant when its exact class changes:
    // fail closed before the original store, while retaining physical evidence.
    playerA.klass = &foreignClass;
    ctorEntry(&playerA, &sourceB, reinterpret_cast<void*>(0xC1A5));
    const auto after = Health();
    CHECK(lastSelf == &playerA && lastArg == &sourceB && lastMethod == reinterpret_cast<void*>(0xC1A5));
    CHECK(beforeStoreValue == &sourceA && playerA.source == &sourceB && originalCalls == 2);
    CHECK(beforeCallbacks == 1 && returnedCallbacks == 1 && currentCallbacks == 2);
    CHECK((originalHealth.Flags & RtSourceUnknown) && originalHealth.InFlight == 1
        && originalHealth.SourceSequence == initial.SourceSequence + 1);
    CHECK((after.Flags & RtSourceUnknown) && after.Faults != 0 && after.InFlight == 0
        && after.Entered == initial.Entered + 1 && after.Exited == initial.Exited + 1
        && after.SourceSequence == initial.SourceSequence + 1);
    RtPlaybackSourceFrame completion{};
    CHECK(readReceipt(owner, lastBefore.Sequence, lastBefore.Token, &completion) == 1);
    CHECK(completion.Stage == RtSourcePhysicalExit && (completion.Flags & RtSourcePhysicalExitedFlag)
        && (completion.Flags & RtSourceUnknown) && (completion.Flags & RtSourceReturnedFlag));
    CHECK(releaseReceipt(owner, lastBefore.Sequence, lastBefore.Token) == 1);
    playerA.klass = &mugClass;
    CHECK((Health().Flags & RtSourceUnknown) != 0); // restoring bytes cannot revive authority
}
DWORD WINAPI PendingWorker(void*) {
    ctorEntry(&playerB, &sourceB, reinterpret_cast<void*>(0x5104));
    return 0;
}
std::uint32_t ExercisePending(PendingCase scenario, void* receipt) {
    pendingSummary = true; pendingScenario = scenario;
    CHECK(scenario != PendingCase::None && Watch(receipt).phase == 1 && Watch(receipt).active == 0);
    const auto before = Health();
    const auto calls = originalCalls.load(), callbacks = beforeCallbacks.load(), returned = returnedCallbacks.load();
    const auto current = currentCallbacks.load();
    pendingSourceBefore = before.SourceSequence; pendingEnteredBefore = before.Entered;
    inspectPendingCallbacks = true;
    unsigned expectedAttempts = 0, expectedOriginals = 0;
    if (scenario == PendingCase::Same) {
        ctorEntry(&playerB, &sourceB, reinterpret_cast<void*>(0x5101));
        expectedAttempts = expectedOriginals = 1;
    } else if (scenario == PendingCase::Aba) {
        ctorEntry(&playerB, &sourceA, reinterpret_cast<void*>(0x5102));
        CHECK(playerB.source == &sourceA);
        ctorEntry(&playerB, &sourceB, reinterpret_cast<void*>(0x5103));
        expectedAttempts = expectedOriginals = 2;
    } else if (scenario == PendingCase::Foreign) {
        ctorEntry(&pendingForeignPlayer, nullptr, reinterpret_cast<void*>(0xF001));
        ctorEntry(&foreignPlayer, &sourceB, reinterpret_cast<void*>(0xF002));
        CHECK(pendingForeignPlayer.source == nullptr && foreignPlayer.source == &sourceB
            && lastSelf == &foreignPlayer && lastArg == &sourceB && lastMethod == reinterpret_cast<void*>(0xF002));
        expectedOriginals = 2;
    } else if (scenario == PendingCase::Worker) {
        auto* thread = CreateThread(nullptr, 0, PendingWorker, nullptr, 0, nullptr);
        CHECK(thread && WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0);
        if (thread) CloseHandle(thread);
        expectedAttempts = expectedOriginals = 1;
    } else if (scenario == PendingCase::Class) {
        playerB.klass = &foreignClass;
        ctorEntry(&playerB, &sourceB, reinterpret_cast<void*>(0x5105));
        playerB.klass = &mugClass; // restored bytes do not restore native authority
        expectedAttempts = expectedOriginals = 1;
    } else CHECK(scenario == PendingCase::Normal);
    inspectPendingCallbacks = false;
    const auto after = Health();
    pendingSourceAfter = after.SourceSequence; pendingEnteredAfter = after.Entered;
    pendingOriginals = originalCalls.load() - calls; pendingAttempts = expectedAttempts;
    CHECK(pendingOriginals == expectedOriginals && playerB.source == &sourceB);
    CHECK(after.Entered == before.Entered + expectedAttempts && after.Exited == before.Exited + expectedAttempts
        && after.InFlight == 0 && after.SourceSequence == before.SourceSequence + expectedAttempts
        && after.WriterSequence == before.WriterSequence && after.SchedulingSequence == before.SchedulingSequence);
    const auto expectedCallbacks = scenario == PendingCase::Worker ? 0u : expectedAttempts;
    CHECK(beforeCallbacks == callbacks + expectedCallbacks && returnedCallbacks == returned + expectedCallbacks
        && currentCallbacks == current + expectedCallbacks * 2 && pendingObserved == expectedCallbacks * 2
        && pendingDormantOnly);
    if (expectedAttempts) {
        CHECK(originalHealth.InFlight == 1 && originalHealth.SourceSequence == before.SourceSequence + expectedAttempts);
        if (expectedCallbacks) {
            RtPlaybackSourceFrame completion{};
            CHECK(readReceipt(owner, lastBefore.Sequence, lastBefore.Token, &completion) == 0);
        }
    }
    const bool unknown = scenario == PendingCase::Worker || scenario == PendingCase::Class;
    CHECK(((after.Flags & RtSourceUnknown) != 0) == unknown && (unknown ? after.Faults != 0 : after.Faults == 0));
    if (!expectedAttempts) CHECK(after.Flags == before.Flags && after.Faults == before.Faults);
    CHECK(Watch(receipt).phase == 1 && Watch(receipt).active == 0 && Watch(receipt).source == before.SourceSequence);
    pendingExpectedStatus = unknown ? 3u : expectedAttempts ? 8u : 0u;
    return pendingExpectedStatus;
}
void PendingTransfer(PendingCase scenario) {
    gate = bindGate(owner, Commit, &foreignClass);
    CHECK(gate && action(gate, 0, &mugClass) == 0 && action(gate, 1, &mugClass) == 0);
    void* receipt = nullptr;
    CHECK(stageWatch(owner, &playerB, (&playerB)->source, &receipt) == 1 && receipt);
    if (!receipt) return;
    const auto expected = ExercisePending(scenario, receipt);
    pendingOpenStatus = action(gate, 4, &mugClass);
    CHECK(pendingOpenStatus == expected);
    if (expected != 0) {
        CHECK(Watch(receipt).phase == 1 && Watch(receipt).active == 0 && Watch(initialReceipt).phase == 2);
        CHECK(action(gate, 4, &mugClass) == expected); // stale capture cannot retry open
        return;
    }
    CHECK(Watch(receipt).phase == 2 && Watch(receipt).active == 1 && Watch(initialReceipt).phase == 3);
    pendingFinalizeAttempted = true;
    CHECK(action(gate, 3, &mugClass) == 0);
}
void Transfer(bool initialAction, bool ctorBeforeArm) {
    gate = bindGate(owner, Commit, &foreignClass);
    CHECK(gate != nullptr);
    CHECK(action(gate, 0, &mugClass) == 0);
    CHECK(action(gate, 1, &mugClass) == 0);
    void* wrong = nullptr;
    CHECK(stageWatch(owner, &foreignPlayer, (&foreignPlayer)->source, &wrong) == 0 && wrong == nullptr);
    void* next = nullptr;
    CHECK(stageWatch(owner, &playerB, (&playerB)->source, &next) == 1 && next && next != initialReceipt);
    CHECK(Watch(next).phase == 1 && Watch(initialReceipt).active == 1);
    CHECK(action(gate, initialAction ? 10u : 4u, &mugClass) == 0);
    CHECK(Watch(initialReceipt).phase == 3 && Watch(next).phase == 2 && Watch(next).active == 1);
    CHECK(Watch(next).expected == reinterpret_cast<std::uint64_t>(initialReceipt));
    CHECK(retireWatch(owner, initialReceipt) == 1 && Watch(next).active == 1);
    auto copied = Watch(next);
    CHECK(retireWatch(owner, &copied) == 0 && Watch(next).active == 1);
    const auto opened = Health();
    pauseEntry(&playerB, nullptr);
    const auto paused = Health();
    CHECK(pauseCalls == 1 && paused.SchedulingSequence == opened.SchedulingSequence + 1
        && paused.SourceSequence == opened.SourceSequence);
    if (ctorBeforeArm) {
        ctorEntry(&playerB, &sourceB, nullptr); // equal-value attempt
        const auto attempted = Health();
        CHECK(beforeStoreValue == &sourceB && attempted.SourceSequence == paused.SourceSequence + 1);
        CHECK(action(gate, initialAction ? 11u : 3u, &mugClass) == 8);
    } else CHECK(action(gate, initialAction ? 11u : 3u, &mugClass) == 0);
    const auto beforeOld = Health();
    ctorEntry(&playerA, &sourceB, reinterpret_cast<void*>(0xDEAD));
    const auto afterOld = Health();
    CHECK(lastSelf == &playerA && lastArg == &sourceB && lastMethod == reinterpret_cast<void*>(0xDEAD));
    CHECK(afterOld.Entered == beforeOld.Entered && afterOld.SourceSequence == beforeOld.SourceSequence
        && afterOld.Flags == beforeOld.Flags && Watch(next).active == 1);
}
void FirstCapture(HMODULE helper, const wchar_t* helperPath, bool ctorBeforeArm, PendingCase pending = PendingCase::None) {
    std::wstring path(helperPath);
    const auto separator = path.find_last_of(L"\\/");
    CHECK(separator != std::wstring::npos);
    if (separator == std::wstring::npos) return;
    path.resize(separator + 1);
    path += L"RizToolbox.SingleReturn.Prototype.dll";
    singleLibrary = LoadLibraryW(path.c_str());
    CHECK(singleLibrary != nullptr);
    if (!singleLibrary) return;
    singleConfigure = Symbol<SingleConfigure>(singleLibrary, "RizSingleReturnPrototype_Configure");
    bindFresh = Symbol<SingleBind>(singleLibrary, "RizSingleReturnPrototype_BindFreshSource");
    bindInitialCapture = Symbol<SingleBind>(singleLibrary, "RizSingleReturnPrototype_BindInitialCaptureSource");
    bindLogical = Symbol<SingleBind>(singleLibrary, "RizSingleReturnPrototype_BindLogicalSource");
    beginInitialCapture = Symbol<SingleBegin>(singleLibrary, "RizSingleReturnPrototype_BeginInitialCapture");
    actInitialCapture = Symbol<SingleAct>(singleLibrary, "RizSingleReturnPrototype_ActInitialCapture");
    readInitialCapture = Symbol<SingleRead>(singleLibrary, "RizSingleReturnPrototype_ReadInitialCapture");
    singleActivate = Symbol<SingleActivate>(singleLibrary, "RizSingleReturnPrototype_ActivateInitialPlayerWatch");
    singlePrepare = Symbol<SinglePrepare>(singleLibrary, "RizSingleReturnPrototype_PrepareDormantMusicClock");
    singleArm = Symbol<SingleArm>(singleLibrary, "RizSingleReturnPrototype_ArmDormantMusicClock");
    if (failures) return;

    const RizSingleConfigV1 config{1, sizeof(RizSingleConfigV1), OriginalClock, ClockEnter, ClockExit, &clockMethod};
    CHECK(singleConfigure(0, &config) == 0);
    CHECK(bindFresh(0, helper, owner) == 0);
    CHECK(bindInitialCapture(0, helper, owner) == 0);

    RizPlaybackInitializationInfoV2 initialization{2, sizeof(RizPlaybackInitializationInfoV2)};
    CHECK(startEntry(&firstStart, nullptr) == 1);
    CHECK(readInitialization(owner, &initialization) == 1 && initialization.phase == 2 && initialization.blocked == 1);
    CHECK(firstStart.state == 1 && firstProgress.player == nullptr);
    CHECK(startEntry(&firstStart, nullptr) == 0);
    initialization = {2, sizeof(RizPlaybackInitializationInfoV2)};
    CHECK(readInitialization(owner, &initialization) == 1);
    firstInitializerPhase = initialization.phase;
    CHECK(initialization.phase == 4 && initialization.receipt && initialization.player == reinterpret_cast<std::uint64_t>(&playerB));
    CHECK(initialization.source == reinterpret_cast<std::uint64_t>(&sourceB)
        && initialization.clip == reinterpret_cast<std::uint64_t>(&firstClip));
    CHECK(firstProgress.player == &playerB && playerB.source == &sourceB && setClipCalls == 1);
    CHECK(Health().Entered == 3 && Health().Exited == 3 && Health().SourceSequence == 4);

    void* capture = nullptr;
    CHECK(beginInitialCapture(0, reinterpret_cast<void*>(initialization.receipt), &capture) == 0 && capture);
    if (!capture) return;
    RizPlaybackInitialCaptureInfoV1 captureInfo{1, sizeof(RizPlaybackInitialCaptureInfoV1)};
    CHECK(readInitialCapture(capture, &captureInfo) == 0 && captureInfo.phase == 1 && captureInfo.epoch == 0);
    CHECK(captureInfo.initializer == initialization.receipt && captureInfo.player == reinterpret_cast<std::uint64_t>(&playerB));
    CHECK(actInitialCapture(capture, 8, owner, 0, 0, 0) == 0);
    captureInfo = {1, sizeof(RizPlaybackInitialCaptureInfoV1)};
    CHECK(readInitialCapture(capture, &captureInfo) == 0 && captureInfo.phase == 2 && captureInfo.epoch != 0);

    void* watchReceipt = nullptr;
    CHECK(stageWatch(owner, &playerB, (&playerB)->source, &watchReceipt) == 1 && watchReceipt);
    if (!watchReceipt) return;
    CHECK(Watch(watchReceipt).phase == 1 && Watch(watchReceipt).active == 0);
    firstActivationStatus = singleActivate(0, owner, watchReceipt);
    CHECK(firstActivationStatus == 2 && Watch(watchReceipt).phase == 1);
    CHECK(actInitialCapture(capture, 9, owner, reinterpret_cast<std::uint64_t>(&playerB),
        reinterpret_cast<std::uint64_t>(&sourceB), reinterpret_cast<std::uint64_t>(&firstClip)) == 0);
    auto expected = pending == PendingCase::None ? 0u : ExercisePending(pending, watchReceipt);
    if(directIndex>=0 && !directLate) {
        const auto before=Health();InvokeDirect();const auto after=Health();
        CHECK(directCalls==1 && after.Entered==before.Entered+1 && after.Exited==before.Exited+1 && after.InFlight==0
            && after.SchedulingSequence==before.SchedulingSequence+1 && after.SourceSequence==before.SourceSequence);
        expected=8;
    }
    firstOpenStatus = actInitialCapture(capture, 10, owner, 0, 0, 0);
    if (pending != PendingCase::None) pendingOpenStatus = firstOpenStatus;
    CHECK(firstOpenStatus == expected);
    if (expected != 0) {
        CHECK(Watch(watchReceipt).phase == 1 && Watch(watchReceipt).active == 0);
        CHECK(actInitialCapture(capture, 10, owner, 0, 0, 0) == expected);
        captureInfo = {1, sizeof(RizPlaybackInitialCaptureInfoV1)};
        CHECK(readInitialCapture(capture, &captureInfo) == 0 && captureInfo.phase == 2);
        firstFinalPhase = captureInfo.phase;
        return; // rejected pending capture never constructs/arms a policy
    }
    CHECK(Watch(watchReceipt).active == 1 && Watch(watchReceipt).phase == 2);
    captureInfo = {1, sizeof(RizPlaybackInitialCaptureInfoV1)};
    CHECK(readInitialCapture(capture, &captureInfo) == 0 && captureInfo.phase == 3);

    if (ctorBeforeArm) {
        const auto before = Health();
        ctorEntry(&playerB, &sourceB, nullptr);
        const auto after = Health();
        CHECK(originalCalls == 1 && beforeStoreValue == &sourceB && playerB.source == &sourceB);
        CHECK(after.Entered == before.Entered + 1 && after.Exited == before.Exited + 1
            && after.SourceSequence == before.SourceSequence + 1 && after.InFlight == 0);
    }
    if(directIndex>=0 && directLate) {
        const auto before=Health();InvokeDirect();const auto after=Health();
        CHECK(directCalls==1 && after.SchedulingSequence==before.SchedulingSequence+1
            && after.SourceSequence==before.SourceSequence && after.InFlight==0);
    }
    RizMusicClockPolicyV1 policy{};
    policy.abiVersion = 1; policy.byteSize = sizeof(policy);
    policy.self = &playerB; policy.method = &clockMethod;
    policy.runHigh = 1; policy.controllerGeneration = 1; policy.policyGeneration = 1;
    policy.phase = RizClockPlaying; policy.rate = 1.0; policy.clipLength = 60.0;
    policy.readClock = ReadClock; policy.clockOwner = &clockMethod;
    void* bootstrap = nullptr;
    CHECK(singlePrepare(0, 0, &policy, &bootstrap) == 0 && bootstrap);
    if (!bootstrap) return;
    CHECK(singleArm(bootstrap) == 0);
    if (pending != PendingCase::None) pendingFinalizeAttempted = true;
    firstFinalizeStatus = bindLogical(0, helper, owner);
    CHECK(firstFinalizeStatus == (ctorBeforeArm || directIndex>=0 && directLate ? 8u : 0u));
    captureInfo = {1, sizeof(RizPlaybackInitialCaptureInfoV1)};
    CHECK(readInitialCapture(capture, &captureInfo) == 0);
    firstFinalPhase = captureInfo.phase;
    CHECK(firstFinalPhase == (ctorBeforeArm || directIndex>=0 && directLate ? 3u : 4u));
    CHECK((Health().Flags & RtSourceUnknown) == 0);
}
}

int wmain(int argc, wchar_t** argv) {
    if (argc != 3) return 64;
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    auto* helper = LoadLibraryW(argv[2]);
    CHECK(helper != nullptr);
    constexpr auto pendingPrefixLength=sizeof(L"audio-first-pending-")/sizeof(wchar_t)-1;
    constexpr auto openPrefixLength=sizeof(L"audio-first-open-")/sizeof(wchar_t)-1;
    const bool directFirstPending=std::wcsncmp(argv[1],L"audio-first-pending-",pendingPrefixLength)==0;
    const bool directFirstLate=std::wcsncmp(argv[1],L"audio-first-open-",openPrefixLength)==0;
    if(directFirstPending || directFirstLate) {
        const auto* suffix=argv[1]+(directFirstPending?pendingPrefixLength:openPrefixLength);
        CHECK(suffix[0]>=L'0' && suffix[0]<=L'5' && suffix[1]==0);
        directIndex=static_cast<int>(suffix[0]-L'0');directLate=directFirstLate;
    }
    const bool pendingFirst = std::wcsncmp(argv[1], L"pending-first-", 14) == 0;
    const bool pendingHandoff = std::wcsncmp(argv[1], L"pending-handoff-", 16) == 0;
    const auto pending = ParsePending(pendingFirst ? argv[1] + 14 : pendingHandoff ? argv[1] + 16 : L"");
    const bool firstStage = directFirstPending || directFirstLate || pendingFirst || std::wcscmp(argv[1], L"first-stage-normal") == 0
        || std::wcscmp(argv[1], L"first-stage-ctor-before-arm") == 0;
    if (!helper || !Setup(helper, firstStage)) return 65;
    if (directFirstPending || directFirstLate) FirstCapture(helper,argv[2],false);
    else if (pendingFirst) FirstCapture(helper, argv[2], false, pending);
    else if (pendingHandoff) PendingTransfer(pending);
    else if (std::wcscmp(argv[1], L"foreign-worker") == 0) Foreign();
    else if (std::wcscmp(argv[1], L"same-null-aba") == 0) SameNullAba();
    else if (std::wcscmp(argv[1], L"relevant-worker") == 0) Worker();
    else if (std::wcscmp(argv[1], L"transfer-normal") == 0) Transfer(false, false);
    else if (std::wcscmp(argv[1], L"transfer-ctor-before-arm") == 0) Transfer(false, true);
    else if (std::wcscmp(argv[1], L"first-open-normal") == 0) Transfer(true, false);
    else if (std::wcscmp(argv[1], L"first-open-ctor-before-arm") == 0) Transfer(true, true);
    else if (std::wcscmp(argv[1], L"current-receiver-class") == 0) CurrentReceiverClass();
    else if (std::wcscmp(argv[1], L"first-stage-normal") == 0) FirstCapture(helper, argv[2], false);
    else if (std::wcscmp(argv[1], L"first-stage-ctor-before-arm") == 0) FirstCapture(helper, argv[2], true);
    else CHECK(false);
    OutsideCurrent();
    if (pendingSummary) {
        std::printf("{\"mode\":\"pending-observation\",\"case\":\"%s\",\"openAction\":%u,\"openStatus\":%u,\"expectedOpenStatus\":%u,\"sourceBefore\":%llu,\"sourceAfter\":%llu,\"enteredBefore\":%llu,\"enteredAfter\":%llu,\"originals\":%u,\"attempts\":%u,\"nativeDormantCallbacks\":%u,\"dormantOnly\":%s,\"finalizeAttempted\":%s,\"gameExecuted\":false}\n",
            PendingName(pendingScenario), pendingFirst ? 10u : 4u, pendingOpenStatus, pendingExpectedStatus,
            pendingSourceBefore, pendingSourceAfter, pendingEnteredBefore, pendingEnteredAfter,
            pendingOriginals, pendingAttempts, pendingObserved, pendingDormantOnly ? "true" : "false",
            pendingFinalizeAttempted ? "true" : "false");
    }
    std::printf("{\"mode\":\"%ls\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"originalCalls\":%u,\"beforeCallbacks\":%u,\"returnedCallbacks\":%u,\"currentCallbacks\":%u,\"outsideCurrentProbes\":%u,\"entered\":%llu,\"exited\":%llu,\"sourceFence\":%llu,\"unknown\":%s,\"firstInitializerPhase\":%u,\"firstActivationStatus\":%u,\"firstOpenStatus\":%u,\"firstFinalizeStatus\":%u,\"firstFinalPhase\":%u,\"directHelperOnly\":true,\"dobbyExecuted\":false,\"gameExecuted\":false}\n",
        argv[1], failures ? "false" : "true", checks.load(), failures.load(), originalCalls.load(), beforeCallbacks.load(), returnedCallbacks.load(), currentCallbacks.load(), outsideCurrentProbes.load(),
        Health().Entered, Health().Exited, Health().SourceSequence, (Health().Flags & RtSourceUnknown) ? "true" : "false",
        firstInitializerPhase, firstActivationStatus, firstOpenStatus, firstFinalizeStatus, firstFinalPhase);
    return failures ? 1 : 0;
}
