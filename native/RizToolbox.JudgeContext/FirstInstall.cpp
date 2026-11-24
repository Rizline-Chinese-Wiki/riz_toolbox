#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstring>
#include <cwchar>
#include <limits>
#include <initializer_list>
#include <algorithm>
#include <vector>
#include "FirstInstall.h"
#include "FirstInstallPlaybackAttestation.h"
#include "FirstInstallProfile.h"
#include "BootstrapVoidContext.h"
#include "RuntimeInvokeContext.h"
#include "ListenerContext.h"
#include "OperationContext.h"
#include "TweenSourcePublication.h"
#include "TweenSourceContext.h"
#include "TweenSourceReferences.h"
#include "TweenSingleEventContext.h"
#include "TweenRetirementContext.h"

#if defined(RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE)
// Compiled only into the independent fixture EXE, never the production helper.
// The seam has no exported symbol or runtime mode in the shipped binary.
extern "C" void __cdecl RizFirstInstallFixture_AfterUnknownAtomic();
extern "C" void __cdecl RizFirstInstallFixture_AfterSingleWrite(std::uint32_t span);
#endif

namespace {
namespace Profile = RizFirstInstallProfile;
constexpr std::uint32_t reservedVoidSlot = 31;
using PrepareFn = int(__cdecl*)(void*, void*, void**);
using CommitFn = int(__cdecl*)(void*);
SRWLOCK infoLock = SRWLOCK_INIT;
volatile LONG attemptClaimed = 0;
volatile LONG phase = RizFirstInstallNever;
// A zero value is the original known state. Latch the reason and detail in
// the same atomic transition so concurrent readers cannot observe a missing
// cause once the publication is unknown.
alignas(8) volatile LONG64 firstFailure = 0;
volatile LONG gameValidated = 0;
RizFirstInstallInfoV1 publishedInfo{ 1, sizeof(RizFirstInstallInfoV1), RizFirstInstallNever,
    0, reservedVoidSlot, 0, 0, 0, nullptr, nullptr, nullptr, nullptr, 0,
    nullptr, nullptr, nullptr, 0, nullptr, nullptr, 0, 0 };
RizFirstInstallInfoV2 publishedInfoV2{};
RizFirstInstallInfoV7 publishedInfoV7{};
// Successful and partially claimed native resources intentionally survive until
// process exit. There is no rollback/retry/unload after a preparation attempt.
HMODULE rootedGame = nullptr, rootedDobby = nullptr, rootedSelf = nullptr;
void* rootedRelay = nullptr;
void* rootedListenerBlock = nullptr;
constexpr std::size_t listenerAllocationSize = 4096;
constexpr std::size_t unityBodySize = Profile::UnityEndRva - Profile::UnityRva;
struct SealedPublication {
    unsigned char originalUnity[unityBodySize]{}, expectedUnity[unityBodySize]{};
    unsigned char expectedRuntime[32]{}, expectedBlock[listenerAllocationSize]{};
    DWORD midProtection = 0, unityProtection = 0, runtimeProtection = 0;
    bool baseline = false, tableRegistered = false, published = false;
    unsigned char expectedOperations[Profile::OperationCount][Profile::MaximumOperationBytes]{};
    DWORD operationContinuationProtection[Profile::OperationContinuationCount]{};
    unsigned char expectedContinuations[Profile::OperationContinuationCount][Profile::MaximumOperationBytes]{};
    bool operationBaseline = false, operationsPublished = false;
};
SealedPublication sealed;
bool OperationOriginalIdentity(const unsigned char* base);
namespace PlaybackAttestation { void Fault(); }

bool IsExecutableProtection(DWORD protection)
{
    if (protection & (PAGE_GUARD | PAGE_NOACCESS)) return false;
    const auto value = protection & 0xff;
    return value == PAGE_EXECUTE || value == PAGE_EXECUTE_READ
        || value == PAGE_EXECUTE_READWRITE || value == PAGE_EXECUTE_WRITECOPY;
}
bool ReadableRange(const void* address, std::size_t size, const void* allocationBase = nullptr, bool executable = false)
{
    auto current = reinterpret_cast<std::uintptr_t>(address);
    if (!current || !size || size > (std::numeric_limits<std::uintptr_t>::max)() - current) return false;
    const auto end = current + size;
    while (current < end) {
        MEMORY_BASIC_INFORMATION memory{};
        if (VirtualQuery(reinterpret_cast<const void*>(current), &memory, sizeof(memory)) != sizeof(memory)
            || memory.State != MEM_COMMIT || (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS))
            || (allocationBase && (memory.AllocationBase != allocationBase || memory.Type != MEM_IMAGE))
            || (executable && !IsExecutableProtection(memory.Protect))) return false;
        const auto start = reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
        if (memory.RegionSize > (std::numeric_limits<std::uintptr_t>::max)() - start) return false;
        const auto next = start + memory.RegionSize;
        if (next <= current) return false;
        current = next < end ? next : end;
    }
    return true;
}
template<std::size_t Size> bool Matches(const unsigned char* address, const unsigned char(&expected)[Size])
{ return ReadableRange(address, Size) && std::memcmp(address, expected, Size) == 0; }

constexpr std::uint64_t Failure(RizFirstInstallFailureReason reason, std::uint32_t detail = 0)
{ return (std::uint64_t{detail} << 32) | reason; }
std::uint64_t FirstFailure()
{ return static_cast<std::uint64_t>(InterlockedCompareExchange64(&firstFailure, 0, 0)); }
void LatchFailure(std::uint64_t failure)
{ InterlockedCompareExchange64(&firstFailure, static_cast<LONG64>(failure), 0); }

void MarkUnknown(std::uint64_t failure)
{
    LatchFailure(failure);
    PlaybackAttestation::Fault();
    RizTweenRetirementFault();
#if defined(RIZ_FIRST_INSTALL_UNKNOWN_FIXTURE)
    RizFirstInstallFixture_AfterUnknownAtomic();
#endif
    AcquireSRWLockExclusive(&infoLock);
    publishedInfo.unknown = 1;
    publishedInfo.phase = RizFirstInstallUnknown;
    InterlockedExchange(&phase, RizFirstInstallUnknown);
    ReleaseSRWLockExclusive(&infoLock);
}
void DisableIfUnwritten(std::uint64_t failure = Failure(RizFirstInstallFailureInstallAbandoned))
{
    AcquireSRWLockExclusive(&infoLock);
    if (publishedInfo.writesStarted != 0 || FirstFailure() != 0) {
        publishedInfo.phase = RizFirstInstallUnknown;
        publishedInfo.unknown = 1;
        LatchFailure(failure);
        InterlockedExchange(&phase, RizFirstInstallUnknown);
    } else {
        publishedInfo.phase = RizFirstInstallDisabledNoWrite;
        InterlockedExchange(&phase, RizFirstInstallDisabledNoWrite);
    }
    ReleaseSRWLockExclusive(&infoLock);
}
bool StillPreparing()
{ return InterlockedCompareExchange(&phase, 0, 0) == RizFirstInstallPreparing && FirstFailure() == 0; }
bool KnownDisabledNoWrite()
{
    return InterlockedCompareExchange(&phase, 0, 0) == RizFirstInstallDisabledNoWrite
        && FirstFailure() == 0;
}
bool BeginGameWrite()
{
    AcquireSRWLockExclusive(&infoLock);
    const auto allowed = StillPreparing();
    if (allowed) ++publishedInfo.writesStarted;
    ReleaseSRWLockExclusive(&infoLock);
    return allowed;
}
void UnexpectedPrepareWrite()
{
    AcquireSRWLockExclusive(&infoLock);
    if (publishedInfo.writesStarted == 0) publishedInfo.writesStarted = 1;
    ReleaseSRWLockExclusive(&infoLock);
    MarkUnknown(Failure(RizFirstInstallFailureUnexpectedPrepareWrite));
}
void StorePreparedIdentity(const RizFirstInstallInfoV1& info)
{
    AcquireSRWLockExclusive(&infoLock);
    // Never allow an owner still preparing to overwrite a racing reentry's
    // sticky unknown. The phase stays authoritative under this same lock.
    const auto savedPhase = publishedInfo.phase;
    const auto savedUnknown = publishedInfo.unknown;
    const auto writes = publishedInfo.writesStarted;
    publishedInfo = info;
    publishedInfo.phase = savedPhase; publishedInfo.unknown = savedUnknown;
    publishedInfo.writesStarted = writes;
    ReleaseSRWLockExclusive(&infoLock);
}
void StorePreparedIdentityV2(const RizFirstInstallInfoV2& info)
{
    StorePreparedIdentity(info.common);
    AcquireSRWLockExclusive(&infoLock);
    publishedInfoV2 = info;
    ReleaseSRWLockExclusive(&infoLock);
}
void CopyV2Locked(RizFirstInstallInfoV2* info)
{
    *info = publishedInfoV2;
    info->common = publishedInfo;
    info->common.abiVersion = 2;
    info->common.byteSize = sizeof(*info);
}
void StorePreparedIdentityV7(const RizFirstInstallInfoV7& info)
{
    StorePreparedIdentityV2(info.publication);
    AcquireSRWLockExclusive(&infoLock);
    publishedInfoV7 = info;
    ReleaseSRWLockExclusive(&infoLock);
}
void CopyV7Locked(RizFirstInstallInfoV7* info)
{
    *info = publishedInfoV7;
    CopyV2Locked(&info->publication);
    info->publication.common.abiVersion = 7;
    info->publication.common.byteSize = sizeof(*info);
    info->singleEvent.phase = info->publication.common.phase;
    info->singleEvent.unknown = info->publication.common.unknown;
}

