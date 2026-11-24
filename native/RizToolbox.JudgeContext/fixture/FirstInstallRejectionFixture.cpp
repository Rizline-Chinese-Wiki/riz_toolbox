#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../FirstInstall.h"
#include "../FirstInstallProfile.h"
#include "../BootstrapVoidContext.h"
#include "../RuntimeInvokeContext.h"
#include "FirstInstallMappedChecks.h"
#include "FirstInstallFixtureReads.h"

namespace {
volatile LONG checks{}, failures{};
void Check(bool condition, const char* expression, int line)
{
    InterlockedIncrement(&checks);
    if (!condition) { InterlockedIncrement(&failures); std::fprintf(stderr, "FAILED first-install rejection line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
template<class T> T Symbol(HMODULE module, const char* name)
{
    const auto pointer = GetProcAddress(module, name); CHECK(pointer);
    T result{}; static_assert(sizeof(result) == sizeof(pointer)); std::memcpy(&result, &pointer, sizeof(result)); return result;
}
}

// Each command is a separate finite process because the production producer
// deliberately offers no reset/test mode. No actual game image is loaded here.
int wmain(int argc, wchar_t** argv)
{
    RizMappedChecks::DisableErrorUi();
    if (argc != 4) { std::fprintf(stderr, "Usage: FirstInstall.Rejection.Fixture.exe helper.dll dobby.dll null|private|module|unmapped|badpath\n"); return 2; }
    const auto helper = LoadLibraryW(argv[1]); CHECK(helper); if (!helper) return 2;
    const auto before = Symbol<decltype(&RizFirstInstall_BeforeIl2CppInit)>(helper, "RizFirstInstall_BeforeIl2CppInit");
    RizFixturePublication::reader = Symbol<RizFixturePublication::PacketReader>(helper, "RizFirstInstall_GetInfoV7");
    const auto getInfo = &RizFixturePublication::Common;
    const auto getInfoV2 = &RizFixturePublication::Listener;
    const auto getRuntime = Symbol<decltype(&RizRuntimeInvokeContext_GetInfo)>(helper, "RizRuntimeInvokeContext_GetInfo");
    const auto getUnity = Symbol<decltype(&RizVoidBootstrap_GetInfo)>(helper, "RizVoidBootstrap_GetInfo");
    const auto getListener = Symbol<decltype(&RizListenerContext_GetInfo)>(helper, "RizListenerContext_GetInfo");
    if (!before || !RizFixturePublication::reader || !getRuntime || !getUnity || !getListener) return 2;
    RizFirstInstallInfoV1 first{ 1, sizeof(first) }; CHECK(getInfo(&first) == 0);
    CHECK(first.phase == RizFirstInstallNever && first.unknown == 0 && first.writesStarted == 0 && first.voidSlot == 31);
    auto firstV2 = RizMappedChecks::Header(); CHECK(getInfoV2(&firstV2) == 0);
    RizMappedChecks::CommonAgrees(first, firstV2, Check);
    const auto invalidV2Headers = RizMappedChecks::InvalidHeaders(getInfoV2, firstV2, Check);
    const auto canonicalizedNever = RizMappedChecks::Canonicalized(getInfoV2, firstV2, Check);
    auto invalid = first; invalid.abiVersion = 2; CHECK(getInfo(&invalid) == 1);
    invalid = first; invalid.reserved0 = 1; CHECK(getInfo(&invalid) == 1);
    invalid = first; invalid.reserved1 = 1; CHECK(getInfo(&invalid) == 1);
    CHECK(getInfo(nullptr) == 1);
    const void* init = nullptr;
    const wchar_t* dobby = argv[2];
    unsigned char* privateMemory = nullptr;
    unsigned char originalBytes[64]{};
    if (_wcsicmp(argv[3], L"null") == 0) {
        init = nullptr;
    } else if (_wcsicmp(argv[3], L"private") == 0) {
        privateMemory = static_cast<unsigned char*>(VirtualAlloc(nullptr, 4096, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
        CHECK(privateMemory); if (!privateMemory) return 2;
        std::memset(privateMemory, 0xcc, 4096);
        std::memcpy(privateMemory, RizFirstInstallProfile::InitPrefix, sizeof(RizFirstInstallProfile::InitPrefix));
        std::memcpy(originalBytes, privateMemory, sizeof(originalBytes));
        DWORD old{}; CHECK(VirtualProtect(privateMemory, 4096, PAGE_EXECUTE_READ, &old));
        CHECK(FlushInstructionCache(GetCurrentProcess(), privateMemory, 4096)); init = privateMemory;
    } else if (_wcsicmp(argv[3], L"module") == 0 || _wcsicmp(argv[3], L"badpath") == 0) {
        init = reinterpret_cast<const void*>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "Sleep")); CHECK(init);
        CHECK(std::memcmp(init, originalBytes, sizeof(originalBytes)) != 0); std::memcpy(originalBytes, init, sizeof(originalBytes));
        if (_wcsicmp(argv[3], L"badpath") == 0) dobby = reinterpret_cast<const wchar_t*>(0x12345000ULL);
    } else if (_wcsicmp(argv[3], L"unmapped") == 0) {
        init = reinterpret_cast<const void*>(0x12345000ULL);
    } else { std::fprintf(stderr, "Unsupported rejection mode\n"); return 2; }
    CHECK(before(init, dobby) == 0);
    CHECK(getInfo(&first) == 0 && first.phase == RizFirstInstallDisabledNoWrite
        && first.unknown == 0 && first.writesStarted == 0 && first.runtimeEpoch == 0 && first.unityEpoch == 0);
    CHECK(first.moduleBase == 0 && first.imageSize == 0 && first.init == nullptr && first.runtimeTarget == nullptr
        && first.runtimeOriginal == nullptr && first.runtimeEntry == nullptr && first.unityTarget == nullptr
        && first.unityOriginal == nullptr && first.unityEntry == nullptr && first.midReturnPc == nullptr && first.relay == nullptr);
    auto disabledV2 = RizMappedChecks::Header(); CHECK(getInfoV2(&disabledV2) == 0);
    RizMappedChecks::CommonAgrees(first, disabledV2, Check);
    const auto disabledV2NoIdentity = disabledV2.common.phase == RizFirstInstallDisabledNoWrite && disabledV2.common.unknown == 0
        && disabledV2.common.writesStarted == 0 && disabledV2.listenerEpoch == 0 && disabledV2.sourcePc[0] == nullptr
        && disabledV2.sourceReturnPc[0] == nullptr && disabledV2.functionTable == nullptr && disabledV2.registeredFunctionCount == 0;
    CHECK(disabledV2NoIdentity);
    const auto canonicalizedDisabled = RizMappedChecks::Canonicalized(getInfoV2, disabledV2, Check);
    // Different arguments cannot retry a rejected process lifetime attempt.
    CHECK(before(reinterpret_cast<const void*>(GetProcAddress(helper, "RizFirstInstall_GetInfoV7")), argv[2]) == 0);
    CHECK(getInfo(&first) == 0 && first.phase == RizFirstInstallDisabledNoWrite && first.writesStarted == 0 && first.unknown == 0);
    auto repeatedV2 = RizMappedChecks::Header();
    CHECK(getInfoV2(&repeatedV2) == 0 && std::memcmp(&repeatedV2, &disabledV2, sizeof(disabledV2)) == 0);
    RizRuntimeInvokeInfoV1 runtime{ 1, sizeof(runtime) };
    CHECK(getRuntime(&runtime) == 2 && runtime.unknown == 0 && runtime.attached == 0
        && runtime.target == nullptr && runtime.original == nullptr && runtime.epoch == 0);
    RizVoidBootstrapInfoV1 unity{ 1, sizeof(unity) };
    CHECK(getUnity(31, &unity) == 2 && unity.unknown == 0 && unity.attached == 0
        && unity.target == nullptr && unity.original == nullptr && unity.epoch == 0);
    RizListenerInfoV1 listener{1, sizeof(listener)};
    CHECK(getListener(31, &listener) == 2 && listener.unknown == 0 && listener.attached == 0 && listener.epoch == 0
        && listener.parentEpoch == 0 && listener.entered == 0 && listener.exited == 0 && listener.inFlight == 0);
    CHECK(GetModuleHandleW(L"GameAssembly.dll") == nullptr && invalidV2Headers == 9);
    if (privateMemory) {
        MEMORY_BASIC_INFORMATION memory{};
        CHECK(std::memcmp(privateMemory, originalBytes, sizeof(originalBytes)) == 0
            && VirtualQuery(privateMemory, &memory, sizeof(memory)) == sizeof(memory) && memory.Protect == PAGE_EXECUTE_READ);
    } else if (init && _wcsicmp(argv[3], L"unmapped") != 0) CHECK(std::memcmp(init, originalBytes, sizeof(originalBytes)) == 0);
    std::printf("{\"schemaVersion\":1,\"kind\":\"first-install-rejection\",\"mode\":\"%ls\",\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"writesStarted\":%llu,"
        "\"v2DisabledVerified\":%s,\"invalidV2HeaderCases\":%u,\"v2CallerIdentityCanonicalized\":%s,\"listenerConfigured\":false,\"gameLoaded\":false,\"gameCodeCalled\":false}\n",
        argv[3], failures ? "false" : "true", checks, failures, first.writesStarted, disabledV2NoIdentity ? "true" : "false", invalidV2Headers,
        canonicalizedNever && canonicalizedDisabled ? "true" : "false");
    return failures ? 1 : 0;
}
