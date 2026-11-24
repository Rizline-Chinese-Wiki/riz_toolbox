#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../FirstInstall.h"
#include "../FirstInstallProfile.h"
#include "../RuntimeInvokeContext.h"
#include "../BootstrapVoidContext.h"
#include "FirstInstallMappedChecks.h"
#include "FirstInstallOperationChecks.h"
#include "FirstInstallFixtureReads.h"
#include "FirstInstallTweenSourceChecks.h"
#include "FirstInstallSingleEventChecks.h"

// Isolated publication test only. DONT_RESOLVE_DLL_REFERENCES maps the fixed PE
// without running its initialization or resolving/calling its imports. Never
// use this mapping mode in the production loader or call any game address here.
namespace {
namespace Profile = RizFirstInstallProfile;
using BeforeFn = decltype(&RizFirstInstall_BeforeIl2CppInit);
using FirstInfoFn = decltype(&RizFixturePublication::Common);
using FirstInfoV2Fn = decltype(&RizFixturePublication::Listener);
using FirstInfoV7Fn = decltype(&RizFirstInstall_GetInfoV7);
using RuntimeInfoFn = decltype(&RizRuntimeInvokeContext_GetInfo);
using VoidInfoFn = decltype(&RizVoidBootstrap_GetInfo);
using VoidStatsFn = decltype(&RizVoidContext_GetStats);
using MidStatsFn = decltype(&RizEventMidContext_GetStats);
using MidEntryFn = decltype(&RizEventMidContext_GetEntry);
std::uint64_t checks = 0, failures = 0;

void Check(bool condition, const char* expression, int line)
{
    ++checks;
    if (!condition) { ++failures; std::fprintf(stderr, "FAILED mapped first install line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto pointer = GetProcAddress(module, name); CHECK(pointer != nullptr);
    T result{}; static_assert(sizeof(result) == sizeof(pointer)); std::memcpy(&result, &pointer, sizeof(result)); return result;
}
bool IsExecutable(DWORD protection)
{
    if ((protection & (PAGE_GUARD | PAGE_NOACCESS)) != 0) return false;
    const auto access = protection & 0xffu;
    return access == PAGE_EXECUTE || access == PAGE_EXECUTE_READ
        || access == PAGE_EXECUTE_READWRITE || access == PAGE_EXECUTE_WRITECOPY;
}
MEMORY_BASIC_INFORMATION Memory(const void* address)
{
    MEMORY_BASIC_INFORMATION info{}; CHECK(VirtualQuery(address, &info, sizeof(info)) == sizeof(info)); return info;
}
void ExecutableImage(const void* address, const void* allocationBase)
{
    const auto info = Memory(address);
    CHECK(info.State == MEM_COMMIT && info.Type == MEM_IMAGE && info.AllocationBase == allocationBase && IsExecutable(info.Protect));
}
void ExecutablePrivate(const void* address)
{
    const auto info = Memory(address);
    CHECK(info.State == MEM_COMMIT && info.Type == MEM_PRIVATE && IsExecutable(info.Protect));
}
bool FunctionAt(const unsigned char* base, std::uint32_t rva, RUNTIME_FUNCTION* copy)
{
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    CHECK(dos->e_magic == IMAGE_DOS_SIGNATURE && dos->e_lfanew > 0 && dos->e_lfanew < 0x1000);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0 || dos->e_lfanew >= 0x1000) return false;
    const auto* pe = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    CHECK(pe->Signature == IMAGE_NT_SIGNATURE && pe->FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64
        && pe->OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC && pe->OptionalHeader.SizeOfImage == Profile::ImageSize
        && pe->FileHeader.TimeDateStamp == Profile::TimeDateStamp);
    const auto& directory = pe->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    CHECK(directory.VirtualAddress == Profile::PdataRva && directory.Size == Profile::PdataSize
        && directory.Size % sizeof(RUNTIME_FUNCTION) == 0
        && directory.VirtualAddress < Profile::ImageSize && directory.Size <= Profile::ImageSize - directory.VirtualAddress);
    if (directory.VirtualAddress != Profile::PdataRva || directory.Size != Profile::PdataSize) return false;
    const auto* table = reinterpret_cast<const RUNTIME_FUNCTION*>(base + directory.VirtualAddress);
    const auto count = directory.Size / sizeof(RUNTIME_FUNCTION);
    std::size_t left = 0, right = count;
    const RUNTIME_FUNCTION* found = nullptr;
    while (left < right) {
        const auto index = left + (right - left) / 2;
        if (rva < table[index].BeginAddress) right = index;
        else if (rva >= table[index].EndAddress) left = index + 1;
        else { found = &table[index]; break; }
    }
    CHECK(found != nullptr); if (!found) return false;
    CHECK(found->BeginAddress <= rva && rva < found->EndAddress && found->EndAddress <= Profile::ImageSize
        && found->UnwindData != 0 && found->UnwindData < Profile::ImageSize);
    DWORD64 actualBase = 0;
    const auto* lookup = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(base + rva), &actualBase, nullptr);
    const auto agreed = lookup && actualBase == reinterpret_cast<DWORD64>(base)
        && lookup->BeginAddress == found->BeginAddress && lookup->EndAddress == found->EndAddress
        && lookup->UnwindData == found->UnwindData;
    CHECK(agreed); *copy = *found; return agreed;
}
void CheckFunctionUnchanged(const unsigned char* base, std::uint32_t rva, const RUNTIME_FUNCTION& before)
{
    RUNTIME_FUNCTION after{}; CHECK(FunctionAt(base, rva, &after)); CHECK(std::memcmp(&before, &after, sizeof(before)) == 0);
}
void TamperByte(const void* address)
{
    auto* byte = static_cast<unsigned char*>(const_cast<void*>(address));
    DWORD old{}; const auto writable = VirtualProtect(byte, 1, PAGE_EXECUTE_READWRITE, &old); CHECK(writable);
    if (!writable) return;
    *byte ^= 1; DWORD ignored{}; CHECK(VirtualProtect(byte, 1, old, &ignored));
    CHECK(FlushInstructionCache(GetCurrentProcess(), byte, 1));
    CHECK(Memory(byte).Protect == old);
}
}