struct FilePin {
    HANDLE file = INVALID_HANDLE_VALUE;
    ~FilePin() { if (file != INVALID_HANDLE_VALUE) CloseHandle(file); }
};
struct HashHandles {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    PUCHAR object = nullptr;
    ~HashHandles() {
        if (hash) BCryptDestroyHash(hash);
        if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
        if (object) HeapFree(GetProcessHeap(), 0, object);
    }
};
bool PinAndHash(const wchar_t* path, const unsigned char(&expected)[32], FilePin& pin)
{
    // Deny write/delete sharing throughout hashing, image identity checks, and
    // publication. In particular, loading Dobby cannot race a path replacement.
    pin.file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
    if (pin.file == INVALID_HANDLE_VALUE) return false;
    HashHandles handles;
    if (BCryptOpenAlgorithmProvider(&handles.algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0) return false;
    ULONG objectLength{}, written{};
    if (BCryptGetProperty(handles.algorithm, BCRYPT_OBJECT_LENGTH,
        reinterpret_cast<PUCHAR>(&objectLength), sizeof(objectLength), &written, 0) < 0
        || written != sizeof(objectLength) || objectLength == 0 || objectLength > 1024 * 1024) return false;
    handles.object = static_cast<PUCHAR>(HeapAlloc(GetProcessHeap(), 0, objectLength));
    if (!handles.object || BCryptCreateHash(handles.algorithm, &handles.hash, handles.object, objectLength, nullptr, 0, 0) < 0) return false;
    unsigned char buffer[64 * 1024];
    for (;;) {
        DWORD count{};
        if (!ReadFile(pin.file, buffer, sizeof(buffer), &count, nullptr)) return false;
        if (count == 0) break;
        if (BCryptHashData(handles.hash, buffer, count, 0) < 0) return false;
    }
    unsigned char digest[32]{};
    return BCryptFinishHash(handles.hash, digest, sizeof(digest), 0) >= 0 && std::memcmp(digest, expected, sizeof(digest)) == 0;
}
bool ModulePath(HMODULE module, wchar_t(&path)[32768])
{
    const auto length = GetModuleFileNameW(module, path, static_cast<DWORD>(sizeof(path) / sizeof(path[0])));
    return length != 0 && length < sizeof(path) / sizeof(path[0]);
}
bool AbsolutePath(const wchar_t* supplied, wchar_t(&path)[32768])
{
    if (!supplied) return false;
    const auto length = wcsnlen_s(supplied, 32768);
    if (length < 3 || length >= 32768) return false;
    const bool drive = ((supplied[0] >= L'A' && supplied[0] <= L'Z') || (supplied[0] >= L'a' && supplied[0] <= L'z'))
        && supplied[1] == L':' && (supplied[2] == L'\\' || supplied[2] == L'/');
    const bool unc = supplied[0] == L'\\' && supplied[1] == L'\\';
    if (!drive && !unc) return false;
    const auto count = GetFullPathNameW(supplied, static_cast<DWORD>(sizeof(path) / sizeof(path[0])), path, nullptr);
    return count != 0 && count < sizeof(path) / sizeof(path[0]);
}
#include "FirstInstallPlaybackAttestation.inl"

bool ImageHeaders(HMODULE module, DWORD expectedSize, DWORD expectedTimestamp, bool game)
{
    if (!ReadableRange(module, 4096, module)) return false;
    const auto* base = reinterpret_cast<const unsigned char*>(module);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < static_cast<LONG>(sizeof(*dos)) || dos->e_lfanew > 1024) return false;
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC || nt->OptionalHeader.SizeOfImage != expectedSize
        || nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_EXCEPTION) return false;
    if (!game) return true;
    const auto& directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_EXCEPTION];
    return nt->FileHeader.TimeDateStamp == expectedTimestamp && nt->FileHeader.NumberOfSections == 7
        && nt->OptionalHeader.SizeOfHeaders == 0x400 && directory.VirtualAddress == Profile::PdataRva && directory.Size == Profile::PdataSize;
}
bool FunctionIdentity(const unsigned char* base, std::uint32_t begin, std::uint32_t end, std::uint32_t unwind)
{
    if (!ReadableRange(base + Profile::PdataRva, Profile::PdataSize, base)) return false;
    const auto* entries = reinterpret_cast<const RUNTIME_FUNCTION*>(base + Profile::PdataRva);
    constexpr auto count = Profile::PdataSize / sizeof(RUNTIME_FUNCTION);
    unsigned matches = 0;
    for (std::size_t i = 0; i != count; ++i) {
        const auto& entry = entries[i];
        if (entry.BeginAddress < end && entry.EndAddress > begin) {
            ++matches;
            if (entry.BeginAddress != begin || entry.EndAddress != end || entry.UnwindData != unwind) return false;
        }
    }
    DWORD64 lookupBase{};
    const auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(base + begin), &lookupBase, nullptr);
    return matches == 1 && actual && lookupBase == reinterpret_cast<DWORD64>(base)
        && actual->BeginAddress == begin && actual->EndAddress == end && actual->UnwindData == unwind;
}
bool FixedGame(HMODULE module, const void* originalInit)
{
    const auto* base = reinterpret_cast<const unsigned char*>(module);
    if (!ImageHeaders(module, Profile::ImageSize, Profile::TimeDateStamp, true)
        || originalInit != base + Profile::InitRva
        || reinterpret_cast<const void*>(GetProcAddress(module, "il2cpp_init")) != originalInit
        || reinterpret_cast<const void*>(GetProcAddress(module, "il2cpp_runtime_invoke")) != base + Profile::RuntimeExportRva
        || !ReadableRange(base + Profile::InitRva, sizeof(Profile::InitPrefix), base, true)
        || !ReadableRange(base + Profile::RuntimeRva, sizeof(Profile::RuntimePrefix), base, true)
        || !ReadableRange(base + Profile::RuntimeExportRva, sizeof(Profile::RuntimeExportPrefix), base, true)
        || !ReadableRange(base + Profile::UnityRva, Profile::UnityEndRva - Profile::UnityRva, base, true)
        || !Matches(base + Profile::InitRva, Profile::InitPrefix)
        || !Matches(base + Profile::RuntimeExportRva, Profile::RuntimeExportPrefix)
        || !Matches(base + Profile::RuntimeRva, Profile::RuntimePrefix)
        || !Matches(base + Profile::UnityRva, Profile::UnityPrefix)
        || !Matches(base + Profile::MidRva - 11, Profile::MidWindow)
        || !Matches(base + 0x1ce4d38, Profile::InitUnwind)
        || !Matches(base + 0x1dd1d24, Profile::RuntimeUnwind)
        || !Matches(base + 0x1db3fb4, Profile::UnityUnwind)
        || !FunctionIdentity(base, Profile::InitRva, 0x2841e8, 0x1ce4d38)
        || !FunctionIdentity(base, Profile::RuntimeRva, 0x2af14e, 0x1dd1d24)
        || !FunctionIdentity(base, Profile::UnityRva, Profile::UnityEndRva, 0x1db3fb4)) return false;
    for (std::size_t site = 0; site != 3; ++site) {
        if (std::memcmp(base + Profile::ListenerSourceRva[site], Profile::ListenerSourceBytes[site], Profile::ListenerSourceWidth[site]) != 0
            || std::memcmp(base + Profile::ListenerReturnRva[site], Profile::ListenerPostBytes[site], Profile::ListenerPostWidth[site]) != 0)
            return false;
        if (site < 2) {
            std::int32_t call{};
            std::memcpy(&call, base + Profile::ListenerSourceRva[site] + 1, sizeof(call));
            if (base + Profile::ListenerReturnRva[site] + call != base + Profile::ListenerDirectRva) return false;
        }
    }
    if (sealed.baseline && std::memcmp(base + Profile::UnityRva, sealed.originalUnity, unityBodySize) != 0) return false;
    if (sealed.operationBaseline && !OperationOriginalIdentity(base)) return false;
    std::int32_t displacement{};
    std::memcpy(&displacement, base + Profile::RuntimeExportRva + 1, sizeof(displacement));
    return base + Profile::RuntimeExportRva + 5 + displacement == base + Profile::RuntimeRva;
}
bool FixedGameGuarded(HMODULE module, const void* originalInit)
{
    __try { return FixedGame(module, originalInit); }
    __except (EXCEPTION_EXECUTE_HANDLER) { return false; }
}
bool FixedDobby(HMODULE module, const wchar_t* validatedPath, PrepareFn& prepare, CommitFn& commit)
{
    wchar_t loaded[32768]{};
    if (!ModulePath(module, loaded) || _wcsicmp(loaded, validatedPath) != 0
        || !ImageHeaders(module, Profile::DobbyImageSize, 0, false)) return false;
    const auto* base = reinterpret_cast<const unsigned char*>(module);
    const auto p = GetProcAddress(module, "DobbyPrepare"), c = GetProcAddress(module, "DobbyCommit");
    if (reinterpret_cast<const void*>(p) != base + Profile::DobbyPrepareRva
        || reinterpret_cast<const void*>(c) != base + Profile::DobbyCommitRva
        || !ReadableRange(reinterpret_cast<const void*>(p), sizeof(Profile::DobbyPreparePrefix), module, true)
        || !ReadableRange(reinterpret_cast<const void*>(c), sizeof(Profile::DobbyCommitPrefix), module, true)
        || !Matches(base + Profile::DobbyPrepareRva, Profile::DobbyPreparePrefix)
        || !Matches(base + Profile::DobbyCommitRva, Profile::DobbyCommitPrefix)) return false;
    static_assert(sizeof(p) == sizeof(prepare) && sizeof(c) == sizeof(commit));
    std::memcpy(&prepare, &p, sizeof(prepare)); std::memcpy(&commit, &c, sizeof(commit)); return true;
}

