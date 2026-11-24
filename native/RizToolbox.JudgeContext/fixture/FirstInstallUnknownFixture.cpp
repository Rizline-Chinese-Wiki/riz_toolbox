#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include "../FirstInstall.h"
#include "../FirstInstallProfile.h"
#include "../RuntimeInvokeContext.h"
#include "../BootstrapVoidContext.h"
#include "FirstInstallMappedChecks.h"
#include "FirstInstallOperationChecks.h"
#include "FirstInstallFixtureReads.h"
#include "FirstInstallSingleEventChecks.h"

    // This EXE compiles the actual FirstInstall.cpp with one compile-time pause
// seam. The production helper does not define that macro or contain this seam.
// GameAssembly is mapped only; neither its DllMain nor any game API is invoked.
namespace {
namespace Profile = RizFirstInstallProfile;
volatile LONG checks = 0, failures = 0, seamArmed = 0, seamCalls = 0, workerReturned = 0;
volatile LONG workerResult = -1;
HANDLE unknownWritten = nullptr, resumeWriter = nullptr;
const void* originalInit = nullptr;
const wchar_t* dobbyPath = nullptr;
int partialStop=-1;
void Check(bool condition, const char* expression, int line)
{
    InterlockedIncrement(&checks);
    if (!condition) { InterlockedIncrement(&failures); std::fprintf(stderr, "FAILED first-install unknown line %d: %s\n", line, expression); }
}
#define CHECK(x) Check(!!(x), #x, __LINE__)
RizFirstInstallInfoV1 Info()
{ RizFixturePublication::reader = &RizFirstInstall_GetInfoV7; RizFirstInstallInfoV1 value{1, sizeof(value)}; CHECK(RizFixturePublication::Common(&value) == 0); return value; }
RizFirstInstallInfoV2 InfoV2()
{ auto value = RizMappedChecks::Header(); CHECK(RizFixturePublication::Listener(&value) == 0); return value; }
DWORD WINAPI Reenter(void*)
{
    const auto result = RizFirstInstall_BeforeIl2CppInit(originalInit, dobbyPath);
    InterlockedExchange(&workerResult, static_cast<LONG>(result));
    InterlockedExchange(&workerReturned, 1);
    return 0;
}
bool SameUnknownIdentity(const RizFirstInstallInfoV1& value, const RizFirstInstallInfoV1& ready)
{
    auto expected = ready; expected.phase = RizFirstInstallUnknown; expected.unknown = 1;
    return std::memcmp(&value, &expected, sizeof(value)) == 0;
}
int PartialSingle(const unsigned char* base,unsigned stop)
{
    const auto status=RizFirstInstall_BeforeIl2CppInit(originalInit,dobbyPath);
    auto actual=RizOperationChecks::Header();CHECK(RizFirstInstall_GetInfoV7(&actual)==0);
    const auto& info=actual.singleEvent;const auto count=stop+1;const auto mask=(1u<<count)-1u;
    CHECK(status==2&&actual.publication.common.phase==RizFirstInstallUnknown&&actual.publication.common.unknown==1
        &&actual.publication.common.reserved1==RizFirstInstallFailurePreinitReentry
        &&actual.publication.common.writesStarted==35+count&&info.phase==RizFirstInstallUnknown&&info.unknown==1
        &&info.configuredMask==127&&info.publishedMask==mask&&info.attemptedWrites==count&&info.completedWrites==count
        &&info.epoch&&info.parentEpoch==actual.operations[25].epoch&&info.allocation&&info.allocationSize==4096&&info.registeredFunctionCount==3);
    for(unsigned span=0;span!=7;++span){const auto* source=base+RizSingleEventChecks::Rvas[span];const bool changed=span<count;
        const unsigned char* original=RizFirstInstallProfile::Operation25Continuation0Bytes+RizSingleEventChecks::Rvas[span]-0xbc106a;
        if(changed){unsigned char expected[9];std::memset(expected,0x90,9);const auto start=span==1||span==5?1u:0u;
            const bool call=span==0||span%2==1;const auto* target=span==0?info.midRelay:call?info.preRelay:info.postStub[(span-2)/2];
            RizSingleEventChecks::Relative(expected+start,source+start,target,5,call?0xe8u:0xe9u);
            CHECK(std::memcmp(source,expected,RizSingleEventChecks::Widths[span])==0);
        }else CHECK(std::memcmp(source,original,RizSingleEventChecks::Widths[span])==0);
    }
    MEMORY_BASIC_INFORMATION block{};CHECK(VirtualQuery(info.allocation,&block,sizeof(block))==sizeof(block)&&block.AllocationBase==info.allocation
        &&block.Type==MEM_PRIVATE&&block.Protect==PAGE_EXECUTE_READ);
    for(unsigned site=0;site!=3;++site){DWORD64 module{};const auto* rf=RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(info.postStub[site]),&module,nullptr);
        CHECK(rf&&module==reinterpret_cast<DWORD64>(base)&&std::memcmp(rf,static_cast<const RUNTIME_FUNCTION*>(info.functionTable)+site,sizeof(*rf))==0);
    }
    RizTweenSingleEventInfoV1 native{1,sizeof(native)};CHECK(RizTweenSingleEventContext_GetInfo(&native)==0&&native.configured==1&&!native.attached
        &&native.epoch==info.epoch&&native.parentEpoch==info.parentEpoch&&!native.parentsEntered&&!native.childrenEntered);
    CHECK(RizFirstInstall_BeforeIl2CppInit(originalInit,dobbyPath)==2);
    auto repeated=RizOperationChecks::Header();CHECK(RizFirstInstall_GetInfoV7(&repeated)==0&&std::memcmp(&actual,&repeated,sizeof(actual))==0);
    std::printf("{\"kind\":\"first-install-single-partial-publication\",\"mode\":\"partial-single-%u\",\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"stoppedAfterSpan\":%u,\"phase\":%u,\"unknown\":%u,\"writesStarted\":%llu,\"singleAttemptedWrites\":%llu,\"singleCompletedWrites\":%llu,\"singleConfiguredMask\":%u,\"singlePublishedMask\":%u,\"singleEpoch\":%llu,\"parentEpoch\":%llu,\"parentRowEpoch\":%llu,\"resourcesRetained\":true,\"reentryDidNotWrite\":true,\"actualHelperObjectsLinked\":true,\"mappedImageOnly\":true,\"gameCodeExecuted\":false,\"transportInvocations\":0}\n",
        stop,failures?"false":"true",checks,failures,stop,actual.publication.common.phase,actual.publication.common.unknown,actual.publication.common.writesStarted,
        info.attemptedWrites,info.completedWrites,info.configuredMask,info.publishedMask,info.epoch,info.parentEpoch,actual.operations[25].epoch);
    return failures?1:0;
}
}

