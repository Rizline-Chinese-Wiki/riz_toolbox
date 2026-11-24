#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <cstdarg>
#include "../RizToolbox.EngineBootstrap/EngineTransportPublication.h"

// One ordinary import forces the real five-MEM_IMAGE owned loader chain before
// wmain. UnityMain2 is the generated owned zero/ret stub, never a game body.
extern "C" __declspec(dllimport) int __cdecl UnityMain2();

namespace {
namespace L = RizEngineTransportPublicationLayout;
constexpr DWORD LastErrorMarker = 0x6244a973;
constexpr std::uint32_t NotCalled = 0xffffffffu;
constexpr std::uint32_t SampleCount = 5;
using Value = std::uint32_t(__cdecl*)();
using Read = std::uint32_t(__cdecl*)(RizEngineTransportPublicationV5*);
struct Sample {
    std::uint32_t index{}, beforeResult{NotCalled}, verifyResult{NotCalled}, afterResult{NotCalled};
    DWORD beforeError{}, verifyError{}, afterError{};
    std::uint64_t beforeTicks{}, verifyTicks{}, afterTicks{};
    bool called{}, clockOkay{}, beforeReady{}, afterReady{}, immutable{}, lastErrorPreserved{};
};
struct Json {
    char bytes[16000]{}; std::size_t used{}; bool okay{true};
    void Add(const char* format, ...) {
        if (!okay) return;
        va_list arguments; va_start(arguments, format);
        const auto count = std::vsnprintf(bytes + used, sizeof(bytes) - used, format, arguments);
        va_end(arguments);
        if (count < 0 || static_cast<std::size_t>(count) >= sizeof(bytes) - used) { okay = false; return; }
        used += static_cast<std::size_t>(count);
    }
};
RizEngineTransportPublicationV5 baseline{}, before{}, after{}, latest{};
Sample samples[SampleCount]{};
const char* Bool(bool value) { return value ? "true" : "false"; }
template<class Function> Function Find(HMODULE module, const char* name) {
    const auto address = GetProcAddress(module, name);
    Function result{}; static_assert(sizeof(result) == sizeof(address));
    std::memcpy(&result, &address, sizeof(result)); return result;
}
bool Absolute(const wchar_t* value) {
    if (!value) return false;
    const auto length = std::wcslen(value);
    if (length < 3 || length >= 32000) return false;
    return ((value[0] >= L'A' && value[0] <= L'Z') || (value[0] >= L'a' && value[0] <= L'z'))
        ? value[1] == L':' && (value[2] == L'\\' || value[2] == L'/')
        : length >= 5 && value[0] == L'\\' && value[1] == L'\\' && value[2] != L'?' && value[2] != L'.';
}
bool WriteNew(const wchar_t* path, const void* bytes, std::size_t count, DWORD& error) {
    error = ERROR_SUCCESS;
    const auto file = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) { error = GetLastError(); return false; }
    DWORD written{}; SetLastError(ERROR_SUCCESS);
    const bool wrote = count <= MAXDWORD && WriteFile(file, bytes, static_cast<DWORD>(count), &written, nullptr)
        && written == static_cast<DWORD>(count);
    if (!wrote) { error = GetLastError(); if (!error) error = ERROR_WRITE_FAULT; }
    const bool flushed = wrote && FlushFileBuffers(file);
    if (wrote && !flushed) error = GetLastError();
    const bool closed = CloseHandle(file) != FALSE;
    if (!closed && !error) error = GetLastError();
    return wrote && flushed && closed;
}
bool Shape(const RizEngineTransportPublicationV5& value) {
    return value.version == 5 && value.byteSize == sizeof(value) && value.schemaMagic == L::Magic
        && value.state == RizEngineTransportReadyV5 && !value.failure && value.profile == RizEngineTransportOwnedProfileV5
        && value.mainThreadProof == RizEngineTransportMainThreadOwnedContractV5
        && value.processId == GetCurrentProcessId() && value.ownerThread == GetCurrentThreadId()
        && value.producerThread == GetCurrentThreadId() && value.mainThread == GetCurrentThreadId()
        && value.claimed == 1 && value.startupStatic == 1 && value.attachReason == DLL_PROCESS_ATTACH
        && value.epoch && value.snapshotSequence && !value.verifyFailures && !value.lastWin32Error
        && value.moduleCount == 5 && value.typedCount == 13 && value.siteCount == 23
        && value.windowCount == 36 && value.familyCount == 12 && value.runtimeFunctionCount == 67
        && value.configuredFamilies == L::AllFamilies && value.boundFamilies == L::AllFamilies
        && value.retainedFamilies == L::AllFamilies && value.preparedWindows == L::AllWindows
        && value.writeAttemptedWindows == L::AllWindows && value.committedWindows == L::AllWindows
        && value.sourceWriteAttempts == 36 && value.sourceWriteCompletions == 36
        && !value.protectionOutstanding && !value.outstandingProtectionWindows
        && value.resourceCount == 1 && value.retainedResources == 1
        && value.resources[0].flags == 15 && value.resources[0].tableCount == 23
        && value.resources[0].rfFirst == 40 && value.resources[0].rfCount == 27
        && value.resources[0].rootRetained == 1 && value.resources[0].tableRegistered == 1
        && value.resources[0].allocationOwned == 1 && !value.resources[0].protectionOutstanding
        && value.eventCapacity == L::EventCapacity && value.eventCount && value.eventCount <= L::EventCapacity
        && !value.eventOverflow && value.events[value.eventCount - 1].stage == RizEngineTransportPublishedV5
        && value.events[value.eventCount - 1].state == RizEngineTransportReadyV5
        && value.failureWindowIndex == L::NoIndex && value.failureFamilyIndex == L::NoIndex
        && value.failureResourceIndex == L::NoIndex;
}
bool Modules(const RizEngineTransportPublicationV5& value) {
    const HMODULE modules[5] = {GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll"),
        GetModuleHandleW(L"RizToolbox.EngineNodeKernel.dll"),
        GetModuleHandleW(L"RizToolbox.TransportOwnedTarget.dll"), GetModuleHandleW(nullptr), GetModuleHandleW(L"winhttp.dll")};
    const std::uint64_t fields[5] = {value.bootstrapModule, value.kernelModule, value.targetModule,
        value.executableModule, value.doorstopModule};
    for (std::uint32_t i = 0; i < 5; ++i) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!modules[i] || fields[i] != reinterpret_cast<std::uint64_t>(modules[i])
            || value.modules[i].module != fields[i] || value.modules[i].kind != i
            || value.modules[i].flags != (i == 0 ? 7u : 15u)
            || !VirtualQuery(modules[i], &memory, sizeof(memory)) || memory.State != MEM_COMMIT
            || memory.Type != MEM_IMAGE || memory.AllocationBase != modules[i]) return false;
        for (std::uint32_t j = 0; j < i; ++j) if (modules[i] == modules[j]) return false;
    }
    return !GetModuleHandleW(L"UnityPlayer.dll") && !GetModuleHandleW(L"GameAssembly.dll")
        && !GetModuleHandleW(L"coreclr.dll") && !GetModuleHandleW(L"hostfxr.dll");
}
bool Tick(LARGE_INTEGER& value) { return QueryPerformanceCounter(&value) != FALSE; }
std::uint64_t Delta(const LARGE_INTEGER& first, const LARGE_INTEGER& last) {
    return last.QuadPart >= first.QuadPart ? static_cast<std::uint64_t>(last.QuadPart - first.QuadPart) : 0;
}
bool TimedRead(Read read, RizEngineTransportPublicationV5& value, std::uint32_t& result,
    DWORD& error, std::uint64_t& ticks) {
    std::memset(&value, 0, sizeof(value)); value.version = 5; value.byteSize = sizeof(value);
    LARGE_INTEGER start{}, finish{};
    const bool began = Tick(start);
    SetLastError(LastErrorMarker); result = read(&value); error = GetLastError();
    const bool ended = Tick(finish); ticks = Delta(start, finish);
    return began && ended && finish.QuadPart >= start.QuadPart;
}
void Measure(Read read, Value verify, Sample& sample) {
    sample.called = true;
    const bool beforeClock = TimedRead(read, before, sample.beforeResult, sample.beforeError, sample.beforeTicks);
    if (!sample.beforeResult) latest = before;
    LARGE_INTEGER start{}, finish{};
    const bool began = Tick(start);
    SetLastError(LastErrorMarker); sample.verifyResult = verify(); sample.verifyError = GetLastError();
    const bool ended = Tick(finish); sample.verifyTicks = Delta(start, finish);
    const bool afterClock = TimedRead(read, after, sample.afterResult, sample.afterError, sample.afterTicks);
    if (!sample.afterResult) latest = after;
    sample.clockOkay = beforeClock && began && ended && afterClock && finish.QuadPart >= start.QuadPart;
    sample.beforeReady = !sample.beforeResult && Shape(before);
    sample.afterReady = !sample.afterResult && Shape(after);
    sample.immutable = !sample.beforeResult && !sample.afterResult
        && std::memcmp(&before, &baseline, sizeof(before)) == 0
        && std::memcmp(&after, &baseline, sizeof(after)) == 0;
    sample.lastErrorPreserved = sample.beforeError == LastErrorMarker && sample.verifyError == LastErrorMarker
        && sample.afterError == LastErrorMarker;
}
double Microseconds(std::uint64_t ticks, const LARGE_INTEGER& frequency) {
    return frequency.QuadPart > 0 ? static_cast<double>(ticks) * 1000000.0 / static_cast<double>(frequency.QuadPart) : 0.0;
}
void SampleJson(Json& json, const Sample& value, const LARGE_INTEGER& frequency) {
    json.Add("{\"index\":%u,\"called\":%s,\"beforeResult\":%u,\"verifyResult\":%u,\"afterResult\":%u,"
        "\"beforeLastError\":%lu,\"verifyLastError\":%lu,\"afterLastError\":%lu,\"clockOkay\":%s,"
        "\"beforeReady\":%s,\"afterReady\":%s,\"immutable\":%s,\"lastErrorPreserved\":%s,"
        "\"beforeReadTicks\":%llu,\"verifyTicks\":%llu,\"afterReadTicks\":%llu,"
        "\"beforeReadMicroseconds\":%.3f,\"verifyMicroseconds\":%.3f,\"afterReadMicroseconds\":%.3f}",
        value.index, Bool(value.called), value.beforeResult, value.verifyResult, value.afterResult,
        value.beforeError, value.verifyError, value.afterError, Bool(value.clockOkay), Bool(value.beforeReady), Bool(value.afterReady),
        Bool(value.immutable), Bool(value.lastErrorPreserved), static_cast<unsigned long long>(value.beforeTicks),
        static_cast<unsigned long long>(value.verifyTicks), static_cast<unsigned long long>(value.afterTicks),
        Microseconds(value.beforeTicks, frequency), Microseconds(value.verifyTicks, frequency), Microseconds(value.afterTicks, frequency));
}
}