bool Relative(unsigned char* instruction, unsigned char opcode, const void* destination)
{
    const auto delta = reinterpret_cast<std::intptr_t>(destination) - reinterpret_cast<std::intptr_t>(instruction + 5);
    if (delta < (std::numeric_limits<std::int32_t>::min)() || delta > (std::numeric_limits<std::int32_t>::max)()) return false;
    const auto offset = static_cast<std::int32_t>(delta);
    instruction[0] = opcode; std::memcpy(instruction + 1, &offset, sizeof(offset)); return true;
}
void Tail(unsigned char* instruction, const void* entry)
{
    const unsigned char bytes[6]{ 0xff, 0x25, 0, 0, 0, 0 };
    std::memcpy(instruction, bytes, sizeof(bytes)); std::memcpy(instruction + 6, &entry, sizeof(entry));
}
bool Protection(const void* address, std::size_t size, const void* allocation, DWORD* protection)
{
    MEMORY_BASIC_INFORMATION memory{};
    if (!ReadableRange(address, size, allocation, true)
        || VirtualQuery(address, &memory, sizeof(memory)) != sizeof(memory)
        || !IsExecutableProtection(memory.Protect)) return false;
    const auto start = reinterpret_cast<std::uintptr_t>(address), region = reinterpret_cast<std::uintptr_t>(memory.BaseAddress);
    if (start < region || size > memory.RegionSize || start - region > memory.RegionSize - size) return false;
    *protection = memory.Protect; return true;
}
bool WriteSource(unsigned char* site, const unsigned char* original, const unsigned char* replacement, std::size_t size, DWORD protection)
{
    // Classify the exact existing seal before the first mutation. Listener
    // sources belong to UnityEvent; Tween sources belong to the three already
    // prepared operation bodies. No other address is a writable source.
    auto* base = reinterpret_cast<unsigned char*>(rootedGame);
    unsigned char* sealedBytes = nullptr;
    const auto address = reinterpret_cast<std::uintptr_t>(site);
    const auto unity = reinterpret_cast<std::uintptr_t>(base + Profile::UnityRva);
    if (sealed.baseline && address >= unity && address - unity <= unityBodySize
        && size <= unityBodySize - (address - unity))
        sealedBytes = sealed.expectedUnity + (address - unity);
    if (!sealedBytes && sealed.operationBaseline && sealed.operationsPublished) {
        for (const auto& row : Profile::Operations) {
            const auto start = reinterpret_cast<std::uintptr_t>(base + row.rva);
            if (address >= start && address - start <= row.originalSize && size <= row.originalSize - (address - start)) {
                sealedBytes = sealed.expectedOperations[row.id] + (address - start); break;
            }
        }
        for (const auto& row : Profile::OperationContinuations) {
            const auto start = reinterpret_cast<std::uintptr_t>(base + row.rva);
            if (address >= start && address - start <= row.originalSize && size <= row.originalSize - (address - start)) {
                sealedBytes = sealed.expectedContinuations[row.index] + (address - start); break;
            }
        }
    }
    if (!sealedBytes || !size || !original || !replacement || std::memcmp(sealedBytes, original, size) != 0) return false;
    DWORD current{};
    if (!StillPreparing() || std::memcmp(site, original, size) != 0
        || !Protection(site, size, rootedGame, &current) || current != protection) return false;
    DWORD old{};
    if (!PlaybackAttestation::BeforeWrite(site, size) || !BeginGameWrite()
        || !VirtualProtect(site, size, PAGE_EXECUTE_READWRITE, &old)) return false;
    bool restored = false, flushed = false;
    __try { std::memcpy(site, replacement, size); }
    __finally {
        DWORD ignored{}; restored = VirtualProtect(site, size, old, &ignored) != FALSE;
        flushed = FlushInstructionCache(GetCurrentProcess(), site, size) != FALSE;
    }
    if (!restored || !flushed || old != protection || !Protection(site, size, rootedGame, &current)
        || current != protection || std::memcmp(site, replacement, size) != 0) return false;
    std::memcpy(sealedBytes, replacement, size);
    return PlaybackAttestation::AfterWrite(site, replacement, size);
}
bool PatchMid(unsigned char* site, const void* relay)
{
    unsigned char replacement[5]{};
    // Encode against the source address, not this local buffer.
    const auto delta = reinterpret_cast<std::intptr_t>(relay) - reinterpret_cast<std::intptr_t>(site + 5);
    if (delta < (std::numeric_limits<std::int32_t>::min)() || delta > (std::numeric_limits<std::int32_t>::max)()) return false;
    replacement[0] = 0xe8; const auto offset = static_cast<std::int32_t>(delta); std::memcpy(replacement + 1, &offset, sizeof(offset));
    const unsigned char original[5]{ 0x0f, 0x1f, 0x44, 0, 0 };
    return WriteSource(site, original, replacement, sizeof(replacement), sealed.midProtection);
}
bool CaptureOriginal(unsigned char* base)
{
    if (!Protection(base + Profile::MidRva, 5, base, &sealed.midProtection)
        || !Protection(base + Profile::UnityRva, 32, base, &sealed.unityProtection)
        || !Protection(base + Profile::RuntimeRva, 32, base, &sealed.runtimeProtection)) return false;
    std::memcpy(sealed.originalUnity, base + Profile::UnityRva, unityBodySize);
    std::memcpy(sealed.expectedUnity, sealed.originalUnity, unityBodySize);
    sealed.baseline = true;
    return true;
}
bool PrepareListenerBlock(RizFirstInstallInfoV2& info)
{
    auto* base = reinterpret_cast<unsigned char*>(info.common.moduleBase);
    const auto imageBase = reinterpret_cast<std::uintptr_t>(base);
    SYSTEM_INFO system{}; GetSystemInfo(&system);
    const auto step = static_cast<std::uintptr_t>(system.dwAllocationGranularity);
    if (!step || (step & (step - 1)) != 0) return false;
    for (std::uintptr_t distance = (Profile::ImageSize + step - 1) & ~(step - 1);
        distance < 0x70000000ULL; distance += step) {
        if (imageBase > (std::numeric_limits<std::uintptr_t>::max)() - distance) return false;
        rootedListenerBlock = VirtualAlloc(reinterpret_cast<void*>(imageBase + distance), listenerAllocationSize,
            MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        if (rootedListenerBlock) break;
    }
    if (!rootedListenerBlock) return false;
    auto* block = static_cast<unsigned char*>(rootedListenerBlock);
    std::memset(block, 0xcc, listenerAllocationSize);
    info.capabilityVersion = 1; info.listenerSlot = reservedVoidSlot;
    info.preEntry = RizListenerContext_GetPreEntry(); info.postEntry = RizListenerContext_GetPostEntry();
    info.preRelay = block + 0x500; info.postRelay = block + 0x540;
    info.common.relay = block + 0x520; rootedRelay = block + 0x520;
    info.functionTable = block + 0x380; info.functionTableBase = imageBase;
    info.allocationSize = listenerAllocationSize;
    Tail(block + 0x500, info.preEntry); Tail(block + 0x520, RizEventMidContext_GetEntry());
    Tail(block + 0x540, info.postEntry);
    const RUNTIME_FUNCTION parent{ Profile::UnityRva, Profile::UnityEndRva, Profile::UnityUnwindRva };
    auto* table = reinterpret_cast<RUNTIME_FUNCTION*>(block + 0x380);
    for (std::size_t site = 0; site != 3; ++site) {
        auto* stub = block + site * 0x100;
        auto* unwind = stub + 0x40;
        if (!Relative(stub, 0xe8, info.postRelay) || !Relative(stub + 9, 0xe9, base + Profile::MidReturnRva)) return false;
        const unsigned char replay[]{ 0xff, 0xc3, 0x8b, 0xc3 };
        std::memcpy(stub + 5, replay, sizeof(replay));
        // Actual primary has frameRegister/offset zero, verified in Profile.
        unwind[0] = 0x21; unwind[1] = 0; unwind[2] = 0; unwind[3] = 0;
        std::memcpy(unwind + 4, &parent, sizeof(parent));
        const auto codeRva = reinterpret_cast<std::uintptr_t>(stub) - imageBase;
        const auto unwindRva = reinterpret_cast<std::uintptr_t>(unwind) - imageBase;
        if (codeRva > MAXDWORD - 14 || unwindRva > MAXDWORD) return false;
        table[site] = { static_cast<DWORD>(codeRva), static_cast<DWORD>(codeRva + 14), static_cast<DWORD>(unwindRva) };
        info.sourcePc[site] = base + Profile::ListenerSourceRva[site];
        info.sourceReturnPc[site] = base + Profile::ListenerReturnRva[site];
        info.postStub[site] = stub; info.postTransportReturnPc[site] = stub + 5;
        info.unwindInfo[site] = unwind;
        info.sourceWidth[site] = Profile::ListenerSourceWidth[site]; info.postWidth[site] = Profile::ListenerPostWidth[site];
        DWORD sourceProtection{}, postProtection{};
        if (!Protection(info.sourcePc[site], info.sourceWidth[site], base, &sourceProtection)
            || !Protection(info.sourceReturnPc[site], info.postWidth[site], base, &postProtection)) return false;
        info.sourceProtection[site] = sourceProtection; info.postProtection[site] = postProtection;
        // Verify both source patches are actually rel32-reachable before any
        // game write. Virtual CALL starts one byte later, preserving +6.
        const auto preDelta = reinterpret_cast<std::intptr_t>(info.preRelay) - reinterpret_cast<std::intptr_t>(info.sourceReturnPc[site]);
        const auto postDelta = reinterpret_cast<std::intptr_t>(stub) - reinterpret_cast<std::intptr_t>(base + Profile::ListenerReturnRva[site] + 5);
        if (preDelta < (std::numeric_limits<std::int32_t>::min)() || preDelta > (std::numeric_limits<std::int32_t>::max)()
            || postDelta < (std::numeric_limits<std::int32_t>::min)() || postDelta > (std::numeric_limits<std::int32_t>::max)()) return false;
    }
    info.directTarget[0] = info.directTarget[1] = base + Profile::ListenerDirectRva;
    std::memcpy(sealed.expectedBlock, block, listenerAllocationSize);
    DWORD old{};
    if (!VirtualProtect(block, listenerAllocationSize, PAGE_EXECUTE_READ, &old)
        || !FlushInstructionCache(GetCurrentProcess(), block, listenerAllocationSize)
        || !RtlAddFunctionTable(table, 3, imageBase)) return false;
    sealed.tableRegistered = true; info.registeredFunctionCount = 3;
    return true;
}
bool BlockIdentity(const RizFirstInstallInfoV2& info)
{
    const auto* base = reinterpret_cast<const unsigned char*>(info.common.moduleBase);
    const auto* block = static_cast<const unsigned char*>(rootedListenerBlock);
    MEMORY_BASIC_INFORMATION memory{};
    // Exact sealed bytes are checked BEFORE touching the OS function-table
    // lookup/unwinder. A caller-corrupted chain must only yield Unknown.
    if (!block || !sealed.tableRegistered || info.capabilityVersion != 1 || info.listenerSlot != reservedVoidSlot
        || info.allocationSize != listenerAllocationSize || info.functionTableBase != info.common.moduleBase
        || info.functionTable != block + 0x380 || info.registeredFunctionCount != 3
        || info.preEntry != RizListenerContext_GetPreEntry() || info.postEntry != RizListenerContext_GetPostEntry()
        || info.preRelay != block + 0x500 || info.common.relay != block + 0x520 || info.postRelay != block + 0x540
        || !ReadableRange(block, listenerAllocationSize, nullptr, true)
        || VirtualQuery(block, &memory, sizeof(memory)) != sizeof(memory) || memory.AllocationBase != block
        || memory.Type != MEM_PRIVATE || memory.Protect != PAGE_EXECUTE_READ || memory.RegionSize < listenerAllocationSize
        || std::memcmp(block, sealed.expectedBlock, listenerAllocationSize) != 0
        || !Matches(base + Profile::UnityUnwindRva, Profile::UnityUnwind)) return false;
    for (std::size_t site = 0; site != 3; ++site) {
        if (info.sourcePc[site] != base + Profile::ListenerSourceRva[site]
            || info.sourceReturnPc[site] != base + Profile::ListenerReturnRva[site]
            || info.postStub[site] != block + site * 0x100 || info.postTransportReturnPc[site] != block + site * 0x100 + 5
            || info.unwindInfo[site] != block + site * 0x100 + 0x40
            || info.sourceWidth[site] != Profile::ListenerSourceWidth[site] || info.postWidth[site] != Profile::ListenerPostWidth[site])
            return false;
        DWORD sourceProtection{}, postProtection{};
        if (!Protection(info.sourcePc[site], info.sourceWidth[site], base, &sourceProtection)
            || !Protection(info.sourceReturnPc[site], info.postWidth[site], base, &postProtection)
            || sourceProtection != info.sourceProtection[site] || postProtection != info.postProtection[site]) return false;
    }
    return info.directTarget[0] == base + Profile::ListenerDirectRva && info.directTarget[1] == info.directTarget[0];
}
bool TableLookup(const RizFirstInstallInfoV2& info, std::size_t site, unsigned offset)
{
    DWORD64 lookupBase{};
    auto* actual = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(info.postStub[site]) + offset, &lookupBase, nullptr);
    const auto* table = static_cast<const RUNTIME_FUNCTION*>(info.functionTable);
    return actual && lookupBase == info.functionTableBase
        && std::memcmp(actual, table + site, sizeof(*actual)) == 0;
}
bool BoundaryUnwind(const RizFirstInstallInfoV2& info, std::size_t site, unsigned offset)
{
    // Exercise the actual registered dynamic chain over a controlled stack.
    // This invokes only OS unwinding metadata, never a game/stub instruction.
    alignas(16) DWORD64 stack[32]{};
    stack[4] = 0x6600660066006600ULL; // RSI pushed by actual Unity prolog.
    stack[5] = reinterpret_cast<DWORD64>(&RizFirstInstall_GetInfoV7);
    stack[6] = 0x3300330033003300ULL; stack[7] = 0x5500550055005500ULL;
    stack[8] = 0x7700770077007700ULL; stack[9] = 0xee00ee00ee00ee00ULL;
    CONTEXT context{}; context.ContextFlags = CONTEXT_ALL;
    context.Rip = reinterpret_cast<DWORD64>(info.postStub[site]) + offset;
    context.Rsp = reinterpret_cast<DWORD64>(stack);
    context.Rbx = offset >= 7 ? 8 : 7;
    context.Rax = offset >= 9 ? context.Rbx : 0x1010;
    context.R12 = 0x1200120012001200ULL; context.R13 = 0x1300130013001300ULL; context.R15 = 0x1500150015001500ULL;
    std::memset(&context.Xmm0, 0x5a, 16 * sizeof(M128A));
    const auto before = context;
    PVOID handlerData{}; DWORD64 establisher{};
    auto* table = const_cast<RUNTIME_FUNCTION*>(static_cast<const RUNTIME_FUNCTION*>(info.functionTable));
    RtlVirtualUnwind(UNW_FLAG_NHANDLER, info.functionTableBase, context.Rip,
        table + site, &context, &handlerData, &establisher, nullptr);
    return context.Rip == stack[5] && context.Rsp == reinterpret_cast<DWORD64>(stack) + 0x30
        && context.Rsi == stack[4] && context.Rbx == stack[6] && context.Rbp == stack[7]
        && context.Rdi == stack[8] && context.R14 == stack[9]
        && context.R12 == before.R12 && context.R13 == before.R13 && context.R15 == before.R15
        && std::memcmp(&context.Xmm6, &before.Xmm6, 10 * sizeof(M128A)) == 0;
}
bool VerifyPrewriteTables(RizFirstInstallInfoV2& info)
{
    if (!BlockIdentity(info)) return false;
    for (std::size_t site = 0; site != 3; ++site) {
        for (const unsigned offset : { 0u, 5u, 7u, 9u }) {
            if (!TableLookup(info, site, offset)) return false;
            ++info.prewriteLookupChecks;
            if (!BoundaryUnwind(info, site, offset)) return false;
            ++info.prewriteBoundaryChecks;
        }
    }
    return info.prewriteLookupChecks == 12 && info.prewriteBoundaryChecks == 12;
}
bool ListenerIdentity(const RizFirstInstallInfoV2& expected)
{
    RizListenerInfoV1 lane{ 1, sizeof(RizListenerInfoV1) };
    if (expected.listenerEpoch == 0 || RizListenerContext_GetInfo(reservedVoidSlot, &lane) != 0
        || lane.unknown != 0 || lane.slot != expected.listenerSlot || lane.epoch != expected.listenerEpoch
        || lane.parentEpoch != expected.common.unityEpoch || lane.preEntry != expected.preEntry || lane.postEntry != expected.postEntry)
        return false;
    for (std::size_t site = 0; site != 3; ++site)
        if (lane.sourceReturnPc[site] != expected.sourceReturnPc[site]
            || lane.postTransportReturnPc[site] != expected.postTransportReturnPc[site]) return false;
    return lane.directTarget[0] == expected.directTarget[0] && lane.directTarget[1] == expected.directTarget[1];
}
bool PatchListeners(const RizFirstInstallInfoV2& info)
{
    auto* base = reinterpret_cast<unsigned char*>(info.common.moduleBase);
    for (std::size_t site = 0; site != 3; ++site) {
        unsigned char call[6]{ 0x90 };
        const auto displacement = static_cast<std::int32_t>(reinterpret_cast<std::intptr_t>(info.preRelay)
            - reinterpret_cast<std::intptr_t>(info.sourceReturnPc[site]));
        const auto start = site == 2 ? 1u : 0u;
        call[start] = 0xe8; std::memcpy(call + start + 1, &displacement, sizeof(displacement));
        if (!WriteSource(base + Profile::ListenerSourceRva[site], Profile::ListenerSourceBytes[site], call,
            info.sourceWidth[site], info.sourceProtection[site])) return false;
        unsigned char jump[9]{}; std::memset(jump, 0x90, sizeof(jump));
        const auto postDelta = static_cast<std::int32_t>(reinterpret_cast<std::intptr_t>(info.postStub[site])
            - reinterpret_cast<std::intptr_t>(base + Profile::ListenerReturnRva[site] + 5));
        jump[0] = 0xe9; std::memcpy(jump + 1, &postDelta, sizeof(postDelta));
        if (!WriteSource(base + Profile::ListenerReturnRva[site], Profile::ListenerPostBytes[site], jump,
            info.postWidth[site], info.postProtection[site])) return false;
    }
    return true;
}
bool DetourReaches(const unsigned char* address, const void* expected)
{
    // Inspect only recognized unconditional jump forms; never execute a stub.
    // Near trampoline chains must end at the exact configured helper entry.
    const unsigned char* visited[4]{};
    for (unsigned i = 0; i != 4; ++i) {
        if (address == expected) return true;
        for (unsigned j = 0; j != i; ++j) if (visited[j] == address) return false;
        visited[i] = address;
        if (!ReadableRange(address, 16, nullptr, true)) return false;
        if (address[0] == 0xe9) {
            std::int32_t offset{}; std::memcpy(&offset, address + 1, sizeof(offset)); address += 5 + offset;
        } else if (address[0] == 0xff && address[1] == 0x25) {
            std::int32_t offset{}; std::memcpy(&offset, address + 2, sizeof(offset)); const auto* slot = address + 6 + offset;
            if (!ReadableRange(slot, sizeof(address))) return false; std::memcpy(&address, slot, sizeof(address));
        } else if (address[0] == 0x48 && address[1] == 0xb8 && address[10] == 0xff && address[11] == 0xe0) {
            std::memcpy(&address, address + 2, sizeof(address));
        } else return false;
    }
    return address == expected;
}
template<std::size_t Size> bool CommitDetour(CommitFn commit, unsigned char* target, const void* entry, const unsigned char(&prefix)[Size])
{
    static_assert(Size > 32);
    MEMORY_BASIC_INFORMATION before{};
    if (!StillPreparing() || std::memcmp(target, prefix, Size) != 0
        || VirtualQuery(target, &before, sizeof(before)) != sizeof(before)
        || !IsExecutableProtection(before.Protect) || !PlaybackAttestation::BeforeWrite(target, 32) || !BeginGameWrite()) return false;
    int result = -1; bool restored = false, flushed = false;
    __try { result = commit(target); }
    __finally {
        DWORD ignored{}; restored = VirtualProtect(target, 32, before.Protect, &ignored) != FALSE;
        flushed = FlushInstructionCache(GetCurrentProcess(), target, 32) != FALSE;
    }
    MEMORY_BASIC_INFORMATION after{};
    return result == 0 && restored && flushed && VirtualQuery(target, &after, sizeof(after)) == sizeof(after)
        && after.Protect == before.Protect && DetourReaches(target, entry)
        && std::memcmp(target + 32, prefix + 32, Size - 32) == 0
        && PlaybackAttestation::AfterWrite(target, target, 32);
}
bool OperationContinuationIdentity(const unsigned char* base)
{
    // The Single event entry has three additional CHAININFO runtime functions.
    // They do not receive detours, but the execution and unwind identities must
    // remain sealed for as long as the entry's publication is considered Ready.
    for (const auto& row : Profile::OperationContinuations) {
        DWORD protection{};
        if (row.index >= Profile::OperationContinuationCount || row.operationId >= Profile::OperationCount
            || row.originalSize != row.endRva - row.rva || row.unwindSize < 16
            || !ReadableRange(base + row.rva, row.originalSize, base, true)
            || !ReadableRange(base + row.unwindRva, row.unwindSize, base)
            || std::memcmp(base + row.rva, sealed.operationBaseline ? sealed.expectedContinuations[row.index] : row.original, row.originalSize) != 0
            || std::memcmp(base + row.unwindRva, row.unwind, row.unwindSize) != 0
            || !Protection(base + row.rva, row.originalSize, base, &protection)
            || (sealed.operationBaseline && protection != sealed.operationContinuationProtection[row.index])) return false;
        // Compare raw unwind and pdata before invoking the OS lookup. In
        // particular, a corrupt CHAININFO parent must never reach the unwinder.
        if (!FunctionIdentity(base, row.rva, row.endRva, row.unwindRva)) return false;
    }
    return true;
}
bool OperationOriginalIdentity(const unsigned char* base)
{
    for (const auto& row : Profile::Operations) {
        if (!ReadableRange(base + row.rva, row.originalSize, base, true)
            || !ReadableRange(base + row.unwindRva, row.unwindSize, base)
            || std::memcmp(base + row.rva, row.original, row.originalSize) != 0
            || std::memcmp(base + row.unwindRva, row.unwind, row.unwindSize) != 0
            || !FunctionIdentity(base, row.rva, row.endRva, row.unwindRva)) return false;
    }
    return OperationContinuationIdentity(base);
}
bool PreflightOperations(RizFirstInstallInfoV7& info)
{
    const auto* base = reinterpret_cast<const unsigned char*>(info.publication.common.moduleBase);
    if (!base || !OperationOriginalIdentity(base)) return false;
    info.operationCapabilityVersion = 4; info.operationCount = Profile::OperationCount;
    info.expectedWrites = Profile::ExpectedWrites;
    for (const auto& row : Profile::Operations) {
        if (row.id >= Profile::OperationCount || row.originalSize < 32 || row.originalSize > Profile::MaximumOperationBytes) return false;
        for (std::size_t prior = 0; prior < row.id; ++prior) {
            const auto& other = Profile::Operations[prior];
            if ((row.kind == other.kind && row.slot == other.slot)
                || (row.rva < other.rva + other.originalSize && other.rva < row.rva + row.originalSize)) return false;
        }
        RizOperationBootstrapInfoV1 native{ 1, sizeof(native) };
        if (RizOperationBootstrap_GetInfo(row.kind, row.slot, &native) != 2 || native.unknown != 0
            || native.attached != 0 || native.target || native.original || native.epoch != 0
            || native.entered != 0 || native.exited != 0 || native.inFlight != 0
            || native.enterFaults != 0 || native.exitFaults != 0 || !native.entry) return false;
        auto& receipt = info.operations[row.id];
        receipt.id = row.id; receipt.kind = row.kind; receipt.slot = row.slot;
        receipt.rva = row.rva; receipt.bodySize = row.endRva - row.rva; receipt.unwindRva = row.unwindRva;
        receipt.target = base + row.rva; receipt.entry = native.entry;
        DWORD protection{};
        if (!Protection(base + row.rva, row.originalSize, base, &protection)) return false;
        receipt.protection = protection;
        std::memcpy(sealed.expectedOperations[row.id], row.original, row.originalSize);
    }
    for (const auto& row : Profile::OperationContinuations) {
        if (!Protection(base + row.rva, row.originalSize, base,
            &sealed.operationContinuationProtection[row.index])) return false;
        std::memcpy(sealed.expectedContinuations[row.index], row.original, row.originalSize);
    }
    sealed.operationBaseline = true;
    return true;
}
bool OperationNativeIdentity(const RizFirstInstallOperationV7& row, bool requireUnattached)
{
    RizOperationBootstrapInfoV1 native{ 1, sizeof(native) };
    return RizOperationBootstrap_GetInfo(row.kind, row.slot, &native) == 0 && native.unknown == 0
        && native.kind == row.kind && native.slot == row.slot && native.target == row.target
        && native.original == row.original && native.entry == row.entry && native.epoch == row.epoch && row.epoch != 0
        && native.attached <= 1 && (!requireUnattached || (native.attached == 0 && native.inFlight == 0));
}
bool PrepareOperations(RizFirstInstallInfoV7& info, PrepareFn prepare)
{
    const auto* base = reinterpret_cast<const unsigned char*>(info.publication.common.moduleBase);
    for (const auto& row : Profile::Operations) {
        if (!StillPreparing() || !OperationOriginalIdentity(base)) return false;
        auto& receipt = info.operations[row.id]; void* original{};
        const auto result = prepare(const_cast<void*>(receipt.target), const_cast<void*>(receipt.entry), &original);
        // Retain actual preparation identity immediately, including failed
        // Configure and any later failure. Prepared allocation is never freed.
        receipt.original = original; StorePreparedIdentityV7(info);
        if (!FixedGameGuarded(rootedGame, info.publication.common.init)) { UnexpectedPrepareWrite(); return false; }
        if (result != 0 || !original || !ReadableRange(original, 16, nullptr, true) || !StillPreparing()
            || !PlaybackAttestation::Prepared(row.id + 2, receipt.target, receipt.entry, original)) return false;
        receipt.original = original; StorePreparedIdentityV7(info);
        const RizOperationBootstrapConfigV1 config{ 1, sizeof(config), const_cast<void*>(receipt.target), original };
        if (RizOperationBootstrap_Configure(row.kind, row.slot, &config) != 0) return false;
        RizOperationBootstrapInfoV1 native{ 1, sizeof(native) };
        if (RizOperationBootstrap_GetInfo(row.kind, row.slot, &native) != 0) return false;
        receipt.epoch = native.epoch;
        info.configuredMask |= std::uint64_t{ 1 } << row.id; StorePreparedIdentityV7(info);
        if (!OperationNativeIdentity(receipt, true) || !PlaybackAttestation::SetEpoch(row.id + 2, receipt.epoch)) return false;
    }
    return true;
}
bool CommitOperations(RizFirstInstallInfoV7& info, CommitFn commit)
{
    auto* base = reinterpret_cast<unsigned char*>(info.publication.common.moduleBase);
    for (const auto& row : Profile::Operations) {
        auto& receipt = info.operations[row.id]; auto* target = base + row.rva; DWORD protection{};
        if (!StillPreparing() || !OperationNativeIdentity(receipt, true) || !OperationContinuationIdentity(base)
            || !ReadableRange(target, row.originalSize, base, true)
            || std::memcmp(target, row.original, row.originalSize) != 0
            || !Protection(target, row.originalSize, base, &protection) || protection != receipt.protection
            || !PlaybackAttestation::BeforeWrite(target, 32) || !BeginGameWrite()) return false;
        int result = -1; bool restored = false, flushed = false;
        __try { result = commit(target); }
        __finally {
            DWORD ignored{}; restored = VirtualProtect(target, 32, receipt.protection, &ignored) != FALSE;
            flushed = FlushInstructionCache(GetCurrentProcess(), target, 32) != FALSE;
        }
        if (result != 0 || !restored || !flushed || !Protection(target, row.originalSize, base, &protection)
            || protection != receipt.protection || !DetourReaches(target, receipt.entry)
            || std::memcmp(target + 32, row.original + 32, row.originalSize - 32) != 0
            || !PlaybackAttestation::AfterWrite(target, target, 32)) return false;
        std::memcpy(sealed.expectedOperations[row.id], target, 32);
        info.publishedMask |= std::uint64_t{ 1 } << row.id; StorePreparedIdentityV7(info);
    }
    sealed.operationsPublished = true;
    return true;
}
#include "TweenSingleEventPublication.inl"
std::uint64_t OperationPublishedFailure(const RizFirstInstallInfoV7& info)
{
    constexpr std::uint64_t all = (std::uint64_t{ 1 } << Profile::OperationCount) - 1;
    const auto* base = reinterpret_cast<const unsigned char*>(info.publication.common.moduleBase);
    if (!sealed.operationBaseline || !sealed.operationsPublished || info.operationCapabilityVersion != 4
        || info.operationCount != Profile::OperationCount || info.expectedWrites != Profile::ExpectedWrites || info.reserved != 0
        || info.configuredMask != all || info.publishedMask != all) return Failure(RizFirstInstallFailurePublishedOperation);
    for (const auto& row : Profile::Operations) {
        const auto& actual = info.operations[row.id]; DWORD protection{};
        if (actual.id != row.id || actual.kind != row.kind || actual.slot != row.slot || actual.reserved != 0
            || actual.rva != row.rva || actual.bodySize != row.endRva - row.rva || actual.unwindRva != row.unwindRva
            || actual.target != base + row.rva || actual.entry != RizOperationContext_GetEntry(row.kind, row.slot)
            || !ReadableRange(base + row.rva, row.originalSize, base, true)
            || std::memcmp(base + row.rva, sealed.expectedOperations[row.id], row.originalSize) != 0
            || !ReadableRange(base + row.unwindRva, row.unwindSize, base)
            || std::memcmp(base + row.unwindRva, row.unwind, row.unwindSize) != 0
            || !Protection(base + row.rva, row.originalSize, base, &protection) || protection != actual.protection
            || !OperationNativeIdentity(actual, false) || !ReadableRange(actual.original, 16, nullptr, true)
            || !DetourReaches(static_cast<const unsigned char*>(actual.target), actual.entry))
            return Failure(RizFirstInstallFailurePublishedOperation, row.id + 1);
        // Validate raw pdata identity before handing this source to the OS
        // lookup; corrupted metadata must fail closed, never become an input
        // for arbitrary unwind interpretation in a receipt query.
        if (!FunctionIdentity(base, row.rva, row.endRva, row.unwindRva))
            return Failure(RizFirstInstallFailurePublishedOperation, row.id + 1);
    }
    return OperationContinuationIdentity(base) ? 0u
        : Failure(RizFirstInstallFailurePublishedOperation, Profile::OperationCount + 1);
}
bool RuntimeInvokeIdentity(const RizFirstInstallInfoV1& expected)
{
    RizRuntimeInvokeInfoV1 runtime{ 1, sizeof(RizRuntimeInvokeInfoV1) };
    return RizRuntimeInvokeContext_GetInfo(&runtime) == 0 && runtime.unknown == 0
        && runtime.target == expected.runtimeTarget && reinterpret_cast<const void*>(runtime.original) == expected.runtimeOriginal
        && runtime.entry == expected.runtimeEntry && runtime.epoch == expected.runtimeEpoch && runtime.epoch != 0;
}
bool UnityIdentity(const RizFirstInstallInfoV1& expected)
{
    RizVoidBootstrapInfoV1 unity{ 1, sizeof(RizVoidBootstrapInfoV1) };
    return RizVoidBootstrap_GetInfo(reservedVoidSlot, &unity) == 0 && unity.unknown == 0
        && unity.slot == reservedVoidSlot && unity.target == expected.unityTarget
        && reinterpret_cast<const void*>(unity.original) == expected.unityOriginal && unity.entry == expected.unityEntry
        && unity.epoch == expected.unityEpoch && unity.epoch != 0;
}
bool DependencyIdentity(const RizFirstInstallInfoV1& expected)
{ return RuntimeInvokeIdentity(expected) && UnityIdentity(expected); }
bool MidPublished(const RizFirstInstallInfoV1& expected)
{
    const auto* base = reinterpret_cast<const unsigned char*>(expected.moduleBase);
    const auto* site = base + Profile::MidRva;
    const auto* relay = static_cast<const unsigned char*>(expected.relay);
    const auto* midEntry = RizEventMidContext_GetEntry();
    MEMORY_BASIC_INFORMATION memory{};
    if (!base || expected.midReturnPc != site + 5 || !relay
        || !ReadableRange(site - 11, sizeof(Profile::MidWindow), base, true)
        || !ReadableRange(relay, 14, nullptr, true) || site[0] != 0xe8
        || VirtualQuery(relay, &memory, sizeof(memory)) != sizeof(memory) || memory.Protect != PAGE_EXECUTE_READ
        || memory.AllocationBase != rootedListenerBlock || relay != static_cast<const unsigned char*>(rootedListenerBlock) + 0x520
        || relay[0] != 0xff || relay[1] != 0x25
        || relay[2] != 0 || relay[3] != 0 || relay[4] != 0 || relay[5] != 0
        || std::memcmp(relay + 6, &midEntry, sizeof(midEntry)) != 0
        || std::memcmp(site - 11, Profile::MidWindow, 11) != 0
        || std::memcmp(site + 5, Profile::MidWindow + 16, sizeof(Profile::MidWindow) - 16) != 0) return false;
    std::int32_t displacement{}; std::memcpy(&displacement, site + 1, sizeof(displacement));
    return site + 5 + displacement == relay;
}

