// Owned native bodies only. Tests physical forwarding and owner isolation.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include "PlaybackSourceContext.h"
#include "../PlaybackInitialization.h"

namespace {
LONG checks{}, failures{};
void Check(bool value, const char* text, int line) {
    InterlockedIncrement(&checks);
    if (!value) { InterlockedIncrement(&failures); std::printf("FAIL %d %s\n", line, text); }
}
#define CHECK(value) Check((value), #value, __LINE__)
struct Audio { void* Class; };
struct Player { void* Class; void* Monitor; Audio* Source; };
struct Progress { void* Class; void* Monitor; unsigned char Reserved[24]; Audio* Source; void* Clip; Player* MusicPlayer; };
struct Start { void* Class; void* Monitor; int State; int Reserved; void* Current; Progress* Host; };
static_assert(offsetof(Progress, Source) == 0x28 && offsetof(Progress, MusicPlayer) == 0x38, "actual Progress offsets");
static_assert(offsetof(Start, Host) == 0x20, "actual Start progress offset");
int playerClass{}, audioClass{}, progressClass{}, startClass{}, foreignClass{};
Audio audio{&audioClass}, foreignAudio{&audioClass};
Player player{&playerClass, nullptr, &audio}, foreignPlayer{&playerClass, nullptr, &foreignAudio};
Progress progress{&progressClass, nullptr, {}, &audio, nullptr, &player};
Progress foreign{&progressClass, nullptr, {}, &foreignAudio, nullptr, &foreignPlayer};
Start start{&startClass, nullptr, 0, 0, nullptr, &progress};
Start foreignStart{&startClass, nullptr, 0, 0, nullptr, &foreign};
constexpr std::uint32_t kinds[] = {RtSourceBool1, RtSourceRef1, RtSourceBooleanReturn0};
constexpr std::uint32_t roles[] = {RtSourceProgressPlay, RtSourceProgressMusicPlayer, RtSourceProgressStartMoveNext};
unsigned char targets[4]{};
void* entries[3]{};
void* owner{};
HMODULE library{};
unsigned roleIndex{};
bool currentWorker{};
std::uint64_t originalCalls{}, observed{}, returns{}, lastBits{};
void* lastSelf{}; void* lastMethod{};
RtPlaybackSourceFrame lastFrame{};
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
using ReadInitialization = int(__cdecl*)(void*, RizPlaybackInitializationInfoV2*);
ReadInitialization readInitialization;

template<class T> T Symbol(const char* name) {
    const auto value = GetProcAddress(library, name); CHECK(value != nullptr); return reinterpret_cast<T>(value);
}
RtPlaybackSourceInfo Health() {
    RtPlaybackSourceInfo value{}; CHECK(readOwner(owner, &value) == 1); return value;
}
void Store(void* self, std::uint64_t bits, void* method) {
    ++originalCalls; lastSelf = self; lastBits = bits; lastMethod = method;
    if (self == &progress || self == &start) {
        const auto health = Health();
        CHECK(health.InFlight == 1 && health.Entered == 1);
        if (currentWorker) CHECK((health.Flags & RtSourceUnknown) != 0 && observed == 0);
        else CHECK(observed == 1 && lastFrame.Self == reinterpret_cast<std::uint64_t>(self));
    }
}
void __cdecl OriginalPlay(void* self, unsigned char value, void* method) { Store(self, value, method); }
void __cdecl OriginalMusicPlayer(void* self, void* value, void* method) { Store(self, reinterpret_cast<std::uint64_t>(value), method); }
unsigned char __cdecl OriginalStart(void* self, void* method) { Store(self, 0, method); return 0x80; }
void __cdecl OriginalConstructor(void* self, void* source, void*) { static_cast<Player*>(self)->Source = static_cast<Audio*>(source); }
void Observe(void*, const RtPlaybackSourceFrame* frame) {
    CHECK(frame && frame->Version == 4 && frame->Size == 168 && frame->Kind == kinds[roleIndex] && frame->Role == roles[roleIndex]);
    if (!frame) return;
    if (frame->Stage == RtSourceBefore) { ++observed; lastFrame = *frame; }
    else if (frame->Stage == RtSourceOriginalReturned) {
        ++returns;
        if (roleIndex == 2) CHECK(frame->ReturnPresent == 1 && frame->RawReturnByte == 0x80);
    }
}
void Call(unsigned index, bool current, void* method, unsigned char bits) {
    roleIndex = index;
    void* self = index == 2 ? static_cast<void*>(current ? &start : &foreignStart) : static_cast<void*>(current ? &progress : &foreign);
    if (index == 0) reinterpret_cast<void(__cdecl*)(void*, unsigned char, void*)>(entries[index])(self, bits, method);
    else if (index == 1) reinterpret_cast<void(__cdecl*)(void*, void*, void*)>(entries[index])(self, &foreignPlayer, method);
    else CHECK(reinterpret_cast<unsigned char(__cdecl*)(void*, void*)>(entries[index])(self, method) == 0x80);
    CHECK(lastSelf == self && lastMethod == method && lastBits == (index == 0 ? static_cast<std::uint64_t>(bits)
        : index == 1 ? reinterpret_cast<std::uint64_t>(&foreignPlayer) : 0));
}
DWORD WINAPI Worker(void*) {
    for (unsigned iteration = 0; iteration < 320; ++iteration)
        for (unsigned index = 0; index < 3; ++index) Call(index, false, nullptr, 0xFF);
    return 0;
}
DWORD WINAPI CurrentWorker(void*) { Call(roleIndex, true, nullptr, 0x80); return 0; }
bool Same(const RtPlaybackSourceInfo& a, const RtPlaybackSourceInfo& b) {
    return a.Entered == b.Entered && a.Exited == b.Exited && a.InFlight == b.InFlight && a.WriterSequence == b.WriterSequence
        && a.SourceSequence == b.SourceSequence && a.SchedulingSequence == b.SchedulingSequence && a.Faults == b.Faults && a.Flags == b.Flags;
}
}
int main(int argc, char** argv) {
    if (argc != 3) return 2;
    library = LoadLibraryA(argv[1]); if (!library) return 3;
    const bool hostOnly = std::strcmp(argv[2], "host-foreign") == 0;
    const bool predecessor = std::strcmp(argv[2], "host-transfer-foreign") == 0;
    const bool classChanged = std::strncmp(argv[2], "class-", 6) == 0;
    const bool pending = std::strncmp(argv[2], "pending-", 8) == 0;
    const bool current = std::strncmp(argv[2], "current-", 8) == 0 || pending || classChanged || std::strncmp(argv[2], "worker-", 7) == 0;
    if (current) { roleIndex = static_cast<unsigned>(std::strtoul(std::strrchr(argv[2], '-') + 1, nullptr, 10)); if (roleIndex >= 3) return 4; }
    readOwner = Symbol<decltype(readOwner)>("RtPlaybackSourceReadOwner");
    readReceipt = Symbol<decltype(readReceipt)>("RtPlaybackSourceReadReceipt");
    releaseReceipt = Symbol<decltype(releaseReceipt)>("RtPlaybackSourceReleaseReceipt");
    readInitialization = Symbol<ReadInitialization>("RtPlaybackSourceReadInitialization");
    owner = Symbol<decltype(&RtPlaybackSourceCreateOwner)>("RtPlaybackSourceCreateOwner")(Observe, nullptr); CHECK(owner);
    const auto configure = Symbol<decltype(&RtPlaybackSourceConfigure)>("RtPlaybackSourceConfigure");
    CHECK(configure(owner, RtSourceRef1, 0, RtSourcePlayerConstructor, &targets[0], reinterpret_cast<void*>(&OriginalConstructor)) == 1);
    void* originals[] = {reinterpret_cast<void*>(&OriginalPlay), reinterpret_cast<void*>(&OriginalMusicPlayer), reinterpret_cast<void*>(&OriginalStart)};
    for (unsigned index = 0; index < 3; ++index) {
        CHECK(configure(owner, kinds[index], index + 1, roles[index], &targets[index + 1], originals[index]) == 1);
        entries[index] = Symbol<decltype(&RtPlaybackSourceGetEntry)>("RtPlaybackSourceGetEntry")(kinds[index], index + 1); CHECK(entries[index]);
    }
    CHECK(Symbol<decltype(&RtPlaybackSourceSetArmed)>("RtPlaybackSourceSetArmed")(owner, Health().OwnerEpoch, 1) == 1);
    CHECK(Symbol<decltype(&RtPlaybackSourceConfigurePlayerWatch)>("RtPlaybackSourceConfigurePlayerWatch")(owner, &playerClass, &audioClass) == 1);
    const bool progressProtocol = GetProcAddress(library, "RtPlaybackProgressWatchVersion") != nullptr;
    void* progressReceipt{}; void* hostReceipt{};
    if (progressProtocol) {
        CHECK(Symbol<decltype(&RtPlaybackProgressWatchVersion)>("RtPlaybackProgressWatchVersion")() == 1);
        CHECK(Symbol<decltype(&RtPlaybackSourceConfigureProgressWatch)>("RtPlaybackSourceConfigureProgressWatch")(owner, &progressClass, &startClass) == 1);
        if (hostOnly || predecessor) {
            CHECK(Symbol<decltype(&RtPlaybackSourceBindHostProgressWatch)>("RtPlaybackSourceBindHostProgressWatch")(owner, &progress, &progressReceipt) == 1);
            hostReceipt = progressReceipt;
        }
    }
    if (!hostOnly) {
        void* watch{};
        CHECK(Symbol<decltype(&RtPlaybackSourceStagePlayerWatch)>("RtPlaybackSourceStagePlayerWatch")(owner, &player, &audio, &watch) == 1);
        if (progressProtocol) CHECK(Symbol<decltype(&RtPlaybackSourceStageProgressWatch)>("RtPlaybackSourceStageProgressWatch")(owner, &progress, watch, &progressReceipt) == 1);
        if (!pending) CHECK(Symbol<decltype(&RtPlaybackSourceActivateInitialPlayerWatch)>("RtPlaybackSourceActivateInitialPlayerWatch")(owner, watch) == 1);
    }
    const auto initial = Health();
    if (current) {
        if (classChanged) { if (roleIndex == 2) start.Class = &foreignClass; else progress.Class = &foreignClass; }
        currentWorker = std::strncmp(argv[2], "worker-", 7) == 0;
        if (currentWorker) {
            const auto worker = CreateThread(nullptr, 0, CurrentWorker, nullptr, 0, nullptr); CHECK(worker);
            CHECK(WaitForSingleObject(worker, 10000) == WAIT_OBJECT_0); CloseHandle(worker);
        } else Call(roleIndex, true, reinterpret_cast<void*>(0x12345678ull), 0x80);
    } else {
        for (unsigned iteration = 0; iteration < 4; ++iteration)
            for (unsigned index = 0; index < 3; ++index) Call(index, false,
                iteration % 3 == 0 ? reinterpret_cast<void*>(0x12345678ull) : iteration % 3 == 1 ? nullptr : reinterpret_cast<void*>(0x23456789ull),
                static_cast<unsigned char>(iteration == 0 ? 0 : iteration == 1 ? 1 : iteration == 2 ? 0x80 : 0xFF));
        const auto worker = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr); CHECK(worker);
        CHECK(WaitForSingleObject(worker, 10000) == WAIT_OBJECT_0); CloseHandle(worker);
    }
    const auto final = Health();
    RizPlaybackInitializationInfoV2 initialization{2, sizeof(initialization)};
    CHECK(readInitialization(owner, &initialization) == 1);
    if (current) {
        CHECK(originalCalls == 1 && final.Entered == initial.Entered + 1 && final.Exited == initial.Exited + 1 && final.InFlight == 0);
        CHECK(final.SourceSequence == initial.SourceSequence + (roleIndex == 0 ? 0 : 1)
            && final.SchedulingSequence == initial.SchedulingSequence + (roleIndex == 0 ? 1 : 0));
        CHECK(currentWorker ? observed == 0 && returns == 0 && (final.Flags & RtSourceUnknown) != 0 : observed == 1 && returns == 1);
        if (classChanged) CHECK((final.Flags & RtSourceUnknown) != 0);
        if (!currentWorker) {
            RtPlaybackSourceFrame frame{};
            CHECK(readReceipt(owner, lastFrame.Sequence, lastFrame.Token, &frame) == 1 && (frame.Flags & RtSourcePhysicalExitedFlag) != 0);
            if (roleIndex == 2) CHECK(frame.ReturnPresent == 1 && frame.RawReturnByte == 0x80);
            CHECK(releaseReceipt(owner, lastFrame.Sequence, lastFrame.Token) == 1);
        }
    } else {
        CHECK(originalCalls == 972 && observed == 0 && returns == 0 && Same(initial, final));
        CHECK(initialization.blocked == 0 && initialization.phase == 0 && initialization.enumerator == 0);
    }
    if (progressProtocol) {
        RtPlaybackProgressWatchInfoV1 info{1, sizeof(info)};
        CHECK(Symbol<decltype(&RtPlaybackSourceReadProgressWatch)>("RtPlaybackSourceReadProgressWatch")(owner, progressReceipt, &info) == 1
            && info.Phase == (pending ? 1u : 2u) && info.Active == (pending ? 0u : 1u) && info.Progress == reinterpret_cast<std::uint64_t>(&progress));
        if (predecessor) {
            RtPlaybackProgressWatchInfoV1 old{1, sizeof(old)};
            CHECK(Symbol<decltype(&RtPlaybackSourceReadProgressWatch)>("RtPlaybackSourceReadProgressWatch")(owner, hostReceipt, &old) == 1
                && old.Phase == 3 && old.Active == 0 && info.Expected == reinterpret_cast<std::uint64_t>(hostReceipt) && old.Ordinal < info.Ordinal);
            CHECK(Symbol<decltype(&RtPlaybackSourceRetireProgressWatch)>("RtPlaybackSourceRetireProgressWatch")(owner, hostReceipt) == 1);
            RtPlaybackProgressWatchInfoV1 after{1, sizeof(after)};
            CHECK(Symbol<decltype(&RtPlaybackSourceReadProgressWatch)>("RtPlaybackSourceReadProgressWatch")(owner, progressReceipt, &after) == 1
                && after.Phase == 2 && after.Active == 1 && after.Receipt == info.Receipt && after.Ordinal == info.Ordinal);
        }
        CHECK(Symbol<decltype(&RtPlaybackSourceRetireProgressWatch)>("RtPlaybackSourceRetireProgressWatch")(owner, progressReceipt) == (currentWorker || classChanged ? 0 : 1));
    }
    std::printf("{\"kind\":\"owned-progress-control-isolation\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"originals\":%llu,\"observedBefore\":%llu,\"entered\":%llu,\"exited\":%llu,\"sourceFence\":%llu,\"schedulingFence\":%llu,\"flags\":%u,\"initializationBlocked\":%u,\"initializationPhase\":%u,\"gameExecuted\":false}\n",
        argv[2], failures == 0 ? "true" : "false", checks, failures, originalCalls, observed, final.Entered, final.Exited, final.SourceSequence, final.SchedulingSequence, final.Flags, initialization.blocked, initialization.phase);
    return failures == 0 ? 0 : 1;
}
