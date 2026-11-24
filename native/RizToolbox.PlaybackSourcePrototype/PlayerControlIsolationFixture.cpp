// Calls real source-helper entries and native originals over owned memory.
// A matching class or a shared AudioSource does not identify a MUG player.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include "PlaybackSourceContext.h"

namespace {
LONG checks{}, failures{};
void Check(bool value, const char* text, int line) {
    InterlockedIncrement(&checks);
    if (!value) { InterlockedIncrement(&failures); std::printf("FAIL %d %s\n", line, text); }
}
#define CHECK(value) Check((value), #value, __LINE__)
struct Audio { void* Class; };
struct Player { void* Class; void* Monitor; Audio* Source; };
int playerClass{}, audioClass{}, differentClass{};
Audio audio{&audioClass};
Player player{&playerClass, nullptr, &audio};
Player foreign{&playerClass, nullptr, &audio};
Player foreignClass{&differentClass, nullptr, &audio};
HMODULE library{};
void* owner{};
void* receipt{};
void* entries[3]{};
unsigned char targetMarkers[4]{};
constexpr std::uint32_t kinds[] = {RtSourceVoid0, RtSourceSingle1, RtSourceSingle1};
constexpr std::uint32_t roles[] = {RtSourcePause, RtSourceReplay, RtSourceResume};
unsigned roleIndex{};
std::uint64_t physicalCalls{}, observed{}, normalReturns{}, abnormalReturns{};
std::uint64_t expectedScheduling{};
std::uint64_t lastBits{};
void* lastSelf{};
void* lastMethod{};
bool workerExpected{}, originalThrows{}, observerThrows{};
RtPlaybackSourceFrame lastBefore{}, storeFrame{};
RtPlaybackSourceInfo storeHealth{};

decltype(&RtPlaybackSourceCreateOwner) createOwner;
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceGetEntry) getEntry;
decltype(&RtPlaybackSourceSetArmed) arm;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadCurrent) readCurrent;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
decltype(&RtPlaybackSourceConfigurePlayerWatch) configureWatch;
decltype(&RtPlaybackSourceStagePlayerWatch) stageWatch;
decltype(&RtPlaybackSourceActivateInitialPlayerWatch) activateWatch;