// argv1: fresh absolute benchmark JSON; argv2: fresh absolute baseline raw.
// Final raw is argv2 + ".final". All Verify calls retain the complete producer
// hash/graph/body/unwind/family checks. No CLR or game body is loaded/executed.
int wmain(int argc, wchar_t** argv) {
    if (argc != 3 || !Absolute(argv[1]) || !Absolute(argv[2]) || !_wcsicmp(argv[1], argv[2])) return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    const auto started = GetTickCount64();
    DWORD rawError{}, finalRawError{}, outputError{}, versionError{}, initialError{};
    std::uint32_t failure{}, completed{}, versionResult{NotCalled}, initialResult{NotCalled};
    const char* failureStage = "none";
    int unityResult{-1};
    bool rawPersisted{}, finalRawPersisted{}, initialReady{}, lastErrors{}, immutable{}, allVerified{};
    LARGE_INTEGER frequency{};
    const bool clockReady = QueryPerformanceFrequency(&frequency) && frequency.QuadPart > 0;
    const auto bootstrap = GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll");
    Value version{}, verify{}; Read read{};
    if (bootstrap) {
        version = Find<Value>(bootstrap, "RizEngineTransport_GetVersion");
        read = Find<Read>(bootstrap, "RizEngineTransport_GetInfoV5");
        verify = Find<Value>(bootstrap, "RizEngineTransport_VerifyV5");
    }
    if (!clockReady) { failure = 3; failureStage = "performance-clock"; }
    else if (!version || !read || !verify) { failure = 4; failureStage = "native-exports"; }
    if (!failure) {
        SetLastError(LastErrorMarker); versionResult = version(); versionError = GetLastError();
        baseline.version = 5; baseline.byteSize = sizeof(baseline);
        SetLastError(LastErrorMarker); initialResult = read(&baseline); initialError = GetLastError();
        if (!initialResult) latest = baseline;
        initialReady = versionResult == 5 && !initialResult && Shape(baseline) && Modules(baseline);
        if (!initialReady || versionError != LastErrorMarker || initialError != LastErrorMarker) {
            failure = 5; failureStage = "native-cold-ready";
        }
    }
    if (!failure) {
        // This call exists only to root the ordinary UnityMain2 import and its
        // generated zero/ret stub. It is outside every measured Verify interval.
        unityResult = UnityMain2();
        if (unityResult) { failure = 6; failureStage = "owned-unity-stub"; }
    }
    if (!initialResult) rawPersisted = WriteNew(argv[2], &baseline, sizeof(baseline), rawError);
    if (!rawPersisted && !failure) { failure = 7; failureStage = "baseline-persistence"; }
    if (!failure) {
        std::printf("{\"kind\":\"engine-transport-verify-benchmark-start\",\"pid\":%lu,\"samplesRequested\":5,\"coldReady\":true}\n", GetCurrentProcessId());
        std::fflush(stdout);
        lastErrors = immutable = allVerified = true;
        for (std::uint32_t i = 0; i < SampleCount; ++i) {
            samples[i].index = i;
            Measure(read, verify, samples[i]); ++completed;
            const auto& sample = samples[i];
            lastErrors = lastErrors && sample.lastErrorPreserved;
            immutable = immutable && sample.immutable;
            allVerified = allVerified && !sample.verifyResult && sample.clockOkay && sample.beforeReady && sample.afterReady;
            // Flush progress after each sample, so a bounded external timeout
            // preserves completed actual measurements even without final JSON.
            Json progress{};
            progress.Add("{\"kind\":\"engine-transport-verify-benchmark-progress\",\"pid\":%lu,\"sample\":", GetCurrentProcessId());
            SampleJson(progress, sample, frequency); progress.Add("}\n");
            if (progress.okay) { std::fwrite(progress.bytes, 1, progress.used, stdout); std::fflush(stdout); }
        }
        if (!lastErrors || !immutable || !allVerified) { failure = 8; failureStage = "measured-verify"; }
    }
    wchar_t finalPath[32768]{};
    if (std::wcslen(argv[2]) + 6 < 32768) {
        const auto copied = wcscpy_s(finalPath, argv[2]); const auto appended = wcscat_s(finalPath, L".final");
        if (!copied && !appended && !initialResult) finalRawPersisted = WriteNew(finalPath, &latest, sizeof(latest), finalRawError);
    } else finalRawError = ERROR_FILENAME_EXCED_RANGE;
    if (!finalRawPersisted && !failure) { failure = 9; failureStage = "final-persistence"; }
    std::uint64_t totalVerifyTicks{}, minimumVerifyTicks{~std::uint64_t(0)}, maximumVerifyTicks{};
    for (std::uint32_t i = 0; i < completed; ++i) {
        const auto ticks = samples[i].verifyTicks; totalVerifyTicks += ticks;
        if (ticks < minimumVerifyTicks) minimumVerifyTicks = ticks;
        if (ticks > maximumVerifyTicks) maximumVerifyTicks = ticks;
    }
    if (!completed) minimumVerifyTicks = 0;
    const bool passed = !failure && completed == SampleCount;
    Json json{};
    json.Add("{\"schemaVersion\":1,\"kind\":\"engine-transport-verify-benchmark\",\"pid\":%lu,\"thread\":%lu,\"passed\":%s,"
        "\"failureCode\":%u,\"failureStage\":\"%s\",\"initialReady\":%s,\"versionResult\":%u,\"initialReadResult\":%u,"
        "\"versionLastError\":%lu,\"initialReadLastError\":%lu,\"ownedUnityResult\":%d,\"samplesRequested\":5,\"samplesCompleted\":%u,"
        "\"performanceCounterFrequency\":%llu,\"verifyTotalTicks\":%llu,\"verifyTotalMicroseconds\":%.3f,"
        "\"verifyMeanMicroseconds\":%.3f,\"verifyMinimumMicroseconds\":%.3f,\"verifyMaximumMicroseconds\":%.3f,"
        "\"elapsedMilliseconds\":%llu,\"lastErrorPreserved\":%s,\"immutable\":%s,\"allVerified\":%s,"
        "\"rawPersisted\":%s,\"finalRawPersisted\":%s,\"rawError\":%lu,\"finalRawError\":%lu,\"samples\":[",
        GetCurrentProcessId(), GetCurrentThreadId(), Bool(passed), failure, failureStage, Bool(initialReady), versionResult,
        initialResult, versionError, initialError, unityResult, completed, static_cast<unsigned long long>(frequency.QuadPart),
        static_cast<unsigned long long>(totalVerifyTicks), Microseconds(totalVerifyTicks, frequency),
        completed ? Microseconds(totalVerifyTicks, frequency) / static_cast<double>(completed) : 0.0,
        Microseconds(minimumVerifyTicks, frequency), Microseconds(maximumVerifyTicks, frequency),
        static_cast<unsigned long long>(GetTickCount64() - started), Bool(lastErrors), Bool(immutable), Bool(allVerified),
        Bool(rawPersisted), Bool(finalRawPersisted), rawError, finalRawError);
    for (std::uint32_t i = 0; i < completed; ++i) { if (i) json.Add(","); SampleJson(json, samples[i], frequency); }
    json.Add("],\"latestNativeResponsibility\":{\"state\":%u,\"failure\":%u,\"runtimeFunctionCount\":%u,"
        "\"configuredFamilies\":%llu,\"boundFamilies\":%llu,\"retainedFamilies\":%llu,\"committedWindows\":%llu,"
        "\"sourceWriteAttempts\":%u,\"sourceWriteCompletions\":%u,\"protectionOutstanding\":%u,\"outstandingProtectionWindows\":%llu,"
        "\"resourceCount\":%u,\"retainedResources\":%llu,\"tableRegistered\":%u,\"allocationOwned\":%u,\"rootRetained\":%u},"
        "\"actualColdProcess\":true,\"verifyChecksUnchanged\":true,\"clrHosted\":false,\"gameExecuted\":false,"
        "\"originalGameCodeExecuted\":false,\"productionAdmissionVerified\":false,\"nativeAbortCalled\":false,\"fixtureFreedClaims\":false}\n",
        latest.state, latest.failure, latest.runtimeFunctionCount, static_cast<unsigned long long>(latest.configuredFamilies),
        static_cast<unsigned long long>(latest.boundFamilies), static_cast<unsigned long long>(latest.retainedFamilies),
        static_cast<unsigned long long>(latest.committedWindows), latest.sourceWriteAttempts, latest.sourceWriteCompletions,
        latest.protectionOutstanding, static_cast<unsigned long long>(latest.outstandingProtectionWindows), latest.resourceCount,
        static_cast<unsigned long long>(latest.retainedResources), latest.resources[0].tableRegistered,
        latest.resources[0].allocationOwned, latest.resources[0].rootRetained);
    if (!json.okay) return 10;
    const bool outputPersisted = WriteNew(argv[1], json.bytes, json.used, outputError);
    std::fwrite(json.bytes, 1, json.used, stdout); std::fflush(stdout);
    if (!outputPersisted) { std::fprintf(stderr, "benchmark JSON persistence failed: %lu\n", outputError); return 11; }
    return passed ? 0 : static_cast<int>(failure);
}
