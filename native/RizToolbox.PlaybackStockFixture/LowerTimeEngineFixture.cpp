#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstring>
#include "PlaybackStockFixture.h"
extern "C" void __cdecl StockLowerSetTime(StockSource*, float);
using TimeTarget = void(__cdecl*)(StockSource*, float);
using TimeBoundary = void(__cdecl*)(StockSource*, std::uint32_t, std::uint32_t);
namespace {
volatile LONG64 calls{}, stores{};
std::uint64_t lastSource{}, lastBits{}, lastThread{}, lastCaller{};
std::uint32_t flags{};
TimeBoundary boundary{};
constexpr auto Functions() {
    std::array<TimeTarget, 51> result{}; result[50] = StockLowerSetTime; return result;
}
}
extern "C" {
extern __declspec(dllexport) const char StockLowerTimeProfileMarker[] = "owned-native-lower-time-icall-v1";
extern __declspec(dllexport) const char StockLowerTimeRegisteredName[] = "UnityEngine.AudioSource::set_time";
}
namespace {
constexpr auto Names() {
    std::array<const char*, 51> result{}; result[50] = StockLowerTimeRegisteredName; return result;
}
}
extern "C" {
extern __declspec(dllexport) const std::array<TimeTarget, 51> StockLowerTimeFunctions = Functions();
extern __declspec(dllexport) const std::array<const char*, 51> StockLowerTimeNames = Names();
extern __declspec(dllexport) const std::uint32_t StockLowerTimeRegistrationCount = 51;
__declspec(dllexport) void __cdecl StockLowerTimeFixtureConfigure(std::uint32_t value, TimeBoundary callback) {
    flags = value; boundary = callback;
}
__declspec(dllexport) std::uint64_t __cdecl StockLowerTimeFixtureRead(std::uint32_t field) {
    switch (field) {
    case 0: return static_cast<std::uint64_t>(InterlockedCompareExchange64(&calls, 0, 0));
    case 1: return static_cast<std::uint64_t>(InterlockedCompareExchange64(&stores, 0, 0));
    case 2: return lastSource; case 3: return lastBits; case 4: return lastThread; case 5: return lastCaller;
    default: return UINT64_MAX;
    }
}
void __cdecl StockLowerTimeBefore(StockSource* self, float value, std::uint64_t caller) {
    std::uint32_t bits{}; std::memcpy(&bits, &value, 4);
    InterlockedIncrement64(&calls); lastSource = reinterpret_cast<std::uint64_t>(self);
    lastBits = bits; lastThread = GetCurrentThreadId(); lastCaller = caller;
    if (boundary) boundary(self, bits, 1);
    if (!self || !self->CachedPtr || (flags & 1u)) RaiseException(0xe2470001u, 0, 0, nullptr);
    ++self->TimeCalls; self->LastTimeBits = bits;
}
void __cdecl StockLowerTimeAfter(StockSource* self, float value) {
    std::uint32_t bits{}; std::memcpy(&bits, &value, 4);
    if (self->TimeReserved != bits) RaiseException(0xe2470003u, 0, 0, nullptr);
    self->Time = static_cast<double>(value); InterlockedIncrement64(&stores);
    if (boundary) boundary(self, bits, 2);
    if (flags & 4u) { flags &= ~4u; StockLowerSetTime(self, value); }
    if (flags & 8u) { flags &= ~8u; StockSetTime(self, value, nullptr); }
    if ((flags & 2u) || (self->Flags & StockTimeThrowAfterStore)) RaiseException(0xe2470002u, 0, 0, nullptr);
}
}
static_assert(offsetof(StockSource, TimeReserved) == 0x84);
static_assert(offsetof(StockSource, LastTimeMethod) == 0x88);
static_assert(offsetof(StockSource, LastTimeBits) == 0x90);
