#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <coreclr_delegates.h>
#include <hostfxr.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <cstdarg>
#include "../RizToolbox.EngineBootstrap/EngineTransportPublication.h"

// The ordinary EXE import roots Target -> winhttp -> Bootstrap -> Kernel before
// wmain. The only target function called here is the generated owned zero/ret
// UnityMain2 stub. CLR hosting begins in wmain, never in a native initializer.
extern "C" __declspec(dllimport) int __cdecl UnityMain2();

namespace {
namespace L = RizEngineTransportPublicationLayout;
constexpr DWORD LastErrorMarker = 0x6244a973;
constexpr std::uint32_t NotCalled = 0xffffffffu;
constexpr std::size_t ArgumentsCapacity = 1024 * 1024;
using Value = std::uint32_t(__cdecl*)();
using Read = std::uint32_t(__cdecl*)(RizEngineTransportPublicationV5*);
using ManagedEntry = int(__cdecl*)(void*, std::int32_t);

struct NativeApi { Value version{}, verify{}; Read read{}; };
struct Observation {
    bool attempted{}, shape{}, immutable{}, lastErrorPreserved{}, clrLoaded{};
    std::uint32_t version{NotCalled}, read{NotCalled}, verify{NotCalled}, after{NotCalled};
    DWORD versionError{}, readError{}, verifyError{}, afterError{};
};
struct Hosting {
    bool attempted{}, libraryLoaded{}, contextCreated{}, delegateObtained{}, contextClosed{},
        entryResolved{}, entryCalled{}, clrBefore{}, clrAfter{}, inputsLocked{};
    std::int32_t initialized{-1}, delegated{-1}, closed{-1}, loaded{-1};
    int managedResult{-1};
    DWORD libraryError{}, initializeError{}, delegateError{}, closeError{}, loadError{},
        managedError{}, inputError{}, exceptionCode{};
    std::uint32_t argumentBytes{};
};
struct Json {
    char bytes[18000]{};
    std::size_t used{};
    bool okay{true};
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
bool baselineCaptured{}, latestCaptured{};
Observation observations[3]{};
Hosting hosting{};
// The CLR, hostfxr module and input file locks deliberately live until process
// exit. No successful Configure/Attach claim or registered arena is released.
HMODULE retainedHostFxr{};
HANDLE inputLocks[4]{INVALID_HANDLE_VALUE, INVALID_HANDLE_VALUE, INVALID_HANDLE_VALUE, INVALID_HANDLE_VALUE};
alignas(8) char arguments[ArgumentsCapacity]{};

const char* Bool(bool value) { return value ? "true" : "false"; }
template<class Function> Function Find(HMODULE module, const char* name) {
    const auto address = GetProcAddress(module, name);
    Function function{}; static_assert(sizeof(function) == sizeof(address));
    std::memcpy(&function, &address, sizeof(function)); return function;
}
std::uint64_t Address(const void* value) { return reinterpret_cast<std::uint64_t>(value); }
bool Absolute(const wchar_t* value) {
    if (!value) return false;
    const auto length = std::wcslen(value);
    if (length < 3 || length >= 32000) return false;
    // Fully qualified local/UNC paths only; no process-directory dependency.
    return ((value[0] >= L'A' && value[0] <= L'Z') || (value[0] >= L'a' && value[0] <= L'z'))
        ? value[1] == L':' && (value[2] == L'\\' || value[2] == L'/')
        : length >= 5 && value[0] == L'\\' && value[1] == L'\\' && value[2] != L'?' && value[2] != L'.';
}
bool SamePath(const wchar_t* left, const wchar_t* right) {
    wchar_t first[32768]{}, second[32768]{};
    const auto a = GetFullPathNameW(left, 32768, first, nullptr);
    const auto b = GetFullPathNameW(right, 32768, second, nullptr);
    if (!a || !b || a >= 32768 || b >= 32768) return true;
    for (auto* p = first; *p; ++p) if (*p == L'/') *p = L'\\';
    for (auto* p = second; *p; ++p) if (*p == L'/') *p = L'\\';
    return _wcsicmp(first, second) == 0;
}
bool WriteNew(const wchar_t* path, const void* value, std::size_t bytes, DWORD& error) {
    error = ERROR_SUCCESS;
    const auto file = CreateFileW(path, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) { error = GetLastError(); return false; }
    DWORD written{}; SetLastError(ERROR_SUCCESS);
    const bool wrote = bytes <= MAXDWORD && WriteFile(file, value, static_cast<DWORD>(bytes), &written, nullptr)
        && written == static_cast<DWORD>(bytes);
    if (!wrote) { error = GetLastError(); if (!error) error = ERROR_WRITE_FAULT; }
    const bool flushed = wrote && FlushFileBuffers(file);
    if (wrote && !flushed) error = GetLastError();
    const bool closed = CloseHandle(file) != FALSE;
    if (!closed && !error) error = GetLastError();
    return wrote && flushed && closed;
}
bool ActualModules(const RizEngineTransportPublicationV5& value) {
    const HMODULE modules[5] = {GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll"),
        GetModuleHandleW(L"RizToolbox.EngineNodeKernel.dll"),
        GetModuleHandleW(L"RizToolbox.TransportOwnedTarget.dll"), GetModuleHandleW(nullptr),
        GetModuleHandleW(L"winhttp.dll")};
    const std::uint64_t fields[5] = {value.bootstrapModule, value.kernelModule, value.targetModule,
        value.executableModule, value.doorstopModule};
    for (std::uint32_t i = 0; i < 5; ++i) {
        MEMORY_BASIC_INFORMATION memory{};
        if (!modules[i] || fields[i] != Address(modules[i]) || value.modules[i].kind != i
            || value.modules[i].module != fields[i] || value.modules[i].flags != (i == 0 ? 7u : 15u)
            || !VirtualQuery(modules[i], &memory, sizeof(memory)) || memory.State != MEM_COMMIT
            || memory.Type != MEM_IMAGE || memory.AllocationBase != modules[i]) return false;
        for (std::uint32_t j = 0; j < i; ++j) if (modules[i] == modules[j]) return false;
    }
    return !GetModuleHandleW(L"UnityPlayer.dll") && !GetModuleHandleW(L"GameAssembly.dll");
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
        && value.failureResourceIndex == L::NoIndex && ActualModules(value);
}
bool Checkpoint(const NativeApi& api, Observation& result) {
    result.attempted = true;
    result.clrLoaded = GetModuleHandleW(L"coreclr.dll") != nullptr;
    std::memset(&before, 0, sizeof(before)); std::memset(&after, 0, sizeof(after));
    before.version = after.version = 5; before.byteSize = after.byteSize = sizeof(before);
    SetLastError(LastErrorMarker); result.version = api.version(); result.versionError = GetLastError();
    SetLastError(LastErrorMarker); result.read = api.read(&before); result.readError = GetLastError();
    if (!result.read) {
        if (!baselineCaptured) { baseline = before; baselineCaptured = true; }
        latest = before; latestCaptured = true;
    }
    SetLastError(LastErrorMarker); result.verify = api.verify(); result.verifyError = GetLastError();
    SetLastError(LastErrorMarker); result.after = api.read(&after); result.afterError = GetLastError();
    if (!result.after) { latest = after; latestCaptured = true; }
    result.lastErrorPreserved = result.versionError == LastErrorMarker && result.readError == LastErrorMarker
        && result.verifyError == LastErrorMarker && result.afterError == LastErrorMarker;
    result.shape = result.version == 5 && !result.read && !result.verify && !result.after
        && Shape(before) && Shape(after);
    result.immutable = baselineCaptured && !result.read && !result.after
        && std::memcmp(&before, &baseline, sizeof(before)) == 0
        && std::memcmp(&after, &baseline, sizeof(after)) == 0;
    return result.shape && result.immutable && result.lastErrorPreserved;
}
bool LockInputs(wchar_t** argv) {
    for (std::uint32_t i = 0; i < 4; ++i) {
        SetLastError(ERROR_SUCCESS);
        inputLocks[i] = CreateFileW(argv[3 + i], GENERIC_READ, FILE_SHARE_READ, nullptr,
            OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (inputLocks[i] == INVALID_HANDLE_VALUE) { hosting.inputError = GetLastError(); return false; }
        BY_HANDLE_FILE_INFORMATION info{};
        if (!GetFileInformationByHandle(inputLocks[i], &info) || (info.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) {
            hosting.inputError = GetLastError(); if (!hosting.inputError) hosting.inputError = ERROR_DIRECTORY; return false;
        }
    }
    LARGE_INTEGER length{};
    if (!GetFileSizeEx(inputLocks[3], &length)) { hosting.inputError = GetLastError(); return false; }
    if (length.QuadPart <= 0 || length.QuadPart > static_cast<LONGLONG>(ArgumentsCapacity)) {
        hosting.inputError = ERROR_BAD_LENGTH; return false;
    }
    hosting.argumentBytes = static_cast<std::uint32_t>(length.QuadPart);
    DWORD received{}; SetLastError(ERROR_SUCCESS);
    if (!ReadFile(inputLocks[3], arguments, hosting.argumentBytes, &received, nullptr)
        || received != hosting.argumentBytes) {
        hosting.inputError = GetLastError(); if (!hosting.inputError) hosting.inputError = ERROR_READ_FAULT; return false;
    }
    hosting.inputsLocked = true; return true;
}
std::uint32_t PrepareManaged(wchar_t** argv, ManagedEntry& entry) {
    hosting.attempted = true;
    hosting.clrBefore = GetModuleHandleW(L"coreclr.dll") != nullptr;
    if (hosting.clrBefore || GetModuleHandleW(L"hostfxr.dll")) return 101;
    if (!LockInputs(argv)) return 102;
    SetLastError(ERROR_SUCCESS);
    retainedHostFxr = LoadLibraryExW(argv[5], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    hosting.libraryError = GetLastError(); hosting.libraryLoaded = retainedHostFxr != nullptr;
    if (!retainedHostFxr) return 103;
    const auto initialize = Find<hostfxr_initialize_for_runtime_config_fn>(retainedHostFxr, "hostfxr_initialize_for_runtime_config");
    const auto getDelegate = Find<hostfxr_get_runtime_delegate_fn>(retainedHostFxr, "hostfxr_get_runtime_delegate");
    const auto close = Find<hostfxr_close_fn>(retainedHostFxr, "hostfxr_close");
    if (!initialize || !getDelegate || !close) return 104;
    hostfxr_handle context{};
    SetLastError(ERROR_SUCCESS); hosting.initialized = initialize(argv[3], nullptr, &context);
    hosting.initializeError = GetLastError(); hosting.contextCreated = context != nullptr;
    if (hosting.initialized < 0 || !context) {
        if (context) { SetLastError(ERROR_SUCCESS); hosting.closed = close(context); hosting.closeError = GetLastError();
            hosting.contextClosed = hosting.closed >= 0; }
        return 105;
    }
    void* loadAddress{};
    SetLastError(ERROR_SUCCESS); hosting.delegated = getDelegate(context, hdt_load_assembly_and_get_function_pointer, &loadAddress);
    hosting.delegateError = GetLastError(); hosting.delegateObtained = hosting.delegated >= 0 && loadAddress;
    SetLastError(ERROR_SUCCESS); hosting.closed = close(context); hosting.closeError = GetLastError();
    hosting.contextClosed = hosting.closed >= 0;
    if (!hosting.delegateObtained || !hosting.contextClosed) return 106;
    load_assembly_and_get_function_pointer_fn load{};
    static_assert(sizeof(load) == sizeof(loadAddress)); std::memcpy(&load, &loadAddress, sizeof(load));
    void* entryAddress{};
    SetLastError(ERROR_SUCCESS);
    hosting.loaded = load(argv[4], L"TransportOwnedAdoptionFixture, RizToolbox.Runtime.Tests", L"Run",
        UNMANAGEDCALLERSONLY_METHOD, nullptr, &entryAddress);
    hosting.loadError = GetLastError(); hosting.entryResolved = hosting.loaded >= 0 && entryAddress;
    hosting.clrAfter = GetModuleHandleW(L"coreclr.dll") != nullptr;
    if (!hosting.entryResolved || !hosting.clrAfter) return 107;
    static_assert(sizeof(entry) == sizeof(entryAddress)); std::memcpy(&entry, &entryAddress, sizeof(entry));
    return 0;
}
bool CallManaged(ManagedEntry entry) {
    hosting.entryCalled = true;
    SetLastError(ERROR_SUCCESS);
    __try { hosting.managedResult = entry(arguments, static_cast<std::int32_t>(hosting.argumentBytes)); }
    __except (EXCEPTION_EXECUTE_HANDLER) { hosting.exceptionCode = GetExceptionCode(); }
    hosting.managedError = GetLastError();
    return !hosting.exceptionCode && hosting.managedResult == 0;
}
void ObservationJson(Json& json, const char* name, const Observation& value) {
    json.Add("{\"name\":\"%s\",\"attempted\":%s,\"version\":%u,\"readResult\":%u,\"verifyResult\":%u,\"afterResult\":%u,"
        "\"shape\":%s,\"immutable\":%s,\"lastErrorPreserved\":%s,\"clrLoaded\":%s,"
        "\"versionLastError\":%lu,\"readLastError\":%lu,\"verifyLastError\":%lu,\"afterLastError\":%lu}",
        name, Bool(value.attempted), value.version, value.read, value.verify, value.after,
        Bool(value.shape), Bool(value.immutable), Bool(value.lastErrorPreserved), Bool(value.clrLoaded),
        value.versionError, value.readError, value.verifyError, value.afterError);
}
}

// argv: native-result.json publication.raw runtimeconfig.json assembly.dll
//       hostfxr.dll managed-arguments.json. All six are fully qualified paths.
// managed-arguments.json is passed unchanged as bounded UTF-8 bytes. Its schema
// and independently frozen five-PE facts are validated by the managed entry.
int wmain(int argc, wchar_t** argv) {
    if (argc != 7) return 2;
    for (int i = 1; i < argc; ++i) if (!Absolute(argv[i])) return 2;
    for (int i = 1; i < argc; ++i) for (int j = 1; j < i; ++j) if (SamePath(argv[i], argv[j])) return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX);
    const auto started = GetTickCount64();
    std::uint32_t failure{};
    const char* failureStage = "none";
    DWORD rawError{}, finalRawError{}, evidenceError{};
    bool rawPersisted{}, finalRawPersisted{}, unityReadPreserved{}, unityUnchanged{};
    int unityResult{-1};
    std::uint32_t unityReadResult{NotCalled}; DWORD unityReadError{};
    const auto bootstrap = GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll");
    NativeApi api{};
    if (!bootstrap || GetModuleHandleW(L"UnityPlayer.dll") || GetModuleHandleW(L"GameAssembly.dll")) {
        failure = 3; failureStage = "native-chain";
    } else {
        api.version = Find<Value>(bootstrap, "RizEngineTransport_GetVersion");
        api.read = Find<Read>(bootstrap, "RizEngineTransport_GetInfoV5");
        api.verify = Find<Value>(bootstrap, "RizEngineTransport_VerifyV5");
        if (!api.version || !api.read || !api.verify) { failure = 4; failureStage = "native-exports"; }
    }
    if (!failure && (!Checkpoint(api, observations[0]) || observations[0].clrLoaded
        || GetModuleHandleW(L"hostfxr.dll"))) { failure = 5; failureStage = "before-clr"; }
    if (!failure) {
        unityResult = UnityMain2(); // Owned stub only, never an original game body.
        std::memset(&after, 0, sizeof(after)); after.version = 5; after.byteSize = sizeof(after);
        SetLastError(LastErrorMarker); unityReadResult = api.read(&after); unityReadError = GetLastError();
        unityReadPreserved = unityReadError == LastErrorMarker;
        if (!unityReadResult) { latest = after; latestCaptured = true; }
        unityUnchanged = !unityReadResult && baselineCaptured && std::memcmp(&after, &baseline, sizeof(after)) == 0;
        if (unityResult != 0 || !unityReadPreserved || !unityUnchanged) { failure = 6; failureStage = "owned-unity-stub"; }
    }
    if (baselineCaptured) rawPersisted = WriteNew(argv[2], &baseline, sizeof(baseline), rawError);
    if (!rawPersisted && !failure) { failure = 7; failureStage = "initial-raw-persistence"; }
    ManagedEntry entry{};
    if (!failure) {
        failure = PrepareManaged(argv, entry);
        hosting.clrAfter = GetModuleHandleW(L"coreclr.dll") != nullptr;
        if (failure) failureStage = "clr-prepare";
    }
    // Even a failed hostfxr operation is followed by actual native verification;
    // it cannot hide native Unknown, changed bytes or retained responsibilities.
    if (hosting.attempted) {
        const bool intact = Checkpoint(api, observations[1]);
        if ((!intact || (hosting.entryResolved && !observations[1].clrLoaded)) && !failure) {
            failure = 108; failureStage = "after-clr-before-entry";
        }
    }
    if (!failure) {
        const bool managedOkay = CallManaged(entry);
        const bool intact = Checkpoint(api, observations[2]);
        if (!managedOkay) { failure = hosting.exceptionCode ? 109u : 110u; failureStage = "managed-entry"; }
        else if (!intact || !observations[2].clrLoaded) { failure = 111; failureStage = "after-managed-entry"; }
    }
    wchar_t finalPath[32768]{};
    if (std::wcslen(argv[2]) + 6 < 32768) {
        const auto copied = wcscpy_s(finalPath, argv[2]);
        const auto appended = wcscat_s(finalPath, L".final");
        if (!copied && !appended && latestCaptured) finalRawPersisted = WriteNew(finalPath, &latest, sizeof(latest), finalRawError);
    } else finalRawError = ERROR_FILENAME_EXCED_RANGE;
    if (!finalRawPersisted && !failure) { failure = 112; failureStage = "final-raw-persistence"; }
    const bool passed = !failure;
    Json json{};
    json.Add("{\"schemaVersion\":1,\"kind\":\"engine-transport-cold-owned-managed\",\"pid\":%lu,\"thread\":%lu,"
        "\"passed\":%s,\"failureCode\":%u,\"failureStage\":\"%s\",\"elapsedMilliseconds\":%llu,"
        "\"baselineCaptured\":%s,\"latestCaptured\":%s,\"rawPersisted\":%s,\"finalRawPersisted\":%s,"
        "\"rawError\":%lu,\"finalRawError\":%lu,\"ownedUnityResult\":%d,\"ownedUnityReadResult\":%u,"
        "\"ownedUnityReadLastError\":%lu,\"ownedUnityLastErrorPreserved\":%s,\"ownedUnityImmutable\":%s,\"checkpoints\":[",
        GetCurrentProcessId(), GetCurrentThreadId(), Bool(passed), failure, failureStage,
        static_cast<unsigned long long>(GetTickCount64() - started), Bool(baselineCaptured), Bool(latestCaptured),
        Bool(rawPersisted), Bool(finalRawPersisted), rawError, finalRawError, unityResult, unityReadResult,
        unityReadError, Bool(unityReadPreserved), Bool(unityUnchanged));
    ObservationJson(json, "before-clr", observations[0]); json.Add(",");
    ObservationJson(json, "after-clr-before-entry", observations[1]); json.Add(",");
    ObservationJson(json, "after-managed-entry", observations[2]);
    json.Add("],\"hosting\":{\"attempted\":%s,\"inputsLocked\":%s,\"argumentBytes\":%u,\"inputError\":%lu,"
        "\"libraryLoaded\":%s,\"libraryError\":%lu,\"contextCreated\":%s,\"initializeResult\":%d,\"initializeLastError\":%lu,"
        "\"delegateObtained\":%s,\"delegateResult\":%d,\"delegateLastError\":%lu,\"contextClosed\":%s,\"closeResult\":%d,"
        "\"closeLastError\":%lu,\"entryResolved\":%s,\"loadAssemblyResult\":%d,\"loadAssemblyLastError\":%lu,"
        "\"entryCalled\":%s,\"managedResult\":%d,\"managedLastError\":%lu,\"exceptionCode\":%lu,\"clrBefore\":%s,\"clrAfter\":%s},",
        Bool(hosting.attempted), Bool(hosting.inputsLocked), hosting.argumentBytes, hosting.inputError,
        Bool(hosting.libraryLoaded), hosting.libraryError, Bool(hosting.contextCreated), hosting.initialized, hosting.initializeError,
        Bool(hosting.delegateObtained), hosting.delegated, hosting.delegateError, Bool(hosting.contextClosed), hosting.closed,
        hosting.closeError, Bool(hosting.entryResolved), hosting.loaded, hosting.loadError, Bool(hosting.entryCalled),
        hosting.managedResult, hosting.managedError, hosting.exceptionCode, Bool(hosting.clrBefore), Bool(hosting.clrAfter));
    json.Add("\"latestNativeResponsibility\":{\"state\":%u,\"failure\":%u,\"lastWin32Error\":%u,\"epoch\":%llu,\"snapshotSequence\":%llu,"
        "\"runtimeFunctionCount\":%u,\"configuredFamilies\":%llu,\"boundFamilies\":%llu,\"retainedFamilies\":%llu,"
        "\"preparedWindows\":%llu,\"writeAttemptedWindows\":%llu,\"committedWindows\":%llu,\"sourceWriteAttempts\":%u,"
        "\"sourceWriteCompletions\":%u,\"protectionOutstanding\":%u,\"outstandingProtectionWindows\":%llu,"
        "\"resourceCount\":%u,\"retainedResources\":%llu,\"tableRegistered\":%u,\"allocationOwned\":%u,\"rootRetained\":%u,"
        "\"eventCount\":%u,\"eventOverflow\":%u},\"actualColdProcess\":true,\"gameExecuted\":false,\"originalGameCodeExecuted\":false,"
        "\"productionAdmissionVerified\":false,\"nativeAbortCalled\":false,\"fixtureFreedClaims\":false,\"managedAdoptionReturnedSuccess\":%s}\n",
        latest.state, latest.failure, latest.lastWin32Error, static_cast<unsigned long long>(latest.epoch),
        static_cast<unsigned long long>(latest.snapshotSequence), latest.runtimeFunctionCount,
        static_cast<unsigned long long>(latest.configuredFamilies), static_cast<unsigned long long>(latest.boundFamilies),
        static_cast<unsigned long long>(latest.retainedFamilies), static_cast<unsigned long long>(latest.preparedWindows),
        static_cast<unsigned long long>(latest.writeAttemptedWindows), static_cast<unsigned long long>(latest.committedWindows),
        latest.sourceWriteAttempts, latest.sourceWriteCompletions, latest.protectionOutstanding,
        static_cast<unsigned long long>(latest.outstandingProtectionWindows), latest.resourceCount,
        static_cast<unsigned long long>(latest.retainedResources), latest.resources[0].tableRegistered,
        latest.resources[0].allocationOwned, latest.resources[0].rootRetained, latest.eventCount, latest.eventOverflow,
        Bool(hosting.entryCalled && !hosting.exceptionCode && hosting.managedResult == 0));
    if (!json.okay) return 113;
    const bool evidencePersisted = WriteNew(argv[1], json.bytes, json.used, evidenceError);
    std::fwrite(json.bytes, 1, json.used, stdout);
    if (!evidencePersisted) { std::fprintf(stderr, "native evidence persistence failed: %lu\n", evidenceError); return 114; }
    return passed ? 0 : static_cast<int>(failure);
}