int wmain(int argc, wchar_t** argv)
{
    RizMappedChecks::DisableErrorUi();
    if (argc != 4 && argc != 5) { std::fprintf(stderr, "Usage: FirstInstallMapped.Fixture.exe existing-helper.dll inspected-dobby.dll fixed-GameAssembly.dll [tamper-source|tamper-post|tamper-relay|tamper-chain|tamper-operation|tamper-delegate-remove|tamper-single-event|tamper-tween-play|tamper-tween-add|tamper-tween-execute|tamper-tween-update|tamper-tween-awake]\n"); return 2; }
    const wchar_t* mode = argc == 5 ? argv[4] : L"normal";
    const bool tamper = argc == 5;
    int singleSpan=-1,singleOffset=-1,singleBlockOffset=-1,singleProtection=-1;
    if(swscanf_s(mode,L"tamper-single-span-%d-byte-%d",&singleSpan,&singleOffset)==2){
        if(singleSpan<0||singleSpan>=7||singleOffset<0||singleOffset>=static_cast<int>(RizSingleEventChecks::Widths[singleSpan]))return 2;
    }else singleSpan=singleOffset=-1;
    if(swscanf_s(mode,L"tamper-single-block-byte-%d",&singleBlockOffset)==1){if(singleBlockOffset<0||singleBlockOffset>=4096)return 2;}
    else singleBlockOffset=-1;
    if(swscanf_s(mode,L"tamper-single-source-protection-%d",&singleProtection)==1){if(singleProtection<0||singleProtection>=7)return 2;}
    else singleProtection=-1;
    if(std::wcscmp(mode,L"tamper-single-block-protection")==0)singleProtection=7;
    const bool singleTamper=singleSpan>=0||singleBlockOffset>=0||singleProtection>=0;
    if (tamper && !singleTamper && std::wcscmp(mode, L"tamper-source") != 0 && std::wcscmp(mode, L"tamper-post") != 0
        && std::wcscmp(mode, L"tamper-relay") != 0 && std::wcscmp(mode, L"tamper-chain") != 0
        && std::wcscmp(mode, L"tamper-operation") != 0 && std::wcscmp(mode, L"tamper-delegate-remove") != 0
        && std::wcscmp(mode, L"tamper-single-event") != 0
        && std::wcscmp(mode, L"tamper-single-event-unwind") != 0
        && std::wcscmp(mode, L"tamper-single-event-chain") != 0
        && std::wcscmp(mode, L"tamper-single-event-pdata") != 0
        && std::wcscmp(mode, L"tamper-tween-play") != 0 && std::wcscmp(mode, L"tamper-tween-add") != 0
        && std::wcscmp(mode, L"tamper-tween-execute") != 0 && std::wcscmp(mode, L"tamper-tween-update") != 0
        && std::wcscmp(mode, L"tamper-tween-awake") != 0
        && RizTweenSourceChecks::ModeId(mode) < 0 && std::wcscmp(mode,L"tamper-tween-source-relay") != 0
        && std::wcscmp(mode,L"tamper-tween-source-unwind") != 0 && std::wcscmp(mode,L"tamper-tween-source-table") != 0) return 2;
    if (GetModuleHandleW(L"GameAssembly.dll")) {
        std::fprintf(stderr, "A GameAssembly module was already loaded into the isolated fixture process.\n"); return 2;
    }
    const auto helper = LoadLibraryW(argv[1]); CHECK(helper); if (!helper) return 2;
    const auto beforeInit = Symbol<BeforeFn>(helper, "RizFirstInstall_BeforeIl2CppInit");
    RizFixturePublication::reader = Symbol<RizFixturePublication::PacketReader>(helper, "RizFirstInstall_GetInfoV7");
    const auto firstInfo = &RizFixturePublication::Common;
    const auto firstInfoV2 = &RizFixturePublication::Listener;
    const auto firstInfoV7 = Symbol<FirstInfoV7Fn>(helper, "RizFirstInstall_GetInfoV7");
    const auto sourceInfo = Symbol<RizTweenSourceChecks::Reader>(helper,"RizFirstInstall_GetTweenSourceInfoV1");
    const auto operationInfo = Symbol<decltype(&RizOperationBootstrap_GetInfo)>(helper, "RizOperationBootstrap_GetInfo");
    const auto operationEntry = Symbol<decltype(&RizOperationContext_GetEntry)>(helper, "RizOperationContext_GetEntry");
    const auto runtimeInfo = Symbol<RuntimeInfoFn>(helper, "RizRuntimeInvokeContext_GetInfo");
    const auto voidInfo = Symbol<VoidInfoFn>(helper, "RizVoidBootstrap_GetInfo");
    const auto voidStats = Symbol<VoidStatsFn>(helper, "RizVoidContext_GetStats");
    const auto midStats = Symbol<MidStatsFn>(helper, "RizEventMidContext_GetStats");
    const auto midEntry = Symbol<MidEntryFn>(helper, "RizEventMidContext_GetEntry");
    const auto listenerInfo = Symbol<decltype(&RizListenerContext_GetInfo)>(helper, "RizListenerContext_GetInfo");
    const auto listenerPre = Symbol<decltype(&RizListenerContext_GetPreEntry)>(helper, "RizListenerContext_GetPreEntry");
    const auto listenerPost = Symbol<decltype(&RizListenerContext_GetPostEntry)>(helper, "RizListenerContext_GetPostEntry");
    if (!beforeInit || !RizFixturePublication::reader || !firstInfoV7 || !sourceInfo || !operationInfo || !operationEntry || !runtimeInfo || !voidInfo || !voidStats || !midStats || !midEntry
        || !listenerInfo || !listenerPre || !listenerPost) return 2;
    const auto image = LoadLibraryExW(argv[3], nullptr, DONT_RESOLVE_DLL_REFERENCES);
    CHECK(image); if (!image) { std::fprintf(stderr, "Fixed PE mapping failed: %lu\n", GetLastError()); return 2; }
    const auto* base = reinterpret_cast<const unsigned char*>(image);
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == image);
    const auto init = GetProcAddress(image, "il2cpp_init");
    const auto runtimeExport = GetProcAddress(image, "il2cpp_runtime_invoke");
    CHECK(reinterpret_cast<const void*>(init) == base + Profile::InitRva
        && reinterpret_cast<const void*>(runtimeExport) == base + Profile::RuntimeExportRva);
    CHECK(std::memcmp(base + Profile::InitRva, Profile::InitPrefix, sizeof(Profile::InitPrefix)) == 0);
    CHECK(std::memcmp(base + Profile::RuntimeExportRva, Profile::RuntimeExportPrefix, sizeof(Profile::RuntimeExportPrefix)) == 0);
    CHECK(std::memcmp(base + Profile::RuntimeRva, Profile::RuntimePrefix, sizeof(Profile::RuntimePrefix)) == 0);
    CHECK(std::memcmp(base + Profile::UnityRva, Profile::UnityPrefix, sizeof(Profile::UnityPrefix)) == 0);
    CHECK(std::memcmp(base + Profile::MidRva - 11, Profile::MidWindow, sizeof(Profile::MidWindow)) == 0);
    std::int32_t runtimeDisplacement = 0;
    std::memcpy(&runtimeDisplacement, base + Profile::RuntimeExportRva + 1, sizeof(runtimeDisplacement));
    CHECK(base + Profile::RuntimeExportRva + 5 + runtimeDisplacement == base + Profile::RuntimeRva);
    RUNTIME_FUNCTION initFunction{}, runtimeFunction{}, unityFunction{}, midFunction{};
    CHECK(FunctionAt(base, Profile::InitRva, &initFunction));
    CHECK(FunctionAt(base, Profile::RuntimeRva, &runtimeFunction));
    CHECK(FunctionAt(base, Profile::UnityRva, &unityFunction));
    CHECK(FunctionAt(base, Profile::MidRva, &midFunction));
    CHECK(unityFunction.BeginAddress == 0x15642e0 && unityFunction.EndAddress == 0x156454e && unityFunction.UnwindData == 0x1db3fb4
        && std::memcmp(&unityFunction, &midFunction, sizeof(unityFunction)) == 0);
    CHECK(std::memcmp(base + initFunction.UnwindData, Profile::InitUnwind, sizeof(Profile::InitUnwind)) == 0);
    CHECK(std::memcmp(base + runtimeFunction.UnwindData, Profile::RuntimeUnwind, sizeof(Profile::RuntimeUnwind)) == 0);
    CHECK(std::memcmp(base + unityFunction.UnwindData, Profile::UnityUnwind, sizeof(Profile::UnityUnwind)) == 0);
    const auto runtimeMemory = Memory(base + Profile::RuntimeRva), unityMemory = Memory(base + Profile::UnityRva), midMemory = Memory(base + Profile::MidRva);
    const auto listenerBefore = RizMappedChecks::CaptureBefore(base, Check);
    const auto operationBefore = RizOperationChecks::Capture(base, Check);
    const auto singleBefore = RizSingleEventChecks::Capture(base,Check);
    ExecutableImage(base + Profile::RuntimeRva, image); ExecutableImage(base + Profile::UnityRva, image); ExecutableImage(base + Profile::MidRva, image);
    RizFirstInstallInfoV1 initial{1, sizeof(initial)};
    CHECK(firstInfo(&initial) == 0 && initial.phase == RizFirstInstallNever && initial.unknown == 0 && initial.writesStarted == 0);
    auto initialV2 = RizMappedChecks::Header(); CHECK(firstInfoV2(&initialV2) == 0);
    RizMappedChecks::CommonAgrees(initial, initialV2, Check);
    CHECK(initialV2.listenerEpoch == 0 && initialV2.sourcePc[0] == nullptr && initialV2.functionTable == nullptr);
    auto initialV7 = RizOperationChecks::Header(); CHECK(firstInfoV7(&initialV7) == 0);
    CHECK(initialV7.publication.common.phase == RizFirstInstallNever && initialV7.configuredMask == 0 && initialV7.publishedMask == 0);
    auto unavailableSource=RizTweenSourceChecks::Header();const auto sourceInput=unavailableSource;
    CHECK(sourceInfo(&unavailableSource)==2 && std::memcmp(&sourceInput,&unavailableSource,sizeof(sourceInput))==0);
    const auto invalidHeaders = RizMappedChecks::InvalidHeaders(firstInfoV2, initialV2, Check);
    CHECK(RizMappedChecks::Canonicalized(firstInfoV2, initialV2, Check));
    // This is the one and only call into the producer. init is a verified opaque
    // export address: it is NEVER invoked by this fixture or by the producer.
    const auto status = beforeInit(reinterpret_cast<const void*>(init), argv[2]);
    RizFirstInstallInfoV1 ready{1, sizeof(ready)}; CHECK(firstInfo(&ready) == 0);
    const auto readyKnown = status == 1 && ready.phase == RizFirstInstallReady && ready.unknown == 0;
    CHECK(readyKnown);
    if (!readyKnown) {
        auto partial = RizOperationChecks::Header(); firstInfoV7(&partial);
        std::fprintf(stderr, "Actual operation claims: configured=%llx published=%llx capability=%u expectedWrites=%u\n",
            partial.configuredMask, partial.publishedMask, partial.operationCapabilityVersion, partial.expectedWrites);
        std::fprintf(stderr, "Fixed mapped image was not accepted: status=%u phase=%u unknown=%u writes=%llu. No game function was invoked.\n",
            status, ready.phase, ready.unknown, ready.writesStarted);
        std::printf("{\"schemaVersion\":1,\"kind\":\"fixed-mapped-image-first-install-publication\",\"passed\":false,\"checks\":%llu,\"failures\":%llu,\"status\":%u,\"phase\":%u,\"unknown\":%u,\"writesStarted\":%llu,\"mappedImageOnly\":true,\"gameCodeExecuted\":false,\"firstInstallSafetyVerified\":false,\"gameRuntimeVerified\":false}\n",
            checks, failures, status, ready.phase, ready.unknown, ready.writesStarted);
        return 1;
    }
    CHECK(ready.voidSlot == 31 && ready.reserved0 == 0 && ready.reserved1 == 0
        && ready.moduleBase == reinterpret_cast<std::uint64_t>(base) && ready.imageSize == Profile::ImageSize
        && ready.init == reinterpret_cast<const void*>(init) && ready.writesStarted == 64);
    CHECK(ready.runtimeTarget == base + Profile::RuntimeRva && ready.runtimeOriginal && ready.runtimeOriginal != ready.runtimeTarget
        && ready.runtimeEntry && ready.runtimeEpoch != 0);
    CHECK(ready.unityTarget == base + Profile::UnityRva && ready.unityOriginal && ready.unityOriginal != ready.unityTarget
        && ready.unityEntry && ready.unityEpoch != 0 && ready.midReturnPc == base + Profile::MidReturnRva && ready.relay);
    auto readyV2 = RizMappedChecks::Header(); CHECK(firstInfoV2(&readyV2) == 0);
    RizMappedChecks::CommonAgrees(ready, readyV2, Check);
    const auto listenerEvidence = RizMappedChecks::ValidatePublished(base, readyV2, listenerBefore, Check);
    auto readyV7 = RizOperationChecks::Header(); CHECK(firstInfoV7(&readyV7) == 0);
    const auto singleEvidence=RizSingleEventChecks::Validate(base,helper,readyV7,singleBefore,Check);
    auto readySources=RizTweenSourceChecks::Header();
    const auto sourceRowsVerified=RizTweenSourceChecks::Validate(base,helper,sourceInfo,Check,readySources);
    CHECK(sourceRowsVerified==22);
    const auto operationRowsVerified = RizOperationChecks::Validate(base, helper, readyV7, operationBefore, operationInfo, operationEntry, Check);
    const auto invalidV7Headers = RizOperationChecks::InvalidHeaders(firstInfoV7, readyV7, Check);
    auto v7Prefix = readyV7.publication; v7Prefix.common.abiVersion = 2; v7Prefix.common.byteSize = sizeof(v7Prefix);
    CHECK(std::memcmp(&v7Prefix, &readyV2, sizeof(v7Prefix)) == 0 && operationRowsVerified == 26 && invalidV7Headers == 17);
    const auto callerIdentityCanonicalized = RizMappedChecks::Canonicalized(firstInfoV2, readyV2, Check);
    const bool v2ReadyVerified = readyV2.common.phase == RizFirstInstallReady && readyV2.common.unknown == 0
        && readyV2.common.writesStarted == 64 && readyV2.listenerEpoch != 0;
    CHECK(v2ReadyVerified && readyV2.preEntry == listenerPre() && readyV2.postEntry == listenerPost());
    ExecutableImage(readyV2.preEntry, helper); ExecutableImage(readyV2.postEntry, helper);
    RizListenerInfoV1 listener{1, sizeof(listener)};
    CHECK(listenerInfo(31, &listener) == 0 && listener.slot == 31 && listener.attached == 0 && listener.unknown == 0
        && listener.epoch == readyV2.listenerEpoch && listener.parentEpoch == ready.unityEpoch
        && listener.preEntry == readyV2.preEntry && listener.postEntry == readyV2.postEntry
        && listener.entered == 0 && listener.exited == 0 && listener.inFlight == 0 && listener.faults == 0
        && listener.abnormal == 0 && listener.overflow == 0 && listener.missingPost == 0);
    CHECK(std::memcmp(listener.sourceReturnPc, readyV2.sourceReturnPc, sizeof(listener.sourceReturnPc)) == 0
        && std::memcmp(listener.postTransportReturnPc, readyV2.postTransportReturnPc, sizeof(listener.postTransportReturnPc)) == 0
        && std::memcmp(listener.directTarget, readyV2.directTarget, sizeof(listener.directTarget)) == 0);
    RizRuntimeInvokeInfoV1 runtime{1, sizeof(runtime)};
    CHECK(runtimeInfo(&runtime) == 0 && runtime.abiVersion == 1 && runtime.byteSize == sizeof(runtime)
        && runtime.attached == 0 && runtime.unknown == 0 && runtime.target == ready.runtimeTarget
        && reinterpret_cast<const void*>(runtime.original) == ready.runtimeOriginal && runtime.entry == ready.runtimeEntry && runtime.epoch == ready.runtimeEpoch
        && runtime.entered == 0 && runtime.exited == 0 && runtime.inFlight == 0 && runtime.observerFaults == 0);
    RizVoidBootstrapInfoV1 parent{1, sizeof(parent)};
    CHECK(voidInfo(ready.voidSlot, &parent) == 0 && parent.abiVersion == 1 && parent.byteSize == sizeof(parent)
        && parent.slot == ready.voidSlot && parent.attached == 0 && parent.unknown == 0 && parent.reserved == 0
        && parent.target == ready.unityTarget && reinterpret_cast<const void*>(parent.original) == ready.unityOriginal
        && parent.entry == ready.unityEntry && parent.epoch == ready.unityEpoch);
    CHECK(std::memcmp(base + Profile::RuntimeRva, Profile::RuntimePrefix, sizeof(Profile::RuntimePrefix)) != 0);
    CHECK(std::memcmp(base + Profile::UnityRva, Profile::UnityPrefix, sizeof(Profile::UnityPrefix)) != 0);
    CHECK(std::memcmp(base + Profile::RuntimeRva + 32, Profile::RuntimePrefix + 32, sizeof(Profile::RuntimePrefix) - 32) == 0);
    CHECK(std::memcmp(base + Profile::UnityRva + 32, Profile::UnityPrefix + 32, sizeof(Profile::UnityPrefix) - 32) == 0);
    CHECK(std::memcmp(base + Profile::InitRva, Profile::InitPrefix, sizeof(Profile::InitPrefix)) == 0);
    CHECK(std::memcmp(base + Profile::RuntimeExportRva, Profile::RuntimeExportPrefix, sizeof(Profile::RuntimeExportPrefix)) == 0);
    const auto* site = base + Profile::MidRva;
    CHECK(site[0] == 0xe8 && site + 5 == ready.midReturnPc);
    std::int32_t midDisplacement = 0; std::memcpy(&midDisplacement, site + 1, sizeof(midDisplacement));
    const auto* decodedRelay = site + 5 + midDisplacement;
    CHECK(decodedRelay == ready.relay);
    const auto* relay = static_cast<const unsigned char*>(ready.relay);
    const unsigned char expectedJump[]{0xff, 0x25, 0, 0, 0, 0};
    CHECK(std::memcmp(relay, expectedJump, sizeof(expectedJump)) == 0);
    const void* decodedHelper{}; std::memcpy(&decodedHelper, relay + 6, sizeof(decodedHelper));
    CHECK(decodedHelper == midEntry());
    CHECK(std::memcmp(base + Profile::MidRva - 11, Profile::MidWindow, 11) == 0);
    CHECK(std::memcmp(base + Profile::MidRva + 5, Profile::MidWindow + 16, sizeof(Profile::MidWindow) - 16) == 0);
    CHECK(Memory(base + Profile::RuntimeRva).Protect == runtimeMemory.Protect
        && Memory(base + Profile::UnityRva).Protect == unityMemory.Protect && Memory(site).Protect == midMemory.Protect);
    ExecutableImage(ready.runtimeTarget, image); ExecutableImage(ready.unityTarget, image); ExecutableImage(site, image);
    ExecutableImage(ready.runtimeEntry, helper); ExecutableImage(ready.unityEntry, helper); ExecutableImage(decodedHelper, helper);
    ExecutablePrivate(ready.runtimeOriginal); ExecutablePrivate(ready.unityOriginal); ExecutablePrivate(relay);
    CHECK((Memory(relay).Protect & 0xffu) == PAGE_EXECUTE_READ);
    CheckFunctionUnchanged(base, Profile::InitRva, initFunction);
    CheckFunctionUnchanged(base, Profile::RuntimeRva, runtimeFunction);
    CheckFunctionUnchanged(base, Profile::UnityRva, unityFunction);
    CheckFunctionUnchanged(base, Profile::MidRva, midFunction);
    for (int iteration = 0; iteration != 10; ++iteration) {
        RizFirstInstallInfoV1 again{1, sizeof(again)};
        CHECK(firstInfo(&again) == 0 && std::memcmp(&again, &ready, sizeof(ready)) == 0);
        auto againV2 = RizMappedChecks::Header();
        CHECK(firstInfoV2(&againV2) == 0 && std::memcmp(&againV2, &readyV2, sizeof(readyV2)) == 0);
        auto againV7 = RizOperationChecks::Header(); CHECK(firstInfoV7(&againV7) == 0 && std::memcmp(&againV7, &readyV7, sizeof(readyV7)) == 0);
        RizRuntimeInvokeInfoV1 currentRuntime{1, sizeof(currentRuntime)};
        CHECK(runtimeInfo(&currentRuntime) == 0 && currentRuntime.attached == 0 && currentRuntime.unknown == 0
            && currentRuntime.entered == 0 && currentRuntime.exited == 0 && currentRuntime.inFlight == 0 && currentRuntime.observerFaults == 0
            && currentRuntime.target == runtime.target && currentRuntime.original == runtime.original
            && currentRuntime.entry == runtime.entry && currentRuntime.epoch == runtime.epoch);
        RizVoidBootstrapInfoV1 currentParent{1, sizeof(currentParent)};
        CHECK(voidInfo(ready.voidSlot, &currentParent) == 0 && std::memcmp(&currentParent, &parent, sizeof(parent)) == 0);
    }
    RizVoidStatsV1 parentStats{1, sizeof(parentStats)};
    CHECK(voidStats(ready.voidSlot, &parentStats) == 0 && parentStats.entered == 0 && parentStats.exited == 0
        && parentStats.inFlight == 0 && parentStats.enterFaults == 0 && parentStats.exitFaults == 0);
    RizEventMidStatsV1 eventStats{1, sizeof(eventStats)};
    CHECK(midStats(ready.voidSlot, &eventStats) == 0 && eventStats.entered == 0 && eventStats.exited == 0 && eventStats.inFlight == 0 && eventStats.faults == 0);
    bool tamperDetected = false;
    bool singlePermanentUnknownAfterRestore=false;
    int tamperedOperationId = -1;
    int tamperedSourceSite = -1;
    std::uint32_t tamperedOperationBodyOffset = 0;
    int tamperedSingleContinuation = -1;
    std::uint32_t tamperedSingleMetadata = 0;
    if (tamper) {
        const void* changed = readyV2.sourcePc[0];
        if(singleSpan>=0)changed=static_cast<const unsigned char*>(readyV7.singleEvent.spanPc[singleSpan])+singleOffset;
        else if(singleBlockOffset>=0)changed=static_cast<const unsigned char*>(readyV7.singleEvent.allocation)+singleBlockOffset;
        else if(singleProtection>=0)changed=singleProtection==7?readyV7.singleEvent.allocation:readyV7.singleEvent.spanPc[singleProtection];
        else if (std::wcscmp(mode, L"tamper-post") == 0) changed = readyV2.sourceReturnPc[1];
        else if (std::wcscmp(mode, L"tamper-relay") == 0) changed = static_cast<const unsigned char*>(readyV2.preRelay) + 6;
        else if (std::wcscmp(mode, L"tamper-chain") == 0) changed = static_cast<const unsigned char*>(readyV2.unwindInfo[0]) + 4;
        else if (std::wcscmp(mode, L"tamper-operation") == 0) changed = readyV7.operations[12].target;
        else if (std::wcscmp(mode, L"tamper-delegate-remove") == 0) changed = readyV7.operations[13].target;
        else if (std::wcscmp(mode, L"tamper-single-event") == 0) {
            tamperedOperationId = 25;
            const auto& row = readyV7.operations[tamperedOperationId];
            const auto& last = Profile::OperationContinuations[Profile::OperationContinuationCount - 1];
            CHECK(row.bodySize == 122 && last.operationId == row.id && last.endRva > row.rva + row.bodySize);
            tamperedSingleContinuation = static_cast<int>(last.index);
            tamperedOperationBodyOffset = last.endRva - row.rva - 1;
            changed = static_cast<const unsigned char*>(row.target) + tamperedOperationBodyOffset;
        }
        else if (std::wcscmp(mode, L"tamper-single-event-unwind") == 0
            || std::wcscmp(mode, L"tamper-single-event-chain") == 0
            || std::wcscmp(mode, L"tamper-single-event-pdata") == 0) {
            const auto& continuation = Profile::OperationContinuations[1];
            tamperedSingleContinuation = static_cast<int>(continuation.index);
            const auto* singleImage = static_cast<const unsigned char*>(readyV7.operations[25].target) - Profile::Operations[25].rva;
            if (std::wcscmp(mode, L"tamper-single-event-pdata") == 0) {
                tamperedSingleMetadata = 3;
                const auto* rows = reinterpret_cast<const RUNTIME_FUNCTION*>(singleImage + Profile::PdataRva);
                changed = nullptr;
                for (std::size_t i = 0; i < Profile::PdataSize / sizeof(RUNTIME_FUNCTION); ++i) {
                    if (rows[i].BeginAddress == continuation.rva) { changed = &rows[i].UnwindData; break; }
                }
                CHECK(changed != nullptr);
            } else {
                tamperedSingleMetadata = std::wcscmp(mode, L"tamper-single-event-unwind") == 0 ? 1u : 2u;
                changed = singleImage + continuation.unwindRva + (tamperedSingleMetadata == 1 ? 0 : continuation.unwindSize - 1);
            }
        }
        else if (RizTweenSourceChecks::ModeId(mode)>=0) {
            tamperedSourceSite=RizTweenSourceChecks::ModeId(mode);const auto& row=readySources.sites[tamperedSourceSite];
            changed=static_cast<const unsigned char*>(row.sourcePc)+row.width-1;
        }
        else if (std::wcscmp(mode,L"tamper-tween-source-relay")==0) changed=static_cast<const unsigned char*>(readySources.sites[0].preRelay)+1;
        else if (std::wcscmp(mode,L"tamper-tween-source-unwind")==0) changed=static_cast<const unsigned char*>(readySources.sites[0].unwindInfo)+4;
        else if (std::wcscmp(mode,L"tamper-tween-source-table")==0) changed=static_cast<const unsigned char*>(readySources.functionTable)+4;
        else {
            const wchar_t* tweenModes[]{ L"tamper-tween-play", L"tamper-tween-add", L"tamper-tween-execute",
                L"tamper-tween-update", L"tamper-tween-awake" };
            for (unsigned index = 0; index != 5; ++index) {
                if (std::wcscmp(mode, tweenModes[index]) != 0) continue;
                tamperedOperationId = static_cast<int>(20 + index);
                const auto& row = readyV7.operations[tamperedOperationId];
                CHECK(row.bodySize > 32);
                tamperedOperationBodyOffset = row.bodySize - 1;
                // The last byte is outside the detour prefix and may be inline
                // switch data. A complete body seal must reject its mutation.
                changed = static_cast<const unsigned char*>(row.target) + tamperedOperationBodyOffset;
                break;
            }
        }
        DWORD beforeProtection{};
        if(singleProtection>=0)CHECK(VirtualProtect(const_cast<void*>(changed),1,PAGE_EXECUTE_READWRITE,&beforeProtection));
        else TamperByte(changed);
        auto corrupted = RizMappedChecks::Header();
        tamperDetected = firstInfoV2(&corrupted) == 0 && RizMappedChecks::SameUnknown(corrupted, readyV2)
            && corrupted.common.writesStarted == 64;
        CHECK(tamperDetected);
        // Restoring the input never restores a permanently Unknown owner.
        if(singleTamper){
            if(singleProtection>=0){DWORD ignored{};CHECK(VirtualProtect(const_cast<void*>(changed),1,beforeProtection,&ignored));}
            else TamperByte(changed);
            auto restored=RizOperationChecks::Header();singlePermanentUnknownAfterRestore=firstInfoV7(&restored)==0&&RizOperationChecks::SameUnknown(restored,readyV7);
            CHECK(singlePermanentUnknownAfterRestore);
        }
        auto corruptV7 = RizOperationChecks::Header(); CHECK(firstInfoV7(&corruptV7) == 0 && RizOperationChecks::SameUnknown(corruptV7, readyV7));
        RizFirstInstallInfoV1 corruptV1{1, sizeof(corruptV1)};
        CHECK(firstInfo(&corruptV1) == 0 && corruptV1.phase == RizFirstInstallUnknown && corruptV1.unknown == 1 && corruptV1.writesStarted == 64);
    }
    // Only after all Ready observations, verify a second init attempt is
    // rejected and invalidates the receipt without another instruction write.
    unsigned char publishedRuntime[sizeof(Profile::RuntimePrefix)]{}, publishedUnity[sizeof(Profile::UnityPrefix)]{};
    unsigned char publishedMid[5]{}, publishedRelay[14]{};
    std::memcpy(publishedRuntime, base + Profile::RuntimeRva, sizeof(publishedRuntime));
    std::memcpy(publishedUnity, base + Profile::UnityRva, sizeof(publishedUnity));
    std::memcpy(publishedMid, site, sizeof(publishedMid)); std::memcpy(publishedRelay, relay, sizeof(publishedRelay));
    const auto publishedListener = RizMappedChecks::CapturePublished(base, readyV2, Check);
    const auto publishedOperations = RizOperationChecks::Capture(base, Check);
    const auto reentryStatus = beforeInit(reinterpret_cast<const void*>(init), argv[2]);
    RizFirstInstallInfoV1 currentInfo{1, sizeof(currentInfo)};
    const auto reentryRead = firstInfo(&currentInfo);
    const auto reentryRejected = reentryStatus == 2 && reentryRead == 0
        && currentInfo.phase == RizFirstInstallUnknown && currentInfo.unknown == 1 && currentInfo.writesStarted == 64;
    CHECK(reentryRejected);
    auto expectedUnknown = ready; expectedUnknown.phase = RizFirstInstallUnknown; expectedUnknown.unknown = 1;
    CHECK(std::memcmp(&currentInfo, &expectedUnknown, sizeof(currentInfo)) == 0);
    auto currentV2 = RizMappedChecks::Header();
    const auto v2UnknownVerified = firstInfoV2(&currentV2) == 0 && RizMappedChecks::SameUnknown(currentV2, readyV2)
        && currentV2.common.writesStarted == 64;
    CHECK(v2UnknownVerified);
    auto currentV7 = RizOperationChecks::Header();
    const auto v7UnknownVerified = firstInfoV7(&currentV7) == 0 && RizOperationChecks::SameUnknown(currentV7, readyV7);
    CHECK(v7UnknownVerified);
    const auto reentryBytesUnchanged = std::memcmp(publishedRuntime, base + Profile::RuntimeRva, sizeof(publishedRuntime)) == 0
        && std::memcmp(publishedUnity, base + Profile::UnityRva, sizeof(publishedUnity)) == 0
        && std::memcmp(publishedMid, site, sizeof(publishedMid)) == 0 && std::memcmp(publishedRelay, relay, sizeof(publishedRelay)) == 0
        && RizMappedChecks::Unchanged(base, publishedListener) && RizOperationChecks::Unchanged(base, publishedOperations);
    CHECK(reentryBytesUnchanged);
    CHECK(Memory(base + Profile::RuntimeRva).Protect == runtimeMemory.Protect
        && Memory(base + Profile::UnityRva).Protect == unityMemory.Protect && Memory(site).Protect == midMemory.Protect);
    RizRuntimeInvokeInfoV1 afterReentryRuntime{1, sizeof(afterReentryRuntime)};
    CHECK(runtimeInfo(&afterReentryRuntime) == 0 && std::memcmp(&afterReentryRuntime, &runtime, sizeof(runtime)) == 0);
    RizVoidBootstrapInfoV1 afterReentryParent{1, sizeof(afterReentryParent)};
    CHECK(voidInfo(ready.voidSlot, &afterReentryParent) == 0 && std::memcmp(&afterReentryParent, &parent, sizeof(parent)) == 0);
    RizListenerInfoV1 afterReentryListener{1, sizeof(afterReentryListener)};
    CHECK(listenerInfo(31, &afterReentryListener) == 0 && std::memcmp(&afterReentryListener, &listener, sizeof(listener)) == 0);
    const auto listenerSourceBytesVerified = listenerEvidence.sources == 3;
    const auto listenerPostBytesVerified = listenerEvidence.posts == 3;
    const auto listenerRxVerified = listenerEvidence.relays == 2;
    const auto listenerUnwindVerified = listenerEvidence.lookups == 12 && listenerEvidence.boundaries == 12;
    CHECK(listenerSourceBytesVerified && listenerPostBytesVerified && listenerRxVerified && listenerUnwindVerified
        && listenerEvidence.restoredProtections == 6 && invalidHeaders == 9);
    std::printf("{\"schemaVersion\":1,\"kind\":\"fixed-mapped-image-first-install-publication\",\"mode\":\"%ls\",\"passed\":%s,\"checks\":%llu,\"failures\":%llu,\"status\":%u,\"phase\":%u,\"unknown\":%u,\"writesStarted\":%llu,\"phaseObservation\":\"before-ready-reentry\",\"readyReentryStatus\":%u,\"readyReentryRejected\":%s,\"reentryBytesUnchanged\":%s,\"currentPhase\":%u,\"currentUnknown\":%u,\"currentWritesStarted\":%llu,\"voidSlot\":%u,\"runtimeEpoch\":%llu,\"unityEpoch\":%llu,\"mappedImageOnly\":true,\"gameCodeExecuted\":false,\"gameInitExecuted\":false,\"transportInvocations\":0,\"repeatedKnownInfoReads\":10,\"actualParentRuntimeBytesChanged\":%s,\"actualMidCallAndRelayVerified\":%s,\"pdataAndRtlLookupAgreed\":%s,\"pageProtectionsRestored\":%s,",
        mode, failures ? "false" : "true", checks, failures, status, ready.phase, ready.unknown, ready.writesStarted,
        reentryStatus, reentryRejected ? "true" : "false", reentryBytesUnchanged ? "true" : "false", currentInfo.phase, currentInfo.unknown, currentInfo.writesStarted,
        ready.voidSlot, ready.runtimeEpoch, ready.unityEpoch,
        failures ? "false" : "true", failures ? "false" : "true", failures ? "false" : "true", failures ? "false" : "true");
    std::printf("\"operationReceiptVersion\":7,\"operationRowsVerified\":%u,\"invalidV7HeaderCases\":%u,\"v7UnknownVerified\":%s,\"operationCallbacksExecuted\":0,",
        operationRowsVerified, invalidV7Headers, v7UnknownVerified ? "true" : "false");
    RizSingleEventChecks::Print(readyV7,singleEvidence,singleSpan,singleOffset,singleBlockOffset,singleProtection);
    std::printf("\"fixtureProtectionChanges\":%u,\"fixtureRestorations\":%u,\"singlePermanentUnknownAfterRestore\":%s,",
        singleProtection>=0?1u:0u,singleTamper?1u:0u,singlePermanentUnknownAfterRestore?"true":"false");
    std::printf("\"tweenSourceRowsVerified\":%u,\"tweenSourceBindingsVerified\":43,\"tweenSourceFunctionsVerified\":56,\"tamperedTweenSourceSite\":%d,\"tweenSourceCallbacksExecuted\":0,",
        sourceRowsVerified,tamperedSourceSite);
    std::printf("\"tamperedOperationId\":%d,\"tamperedOperationBodyOffset\":%u,\"tweenFullBodyTailTamperVerified\":%s,"
        "\"singleEventFullBodyTailTamperVerified\":%s,",
        tamperedOperationId, tamperedOperationBodyOffset,
        tamperedOperationId >= 20 && tamperedOperationId <= 24 && tamperDetected ? "true" : "false",
        tamperedOperationId == 25 && tamperDetected ? "true" : "false");
    std::printf("\"singleEventContinuationsVerified\":%u,\"tamperedSingleContinuation\":%d,\"tamperedSingleMetadata\":%u,",
        Profile::OperationContinuationCount, tamperedSingleContinuation, tamperedSingleMetadata);
    std::printf("\"v2ReadyVerified\":%s,\"v2UnknownVerified\":%s,\"invalidV2HeaderCases\":%u,\"v2CallerIdentityCanonicalized\":%s,"
        "\"listenerSourceBytesVerified\":%s,\"listenerPostBytesVerified\":%s,\"listenerRxVerified\":%s,\"listenerUnwindVerified\":%s,"
        "\"listenerBoundaryChecks\":%u,\"listenerLookupChecks\":%u,\"listenerRestoredProtectionChecks\":%u,\"listenerEpoch\":%llu,"
        "\"sourceReturnPcObservedByExecution\":false,\"unwindContext\":\"self-owned-stack-actual-producer-chain\",\"tamperDetected\":%s,"
        "\"fixtureTamperWrites\":%u,\"noAdditionalProducerWrite\":%s,\"firstInstallSafetyVerified\":false,\"gameRuntimeVerified\":false}\n",
        v2ReadyVerified ? "true" : "false", v2UnknownVerified ? "true" : "false", invalidHeaders, callerIdentityCanonicalized ? "true" : "false",
        listenerSourceBytesVerified ? "true" : "false", listenerPostBytesVerified ? "true" : "false", listenerRxVerified ? "true" : "false",
        listenerUnwindVerified ? "true" : "false", listenerEvidence.boundaries, listenerEvidence.lookups, listenerEvidence.restoredProtections, readyV2.listenerEpoch,
        tamperDetected ? "true" : "false", tamper&&singleProtection<0 ? 1u : 0u, reentryBytesUnchanged && currentInfo.writesStarted == 64 ? "true" : "false");
    // All native publication roots and the mapped image intentionally remain
    // owned by this short-lived process. No unload or detour removal is tested.
    return failures ? 1 : 0;
}
