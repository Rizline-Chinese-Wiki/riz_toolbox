#define WIN32_LEAN_AND_MEAN
#include <windows.h>

// These are owned fixture state, never production receipts. The managed
// issuer independently pins both files and this exact cache/export layout.
extern "C" __declspec(dllexport) const char StockLowerPitchWrapperMarker[] =
    "owned-native-engine-pitch-wrapper-v2";
extern "C" {
__declspec(dllexport) void* StockLowerPitchCache = nullptr;
__declspec(dllexport) int __cdecl StockFixtureBindLowerPitch(void* target) {
    if (!target) return 0;
    const auto previous = InterlockedCompareExchangePointer(&StockLowerPitchCache, target, nullptr);
    return previous == nullptr || previous == target ? 1 : 0;
}
}