extern "C" void __cdecl RizFirstInstallFixture_AfterUnknownAtomic()
{
    InterlockedIncrement(&seamCalls);
    if (InterlockedCompareExchange(&seamArmed, 0, 1) != 1) return;
    // This function is reached strictly after the real production atomic
    // unknown=1 store and before its exclusive snapshot-lock acquisition.
    CHECK(SetEvent(unknownWritten));
    CHECK(WaitForSingleObject(resumeWriter, 15000) == WAIT_OBJECT_0);
}
extern "C" void __cdecl RizFirstInstallFixture_AfterSingleWrite(std::uint32_t span)
{
    if(partialStop>=0&&span==static_cast<unsigned>(partialStop))CHECK(RizFirstInstall_BeforeIl2CppInit(originalInit,dobbyPath)==2);
}

int wmain(int argc, wchar_t** argv)
{
    RizMappedChecks::DisableErrorUi();
    if (argc != 3&&argc!=4) { std::fprintf(stderr, "Usage: FirstInstallUnknown.Fixture.exe inspected-dobby.dll fixed-GameAssembly.dll [partial-single-0..6]\n"); return 2; }
    if(argc==4&&(swscanf_s(argv[3],L"partial-single-%d",&partialStop)!=1||partialStop<0||partialStop>=7))return 2;
    if (GetModuleHandleW(L"GameAssembly.dll")) {
        std::fprintf(stderr, "GameAssembly already loaded into isolated fixture process.\n"); return 2;
    }
    dobbyPath = argv[1];
    unknownWritten = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    resumeWriter = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    CHECK(unknownWritten && resumeWriter); if (!unknownWritten || !resumeWriter) return 2;
    const auto game = LoadLibraryExW(argv[2], nullptr, DONT_RESOLVE_DLL_REFERENCES);
    CHECK(game); if (!game) return 2;
    const auto* base = reinterpret_cast<const unsigned char*>(game);
    originalInit = reinterpret_cast<const void*>(GetProcAddress(game, "il2cpp_init"));
    CHECK(originalInit == base + Profile::InitRva);
    if(partialStop>=0)return PartialSingle(base,static_cast<unsigned>(partialStop));
    const auto listenerBefore = RizMappedChecks::CaptureBefore(base, Check);
    const auto singleBefore = RizSingleEventChecks::Capture(base,Check);
    const auto status = RizFirstInstall_BeforeIl2CppInit(originalInit, dobbyPath);
    const auto ready = Info();
    CHECK(status == 1 && ready.phase == RizFirstInstallReady && ready.unknown == 0 && ready.writesStarted == 64
        && ready.moduleBase == reinterpret_cast<std::uint64_t>(base) && ready.imageSize == Profile::ImageSize && ready.voidSlot == 31);
    if (status != 1 || ready.phase != RizFirstInstallReady || ready.unknown != 0) return 1;
    auto readyV7 = RizOperationChecks::Header(); CHECK(RizFirstInstall_GetInfoV7(&readyV7) == 0);
    const auto singleEvidence=RizSingleEventChecks::Validate(base,GetModuleHandleW(nullptr),readyV7,singleBefore,Check);
    CHECK(readyV7.operationCount == 26 && readyV7.expectedWrites == 64 && readyV7.configuredMask == 0x3ffffff && readyV7.publishedMask == 0x3ffffff);
    const auto readyV2 = InfoV2(); RizMappedChecks::CommonAgrees(ready, readyV2, Check);
    const auto listenerEvidence = RizMappedChecks::ValidatePublished(base, readyV2, listenerBefore, Check);
    const auto invalidHeaders = RizMappedChecks::InvalidHeaders(&RizFixturePublication::Listener, readyV2, Check);
    const auto callerIdentityCanonicalized = RizMappedChecks::Canonicalized(&RizFixturePublication::Listener, readyV2, Check);
    const auto v2ReadyVerified = readyV2.common.phase == RizFirstInstallReady && readyV2.common.unknown == 0
        && readyV2.common.writesStarted == 64 && readyV2.listenerEpoch != 0;
    CHECK(v2ReadyVerified && invalidHeaders == 9);
    CHECK(InterlockedCompareExchange(&seamCalls, 0, 0) == 0);
    RizRuntimeInvokeInfoV1 runtime{1, sizeof(runtime)};
    RizVoidBootstrapInfoV1 parent{1, sizeof(parent)};
    CHECK(RizRuntimeInvokeContext_GetInfo(&runtime) == 0 && runtime.unknown == 0 && runtime.attached == 0
        && runtime.target == ready.runtimeTarget && reinterpret_cast<const void*>(runtime.original) == ready.runtimeOriginal
        && runtime.entry == ready.runtimeEntry && runtime.epoch == ready.runtimeEpoch
        && runtime.entered == 0 && runtime.exited == 0 && runtime.inFlight == 0 && runtime.observerFaults == 0);
    CHECK(RizVoidBootstrap_GetInfo(ready.voidSlot, &parent) == 0 && parent.unknown == 0 && parent.attached == 0
        && parent.target == ready.unityTarget && reinterpret_cast<const void*>(parent.original) == ready.unityOriginal
        && parent.entry == ready.unityEntry && parent.epoch == ready.unityEpoch);
    CHECK(ready.runtimeTarget == base + Profile::RuntimeRva && ready.unityTarget == base + Profile::UnityRva
        && ready.midReturnPc == base + Profile::MidReturnRva && ready.relay);
    RizListenerInfoV1 listener{1, sizeof(listener)};
    CHECK(RizListenerContext_GetInfo(31, &listener) == 0 && listener.unknown == 0 && listener.attached == 0
        && listener.epoch == readyV2.listenerEpoch && listener.parentEpoch == ready.unityEpoch
        && listener.preEntry == readyV2.preEntry && listener.postEntry == readyV2.postEntry
        && listener.entered == 0 && listener.exited == 0 && listener.inFlight == 0 && listener.faults == 0
        && listener.abnormal == 0 && listener.overflow == 0 && listener.missingPost == 0);
    unsigned char runtimeBytes[sizeof(Profile::RuntimePrefix)]{}, unityBytes[sizeof(Profile::UnityPrefix)]{}, midBytes[5]{}, relayBytes[14]{};
    std::memcpy(runtimeBytes, base + Profile::RuntimeRva, sizeof(runtimeBytes));
    std::memcpy(unityBytes, base + Profile::UnityRva, sizeof(unityBytes));
    std::memcpy(midBytes, base + Profile::MidRva, sizeof(midBytes));
    std::memcpy(relayBytes, ready.relay, sizeof(relayBytes));
    const auto listenerBytes = RizMappedChecks::CapturePublished(base, readyV2, Check);
    const auto operationBytes = RizOperationChecks::Capture(base, Check);
    CHECK(midBytes[0] == 0xe8 && std::memcmp(runtimeBytes, Profile::RuntimePrefix, sizeof(runtimeBytes)) != 0
        && std::memcmp(unityBytes, Profile::UnityPrefix, sizeof(unityBytes)) != 0);
    InterlockedExchange(&seamArmed, 1);
    const auto worker = CreateThread(nullptr, 0, Reenter, nullptr, 0, nullptr); CHECK(worker);
    if (!worker) return 2;
    CHECK(WaitForSingleObject(unknownWritten, 5000) == WAIT_OBJECT_0);
    CHECK(InterlockedCompareExchange(&seamCalls, 0, 0) == 1 && InterlockedCompareExchange(&workerReturned, 0, 0) == 0);
    CHECK(WaitForSingleObject(worker, 0) == WAIT_TIMEOUT && WaitForSingleObject(resumeWriter, 0) == WAIT_TIMEOUT);
    // No release signal is sent during this entire sequence. Thus unknown was
    // already 1 before every GetInfo began and the detailed writer is still
    // paused afterwards; accepting Ready cannot be explained as future change.
    std::uint32_t observedUnknown = 0;
    std::uint32_t observedUnknownV2 = 0, observedUnknownV7 = 0;
    for (unsigned index = 0; index != 100; ++index) {
        const auto value = Info();
        const auto exactUnknown = SameUnknownIdentity(value, ready);
        CHECK(exactUnknown && value.phase == RizFirstInstallUnknown && value.unknown == 1 && value.writesStarted == 64);
        if (exactUnknown) ++observedUnknown;
        const auto valueV2 = InfoV2(); const auto exactUnknownV2 = RizMappedChecks::SameUnknown(valueV2, readyV2);
        CHECK(exactUnknownV2 && valueV2.common.phase == RizFirstInstallUnknown && valueV2.common.unknown == 1
            && valueV2.common.writesStarted == 64);
        if (exactUnknownV2) ++observedUnknownV2;
        auto valueV7 = RizOperationChecks::Header();
        const auto exactUnknownV7 = RizFirstInstall_GetInfoV7(&valueV7) == 0 && RizOperationChecks::SameUnknown(valueV7, readyV7)
            && valueV7.publication.common.reserved1 == RizFirstInstallFailurePreinitReentry;
        CHECK(exactUnknownV7); if (exactUnknownV7) ++observedUnknownV7;
        CHECK(InterlockedCompareExchange(&workerReturned, 0, 0) == 0 && WaitForSingleObject(worker, 0) == WAIT_TIMEOUT);
    }
    CHECK(observedUnknown == 100 && observedUnknownV2 == 100 && observedUnknownV7 == 100 && WaitForSingleObject(resumeWriter, 0) == WAIT_TIMEOUT);
    CHECK(SetEvent(resumeWriter));
    const auto joined = WaitForSingleObject(worker, 5000); CHECK(joined == WAIT_OBJECT_0);
    if (joined != WAIT_OBJECT_0) ExitProcess(3);
    CHECK(InterlockedCompareExchange(&workerResult, 0, 0) == 2 && InterlockedCompareExchange(&workerReturned, 0, 0) == 1);
    const auto final = Info(); CHECK(SameUnknownIdentity(final, ready));
    const auto finalV2 = InfoV2();
    const auto v2UnknownVerified = RizMappedChecks::SameUnknown(finalV2, readyV2) && observedUnknownV2 == 100;
    CHECK(v2UnknownVerified && finalV2.common.writesStarted == 64);
    CHECK(std::memcmp(runtimeBytes, base + Profile::RuntimeRva, sizeof(runtimeBytes)) == 0
        && std::memcmp(unityBytes, base + Profile::UnityRva, sizeof(unityBytes)) == 0
        && std::memcmp(midBytes, base + Profile::MidRva, sizeof(midBytes)) == 0
        && std::memcmp(relayBytes, ready.relay, sizeof(relayBytes)) == 0);
    auto finalV7 = RizOperationChecks::Header();
    const auto v7UnknownVerified = RizFirstInstall_GetInfoV7(&finalV7) == 0 && RizOperationChecks::SameUnknown(finalV7, readyV7)
        && finalV7.publication.common.reserved1 == RizFirstInstallFailurePreinitReentry && observedUnknownV7 == 100;
    CHECK(v7UnknownVerified && RizOperationChecks::Unchanged(base, operationBytes));
    std::printf("{\"v7UnknownVerified\":%s,\"v7ObservedUnknownSnapshots\":%u,", v7UnknownVerified ? "true" : "false", observedUnknownV7);
    RizSingleEventChecks::Print(readyV7,singleEvidence,-1,-1,-1,-1);
    std::printf("\"actualHelperObjectsLinked\":true,\"productionDLLInvoked\":false,");
    const auto listenerBytesUnchanged = RizMappedChecks::Unchanged(base, listenerBytes); CHECK(listenerBytesUnchanged);
    RizRuntimeInvokeInfoV1 currentRuntime{1, sizeof(currentRuntime)};
    RizVoidBootstrapInfoV1 currentParent{1, sizeof(currentParent)};
    CHECK(RizRuntimeInvokeContext_GetInfo(&currentRuntime) == 0 && std::memcmp(&runtime, &currentRuntime, sizeof(runtime)) == 0);
    CHECK(RizVoidBootstrap_GetInfo(ready.voidSlot, &currentParent) == 0 && std::memcmp(&parent, &currentParent, sizeof(parent)) == 0);
    RizListenerInfoV1 currentListener{1, sizeof(currentListener)};
    CHECK(RizListenerContext_GetInfo(31, &currentListener) == 0 && std::memcmp(&listener, &currentListener, sizeof(listener)) == 0);
    RizVoidStatsV1 parentStats{1, sizeof(parentStats)};
    RizEventMidStatsV1 midStats{1, sizeof(midStats)};
    CHECK(RizVoidContext_GetStats(ready.voidSlot, &parentStats) == 0 && parentStats.entered == 0 && parentStats.exited == 0
        && parentStats.inFlight == 0 && parentStats.enterFaults == 0 && parentStats.exitFaults == 0);
    CHECK(RizEventMidContext_GetStats(ready.voidSlot, &midStats) == 0 && midStats.entered == 0 && midStats.exited == 0 && midStats.inFlight == 0 && midStats.faults == 0);
    CHECK(InterlockedCompareExchange(&seamCalls, 0, 0) == 1);
    std::printf("\"schemaVersion\":1,\"kind\":\"first-install-atomic-unknown-snapshot-race\",\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"readySnapshotPhase\":%u,\"readySnapshotUnknown\":%u,\"writesStarted\":%llu,\"unknownAtomicPrecedesEveryRead\":true,\"detailWriterPausedDuringReads\":true,\"readsWhileWriterPaused\":100,\"observedUnknownSnapshots\":%u,\"reentryStatus\":%ld,\"currentPhase\":%u,\"currentUnknown\":%u,\"currentWritesStarted\":%llu,\"productionFirstInstallSourceExecuted\":true,\"fixtureCompileTimeSeam\":true,\"nativeTransportsFromRealHelper\":true,\"mappedImageOnly\":true,\"gameCodeExecuted\":false,\"gameInitExecuted\":false,\"transportInvocations\":0,",
        failures ? "false" : "true", checks, failures, ready.phase, ready.unknown, ready.writesStarted, observedUnknown,
        workerResult, final.phase, final.unknown, final.writesStarted);
    std::printf("\"v2ReadyVerified\":%s,\"v2UnknownVerified\":%s,\"v2ObservedUnknownSnapshots\":%u,\"v2ReadsWhileWriterPaused\":100,"
        "\"invalidV2HeaderCases\":%u,\"v2CallerIdentityCanonicalized\":%s,\"listenerSourceBytesVerified\":%s,\"listenerPostBytesVerified\":%s,"
        "\"listenerRxVerified\":%s,\"listenerUnwindVerified\":%s,\"listenerBoundaryChecks\":%u,\"listenerLookupChecks\":%u,"
        "\"listenerRestoredProtectionChecks\":%u,\"listenerBytesUnchanged\":%s,\"sourceReturnPcObservedByExecution\":false,"
        "\"unwindContext\":\"self-owned-stack-actual-producer-chain\",\"noAdditionalProducerWrite\":%s,\"firstInstallSafetyVerified\":false,\"gameRuntimeVerified\":false}\n",
        v2ReadyVerified ? "true" : "false", v2UnknownVerified ? "true" : "false", observedUnknownV2,
        invalidHeaders, callerIdentityCanonicalized ? "true" : "false", listenerEvidence.sources == 3 ? "true" : "false",
        listenerEvidence.posts == 3 ? "true" : "false", listenerEvidence.relays == 2 ? "true" : "false",
        listenerEvidence.boundaries == 12 && listenerEvidence.lookups == 12 ? "true" : "false", listenerEvidence.boundaries,
        listenerEvidence.lookups, listenerEvidence.restoredProtections, listenerBytesUnchanged ? "true" : "false",
        listenerBytesUnchanged && final.writesStarted == 64 ? "true" : "false");
    CloseHandle(worker); CloseHandle(unknownWritten); CloseHandle(resumeWriter);
    return failures ? 1 : 0;
}
