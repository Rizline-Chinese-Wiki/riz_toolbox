#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include "../TweenSingleEventContext.h"
#include "../TweenAdmission.h"

// This DLL exercises Single transport only. It has no native admission issuer.
extern "C" bool RizTweenAdmissionFixture_Ready() { return true; }
extern "C" bool RizTweenAdmissionFixture_Validate(const RizTweenAdmissionConfigV1*) { return false; }

extern "C" {
void __cdecl RtSinglePlayFixture_Original(void*, float, const void*);
extern char RtSinglePlayFixture_MidReturn, RtSinglePlayFixture_Source0, RtSinglePlayFixture_Source1, RtSinglePlayFixture_Source2;
extern char RtSinglePlayFixture_Post0, RtSinglePlayFixture_Post1, RtSinglePlayFixture_Post2;
__declspec(dllexport) unsigned RtSinglePlayFixture_SkipPost{};
__declspec(dllexport) std::uint64_t RtSinglePlayFixture_Originals{}, RtSinglePlayFixture_Listeners{}, RtSinglePlayFixture_Plays{};
}
namespace {
using PlayCallback = void(__cdecl*)(void*, const void*);
PlayCallback playCallback{};
const void* directInvoke{};
void __cdecl TargetMarker(void*, float, const void*) {}
}
extern "C" __declspec(dllexport) void __cdecl RtSinglePlayFixture_Invoke(void* self, const void*)
{
    ++RtSinglePlayFixture_Listeners;
    auto* action = *reinterpret_cast<unsigned char**>(static_cast<unsigned char*>(self) + 0x10);
    if (!action) return;
    const auto invoke = *reinterpret_cast<PlayCallback*>(action + 0x18);
    auto* target = *reinterpret_cast<void**>(action + 0x20);
    const auto* method = *reinterpret_cast<const void**>(action + 0x28);
    invoke(target, method);
}
extern "C" __declspec(dllexport) void __cdecl RtSinglePlayFixture_Play(void* self, const void* method)
{ ++RtSinglePlayFixture_Plays; playCallback(self, method); }
extern "C" __declspec(dllexport) std::uint32_t __cdecl RtSinglePlayFixture_Configure(
    const void* invoke, PlayCallback callback, unsigned skipPost)
{
    if (!invoke || !callback || skipPost > 1) return 1;
    directInvoke = invoke; playCallback = callback; RtSinglePlayFixture_SkipPost = skipPost;
    const RizOperationBootstrapConfigV1 parent{1, sizeof(parent), reinterpret_cast<void*>(&TargetMarker),
        reinterpret_cast<void*>(&RtSinglePlayFixture_Original)};
    auto result = RizOperationBootstrap_Configure(RizOperationSingle1Void, 31, &parent);
    if (result) return result;
    const RizTweenSingleEventConfigV1 single{1, sizeof(single), 1, &RtSinglePlayFixture_MidReturn,
        {&RtSinglePlayFixture_Source0, &RtSinglePlayFixture_Source1, &RtSinglePlayFixture_Source2},
        {&RtSinglePlayFixture_Post0, &RtSinglePlayFixture_Post1, &RtSinglePlayFixture_Post2}, invoke};
    return RizTweenSingleEventConfigure(&single);
}
extern "C" __declspec(dllexport) void __cdecl RtSinglePlayFixture_Points(const void** result)
{
    const void* points[]{&RtSinglePlayFixture_MidReturn, &RtSinglePlayFixture_Source0, &RtSinglePlayFixture_Source1,
        &RtSinglePlayFixture_Source2, &RtSinglePlayFixture_Post0, &RtSinglePlayFixture_Post1,
        &RtSinglePlayFixture_Post2, directInvoke};
    for (unsigned index = 0; index != 8; ++index) result[index] = points[index];
}