bool PrimaryLookup(const unsigned char* base, std::uint32_t begin, std::uint32_t end, std::uint32_t unwind)
{
    DWORD64 lookupBase{};
    auto* entry = RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(base + begin), &lookupBase, nullptr);
    return entry && lookupBase == reinterpret_cast<DWORD64>(base)
        && entry->BeginAddress == begin && entry->EndAddress == end && entry->UnwindData == unwind;
}
std::uint32_t PublishedCoreFailure(const RizFirstInstallInfoV2& info)
{
    const auto& common = info.common;
    const auto* base = reinterpret_cast<const unsigned char*>(common.moduleBase);
    if (!base || !sealed.published || common.writesStarted != Profile::ExpectedWrites || common.voidSlot != reservedVoidSlot
        || common.imageSize != Profile::ImageSize || common.init != base + Profile::InitRva
        || common.unityTarget != base + Profile::UnityRva || common.runtimeTarget != base + Profile::RuntimeRva
        || info.capabilityVersion != 1 || info.prewriteLookupChecks != 12 || info.prewriteBoundaryChecks != 12
        || info.reserved2 != 0 || !BlockIdentity(info)) return RizFirstInstallCoreReceiptOrBlock;
    // BlockIdentity verifies sealed code/table/chain bytes before any dynamic
    // lookup. Damaged xdata never reaches RtlVirtualUnwind on a receipt read.
    if (!ImageHeaders(rootedGame, Profile::ImageSize, Profile::TimeDateStamp, true)
        || !Matches(base + Profile::InitRva, Profile::InitPrefix)
        || !Matches(base + Profile::RuntimeExportRva, Profile::RuntimeExportPrefix)
        || !Matches(base + 0x1ce4d38, Profile::InitUnwind)
        || !Matches(base + 0x1dd1d24, Profile::RuntimeUnwind)
        || !ReadableRange(base + Profile::UnityRva, unityBodySize, base, true)
        || !ReadableRange(base + Profile::RuntimeRva, sizeof(Profile::RuntimePrefix), base, true)
        || std::memcmp(base + Profile::UnityRva, sealed.expectedUnity, unityBodySize) != 0
        || std::memcmp(base + Profile::RuntimeRva, sealed.expectedRuntime, sizeof(sealed.expectedRuntime)) != 0
        || std::memcmp(base + Profile::RuntimeRva + 32, Profile::RuntimePrefix + 32, sizeof(Profile::RuntimePrefix) - 32) != 0
        || !PrimaryLookup(base, Profile::InitRva, 0x2841e8, 0x1ce4d38)
        || !PrimaryLookup(base, Profile::RuntimeRva, 0x2af14e, 0x1dd1d24)
        || !PrimaryLookup(base, Profile::UnityRva, Profile::UnityEndRva, Profile::UnityUnwindRva))
        return RizFirstInstallCoreImageOrPrimaryUnwind;
    DWORD mid{}, unity{}, runtime{};
    if (!Protection(base + Profile::MidRva, 5, base, &mid) || mid != sealed.midProtection
        || !Protection(base + Profile::UnityRva, 32, base, &unity) || unity != sealed.unityProtection
        || !Protection(base + Profile::RuntimeRva, 32, base, &runtime) || runtime != sealed.runtimeProtection)
        return RizFirstInstallCoreProtections;
    if (!RuntimeInvokeIdentity(common)) return RizFirstInstallCoreRuntimeInvoke;
    if (!UnityIdentity(common)) return RizFirstInstallCoreUnity;
    if (!ListenerIdentity(info)) return RizFirstInstallCoreListener;
    if (!MidPublished(common)) return RizFirstInstallCoreMid;
    if (!DetourReaches(static_cast<const unsigned char*>(common.unityTarget), common.unityEntry)
        || !DetourReaches(static_cast<const unsigned char*>(common.runtimeTarget), common.runtimeEntry))
        return RizFirstInstallCoreDetours;
    for (std::size_t site = 0; site != 3; ++site)
        for (const unsigned offset : { 0u, 5u, 7u, 9u })
            if (!TableLookup(info, site, offset)) return RizFirstInstallCoreListenerUnwind;
    return 0;
}
std::uint64_t PublishedIdentityFailure(const RizFirstInstallInfoV7& info)
{
    if (const auto detail = PublishedCoreFailure(info.publication))
        return Failure(RizFirstInstallFailurePublishedCore, detail);
    if (const auto failure = OperationPublishedFailure(info)) return failure;
    if (const auto detail = SinglePublishedIdentityFailure(info)) return Failure(RizFirstInstallFailurePublishedSingleEvent, detail);
    std::uint32_t detail{};
    if (!RizTweenSourceVerifyPublication(true, &detail)) return Failure(RizFirstInstallFailurePublishedTweenSource, detail);
    return 0;
}
std::uint64_t PublishedIdentityFailureGuarded(const RizFirstInstallInfoV7& info)
{
    __try { return PublishedIdentityFailure(info); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        return Failure(RizFirstInstallFailurePublishedVerificationException, GetExceptionCode());
    }
}

