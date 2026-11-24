#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"

// This host calls only the eight descriptor APIs. It never calls a typed entry,
// publishes a hook, attaches an observer, or invokes any configured original.
namespace {
constexpr std::uint32_t KindCount = 4, SlotCount = 32;
constexpr std::size_t EntryCount = KindCount * SlotCount;
using Address = std::uintptr_t;
volatile LONG witnessCalls = 0;

__declspec(noinline) std::uint8_t __cdecl WitnessStart(void*, void*, void*, void*)
{ InterlockedIncrement(&witnessCalls); return 0; }
__declspec(noinline) std::uint32_t __cdecl WitnessExecute(void*, void*)
{ InterlockedIncrement(&witnessCalls); return 0; }
__declspec(noinline) std::uint8_t __cdecl WitnessRelease(void*)
{ InterlockedIncrement(&witnessCalls); return 0; }
__declspec(noinline) void __cdecl WitnessDetach(void*)
{ InterlockedIncrement(&witnessCalls); }
__declspec(noinline) std::uint8_t __cdecl OtherWitnessStart(void*, void*, void*, void*)
{ InterlockedIncrement(&witnessCalls); return 1; }
std::uintptr_t __cdecl WitnessEnter(const RizEngineNodeFrameV1*, void*)
{ InterlockedIncrement(&witnessCalls); return 0; }
void __cdecl WitnessExit(const RizEngineNodeFrameV1*, std::uintptr_t,
    const RizEngineNodeCompletionV1*, void*)
{ InterlockedIncrement(&witnessCalls); }

void* Witness(std::uint32_t kind)
{
    switch (kind) {
    case 0: return reinterpret_cast<void*>(&WitnessStart);
    case 1: return reinterpret_cast<void*>(&WitnessExecute);
    case 2: return reinterpret_cast<void*>(&WitnessRelease);
    default: return reinterpret_cast<void*>(&WitnessDetach);
    }
}
struct Api {
    decltype(&RizEngineNodeContext_GetAbiVersion) getAbi{};
    decltype(&RizEngineNodeContext_GetLimit) getLimit{};
    decltype(&RizEngineNodeContext_GetFrameSize) getFrameSize{};
    decltype(&RizEngineNodeContext_GetCompletionSize) getCompletionSize{};
    decltype(&RizEngineNodeContext_GetEntry) getEntry{};
    decltype(&RizEngineNodeContext_Configure) configure{};
    decltype(&RizEngineNodeContext_Attach) attach{};
    decltype(&RizEngineNodeContext_GetInfo) getInfo{};
};
struct Memory {
    Address base{}, allocationBase{};
    std::uint64_t regionSize{};
    DWORD state{}, type{}, protect{}, allocationProtect{}, error{};
    bool queried{}, exactModule{}, rx{}, readOnly{};
};
struct Info {
    std::uint32_t status{};
    RizEngineNodeInfoV1 value{};
};
struct Entry {
    std::uint32_t kind{}, slot{};
    Address entry{}, entryRva{};
    Memory memory{};
    Info initialInfo{}, afterInfo{};
};
struct Export {
    std::string name;
    Address address{}, rva{};
    Memory memory{};
};
struct TableSlot {
    std::uint32_t slot{};
    Address address{}, pointer{}, pointerRva{};
};
struct Table {
    std::uint32_t kind{}, sectionCharacteristics{};
    Address address{}, rva{};
    std::string section;
    Memory memory{};
    std::vector<Address> candidateRvas;
    std::vector<TableSlot> slots;
};
struct Rejection {
    std::uint32_t candidateKind{}, candidateSlot{}, targetKind{}, targetSlot{}, configureResult{};
    std::string field;
    Address target{}, original{};
    Info infoAfter{};
};
struct Invalid {
    std::uint32_t kind{}, slot{}, configureResult{}, attachResult{};
    Address entry{};
    Info info{};
};
struct Failure {
    std::string name;
    std::uint32_t kind{}, slot{};
};
struct FileIdentity {
    DWORD volumeSerial{}, indexHigh{}, indexLow{}, error{};
    bool available{};
};
struct Report {
    std::wstring kernelPath, outputPath, actualPath;
    HMODULE module{};
    Address mappedHeaderImageBase{};
    std::uint32_t imageSize{}, machine{}, entryPointRva{};
    std::uint32_t abi{}, limit{}, frameSize{}, completionSize{}, sehCode{}, apiLastError{};
    std::uint64_t checks{};
    FileIdentity requestedIdentity{}, loadedIdentity{};
    std::vector<Export> exports;
    std::vector<Entry> entries;
    std::vector<Table> tables;
    std::vector<Rejection> rejections;
    std::vector<Invalid> invalidRequests;
    std::vector<Failure> failures;
    std::string error;
    bool sameFile{}, finished{};
};

std::string Utf8(const std::wstring& text)
{
    if (text.empty()) return {};
    const auto length = static_cast<int>(text.size());
    const auto bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS,
        text.data(), length, nullptr, 0, nullptr, nullptr);
    if (bytes <= 0) throw std::runtime_error("UTF-8 conversion failed");
    std::string result(static_cast<std::size_t>(bytes), '\0');
    if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), length,
        result.data(), bytes, nullptr, nullptr) != bytes)
        throw std::runtime_error("UTF-8 conversion failed");
    return result;
}
std::string JsonString(const std::string& text)
{
    constexpr char digits[] = "0123456789abcdef";
    std::string result = "\"";
    for (const unsigned char value : text) {
        if (value == '\"' || value == '\\') {
            result += '\\'; result += static_cast<char>(value);
        } else if (value < 0x20) {
            result += "\\u00"; result += digits[value >> 4]; result += digits[value & 15];
        } else result += static_cast<char>(value);
    }
    result += '\"'; return result;
}
const char* Boolean(bool value) { return value ? "true" : "false"; }
void Check(Report& report, bool passed, const char* name,
    std::uint32_t kind = 0, std::uint32_t slot = 0)
{
    ++report.checks;
    if (!passed) report.failures.push_back({name, kind, slot});
}
Memory Query(Address address, HMODULE expectedModule)
{
    Memory result{}; MEMORY_BASIC_INFORMATION information{};
    if (VirtualQuery(reinterpret_cast<void*>(address), &information, sizeof(information))
        != sizeof(information)) {
        result.error = GetLastError(); return result;
    }
    result.queried = true;
    result.base = reinterpret_cast<Address>(information.BaseAddress);
    result.allocationBase = reinterpret_cast<Address>(information.AllocationBase);
    result.regionSize = static_cast<std::uint64_t>(information.RegionSize);
    result.state = information.State; result.type = information.Type;
    result.protect = information.Protect; result.allocationProtect = information.AllocationProtect;
    HMODULE actual{};
    result.exactModule = result.allocationBase == reinterpret_cast<Address>(expectedModule)
        && GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS
            | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(address), &actual) && actual == expectedModule;
    const auto protection = information.Protect & 0xffu;
    const bool plain = (information.Protect & (PAGE_GUARD | PAGE_NOCACHE | PAGE_WRITECOMBINE)) == 0;
    result.rx = result.exactModule && information.State == MEM_COMMIT
        && information.Type == MEM_IMAGE && plain && protection == PAGE_EXECUTE_READ;
    result.readOnly = result.exactModule && information.State == MEM_COMMIT
        && information.Type == MEM_IMAGE && plain && protection == PAGE_READONLY;
    return result;
}
bool ReadOnlyRange(Address begin, std::size_t size, HMODULE module)
{
    if (size == 0 || begin > (std::numeric_limits<Address>::max)() - size) return false;
    const auto end = begin + size;
    while (begin < end) {
        const auto memory = Query(begin, module);
        if (!memory.readOnly || memory.regionSize == 0
            || memory.base > (std::numeric_limits<Address>::max)() - memory.regionSize) return false;
        const auto regionEnd = memory.base + static_cast<Address>(memory.regionSize);
        if (regionEnd <= begin) return false;
        begin = regionEnd < end ? regionEnd : end;
    }
    return true;
}
FileIdentity Identity(const std::wstring& path)
{
    FileIdentity result{};
    const auto handle = CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES,
        FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) { result.error = GetLastError(); return result; }
    BY_HANDLE_FILE_INFORMATION info{};
    if (GetFileInformationByHandle(handle, &info)) {
        result.available = true; result.volumeSerial = info.dwVolumeSerialNumber;
        result.indexHigh = info.nFileIndexHigh; result.indexLow = info.nFileIndexLow;
    } else result.error = GetLastError();
    CloseHandle(handle); return result;
}
template<class Function> Function Resolve(Report& report, const char* name)
{
    const auto raw = GetProcAddress(report.module, name);
    if (!raw) { report.apiLastError = GetLastError(); throw std::runtime_error(name); }
    Function result{}; static_assert(sizeof(result) == sizeof(raw));
    std::memcpy(&result, &raw, sizeof(result));
    const auto address = reinterpret_cast<Address>(raw);
    const auto base = reinterpret_cast<Address>(report.module);
    const auto memory = Query(address, report.module);
    report.exports.push_back({name, address, address - base, memory});
    Check(report, address >= base && address - base < report.imageSize && memory.rx,
        "export-is-RX-in-exact-module");
    if (!memory.rx) throw std::runtime_error("API export is not RX in exact kernel module");
    return result;
}
Info ReadInfo(const Api& api, std::uint32_t kind, std::uint32_t slot)
{
    Info result{};
    // Deliberately poison identity and counters: GetInfo must supply its actual
    // kind/slot/entry even for an unconfigured slot, not echo caller identity.
    std::memset(&result.value, 0xa5, sizeof(result.value));
    result.value.abiVersion = 1; result.value.byteSize = sizeof(result.value);
    result.status = api.getInfo(kind, slot, &result.value); return result;
}
bool Fresh(const Info& info, std::uint32_t kind, std::uint32_t slot, Address entry)
{
    const auto& value = info.value;
    return info.status == 2 && value.abiVersion == 1 && value.byteSize == sizeof(value)
        && value.kind == kind && value.slot == slot
        && reinterpret_cast<Address>(value.entry) == entry
        && !value.attached && !value.unknown && !value.target && !value.original && !value.epoch
        && !value.entered && !value.exited && !value.inFlight
        && !value.enterFaults && !value.exitFaults && !value.snapshotFaults && !value.outputFaults;
}
void LocateTables(Report& report, const IMAGE_NT_HEADERS64* nt)
{
    const auto base = reinterpret_cast<Address>(report.module);
    const auto* sections = IMAGE_FIRST_SECTION(nt);
    for (std::uint32_t kind = 0; kind < KindCount; ++kind) {
        Table table{}; table.kind = kind;
        std::array<Address, SlotCount> expected{};
        for (std::uint32_t slot = 0; slot < SlotCount; ++slot)
            expected[slot] = report.entries[static_cast<std::size_t>(kind) * SlotCount + slot].entry;
        for (WORD index = 0; index < nt->FileHeader.NumberOfSections; ++index) {
            const auto& section = sections[index];
            const auto flags = section.Characteristics;
            if (!(flags & IMAGE_SCN_MEM_READ) || (flags & (IMAGE_SCN_MEM_WRITE | IMAGE_SCN_MEM_EXECUTE))) continue;
            const auto sectionRva = static_cast<std::size_t>(section.VirtualAddress);
            const auto size = static_cast<std::size_t>(section.Misc.VirtualSize);
            if (sectionRva > report.imageSize || size > report.imageSize - sectionRva
                || size < sizeof(expected)) continue;
            const auto begin = base + sectionRva;
            if (!ReadOnlyRange(begin, size, report.module)) continue;
            const auto aligned = (begin + sizeof(Address) - 1) & ~(static_cast<Address>(sizeof(Address)) - 1);
            const auto end = begin + size - sizeof(expected);
            for (auto address = aligned; address <= end; address += sizeof(Address)) {
                if (std::memcmp(reinterpret_cast<const void*>(address), expected.data(), sizeof(expected)) != 0) continue;
                table.candidateRvas.push_back(address - base);
                if (table.candidateRvas.size() != 1) continue;
                table.address = address; table.rva = address - base;
                char name[IMAGE_SIZEOF_SHORT_NAME + 1]{};
                std::memcpy(name, section.Name, IMAGE_SIZEOF_SHORT_NAME);
                table.section = name; table.sectionCharacteristics = flags;
                table.memory = Query(address, report.module);
                for (std::uint32_t slot = 0; slot < SlotCount; ++slot) {
                    const auto slotAddress = address + static_cast<Address>(slot) * sizeof(Address);
                    Address pointer{};
                    std::memcpy(&pointer, reinterpret_cast<const void*>(slotAddress), sizeof(pointer));
                    table.slots.push_back({slot, slotAddress, pointer, pointer - base});
                }
            }
        }
        Check(report, table.candidateRvas.size() == 1, "one-actual-readonly-typed-pointer-table", kind);
        Check(report, table.slots.size() == SlotCount, "actual-table-has-32-pointers", kind);
        report.tables.push_back(std::move(table));
    }
}
void Probe(Report& report)
{
    report.requestedIdentity = Identity(report.kernelPath);
    if (!report.requestedIdentity.available) throw std::runtime_error("Cannot identify requested kernel file");
    report.module = LoadLibraryExW(report.kernelPath.c_str(), nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!report.module) { report.apiLastError = GetLastError(); throw std::runtime_error("LoadLibraryExW kernel failed"); }
    // Keep the loaded module until process exit. No configured descriptor,
    // observer, or entry is ever made executable by this fixture.
    std::vector<wchar_t> actual(32768);
    const auto actualLength = GetModuleFileNameW(report.module, actual.data(), static_cast<DWORD>(actual.size()));
    if (!actualLength || actualLength >= actual.size()) throw std::runtime_error("Cannot identify loaded kernel path");
    report.actualPath.assign(actual.data(), actualLength);
    report.loadedIdentity = Identity(report.actualPath);
    const auto& requested = report.requestedIdentity; const auto& loaded = report.loadedIdentity;
    report.sameFile = loaded.available && requested.volumeSerial == loaded.volumeSerial
        && requested.indexHigh == loaded.indexHigh && requested.indexLow == loaded.indexLow;
    Check(report, report.sameFile, "loaded-module-is-requested-file");
    if (!report.sameFile) throw std::runtime_error("Loaded module does not identify requested kernel file");
    const auto base = reinterpret_cast<Address>(report.module);
    const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew < static_cast<LONG>(sizeof(*dos))
        || dos->e_lfanew > 0x10000) throw std::runtime_error("Invalid loaded DOS header");
    const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + static_cast<Address>(dos->e_lfanew));
    if (nt->Signature != IMAGE_NT_SIGNATURE || nt->FileHeader.Machine != IMAGE_FILE_MACHINE_AMD64
        || nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR64_MAGIC
        || nt->FileHeader.SizeOfOptionalHeader != sizeof(IMAGE_OPTIONAL_HEADER64))
        throw std::runtime_error("Kernel must be AMD64 PE32+");
    // Read from the mapped OptionalHeader. This is not the disk preferred base;
    // the parent verifier independently reads that value from its held PE file.
    report.mappedHeaderImageBase = static_cast<Address>(nt->OptionalHeader.ImageBase);
    report.imageSize = nt->OptionalHeader.SizeOfImage; report.machine = nt->FileHeader.Machine;
    report.entryPointRva = nt->OptionalHeader.AddressOfEntryPoint;
    const auto sectionsEnd = reinterpret_cast<Address>(IMAGE_FIRST_SECTION(nt))
        + static_cast<Address>(nt->FileHeader.NumberOfSections) * sizeof(IMAGE_SECTION_HEADER);
    if (sectionsEnd < base || sectionsEnd - base > nt->OptionalHeader.SizeOfHeaders
        || nt->OptionalHeader.SizeOfHeaders > report.imageSize)
        throw std::runtime_error("Loaded section headers exceed image headers");
    Api api{};
    api.getAbi = Resolve<decltype(api.getAbi)>(report, "RizEngineNodeContext_GetAbiVersion");
    api.getLimit = Resolve<decltype(api.getLimit)>(report, "RizEngineNodeContext_GetLimit");
    api.getFrameSize = Resolve<decltype(api.getFrameSize)>(report, "RizEngineNodeContext_GetFrameSize");
    api.getCompletionSize = Resolve<decltype(api.getCompletionSize)>(report, "RizEngineNodeContext_GetCompletionSize");
    api.getEntry = Resolve<decltype(api.getEntry)>(report, "RizEngineNodeContext_GetEntry");
    api.configure = Resolve<decltype(api.configure)>(report, "RizEngineNodeContext_Configure");
    api.attach = Resolve<decltype(api.attach)>(report, "RizEngineNodeContext_Attach");
    api.getInfo = Resolve<decltype(api.getInfo)>(report, "RizEngineNodeContext_GetInfo");
    report.abi = api.getAbi(); report.limit = api.getLimit();
    report.frameSize = api.getFrameSize(); report.completionSize = api.getCompletionSize();
    Check(report, report.abi == 2, "transport-ABI-2");
    Check(report, report.limit == SlotCount, "limit-32");
    Check(report, report.frameSize == sizeof(RizEngineNodeFrameV1), "frame-112");
    Check(report, report.completionSize == sizeof(RizEngineNodeCompletionV1), "completion-32");
    if (report.abi != 2 || report.limit != SlotCount || report.frameSize != 112 || report.completionSize != 32)
        throw std::runtime_error("Kernel transport ABI mismatch; no descriptor operations performed");
    report.entries.reserve(EntryCount);
    for (std::uint32_t kind = 0; kind < KindCount; ++kind) {
        for (std::uint32_t slot = 0; slot < SlotCount; ++slot) {
            Entry entry{}; entry.kind = kind; entry.slot = slot;
            entry.entry = reinterpret_cast<Address>(api.getEntry(kind, slot));
            entry.entryRva = entry.entry >= base ? entry.entry - base : 0;
            entry.memory = Query(entry.entry, report.module);
            Check(report, entry.entry != 0, "entry-nonzero", kind, slot);
            Check(report, entry.entry >= base && entry.entry - base < report.imageSize,
                "entry-in-image-bounds", kind, slot);
            Check(report, entry.memory.rx, "entry-RX-MEM_IMAGE-exact-module", kind, slot);
            bool unique = true;
            for (const auto& earlier : report.entries) if (earlier.entry == entry.entry) unique = false;
            Check(report, unique, "entry-unique-among-all-128", kind, slot);
            entry.initialInfo = ReadInfo(api, kind, slot);
            Check(report, Fresh(entry.initialInfo, kind, slot, entry.entry), "initial-Fresh-real-identity", kind, slot);
            report.entries.push_back(entry);
        }
    }
    if (!report.failures.empty()) throw std::runtime_error("Entry identity/ABI prerequisite failed");
    LocateTables(report, nt);
    // Each candidate is rejected in a *different* kind, testing IsEntry across
    // all four typed tables. Every destination slot is exercised twice and is
    // read back immediately; no successful Configure or entry call is expected.
    for (const auto& candidate : report.entries) {
        const auto destinationKind = (candidate.kind + 1u) % KindCount;
        const auto destinationSlot = candidate.slot;
        const auto destinationIndex = static_cast<std::size_t>(destinationKind) * SlotCount + destinationSlot;
        for (std::uint32_t field = 0; field < 2; ++field) {
            Rejection rejection{};
            rejection.candidateKind = candidate.kind; rejection.candidateSlot = candidate.slot;
            rejection.targetKind = destinationKind; rejection.targetSlot = destinationSlot;
            rejection.field = field == 0 ? "target" : "original";
            RizEngineNodeConfigV1 config{1, sizeof(config), Witness(destinationKind), Witness(destinationKind)};
            if (field == 0) config.target = reinterpret_cast<void*>(candidate.entry);
            else config.original = reinterpret_cast<void*>(candidate.entry);
            rejection.target = reinterpret_cast<Address>(config.target);
            rejection.original = reinterpret_cast<Address>(config.original);
            rejection.configureResult = api.configure(destinationKind, destinationSlot, &config);
            rejection.infoAfter = ReadInfo(api, destinationKind, destinationSlot);
            Check(report, rejection.target != rejection.original && rejection.target && rejection.original,
                "entry-rejection-nonnull-distinct-inputs", destinationKind, destinationSlot);
            Check(report, rejection.configureResult == 1, "all-entry-pointers-rejected-before-claim", destinationKind, destinationSlot);
            Check(report, Fresh(rejection.infoAfter, destinationKind, destinationSlot,
                report.entries[destinationIndex].entry), "rejected-entry-keeps-destination-Fresh", destinationKind, destinationSlot);
            report.rejections.push_back(std::move(rejection));
        }
    }
    const std::array<std::array<std::uint32_t, 2>, 12> invalid{{
        {{4, 0}}, {{4, 31}}, {{0xffffffffu, 0}}, {{0xffffffffu, 31}},
        {{0, 32}}, {{1, 32}}, {{2, 32}}, {{3, 32}},
        {{0, 0xffffffffu}}, {{1, 0xffffffffu}}, {{2, 0xffffffffu}}, {{3, 0xffffffffu}}
    }};
    const RizEngineNodeObserverV1 observer{1, sizeof(observer), &WitnessEnter, &WitnessExit, nullptr};
    const RizEngineNodeConfigV1 config{1, sizeof(config), reinterpret_cast<void*>(&WitnessStart),
        reinterpret_cast<void*>(&OtherWitnessStart)};
    for (const auto& coordinates : invalid) {
        Invalid request{}; request.kind = coordinates[0]; request.slot = coordinates[1];
        request.entry = reinterpret_cast<Address>(api.getEntry(request.kind, request.slot));
        request.info = ReadInfo(api, request.kind, request.slot);
        request.configureResult = api.configure(request.kind, request.slot, &config);
        request.attachResult = api.attach(request.kind, request.slot, 1, &observer);
        Check(report, request.entry == 0, "invalid-kind-slot-GetEntry-rejects", request.kind, request.slot);
        Check(report, request.info.status == 1, "invalid-kind-slot-GetInfo-rejects", request.kind, request.slot);
        Check(report, request.configureResult == 1, "invalid-kind-slot-Configure-rejects", request.kind, request.slot);
        Check(report, request.attachResult == 1, "invalid-kind-slot-Attach-rejects", request.kind, request.slot);
        report.invalidRequests.push_back(request);
    }
    for (auto& entry : report.entries) {
        entry.afterInfo = ReadInfo(api, entry.kind, entry.slot);
        Check(report, Fresh(entry.afterInfo, entry.kind, entry.slot, entry.entry), "final-all-128-still-Fresh", entry.kind, entry.slot);
    }
    Check(report, InterlockedCompareExchange(&witnessCalls, 0, 0) == 0, "no-witness-original-or-observer-executed");
    report.finished = true;
}
DWORD GuardedProbe(Report* report)
{
    __try { Probe(*report); return 0; }
    __except (GetExceptionCode() == 0xe06d7363u ? EXCEPTION_CONTINUE_SEARCH : EXCEPTION_EXECUTE_HANDLER)
    { return GetExceptionCode(); }
}
void WriteMemory(std::ostream& out, const Memory& memory)
{
    out << "{\"queried\":" << Boolean(memory.queried) << ",\"base\":" << memory.base
        << ",\"allocationBase\":" << memory.allocationBase << ",\"regionSize\":" << memory.regionSize
        << ",\"state\":" << memory.state << ",\"type\":" << memory.type
        << ",\"protect\":" << memory.protect << ",\"allocationProtect\":" << memory.allocationProtect
        << ",\"exactModule\":" << Boolean(memory.exactModule) << ",\"rx\":" << Boolean(memory.rx)
        << ",\"readOnly\":" << Boolean(memory.readOnly) << ",\"error\":" << memory.error << '}';
}
void WriteIdentity(std::ostream& out, const FileIdentity& identity)
{
    out << "{\"available\":" << Boolean(identity.available) << ",\"volumeSerial\":" << identity.volumeSerial
        << ",\"indexHigh\":" << identity.indexHigh << ",\"indexLow\":" << identity.indexLow
        << ",\"error\":" << identity.error << '}';
}
void WriteInfo(std::ostream& out, const Info& info)
{
    const auto& value = info.value;
    out << "{\"status\":" << info.status << ",\"abiVersion\":" << value.abiVersion
        << ",\"byteSize\":" << value.byteSize << ",\"kind\":" << value.kind << ",\"slot\":" << value.slot
        << ",\"attached\":" << value.attached << ",\"unknown\":" << value.unknown
        << ",\"target\":" << reinterpret_cast<Address>(value.target)
        << ",\"original\":" << reinterpret_cast<Address>(value.original)
        << ",\"entry\":" << reinterpret_cast<Address>(value.entry)
        << ",\"epoch\":" << value.epoch << ",\"entered\":" << value.entered
        << ",\"exited\":" << value.exited << ",\"inFlight\":" << value.inFlight
        << ",\"enterFaults\":" << value.enterFaults << ",\"exitFaults\":" << value.exitFaults
        << ",\"snapshotFaults\":" << value.snapshotFaults << ",\"outputFaults\":" << value.outputFaults << '}';
}
std::string Serialize(const Report& report)
{
    const bool passed = report.finished && report.failures.empty() && report.error.empty() && report.sehCode == 0;
    std::ostringstream out;
    out << "{\"schemaVersion\":1,\"kind\":\"engine-node-kernel-entry-fixture\",\"passed\":" << Boolean(passed)
        << ",\"finished\":" << Boolean(report.finished) << ",\"checks\":" << report.checks
        << ",\"error\":" << JsonString(report.error) << ",\"sehCode\":" << report.sehCode
        << ",\"apiLastError\":" << report.apiLastError
        << ",\"arguments\":{\"kernelPath\":" << JsonString(Utf8(report.kernelPath))
        << ",\"outputPath\":" << JsonString(Utf8(report.outputPath)) << "},\"module\":{\"actualPath\":"
        << JsonString(Utf8(report.actualPath)) << ",\"base\":" << reinterpret_cast<Address>(report.module)
        << ",\"mappedHeaderImageBase\":" << report.mappedHeaderImageBase << ",\"imageSize\":" << report.imageSize
        << ",\"machine\":" << report.machine << ",\"entryPointRva\":" << report.entryPointRva
        << ",\"sameFile\":" << Boolean(report.sameFile) << ",\"requestedIdentity\":";
    WriteIdentity(out, report.requestedIdentity); out << ",\"loadedIdentity\":";
    WriteIdentity(out, report.loadedIdentity);
    out << "},\"abi\":{\"version\":" << report.abi << ",\"limit\":" << report.limit
        << ",\"frameSize\":" << report.frameSize << ",\"completionSize\":" << report.completionSize
        << "},\"witnessCalls\":" << InterlockedCompareExchange(&witnessCalls, 0, 0)
        << ",\"typedEntriesInvoked\":false,\"hooksPublished\":false,\"exports\":[";
    for (std::size_t i = 0; i < report.exports.size(); ++i) {
        if (i) out << ','; const auto& value = report.exports[i];
        out << "{\"name\":" << JsonString(value.name) << ",\"address\":" << value.address
            << ",\"rva\":" << value.rva << ",\"memory\":";
        WriteMemory(out, value.memory); out << '}';
    }
    out << "],\"entries\":[";
    for (std::size_t i = 0; i < report.entries.size(); ++i) {
        if (i) out << ','; const auto& value = report.entries[i];
        out << "{\"kind\":" << value.kind << ",\"slot\":" << value.slot
            << ",\"entry\":" << value.entry << ",\"entryRva\":" << value.entryRva << ",\"memory\":";
        WriteMemory(out, value.memory); out << ",\"initialInfo\":"; WriteInfo(out, value.initialInfo);
        out << ",\"afterInfo\":"; WriteInfo(out, value.afterInfo); out << '}';
    }
    out << "],\"tables\":[";
    for (std::size_t i = 0; i < report.tables.size(); ++i) {
        if (i) out << ','; const auto& value = report.tables[i];
        out << "{\"kind\":" << value.kind << ",\"address\":" << value.address << ",\"rva\":" << value.rva
            << ",\"section\":" << JsonString(value.section) << ",\"sectionCharacteristics\":" << value.sectionCharacteristics
            << ",\"candidateCount\":" << value.candidateRvas.size() << ",\"candidateRvas\":[";
        for (std::size_t n = 0; n < value.candidateRvas.size(); ++n) { if (n) out << ','; out << value.candidateRvas[n]; }
        out << "],\"memory\":"; WriteMemory(out, value.memory); out << ",\"slots\":[";
        for (std::size_t n = 0; n < value.slots.size(); ++n) {
            if (n) out << ','; const auto& slot = value.slots[n];
            out << "{\"slot\":" << slot.slot << ",\"address\":" << slot.address
                << ",\"pointer\":" << slot.pointer << ",\"pointerRva\":" << slot.pointerRva << '}';
        }
        out << "]}";
    }
    out << "],\"rejections\":[";
    for (std::size_t i = 0; i < report.rejections.size(); ++i) {
        if (i) out << ','; const auto& value = report.rejections[i];
        out << "{\"candidateKind\":" << value.candidateKind << ",\"candidateSlot\":" << value.candidateSlot
            << ",\"field\":" << JsonString(value.field) << ",\"targetKind\":" << value.targetKind
            << ",\"targetSlot\":" << value.targetSlot << ",\"target\":" << value.target
            << ",\"original\":" << value.original << ",\"configureResult\":" << value.configureResult << ",\"infoAfter\":";
        WriteInfo(out, value.infoAfter); out << '}';
    }
    out << "],\"invalidRequests\":[";
    for (std::size_t i = 0; i < report.invalidRequests.size(); ++i) {
        if (i) out << ','; const auto& value = report.invalidRequests[i];
        out << "{\"kind\":" << value.kind << ",\"slot\":" << value.slot << ",\"entry\":" << value.entry
            << ",\"configureResult\":" << value.configureResult << ",\"attachResult\":" << value.attachResult << ",\"info\":";
        WriteInfo(out, value.info); out << '}';
    }
    out << "],\"failures\":[";
    for (std::size_t i = 0; i < report.failures.size(); ++i) {
        if (i) out << ','; const auto& value = report.failures[i];
        out << "{\"name\":" << JsonString(value.name) << ",\"kind\":" << value.kind << ",\"slot\":" << value.slot << '}';
    }
    out << "]}\n"; return out.str();
}
bool WriteReport(const std::wstring& path, const std::string& bytes)
{
    if (bytes.size() > (std::numeric_limits<DWORD>::max)()) return false;
    const auto handle = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (handle == INVALID_HANDLE_VALUE) return false;
    DWORD written{};
    const bool success = WriteFile(handle, bytes.data(), static_cast<DWORD>(bytes.size()), &written, nullptr)
        && written == bytes.size();
    CloseHandle(handle); return success;
}
} // namespace

int wmain(int argc, wchar_t** argv)
{
    Report report{};
    try {
        for (int i = 1; i < argc; ++i) {
            const std::wstring argument = argv[i];
            if (argument == L"--kernel" && report.kernelPath.empty() && i + 1 < argc)
                report.kernelPath = argv[++i];
            else if (argument == L"--output" && report.outputPath.empty() && i + 1 < argc)
                report.outputPath = argv[++i];
            else return 2;
        }
        if (report.kernelPath.empty() || report.outputPath.empty()
            || !std::filesystem::path(report.kernelPath).is_absolute()
            || !std::filesystem::path(report.outputPath).is_absolute()) return 2;
        report.sehCode = GuardedProbe(&report);
        if (report.sehCode) report.error = "Native exception in descriptor-only probe";
    } catch (const std::exception& exception) {
        report.error = exception.what();
    } catch (...) {
        report.error = "Unexpected C++ exception in descriptor-only probe";
    }
    try {
        if (report.outputPath.empty() || !WriteReport(report.outputPath, Serialize(report))) return 2;
    } catch (...) { return 2; }
    if (report.sehCode) return 3;
    return report.finished && report.failures.empty() && report.error.empty() ? 0 : 1;
}