template<class T> T Symbol(const char* name) {
    const auto address = GetProcAddress(library, name);
    CHECK(address != nullptr); return reinterpret_cast<T>(address);
}
RtPlaybackSourceInfo Health() {
    RtPlaybackSourceInfo value{}; CHECK(readOwner(owner, &value) == 1); return value;
}
bool SameAuthority(const RtPlaybackSourceInfo& a, const RtPlaybackSourceInfo& b) {
    return a.Entered == b.Entered && a.Exited == b.Exited && a.InFlight == b.InFlight
        && a.WriterSequence == b.WriterSequence && a.SourceSequence == b.SourceSequence
        && a.SchedulingSequence == b.SchedulingSequence && a.Flags == b.Flags && a.Faults == b.Faults;
}
void Store(void* self, std::uint64_t bits, void* method) {
    // The original accepts arbitrary receivers so foreign transparency can be
    // checked without dereferencing deliberately invalid test addresses.
    lastSelf = self; lastBits = bits; lastMethod = method; ++physicalCalls;
    storeHealth = Health();
    if (self == &player) {
        CHECK(storeHealth.InFlight == 1);
        CHECK(readCurrent(kinds[roleIndex], roleIndex + 1, &storeFrame) == 0);
        // Observation authority is callback-scoped, never available to an
        // original (or a worker). Use only the actual earlier callback copy.
        if (!workerExpected) {
            storeFrame = lastBefore;
            CHECK(storeFrame.Self == reinterpret_cast<std::uint64_t>(self)
                && storeFrame.ArgBits == bits && storeFrame.IncomingMethod == reinterpret_cast<std::uint64_t>(method));
        }
        if (workerExpected) CHECK((storeHealth.Flags & RtSourceUnknown) != 0);
    }
    if (originalThrows) RaiseException(0xE2430002, 0, 0, nullptr);
}
__declspec(noinline) void __cdecl OriginalPause(void* self, void* method) { Store(self, 0, method); }
__declspec(noinline) void __cdecl OriginalReplay(void* self, float value, void* method) {
    std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits)); Store(self, bits, method);
}
__declspec(noinline) void __cdecl OriginalResume(void* self, float value, void* method) {
    std::uint32_t bits; std::memcpy(&bits, &value, sizeof(bits)); Store(self, bits, method);
}
__declspec(noinline) void __cdecl OriginalConstructor(void* self, void* source, void*) {
    static_cast<Player*>(self)->Source = static_cast<Audio*>(source);
}
void* originals[] = {reinterpret_cast<void*>(&OriginalPause), reinterpret_cast<void*>(&OriginalReplay), reinterpret_cast<void*>(&OriginalResume)};
void Observe(void*, const RtPlaybackSourceFrame* frame) {
    CHECK(frame && frame->Version == 4 && frame->Size == sizeof(*frame));
    if (!frame) return;
    CHECK(frame->Role == roles[roleIndex] && frame->Kind == kinds[roleIndex]);
    if (frame->Stage == RtSourceBefore) {
        ++observed; lastBefore = *frame;
        if (frame->Self == reinterpret_cast<std::uint64_t>(&player))
            CHECK(physicalCalls == 0 && frame->InFlight == 1 && frame->SchedulingSequence == expectedScheduling);
        if (observerThrows) RaiseException(0xE2430003, 0, 0, nullptr);
    } else if (frame->Stage == RtSourceOriginalReturned) ++normalReturns;
    else if (frame->Stage == RtSourceAbnormal) ++abnormalReturns;
}
int Call(unsigned index, void* self, std::uint32_t bits, void* method, std::uint32_t* code) {
    *code = 0;
    __try {
        if (index == 0) reinterpret_cast<void(__cdecl*)(void*, void*)>(entries[index])(self, method);
        else {
            float value; std::memcpy(&value, &bits, sizeof(value));
            reinterpret_cast<void(__cdecl*)(void*, float, void*)>(entries[index])(self, value, method);
        }
        return 1;
    } __except (EXCEPTION_EXECUTE_HANDLER) { *code = GetExceptionCode(); return 2; }
}
void* Method(unsigned iteration) {
    return iteration % 3 == 0 ? reinterpret_cast<void*>(0x12345678ull)
        : iteration % 3 == 1 ? nullptr : reinterpret_cast<void*>(0x23456789ull);
}
void* Foreign(unsigned iteration) {
    return iteration % 3 == 0 ? &foreign : iteration % 3 == 1 ? &foreignClass : reinterpret_cast<void*>(1ull);
}
DWORD WINAPI ForeignWorker(void*) {
    for (unsigned iteration = 0; iteration < 320; ++iteration)
        for (unsigned index = 0; index < 3; ++index) {
            roleIndex = index; std::uint32_t code{};
            CHECK(Call(index, Foreign(iteration), 0x80000000u, Method(iteration), &code) == 1 && code == 0);
            CHECK(lastSelf == Foreign(iteration) && lastMethod == Method(iteration)
                && lastBits == (index == 0 ? 0u : 0x80000000u));
        }
    return 0;
}
DWORD WINAPI CurrentWorker(void*) {
    std::uint32_t code{}; CHECK(Call(roleIndex, &player, 0x7FC01234u, Method(0), &code) == 1 && code == 0); return 0;
}
void Load() {
#define LOAD(field, name) field = Symbol<decltype(field)>(name)
    LOAD(createOwner, "RtPlaybackSourceCreateOwner"); LOAD(configure, "RtPlaybackSourceConfigure");
    LOAD(getEntry, "RtPlaybackSourceGetEntry"); LOAD(arm, "RtPlaybackSourceSetArmed");
    LOAD(readOwner, "RtPlaybackSourceReadOwner"); LOAD(readCurrent, "RtPlaybackSourceReadCurrent");
    LOAD(readReceipt, "RtPlaybackSourceReadReceipt"); LOAD(releaseReceipt, "RtPlaybackSourceReleaseReceipt");
    LOAD(configureWatch, "RtPlaybackSourceConfigurePlayerWatch"); LOAD(stageWatch, "RtPlaybackSourceStagePlayerWatch");
    LOAD(activateWatch, "RtPlaybackSourceActivateInitialPlayerWatch");
#undef LOAD
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackSourceVersion")() == 4);
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackPlayerWatchVersion")() == 2);
    owner = createOwner(Observe, nullptr); CHECK(owner != nullptr);
    CHECK(configure(owner, RtSourceRef1, 0, RtSourcePlayerConstructor, &targetMarkers[0], reinterpret_cast<void*>(&OriginalConstructor)) == 1);
    for (unsigned index = 0; index < 3; ++index) {
        CHECK(configure(owner, kinds[index], index + 1, roles[index], &targetMarkers[index + 1], originals[index]) == 1);
        entries[index] = getEntry(kinds[index], index + 1); CHECK(entries[index] != nullptr);
    }
    CHECK(arm(owner, Health().OwnerEpoch, 1) == 1);
    CHECK(configureWatch(owner, &playerClass, &audioClass) == 1);
}
}
int main(int argc, char** argv) {
    if (argc < 3 || argc > 4) return 2;
    library = LoadLibraryA(argv[1]); if (!library) return 3;
    const auto* mode = argv[2];
    if (argc == 4) roleIndex = static_cast<unsigned>(std::strtoul(argv[3], nullptr, 10));
    if (roleIndex >= 3) return 4;
    Load();
    const bool early = std::strcmp(mode, "early-foreign") == 0;
    const bool pending = std::strcmp(mode, "pending") == 0;
    if (!early) {
        CHECK(stageWatch(owner, &player, &audio, &receipt) == 1 && receipt);
        if (!pending) CHECK(activateWatch(owner, receipt) == 1);
    }
    const auto initial = Health(); expectedScheduling = initial.SchedulingSequence + 1; std::uint32_t code{};
    if (std::strcmp(mode, "foreign") == 0 || early) {
        for (unsigned iteration = 0; iteration < 3; ++iteration)
            for (unsigned index = 0; index < 3; ++index) {
                roleIndex = index; CHECK(Call(index, Foreign(iteration), 0x7FA54321u, Method(iteration), &code) == 1 && code == 0);
                CHECK(lastSelf == Foreign(iteration) && lastMethod == Method(iteration)
                    && lastBits == (index == 0 ? 0u : 0x7FA54321u));
            }
        const auto thread = CreateThread(nullptr, 0, ForeignWorker, nullptr, 0, nullptr); CHECK(thread != nullptr);
        CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
        CHECK(physicalCalls == 969 && observed == 0 && normalReturns == 0 && SameAuthority(initial, Health()));
    } else if (std::strcmp(mode, "current") == 0 || pending || std::strcmp(mode, "worker") == 0
        || std::strcmp(mode, "class") == 0 || std::strcmp(mode, "seh") == 0 || std::strcmp(mode, "observer-seh") == 0) {
        workerExpected = std::strcmp(mode, "worker") == 0; originalThrows = std::strcmp(mode, "seh") == 0;
        observerThrows = std::strcmp(mode, "observer-seh") == 0;
        if (std::strcmp(mode, "class") == 0) player.Class = &differentClass;
        if (workerExpected) {
            const auto thread = CreateThread(nullptr, 0, CurrentWorker, nullptr, 0, nullptr); CHECK(thread != nullptr);
            CHECK(WaitForSingleObject(thread, 10000) == WAIT_OBJECT_0); CloseHandle(thread);
        } else {
            const auto status = Call(roleIndex, &player, 0x7FC01234u, Method(0), &code);
            CHECK(originalThrows ? status == 2 && code == 0xE2430002u : status == 1 && code == 0);
        }
        const auto final = Health();
        CHECK(final.Entered == initial.Entered + 1 && final.Exited == initial.Exited + 1 && final.InFlight == 0);
        CHECK(final.WriterSequence == initial.WriterSequence && final.SourceSequence == initial.SourceSequence
            && final.SchedulingSequence == initial.SchedulingSequence + 1);
        if (std::strcmp(mode, "class") == 0 || observerThrows)
            CHECK((final.Flags & RtSourceUnknown) != 0);
        CHECK(physicalCalls == 1 && lastSelf == &player && lastMethod == Method(0)
            && lastBits == (roleIndex == 0 ? 0u : 0x7FC01234u));
        if (workerExpected) CHECK((final.Flags & RtSourceUnknown) != 0 && observed == 0 && normalReturns == 0);
        else CHECK(observed == 1 && (originalThrows ? abnormalReturns == 1 : normalReturns == 1));
        if (!workerExpected) {
            RtPlaybackSourceFrame completion{};
            CHECK(readReceipt(owner, storeFrame.Sequence, storeFrame.Token, &completion) == 1);
            CHECK((completion.Flags & RtSourcePhysicalExitedFlag) != 0);
            if (originalThrows) CHECK((completion.Flags & RtSourceAbnormalFlag) != 0);
            CHECK(releaseReceipt(owner, storeFrame.Sequence, storeFrame.Token) == 1);
        }
        if (pending) CHECK(activateWatch(owner, receipt) == 0);
    } else return 5;
    const auto final = Health();
    std::printf("{\"kind\":\"owned-player-control-isolation\",\"mode\":\"%s\",\"index\":%u,\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"physicalOriginals\":%llu,\"observedBefore\":%llu,\"entered\":%llu,\"exited\":%llu,\"scheduling\":%llu,\"flags\":%u,\"gameExecuted\":false}\n",
        mode, roleIndex, failures == 0 ? "true" : "false", checks, failures, physicalCalls, observed,
        final.Entered, final.Exited, final.SchedulingSequence, final.Flags);
    return failures == 0 ? 0 : 1;
}