std::uint32_t Attempt(const void* originalInit, const wchar_t* absoluteDobbyPath)
{
    if (!originalInit || !absoluteDobbyPath) return 0;
    MEMORY_BASIC_INFORMATION allocation{};
    if (VirtualQuery(originalInit, &allocation, sizeof(allocation)) != sizeof(allocation)
        || allocation.Type != MEM_IMAGE || !IsExecutableProtection(allocation.Protect)) return 0;
    HMODULE module = nullptr;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
        reinterpret_cast<LPCWSTR>(originalInit), &module) || module != allocation.AllocationBase) return 0;
    rootedGame = module;
    wchar_t gamePath[32768]{}, dobbyPath[32768]{};
    if (!ModulePath(module, gamePath) || !AbsolutePath(absoluteDobbyPath, dobbyPath)) return 0;
    const auto* basename = std::wcsrchr(gamePath, L'\\');
    if (!basename || _wcsicmp(basename + 1, L"GameAssembly.dll") != 0) return 0;
    FilePin gameFile, dobbyFile;
    if (!PinAndHash(gamePath, Profile::GameSha256, gameFile) || !FixedGame(module, originalInit)
        || !PinAndHash(dobbyPath, Profile::DobbySha256, dobbyFile) || !StillPreparing()) return 0;
    InterlockedExchange(&gameValidated, 1);
    rootedDobby = LoadLibraryExW(dobbyPath, nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    PrepareFn prepare{}; CommitFn commit{};
    if (!rootedDobby || !FixedDobby(rootedDobby, dobbyPath, prepare, commit)) return 0;
    auto* base = reinterpret_cast<unsigned char*>(module);
    auto* unityTarget = base + Profile::UnityRva;
    auto* runtimeTarget = base + Profile::RuntimeRva;
    const auto* midEntry = RizEventMidContext_GetEntry();
    auto* unityEntry = RizVoidContext_GetEntry(reservedVoidSlot);
    RizRuntimeInvokeInfoV1 runtime{ 1, sizeof(RizRuntimeInvokeInfoV1) };
    if (RizRuntimeInvokeContext_GetInfo(&runtime) != 2 || runtime.unknown != 0 || runtime.attached != 0
        || !runtime.entry || !midEntry || !unityEntry || !StillPreparing()) return 0;
    RizFirstInstallInfoV7 receipt{};
    auto& identity = receipt.publication;
    identity.common.abiVersion = 1; identity.common.byteSize = sizeof(RizFirstInstallInfoV1);
    identity.common.voidSlot = reservedVoidSlot; identity.common.imageSize = Profile::ImageSize;
    identity.common.init = originalInit;
    identity.common.moduleBase = reinterpret_cast<std::uint64_t>(module);
    // Every operation's full source window/unwind/OS lookup and empty permanent
    // slot is validated before the first Dobby preparation or source write.
    if (!PreflightOperations(receipt)) return 0;
    StorePreparedIdentityV7(receipt);
    // Capture every byte of the original Unity body before DobbyPrepare, which
    // is required to be nonpublishing. All original-byte evidence is bound to
    // the fixed SHA/profile already validated under the pinned input handle.
    if (!CaptureOriginal(base)
        || !PlaybackAttestation::Begin(module, rootedSelf, rootedDobby, gameFile.file, dobbyFile.file)) return 0;
    void* unityOriginal = nullptr; void* runtimeOriginal = nullptr;
    const auto unityPrepared = prepare(unityTarget, unityEntry, &unityOriginal);
    if (!FixedGameGuarded(module, originalInit)) { UnexpectedPrepareWrite(); return 2; }
    if (unityPrepared != 0 || !unityOriginal || !StillPreparing()
        || !PlaybackAttestation::Prepared(0, unityTarget, unityEntry, unityOriginal)) return 0;
    const auto runtimePrepared = prepare(runtimeTarget, runtime.entry, &runtimeOriginal);
    if (!FixedGameGuarded(module, originalInit)) { UnexpectedPrepareWrite(); return 2; }
    if (runtimePrepared != 0 || !runtimeOriginal || !StillPreparing()
        || !ReadableRange(unityOriginal, 16, nullptr, true) || !ReadableRange(runtimeOriginal, 16, nullptr, true)
        || !PlaybackAttestation::Prepared(1, runtimeTarget, runtime.entry, runtimeOriginal)) return 0;
    const RizRuntimeInvokeConfigV1 runtimeConfig{ 1, sizeof(RizRuntimeInvokeConfigV1), runtimeTarget,
        reinterpret_cast<RizRuntimeInvokeOriginalV1>(runtimeOriginal) };
    const RizVoidBootstrapConfigV1 unityConfig{ 1, sizeof(RizVoidBootstrapConfigV1), unityTarget,
        reinterpret_cast<RizVoidOriginalV1>(unityOriginal), base + Profile::MidReturnRva };
    if (RizRuntimeInvokeContext_Configure(&runtimeConfig) != 0 || RizVoidBootstrap_Configure(reservedVoidSlot, &unityConfig) != 0) return 0;
    RizVoidBootstrapInfoV1 unity{ 1, sizeof(RizVoidBootstrapInfoV1) };
    if (RizRuntimeInvokeContext_GetInfo(&runtime) != 0 || runtime.unknown != 0 || runtime.attached != 0
        || RizVoidBootstrap_GetInfo(reservedVoidSlot, &unity) != 0 || unity.unknown != 0 || unity.attached != 0
        || !PlaybackAttestation::SetEpoch(0, unity.epoch) || !PlaybackAttestation::SetEpoch(1, runtime.epoch)) return 0;
    identity.common = RizFirstInstallInfoV1{ 1, sizeof(RizFirstInstallInfoV1), RizFirstInstallPreparing, 0,
        reservedVoidSlot, 0, reinterpret_cast<std::uint64_t>(module), Profile::ImageSize, originalInit,
        runtimeTarget, runtimeOriginal, runtime.entry, runtime.epoch, unityTarget, unityOriginal,
        unityEntry, unity.epoch, base + Profile::MidReturnRva, nullptr, 0, 0 };
    if (!PrepareListenerBlock(identity)) return 0;
    // Retain prepared resources/identity even if later prewrite verification
    // refuses publication. No table, stub or partially claimed slot is freed.
    StorePreparedIdentityV7(receipt);
    RizListenerConfigV1 listenerConfig{ 1, sizeof(RizListenerConfigV1), unity.epoch };
    for (std::size_t site = 0; site != 3; ++site) {
        listenerConfig.sourceReturnPc[site] = identity.sourceReturnPc[site];
        listenerConfig.postTransportReturnPc[site] = identity.postTransportReturnPc[site];
    }
    listenerConfig.directTarget[0] = identity.directTarget[0];
    listenerConfig.directTarget[1] = identity.directTarget[1];
    if (RizListenerContext_Configure(reservedVoidSlot, &listenerConfig) != 0) return 0;
    RizListenerInfoV1 listener{ 1, sizeof(RizListenerInfoV1) };
    if (RizListenerContext_GetInfo(reservedVoidSlot, &listener) != 0 || listener.unknown != 0
        || listener.attached != 0 || listener.parentEpoch != unity.epoch || listener.epoch == 0) return 0;
    identity.listenerEpoch = listener.epoch;
    StorePreparedIdentityV7(receipt);
    if (!VerifyPrewriteTables(identity)) {
        StorePreparedIdentityV7(receipt); return 0;
    }
    StorePreparedIdentityV7(receipt);
    if (!PrepareOperations(receipt, prepare) || !PrepareSingleEvent(receipt)
        || !RizTweenSourcePreparePublication(base) || !PlaybackAttestation::SealPrepared()) return StillPreparing() ? 0u : 2u;
    // All fixed bytes, metadata, digests, original trampolines, and permanent
    // identities are checked before the first game instruction may be written.
    if (!DependencyIdentity(identity.common) || !ListenerIdentity(identity)
        || !FixedGame(module, originalInit) || !BlockIdentity(identity) || !OperationOriginalIdentity(base) || !StillPreparing()) return 0;
    if (!PatchMid(base + Profile::MidRva, identity.common.relay)
        || !PatchListeners(identity)
        || !CommitDetour(commit, unityTarget, unityEntry, Profile::UnityPrefix)
        || !CommitDetour(commit, runtimeTarget, runtime.entry, Profile::RuntimePrefix)
        || !CommitOperations(receipt, commit)
        || !CommitSingleEvent(receipt)
        || !RizTweenSourceCommitPublication(WriteSource)
        || !DependencyIdentity(identity.common) || !ListenerIdentity(identity) || !MidPublished(identity.common)
        || !StillPreparing()) { MarkUnknown(Failure(RizFirstInstallFailureInstallCommit)); return 2; }
    // Extend the complete operation-body seal with each actual source patch.
    // Only the enclosing pre-init transaction may install these relays.
    for (std::uint32_t site = 0; site != RizTweenPublishedSourceCount; ++site) {
        const void* source{}; const unsigned char* bytes{}; std::size_t count{}; bool matched = false;
        if (!RizTweenSourceGetPublishedPatch(site, &source, &bytes, &count)) {
            MarkUnknown(Failure(RizFirstInstallFailureInstallPublication, 1)); return 2;
        }
        for (const auto& row : Profile::Operations) {
            const auto* start = base + row.rva; const auto* pointer = static_cast<const unsigned char*>(source);
            if (pointer >= start && pointer < start + row.originalSize && count <= static_cast<std::size_t>(start + row.originalSize - pointer)) {
                std::memcpy(sealed.expectedOperations[row.id] + (pointer - start), bytes, count); matched = true; break;
            }
        }
        if (!matched) { MarkUnknown(Failure(RizFirstInstallFailureInstallPublication, 2)); return 2; }
    }
    // Dobby owns the exact detour encoding. Seal its first 32 bytes only after
    // successful commit plus reachability/prefix-tail/protection verification.
    std::memcpy(sealed.expectedUnity, unityTarget, 32);
    std::memcpy(sealed.expectedRuntime, runtimeTarget, sizeof(sealed.expectedRuntime));
    sealed.published = true;
    AcquireSRWLockShared(&infoLock); identity.common.writesStarted = publishedInfo.writesStarted; ReleaseSRWLockShared(&infoLock);
    StorePreparedIdentityV7(receipt);
    if (const auto failure = PublishedIdentityFailure(receipt)) { MarkUnknown(failure); return 2; }
    if (!StillPreparing()) { MarkUnknown(Failure(RizFirstInstallFailureInstallPublication, 4)); return 2; }
    // Neighbor bytes and the actual native CALL return stay unchanged.
    const auto* window = base + Profile::MidRva - 11;
    if (std::memcmp(window, Profile::MidWindow, 11) != 0
        || std::memcmp(base + Profile::MidReturnRva, Profile::MidWindow + 16, sizeof(Profile::MidWindow) - 16) != 0) {
        MarkUnknown(Failure(RizFirstInstallFailureInstallPublication, 3)); return 2;
    }
    if (!PlaybackAttestation::Publish()) { MarkUnknown(Failure(RizFirstInstallFailurePlaybackAttestation, 2)); return 2; }
    AcquireSRWLockExclusive(&infoLock);
    const auto ready = StillPreparing() && publishedInfo.writesStarted == Profile::ExpectedWrites;
    if (ready) { publishedInfo.phase = RizFirstInstallReady; InterlockedExchange(&phase, RizFirstInstallReady); }
    ReleaseSRWLockExclusive(&infoLock);
    if (!ready) { MarkUnknown(Failure(RizFirstInstallFailureInstallPublication, 4)); return 2; }
    return 1;
}
std::uint32_t AttemptGuarded(const void* originalInit, const wchar_t* absoluteDobbyPath)
{
    __try { return Attempt(originalInit, absoluteDobbyPath); }
    __except (EXCEPTION_EXECUTE_HANDLER) {
        // No original game call is inside this exception boundary. Whether an
        // instruction write was attempted determines whether startup may run.
        if (InterlockedCompareExchange(&gameValidated, 0, 0) != 0
            && !FixedGameGuarded(rootedGame, originalInit)) {
            UnexpectedPrepareWrite(); return 2;
        }
        DisableIfUnwritten(Failure(RizFirstInstallFailureInstallException, GetExceptionCode()));
        return KnownDisabledNoWrite() ? 0u : 2u;
    }
}
}

RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_BeforeIl2CppInit(const void* originalInit, const wchar_t* absoluteDobbyPath)
{
    if (InterlockedCompareExchange(&attemptClaimed, 1, 0) != 0) {
        if (KnownDisabledNoWrite()) return 0;
        MarkUnknown(Failure(RizFirstInstallFailurePreinitReentry)); return 2;
    }
    AcquireSRWLockExclusive(&infoLock);
    if (FirstFailure() == 0) {
        publishedInfo.phase = RizFirstInstallPreparing; InterlockedExchange(&phase, RizFirstInstallPreparing);
    }
    ReleaseSRWLockExclusive(&infoLock);
    if (!StillPreparing()) return 2;
    if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
        reinterpret_cast<LPCWSTR>(&RizFirstInstall_BeforeIl2CppInit), &rootedSelf)) {
        DisableIfUnwritten();
        return KnownDisabledNoWrite() ? 0u : 2u;
    }
    const auto result = AttemptGuarded(originalInit, absoluteDobbyPath);
    if (result == 0) DisableIfUnwritten();
    if (InterlockedCompareExchange(&phase, 0, 0) == RizFirstInstallUnknown
        || FirstFailure() != 0) return 2;
    return result;
}

RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_GetInfoV7(RizFirstInstallInfoV7* info)
{
    if (!info || info->publication.common.abiVersion != 7 || info->publication.common.byteSize != sizeof(*info)
        || info->publication.common.reserved0 != 0 || info->publication.common.reserved1 != 0
        || info->publication.reserved2 != 0 || info->reserved != 0 || info->singleEvent.reserved != 0) return 1;
    RizFirstInstallInfoV7 snapshot{};
    AcquireSRWLockShared(&infoLock); CopyV7Locked(&snapshot); ReleaseSRWLockShared(&infoLock);
    if (snapshot.publication.common.phase == RizFirstInstallReady) {
        if (const auto failure = PublishedIdentityFailureGuarded(snapshot)) MarkUnknown(failure);
    }
    AcquireSRWLockShared(&infoLock);
    CopyV7Locked(info);
    if (const auto failure = FirstFailure()) {
        info->publication.common.phase = RizFirstInstallUnknown;
        info->publication.common.unknown = 1;
        info->publication.common.reserved1 = failure;
        info->singleEvent.phase = RizFirstInstallUnknown;
        info->singleEvent.unknown = 1;
    }
    ReleaseSRWLockShared(&infoLock);
    return 0;
}

