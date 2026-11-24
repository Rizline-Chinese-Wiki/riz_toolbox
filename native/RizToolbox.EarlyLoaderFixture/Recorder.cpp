#define RIZ_EARLY_RECORDER_BUILD
#include "EarlyLoaderShared.h"
#include <intrin.h>

// All storage has constant/zero initialization. No CRT, heap, TLS object,
// formatting, loader notification registration or cross-thread wait is used.
namespace {
RizEarlySharedV1* shared;
HANDLE mapping;
[[noreturn]] void Stop(std::uint32_t code)
{
    TerminateProcess(GetCurrentProcess(), 0xe04c0000u | (code & 0xffffu));
    __fastfail(7);
}
}
RIZ_EARLY_RECORDER_API RizEarlySharedV1* __cdecl RizEarlyRecorder_GetShared() { return shared; }
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
    auto& header = shared->header;
    const bool unknown = uncertain != 0 || InterlockedCompareExchange(&header.writeAttempts, 0, 0) != 0
        || InterlockedCompareExchange(&header.overflow, 0, 0) != 0
        || InterlockedCompareExchange(&header.state, 0, 0) == RizEarlyUnknown;
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
    if (header.magic != RizEarlyMagic || header.version != 1 || header.byteSize != sizeof(*shared)
        || header.eventCapacity != RizEarlyEventCapacity || header.config.version != 1
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
    // Mapping and handle remain retained until process exit. In particular,
    // no process-detach callback erases evidence after a loader-stage failure.
    return TRUE;
}
