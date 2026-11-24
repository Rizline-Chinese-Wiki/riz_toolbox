#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "PlaybackStockFixture.h"
using Boundary = void(__cdecl*)(StockSource*, std::uint32_t, std::uint32_t);
extern "C" {
__declspec(dllimport) int __cdecl StockFixtureBindLowerPitch(void*);
__declspec(dllimport) int __cdecl StockFixtureBindLowerTime(void*);
__declspec(dllimport) void __cdecl StockLowerSetPitch(StockSource*, float);
__declspec(dllimport) void __cdecl StockLowerSetTime(StockSource*, float);
__declspec(dllimport) void __cdecl StockLowerTimeFixtureConfigure(std::uint32_t, Boundary);
__declspec(dllimport) std::uint64_t __cdecl StockLowerTimeFixtureRead(std::uint32_t);
__declspec(dllimport) std::uint64_t __cdecl StockEngineFixtureRead(std::uint32_t);
__declspec(dllimport) void* StockLowerTimeCache;
}
namespace {
unsigned checks{}, failures{}, before{}, after{};
std::uint32_t beforeBits{};
bool checkBefore{};
void Check(bool value, const char* expression, unsigned line) {
    ++checks; if (!value) { ++failures; std::printf("FAIL %u %s\n", line, expression); }
}
#define CHECK(value) Check((value), #value, __LINE__)
float Float(std::uint32_t bits) { float value{}; std::memcpy(&value, &bits, 4); return value; }
void __cdecl StoreBoundary(StockSource* self, std::uint32_t bits, std::uint32_t at) {
    CHECK(self != nullptr);
    if (at == 1) {
        ++before;
        CHECK(StockLowerTimeFixtureRead(0) == StockLowerTimeFixtureRead(1) + 1);
        if (checkBefore) CHECK(self->TimeReserved == beforeBits);
    } else {
        CHECK(at == 2); ++after;
        CHECK(self->TimeReserved == bits && self->LastTimeBits == bits);
        if ((bits & 0x7f800000u) != 0x7f800000u) CHECK(self->Time == static_cast<double>(Float(bits)));
    }
}
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    CHECK(StockFixtureBindLowerTime(nullptr) == 0);
    CHECK(StockFixtureBindLowerTime(reinterpret_cast<void*>(StockSetPitch)) == 0);
    CHECK(StockFixtureBindLowerTime(&StockLowerTimeCache) == 0);
    CHECK(StockFixtureBindLowerPitch(reinterpret_cast<void*>(StockLowerSetPitch)) == 1);
    CHECK(StockFixtureBindLowerTime(reinterpret_cast<void*>(StockLowerSetTime)) == 1);
    CHECK(StockFixtureBindLowerTime(reinterpret_cast<void*>(StockLowerSetTime)) == 1);
    CHECK(StockLowerTimeCache == reinterpret_cast<void*>(StockLowerSetTime));
    if (std::wcscmp(argv[1], L"cache-tamper") == 0) {
        StockLowerTimeCache = reinterpret_cast<void*>(StockSetPitch);
        CHECK(StockFixtureBindLowerTime(reinterpret_cast<void*>(StockLowerSetTime)) == 0);
        StockLowerTimeCache = reinterpret_cast<void*>(StockLowerSetTime);
        CHECK(StockFixtureBindLowerTime(reinterpret_cast<void*>(StockLowerSetTime)) == 0);
    } else {
        auto* source = new StockSource{};
        CHECK(StockFixtureUnityInitializeWrapper(source, StockUnitySourceStorage) == 1);
        StockLowerTimeFixtureConfigure(0, StoreBoundary);
        if (std::wcscmp(argv[1], L"raw32") == 0) {
            const std::uint32_t values[]{0, 0x80000000u, 1, 0x80000001u, 0x3fa00000u, 0x7f7fffffu,
                0xff7fffffu, 0x7f800000u, 0xff800000u, 0x7fc12345u, 0xffc12345u, 0x7f812345u, 0xff812345u};
            void* const method = &checks; checkBefore = true;
            for (const auto bits : values) {
                beforeBits = source->TimeReserved; StockSetTime(source, Float(bits), method);
                CHECK(source->LastTimeMethod == method && source->TimeReserved == bits && source->LastTimeBits == bits);
                CHECK(StockLowerTimeFixtureRead(3) == bits && StockLowerTimeFixtureRead(2) == reinterpret_cast<std::uint64_t>(source));
                CHECK(StockLowerTimeFixtureRead(4) == GetCurrentThreadId() && StockLowerTimeFixtureRead(5) != reinterpret_cast<std::uint64_t>(method));
            }
            CHECK(before == 13 && after == 13 && source->TimeCalls == 13);
        } else if (std::wcscmp(argv[1], L"semantics") == 0) {
            source->Pitch = 123; StockSetPitch(source, 2.0f, nullptr);
            CHECK(StockFixtureNativeGetPitch(source) == 2.0f && source->Pitch == 123 && StockEngineFixtureRead(1) == 1);
            StockPlayer player{}; player.AudioSource = source; player.Active = source->Active = 1; player.Dsp = 10;
            source->Time = 3.5; StockResume(&player, 1.0f, nullptr);
            CHECK(player.ResumeCalls == 1 && source->TimeCalls == 1 && source->PitchCalls == 2 && source->ScheduleCalls == 1);
            CHECK(source->Time == 3.5 && source->Scheduled == 11 && source->Playing == 1 && source->Pitch == 123);
            CHECK(StockFixtureNativeGetPitch(source) == 1 && StockGetTime(&player, nullptr) == 3.5f);
            player.Dsp = 12; CHECK(StockGetTime(&player, nullptr) == 4.5f);
            source->Playing = 0; StockReplay(&player, 1.0f, nullptr);
            CHECK(player.ReplayCalls == 1 && source->TimeCalls == 1 && source->PitchCalls == 3 && source->ScheduleCalls == 2);
            CHECK(source->Time == 0 && source->Scheduled == 13 && StockFixtureNativeGetPitch(source) == 1 && source->Pitch == 123);
            CHECK(StockLowerTimeFixtureRead(0) == 1 && StockLowerTimeFixtureRead(1) == 1);
        } else if (std::wcscmp(argv[1], L"reentry") == 0) {
            StockLowerTimeFixtureConfigure(4, StoreBoundary); StockSetTime(source, 2.0f, nullptr);
            CHECK(StockLowerTimeFixtureRead(0) == 2 && StockLowerTimeFixtureRead(1) == 2);
            StockLowerTimeFixtureConfigure(8, StoreBoundary); StockLowerSetTime(source, 3.0f);
            CHECK(StockLowerTimeFixtureRead(0) == 4 && StockLowerTimeFixtureRead(1) == 4 && source->Time == 3);
        } else {
            const bool throwBefore = std::wcscmp(argv[1], L"throw-before") == 0;
            CHECK(throwBefore || std::wcscmp(argv[1], L"throw-after") == 0);
            StockLowerTimeFixtureConfigure(throwBefore ? 1u : 2u, StoreBoundary);
            bool caught = false;
            __try { StockSetTime(source, 7.0f, nullptr); }
            __except(EXCEPTION_EXECUTE_HANDLER) { caught = true; }
            CHECK(caught && StockLowerTimeFixtureRead(0) == 1 && StockLowerTimeFixtureRead(1) == (throwBefore ? 0u : 1u));
            CHECK(source->Time == (throwBefore ? 0.0 : 7.0) && before == 1 && after == (throwBefore ? 0u : 1u));
        }
        CHECK(StockFixtureUnityDestroy(source, StockUnitySourceStorage) == 1);
        CHECK(StockFixtureUnityRead(0) == StockFixtureUnityRead(1) && StockFixtureUnityRead(2) == StockFixtureUnityRead(3));
    }
    std::printf("{\"mode\":\"%ls\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"lowerTimeCalls\":%llu,\"lowerTimeStores\":%llu,\"boundaryBefore\":%u,\"boundaryAfter\":%u,\"actualDobbyPrepared\":0,\"actualDobbyCommitted\":0,\"fixtureRawStoreOffset\":132,\"gameExecuted\":false,\"productionAdmission\":false}\n",
        argv[1], failures ? "false" : "true", checks, failures, StockLowerTimeFixtureRead(0), StockLowerTimeFixtureRead(1), before, after);
    return failures ? 1 : 0;
}