namespace {
SRWLOCK playbackAttestationReadLock = SRWLOCK_INIT;
bool PlaybackAttestationCurrentGuarded()
{
    __try {
        RizFirstInstallInfoV7 snapshot{};
        AcquireSRWLockShared(&infoLock); CopyV7Locked(&snapshot); ReleaseSRWLockShared(&infoLock);
        if (snapshot.publication.common.phase != RizFirstInstallReady || snapshot.publication.common.unknown != 0) return false;
        if (const auto failure = PublishedIdentityFailureGuarded(snapshot)) { MarkUnknown(failure); return false; }
        if (!PlaybackAttestation::Verify()) {
            MarkUnknown(Failure(RizFirstInstallFailurePlaybackAttestation, 1)); return false;
        }
        return InterlockedCompareExchange(&phase, 0, 0) == RizFirstInstallReady
            && FirstFailure() == 0;
    } __except (EXCEPTION_EXECUTE_HANDLER) {
        MarkUnknown(Failure(RizFirstInstallFailurePlaybackVerificationException, GetExceptionCode())); return false;
    }
}
}
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_GetPlaybackAttestationV1(RizFirstInstallPlaybackAttestationV1* info)
{
    if (!info || info->abiVersion != 1 || info->byteSize != sizeof(*info) || info->reserved != 0) return 1;
    AcquireSRWLockExclusive(&playbackAttestationReadLock);
    const auto ready = PlaybackAttestationCurrentGuarded();
    // No reader touches a producer vector while the actual transaction is
    // preparing. The publication lock's Ready transition publishes all facts.
    if (ready || InterlockedCompareExchange(&phase, 0, 0) == RizFirstInstallDisabledNoWrite)
        PlaybackAttestation::Information(*info);
    else { *info = {}; info->abiVersion = 1; info->byteSize = sizeof(*info);
        if (FirstFailure()) { info->state = 4; info->unknown = 1; } }
    ReleaseSRWLockExclusive(&playbackAttestationReadLock);
    return ready ? 0u : 2u;
}
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_ReadPlaybackAttestedRangeV1(
    const void* token, std::uint32_t index, RizFirstInstallPlaybackRangeV1* range)
{
    if (!range || range->abiVersion != 1 || range->byteSize != sizeof(*range) || range->reserved0 || range->reserved1
        || !token || token != &PlaybackAttestation::owner || index >= PlaybackAttestation::WriteCount) return 1;
    AcquireSRWLockExclusive(&playbackAttestationReadLock);
    const auto ready = PlaybackAttestationCurrentGuarded();
    if (ready) *range = PlaybackAttestation::owner.writes[index];
    ReleaseSRWLockExclusive(&playbackAttestationReadLock);
    return ready ? 0u : 2u;
}

RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_ReadPlaybackAttestedRangesV1(
    const void* token, RizFirstInstallPlaybackRangeV1* ranges, std::uint32_t count)
{
    if (!token || token != &PlaybackAttestation::owner || !ranges || count != PlaybackAttestation::WriteCount) return 1;
    for (std::uint32_t index = 0; index < count; ++index)
        if (ranges[index].abiVersion != 1 || ranges[index].byteSize != sizeof(*ranges)
            || ranges[index].reserved0 || ranges[index].reserved1) return 1;
    AcquireSRWLockExclusive(&playbackAttestationReadLock);
    auto ready = PlaybackAttestationCurrentGuarded();
    if (ready) {
        for (std::uint32_t index = 0; index < count; ++index) ranges[index] = PlaybackAttestation::owner.writes[index];
        ready = PlaybackAttestationCurrentGuarded();
    }
    ReleaseSRWLockExclusive(&playbackAttestationReadLock);
    return ready ? 0u : 2u;
}

#if defined(RIZ_FIRST_INSTALL_ATTESTATION_FIXTURE)
// Owned machine fixture only; no production token/registry can be issued here.
namespace { PlaybackAttestation::Original fixturePlaybackOriginals[PlaybackAttestation::OriginalCount]; }
extern "C" bool __cdecl RizFirstInstallFixture_ProvePlaybackOriginal(unsigned index,
    const void* target, const void* entry, void* raw, RizFirstInstallPlaybackRangeV1* row)
{
    if (index >= PlaybackAttestation::OriginalCount || !row) return false;
    auto& proof = fixturePlaybackOriginals[index];
    if (!PlaybackAttestation::Prove(index, proof, target, entry, raw)) return false;
    proof.epoch = index + 1;
    if (!PlaybackAttestation::Facts(proof.code, proof.length, proof.codeFacts)
        || !PlaybackAttestation::Facts(proof.rawCode, proof.length, proof.rawFacts)
        || !PlaybackAttestation::VerifyOriginal(proof, PlaybackAttestation::Shapes[index], true)) return false;
    *row = {}; row->abiVersion = 1; row->byteSize = sizeof(*row); row->kind = 1; row->id = index;
    row->address = target; row->original = proof.code; row->entry = entry; row->epoch = proof.epoch;
    row->functionTable = proof.block; row->functionTableBase = reinterpret_cast<std::uint64_t>(proof.code);
    row->length = proof.length; row->movedByteCount = proof.moved; row->originalByteCount = proof.length;
    row->functionCount = proof.functions; row->unwindByteCount = proof.unwindBytes;
    row->lookupChecks = proof.lookups; row->boundaryChecks = proof.boundaries; row->continuation = proof.target + proof.moved;
    std::memcpy(row->beforeBytes, proof.raw, proof.length); std::memcpy(row->afterBytes, proof.expected, proof.length);
    return true;
}
extern "C" bool __cdecl RizFirstInstallFixture_VerifyPlaybackOriginal(unsigned index)
{
    return index < PlaybackAttestation::OriginalCount
        && PlaybackAttestation::VerifyOriginal(fixturePlaybackOriginals[index], PlaybackAttestation::Shapes[index], true);
}
#endif

RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_GetTweenSourceInfoV1(RizTweenSourcePublicationV1* info)
{
    if (!info || info->abiVersion != 1 || info->byteSize != sizeof(*info) || info->reserved != 0) return 1;
    RizFirstInstallInfoV7 parent{}; parent.publication.common.abiVersion = 7; parent.publication.common.byteSize = sizeof(parent);
    if (RizFirstInstall_GetInfoV7(&parent) != 0 || parent.publication.common.phase != RizFirstInstallReady
        || parent.publication.common.unknown != 0) return 2;
    if (!RizTweenSourceCopyPublication(info)) return 2;
    return info->prepared == 1 && info->published == 1 && info->unknown == 0 ? 0u : 2u;
}
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_BindTweenSourceMetadata(const RizTweenSourceMetadataV1* metadata)
{
    RizTweenSourcePublicationV1 source{1,sizeof(source)};
    if (RizFirstInstall_GetTweenSourceInfoV1(&source) != 0) return 2;
    // Metadata does not exist at pre-init publication. The late binding checks
    // official types independently and cannot write a source instruction.
    const auto result = RizTweenSourceBindMetadata(metadata);
    source = {1,sizeof(source)};
    if (RizFirstInstall_GetTweenSourceInfoV1(&source) != 0) return 5;
    return result;
}
RIZFI_EXPORT std::uint32_t __cdecl RizFirstInstall_AttachTweenSourceReferencesV1(const RizTweenSourceReferencesObserverV1* observer)
{
    RizTweenSourcePublicationV1 source{1,sizeof(source)};
    if (RizFirstInstall_GetTweenSourceInfoV1(&source) != 0) return 2;
    RizTweenSourceInfoV1 metadata{1,sizeof(metadata)};
    if (RizTweenSourceContext_GetInfo(&metadata) != 0 || metadata.configured != 1 || metadata.metadataBound != 1
        || metadata.unknown != 0 || metadata.epoch != source.epoch) return 2;
    const auto result = RizTweenSourceReferencesAttach(source.epoch, observer);
    source = {1,sizeof(source)};
    if (RizFirstInstall_GetTweenSourceInfoV1(&source) != 0) return 5;
    return result;
}
