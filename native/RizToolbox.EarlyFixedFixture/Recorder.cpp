#define RIZ_EARLY_RECORDER_BUILD
#include "EarlyFixedShared.h"
#include <intrin.h>

// All storage has constant/zero initialization. No CRT, heap, TLS object,
// formatting, loader notification registration or cross-thread wait is used.
namespace {
RizEarlySharedV1* shared;
HANDLE mapping;
volatile LONG captureGate;
volatile LONG captureFaults, busyProbeEntered;
volatile LONG64 captureCalls;
std::uint32_t busyProbeInsideResult;
[[noreturn]] void Stop(std::uint32_t code)
{
    TerminateProcess(GetCurrentProcess(), 0xe04c0000u | (code & 0xffffu));
    __fastfail(7);
}
}
RIZ_EARLY_RECORDER_API RizEarlySharedV1* __cdecl RizEarlyRecorder_GetShared() { return shared; }
RIZ_EARLY_RECORDER_API std::uint32_t __cdecl RizEarlyRecorder_Capture()
{
    if(!shared)return 1;
    auto& meta=shared->publisherSnapshot;
    InterlockedIncrement64(&captureCalls);
    if(InterlockedCompareExchange(&captureGate,1,0)!=0){
        InterlockedOr(&captureFaults,RizEarlyCaptureBusy);
        // Native admission closes immediately. The gate owner publishes the
        // permanent bit into the final snapshot; no racing ordinary metadata
        // read/modify/write is needed in this losing invocation.
        InterlockedExchange(&shared->header.state,RizEarlyUnknown);
        return 2;
    }
    meta.version=RizEarlyVersion;meta.byteSize=sizeof(meta);
    meta.infoResult=3;meta.memoryResult=ERROR_GEN_FAILURE;meta.publisherState=UINT32_MAX;
    meta.flags=static_cast<std::uint32_t>(InterlockedCompareExchange(&captureFaults,0,0));
    __try {
        // One fixed, test-only early selector creates a REAL reentrant Capture
        // after this invocation acquired the gate. It does not model arbitrary
        // thread scheduling, register a callback, wait or execute target code.
        if(shared->header.config.mode==RizEarlyCaptureBusyProbe
            &&InterlockedCompareExchange(&busyProbeEntered,1,0)==0)
            busyProbeInsideResult=RizEarlyRecorder_Capture();
        shared->publisherInfo.version=RizFixedVersion;shared->publisherInfo.byteSize=sizeof(RizFixedInfoV1);
        if(RizFixedPublisher_GetVersion()==RizFixedVersion&&RizFixedPublisher_GetInfoSize()==sizeof(RizFixedInfoV1)){
            meta.infoResult=RizFixedPublisher_GetInfo(&shared->publisherInfo);
            if(meta.infoResult==0){meta.flags|=RizEarlyInfoAvailable;meta.publisherState=shared->publisherInfo.state;}
        }
        auto& memory=shared->actualMemory;
        memory.version=1;memory.byteSize=sizeof(memory);memory.reserved=0;
        SYSTEM_INFO system;GetSystemInfo(&system);memory.systemPageSize=system.dwPageSize;
        const auto module=GetModuleHandleW(RizEarlyTargetsModuleName);
        const char* const names[4]{"RizFixedOwnedStart","RizFixedOwnedExecute","RizFixedOwnedRelease","RizFixedOwnedDetach"};
        bool okay=module!=nullptr;DWORD failure=module?ERROR_SUCCESS:ERROR_MOD_NOT_FOUND;
        for(unsigned k=0;k!=4;++k){
            auto& row=memory.rows[k];row.kind=k;row.protection=0;row.allocationBase=0;
            row.target=module?reinterpret_cast<std::uint64_t>(GetProcAddress(module,names[k])):0;
            auto* bytes=reinterpret_cast<volatile unsigned char*>(row.bytes);for(unsigned j=0;j!=32;++j)bytes[j]=0;
            MEMORY_BASIC_INFORMATION page;
            if(!row.target||VirtualQuery(reinterpret_cast<void*>(row.target),&page,sizeof(page))!=sizeof(page)
                ||page.State!=MEM_COMMIT||page.AllocationBase!=module||page.Type!=MEM_IMAGE
                ||(page.Protect&(PAGE_GUARD|PAGE_NOACCESS))
                ||reinterpret_cast<std::uintptr_t>(page.BaseAddress)+page.RegionSize<row.target+32){okay=false;failure=ERROR_INVALID_ADDRESS;continue;}
            row.protection=page.Protect;row.allocationBase=reinterpret_cast<std::uint64_t>(page.AllocationBase);
            const auto* source=reinterpret_cast<const volatile unsigned char*>(row.target);
            for(unsigned j=0;j!=32;++j)bytes[j]=source[j];
        }
        meta.memoryResult=okay?0u:failure;
        if(okay)meta.flags|=RizEarlyMemoryAvailable;else meta.flags|=RizEarlyMemoryFault;
    }
    __except(EXCEPTION_EXECUTE_HANDLER){meta.memoryResult=GetExceptionCode();meta.flags|=RizEarlyMemoryFault;}
    if(meta.infoResult!=0||meta.memoryResult!=0
        ||(meta.flags&(RizEarlyInfoAvailable|RizEarlyMemoryAvailable))!=(RizEarlyInfoAvailable|RizEarlyMemoryAvailable))
        InterlockedOr(&captureFaults,RizEarlyMemoryFault);
    meta.flags|=static_cast<std::uint32_t>(InterlockedCompareExchange(&captureFaults,0,0));
    meta.captureCalls=static_cast<std::uint64_t>(InterlockedCompareExchange64(&captureCalls,0,0));
    // Header fields copy actual publisher facts, not guessed Commit intents.
    if(meta.flags&RizEarlyInfoAvailable){
        InterlockedExchange(&shared->header.writeAttempts,static_cast<LONG>(shared->publisherInfo.sourceWriteAttempts));
        InterlockedExchange(&shared->header.writeCompletions,static_cast<LONG>(shared->publisherInfo.sourceWriteCompletions));
    }
    InterlockedExchange(&captureGate,0);
    const bool okay=meta.flags==(RizEarlyInfoAvailable|RizEarlyMemoryAvailable)
        &&InterlockedCompareExchange(&captureFaults,0,0)==0;
    if(!okay)InterlockedExchange(&shared->header.state,RizEarlyUnknown);
    return okay?0u:1u;
}
RIZ_EARLY_RECORDER_API std::uint64_t __cdecl RizEarlyRecorder_Event(std::uint32_t type,
    std::uint32_t phase, std::uint32_t kind, std::uint32_t index, std::uint32_t result,
    std::uint32_t flags, std::uint64_t module, std::uint64_t target,
    std::uint64_t original, std::uint64_t entry, std::uint64_t epoch,
    std::uint64_t value0, std::uint64_t value1, std::uint64_t caller)
{
    if (!shared) Stop(RizEarlyBadMapping);
    auto& header = shared->header;
    const auto slot = InterlockedIncrement(&header.eventCount) - 1;
    const auto sequence = InterlockedIncrement64(&header.nextSequence);
    if (slot < 0 || static_cast<std::uint32_t>(slot) >= header.config.eventLimit
        || static_cast<std::uint32_t>(slot) >= RizEarlyEventCapacity || sequence <= 0) {
        InterlockedExchange(&header.overflow, 1);
        InterlockedCompareExchange(&header.failure, RizEarlyEventOverflow, 0);
        InterlockedExchange(&header.state, RizEarlyUnknown);
        RizEarlyRecorder_Capture();
        Stop(RizEarlyEventOverflow);
    }
    auto& row = shared->events[slot];
    row.pid = GetCurrentProcessId(); row.tid = GetCurrentThreadId();
    row.type = type; row.phase = phase; row.kind = kind; row.index = index;
    row.result = result; row.flags = flags; row.module = module;
    row.target = target; row.original = original; row.entry = entry; row.epoch = epoch;
    row.value0 = value0; row.value1 = value1; row.caller = caller;
    InterlockedExchange64(&row.sequence, sequence);
    return static_cast<std::uint64_t>(sequence);
}
RIZ_EARLY_RECORDER_API __declspec(noreturn) void __cdecl RizEarlyRecorder_Fail(std::uint32_t failure, std::uint32_t uncertain)
{
    if (!shared) Stop(failure);
    const auto captured=RizEarlyRecorder_Capture();
    auto& header = shared->header;
    const bool unknown = captured != 0 || uncertain != 0 || InterlockedCompareExchange(&captureFaults,0,0) != 0
        || InterlockedCompareExchange(&header.writeAttempts, 0, 0) != 0
        || InterlockedCompareExchange(&header.overflow, 0, 0) != 0
        || InterlockedCompareExchange(&header.state, 0, 0) == RizEarlyUnknown
        || shared->publisherSnapshot.infoResult != 0
        || shared->publisherSnapshot.flags != (RizEarlyInfoAvailable | RizEarlyMemoryAvailable)
        || shared->publisherInfo.state == RizFixedUnknown || shared->publisherInfo.protectionOutstanding != 0;
    InterlockedCompareExchange(&header.failure, static_cast<LONG>(failure), 0);
    InterlockedExchange(&header.state, unknown ? RizEarlyUnknown : RizEarlyDisabledNoWrite);
    if (!header.overflow && header.eventCount >= 0
        && static_cast<std::uint32_t>(header.eventCount) + 2u <= header.config.eventLimit) {
        RizEarlyRecorder_Event(RizEarlyReject, 0, 0, 0, failure, unknown ? 1u : 0u,
            header.proxyModule, 0, 0, 0, 0, static_cast<std::uint64_t>(header.writeAttempts),
            static_cast<std::uint64_t>(header.writeCompletions), 0);
        const auto sequence = RizEarlyRecorder_Event(RizEarlyTerminating, 0, 0, 0, failure,
            unknown ? 1u : 0u, header.proxyModule, 0, 0, 0, 0, GetCurrentProcessId(), 0, 0);
        InterlockedExchange64(&header.terminationSequence, static_cast<LONG64>(sequence));
    } else {
        InterlockedExchange(&header.overflow, 1);
        InterlockedExchange(&header.state, RizEarlyUnknown);
    }
    Stop(failure);
}
extern "C" BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason != DLL_PROCESS_ATTACH) return TRUE;
    wchar_t name[256];
    const auto length = GetEnvironmentVariableW(RizEarlyMappingEnvironment, name, 256);
    if (!length || length >= 256) Stop(RizEarlyBadMapping);
    mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name);
    if (!mapping) Stop(RizEarlyBadMapping);
    shared = static_cast<RizEarlySharedV1*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE,
        0, 0, sizeof(RizEarlySharedV1)));
    if (!shared) Stop(RizEarlyBadMapping);
    auto& header = shared->header;
    if (header.magic != RizEarlyMagic || header.version != RizEarlyVersion || header.byteSize != sizeof(*shared)
        || header.eventCapacity != RizEarlyEventCapacity || header.config.version != RizEarlyVersion
        || header.config.byteSize != sizeof(RizEarlyConfigV1)
        || !header.config.eventLimit || header.config.eventLimit > RizEarlyEventCapacity)
        Stop(RizEarlyBadMapping);
    if (InterlockedCompareExchange(&header.childPid, static_cast<LONG>(GetCurrentProcessId()), 0) != 0
        || InterlockedCompareExchange(&header.state, RizEarlyRecording, RizEarlyCreated) != RizEarlyCreated)
        RizEarlyRecorder_Fail(RizEarlyBadMapping, 1);
    header.recorderModule = reinterpret_cast<std::uint64_t>(module);
    const auto sequence = RizEarlyRecorder_Event(RizEarlyRecorderAttach, 0, 0, 0, reason, 0,
        header.recorderModule, 0, 0, 0, 0, sizeof(*shared), 0, 0);
    InterlockedExchange64(&header.recorderAttachSequence, static_cast<LONG64>(sequence));
    if(header.config.mode==RizEarlyCaptureBusyProbe){
        const auto first=RizEarlyRecorder_Capture();
        const auto later=RizEarlyRecorder_Capture();
        RizEarlyRecorder_Event(RizEarlyCaptureBusyObserved,0,4,0,first,shared->publisherSnapshot.flags,
            header.recorderModule,0,0,0,shared->publisherSnapshot.captureCalls,busyProbeInsideResult,later,0);
        // Always terminate this controlled negative child. Parent separately
        // requires inside=2, outer/later!=0, permanent Busy and zero importer.
        RizEarlyRecorder_Fail(RizEarlyCaptureFault,1);
    }
    // Mapping and handle remain retained until process exit. In particular,
    // no process-detach callback erases evidence after a loader-stage failure.
    return TRUE;
}
