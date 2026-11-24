#define WIN32_LEAN_AND_MEAN
#include <windows.h>

extern "C" __declspec(dllexport) const char StockLowerTimeWrapperMarker[] =
    "owned-native-lower-time-wrapper-v1";
namespace { SRWLOCK bindLock = SRWLOCK_INIT; void* boundTarget{}; bool withdrawn{}; }
extern "C" {
__declspec(dllexport) void* StockLowerTimeCache = nullptr;
__declspec(dllexport) int __cdecl StockFixtureBindLowerTime(void* target) {
    const auto module = GetModuleHandleW(L"RizToolbox.PlaybackLowerPitchFixture.dll");
    const auto actual = module ? reinterpret_cast<void*>(GetProcAddress(module, "StockLowerSetTime")) : nullptr;
    if (!target || target != actual) return 0;
    AcquireSRWLockExclusive(&bindLock);
    const auto cache = InterlockedCompareExchangePointer(&StockLowerTimeCache, nullptr, nullptr);
    if (boundTarget ? cache != boundTarget : cache != nullptr) withdrawn = true;
    const bool valid = !withdrawn && (!boundTarget || boundTarget == target);
    if (valid && !boundTarget) { boundTarget = target; InterlockedExchangePointer(&StockLowerTimeCache, target); }
    ReleaseSRWLockExclusive(&bindLock); return valid ? 1 : 0;
}
}
