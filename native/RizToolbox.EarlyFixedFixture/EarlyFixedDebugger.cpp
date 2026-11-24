#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <map>
#include <stdexcept>
#include <string>
#include <vector>
#include "EarlyFixedShared.h"

// Test observer only. Creates its own child, never attaches to another process.
// Breakpoints briefly change owned-child TLS/CRT entry/return instructions in
// memory. Main fixture runs remain uninstrumented; no game permission is implied.
namespace {
constexpr DWORD ObserverFlag = 0x80000000u;
constexpr DWORD DebugLoad = 1001, DebugEntry = 1002, DebugReturn = 1003,
    DebugTls = 1004, DebugTlsReturn = 1005, DebugUnload = 1006,
    DebugRetiredUnload = 1007, DebugReturnStackMismatch = 1008;
// Intel SDM Vol.3B 19.2.3/19.2.4/19.3.1.1: local execute enables
// occupy DR7[0,2,4,6], R/W and LEN stay 00; RF is EFLAGS[16].
// Windows SetThreadContext can normalize OS-controlled/reserved bits.
constexpr DWORD ResumeFlag = 1u << 16, TrapFlag = 1u << 8;
constexpr DWORD64 DebugBreakpointStatus = 0xfull, DebugOtherStatus = 0xe000ull;
constexpr DWORD64 DebugStatusSetOnResume = (1ull << 11) | (1ull << 16);
constexpr DWORD64 DebugControlFixedOne = 1ull << 10;
struct FileIdentity { DWORD volumeSerial{}; std::uint64_t fileIndex{}; };
struct Module {
    std::uint32_t id{}, generation{};
    std::uint64_t base{}, bytes{}, instanceId{}, loadSequence{};
    std::string name;
    FileIdentity file{}, expectedFile{};
    bool live{};
};
struct Breakpoint {
    std::uint64_t address{};
    BYTE original{};
    bool active{}, tls{};
    Module module;
};
struct ReturnSite {
    std::uint64_t address{}, stack{};
    DWORD thread{};
    BYTE original{};
    Breakpoint entry;
    bool hardware{};
    DWORD debugSlot{};
};
struct ReturnDiagnostic { std::uint64_t expectedRsp{}, actualRsp{}, dr6{}, dr7{}; DWORD slot{}; };
struct HardwareContext {
    std::uint64_t rip{}, rsp{}, dr6{}, dr7{};
    DWORD eflags{};
    std::array<std::uint64_t, 4> dr{};
};
struct HardwareEvidence {
    HardwareContext before, after;
    std::uint64_t expectedRsp{};
    DWORD slot{};
    bool resumeFlagRequested{};
};
HANDLE process{};
RizEarlySharedV1* shared{};
std::ofstream output;
DWORD childPid{};
std::vector<Breakpoint> entries;
std::vector<ReturnSite> returns;
std::map<DWORD, HANDLE> threads;
std::vector<Module> modules;
std::uint64_t ntdllBase{}, ntdllBytes{};
std::filesystem::path hostDirectory;

void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
void Read(std::uint64_t address, void* destination, std::size_t size)
{
    SIZE_T read{};
    Require(ReadProcessMemory(process, reinterpret_cast<void*>(address), destination, size, &read)
        && read == size, "ReadProcessMemory failed");
}
template<class T> T Read(std::uint64_t address) { T value{}; Read(address, &value, sizeof(value)); return value; }
void WriteByte(std::uint64_t address, BYTE value)
{
    DWORD old{};
    auto pointer = reinterpret_cast<void*>(address);
    Require(VirtualProtectEx(process, pointer, 1, PAGE_EXECUTE_READWRITE, &old) != FALSE,
        "Cannot protect observed instruction");
    SIZE_T written{};
    const bool wrote = WriteProcessMemory(process, pointer, &value, 1, &written) && written == 1;
    DWORD ignored{};
    const bool protectedAgain = VirtualProtectEx(process, pointer, 1, old, &ignored) != FALSE;
    const bool flushed = FlushInstructionCache(process, pointer, 1) != FALSE;
    Require(wrote && protectedAgain && flushed, "Cannot publish/restore observer breakpoint");
}
std::wstring ImagePath(HANDLE file)
{
    Require(file && file != INVALID_HANDLE_VALUE, "Missing debug image file handle");
    std::array<wchar_t, 32768> path{};
    const DWORD length = GetFinalPathNameByHandleW(file, path.data(), static_cast<DWORD>(path.size()), FILE_NAME_NORMALIZED);
    Require(length && length < path.size(), "Cannot identify debug image");
    return std::wstring(path.data(), length);
}
std::wstring Lower(std::wstring value)
{
    for (auto& character : value) if (character >= L'A' && character <= L'Z') character += L'a' - L'A';
    return value;
}
std::uint32_t ModuleId(const std::wstring& name)
{
    if (name == RizEarlyPublisherModuleNameLower) return 1;
    if (name == RizEarlyHelperModuleNameLower) return 2;
    if (name == RizEarlyRecorderModuleNameLower) return 3;
    if (name == RizEarlyProxyModuleNameLower) return 4;
    if (name == RizEarlyTargetsModuleNameLower) return 5;
    return 0;
}
FileIdentity Identify(HANDLE file)
{
    BY_HANDLE_FILE_INFORMATION information{};
    Require(file && file != INVALID_HANDLE_VALUE && GetFileInformationByHandle(file, &information),
        "Cannot read observed dependency file identity");
    return {information.dwVolumeSerialNumber,
        (static_cast<std::uint64_t>(information.nFileIndexHigh) << 32) | information.nFileIndexLow};
}
FileIdentity ExpectedIdentity(const std::wstring& name)
{
    const auto path = hostDirectory / name;
    const auto file = CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES, FILE_SHARE_READ, nullptr,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    Require(file != INVALID_HANDLE_VALUE, "Cannot open expected owned dependency beside the host");
    FileIdentity result{};
    try { result = Identify(file); }
    catch (...) { CloseHandle(file); throw; }
    Require(CloseHandle(file) != FALSE, "Cannot close expected dependency identity handle");
    return result;
}
HardwareContext HardwareSnapshot(const CONTEXT& context)
{
    return {context.Rip, context.Rsp, context.Dr6, context.Dr7, context.EFlags,
        {context.Dr0, context.Dr1, context.Dr2, context.Dr3}};
}
void WriteHardwareContext(const HardwareContext& context)
{
    output << "{\"rip\":" << context.rip << ",\"rsp\":" << context.rsp
        << ",\"eflags\":" << context.eflags << ",\"dr\":[";
    for (std::size_t slot = 0; slot < context.dr.size(); ++slot) {
        if (slot) output << ',';
        output << context.dr[slot];
    }
    output << "],\"dr6\":" << context.dr6 << ",\"dr7\":" << context.dr7 << '}';
}
std::uint64_t Emit(DWORD type, const Module& module, DWORD thread, std::uint64_t target,
    std::uint64_t returnAddress = 0, std::uint64_t returnValue = 0,
    const MEMORY_BASIC_INFORMATION* retiredQuery = nullptr,
    const ReturnDiagnostic* returnDiagnostic = nullptr,
    const HardwareEvidence* hardware = nullptr)
{
    const LONG index = InterlockedIncrement(&shared->header.eventCount) - 1;
    const auto sequence = InterlockedIncrement64(&shared->header.nextSequence);
    Require(index >= 0 && static_cast<std::uint32_t>(index) < RizEarlyEventCapacity,
        "Debugger shared log overflow");
    RizEarlyEventV1 row{};
    row.pid = childPid; row.tid = thread; row.type = type; row.kind = module.id;
    row.flags = ObserverFlag; row.module = module.base; row.target = target;
    row.original = returnAddress; row.value0 = DLL_PROCESS_ATTACH;
    row.value1 = returnValue; row.caller = returnAddress;
    const bool lifetime = RizEarlyKernelProfile
        && (type == DebugLoad || type == DebugUnload || type == DebugRetiredUnload);
    const auto loadSequence = type == DebugLoad ? static_cast<std::uint64_t>(sequence) : module.loadSequence;
    if (lifetime) {
        // Only the new profile's load/unload rows use this identity payload.
        // Initialization/return rows and the entire legacy profile retain the
        // historical argument/return transport. A load is not an init return.
        row.index = module.generation; row.result = module.live ? 1u : 0u;
        row.original = module.bytes; row.entry = module.file.fileIndex;
        row.epoch = module.instanceId; row.value0 = module.file.volumeSerial;
        row.value1 = loadSequence; row.caller = 0;
    }
    if (retiredQuery) {
        Require(type == DebugRetiredUnload && !module.live, "Invalid retired-unload evidence request");
        row.phase = retiredQuery->Type; row.result = retiredQuery->State;
        row.flags |= retiredQuery->Protect;
        row.target = reinterpret_cast<std::uint64_t>(retiredQuery->BaseAddress);
        row.original = static_cast<std::uint64_t>(retiredQuery->RegionSize);
        row.caller = reinterpret_cast<std::uint64_t>(retiredQuery->AllocationBase);
    }
    if (returnDiagnostic) {
        Require(type == DebugReturnStackMismatch, "Invalid return-stack diagnostic request");
        row.index = returnDiagnostic->slot;
        row.original = returnDiagnostic->expectedRsp; row.entry = returnDiagnostic->actualRsp;
        row.epoch = module.instanceId; row.value0 = returnDiagnostic->dr6;
        row.value1 = returnDiagnostic->dr7; row.caller = target;
    }
    if (hardware && !returnDiagnostic) {
        Require(RizEarlyKernelProfile && (type == DebugEntry || type == DebugTls
            || type == DebugReturn || type == DebugTlsReturn), "Invalid hardware entry/return evidence request");
        row.index = hardware->slot; row.result = 1;
        row.entry = module.instanceId; row.epoch = hardware->expectedRsp;
        row.value0 = hardware->before.rsp;
    }
    shared->events[index] = row;
    InterlockedExchange64(&shared->events[index].sequence, sequence);
    output << "{\"sequence\":" << sequence << ",\"type\":" << type
        << ",\"moduleId\":" << module.id << ",\"module\":\"" << module.name
        << "\",\"base\":" << module.base << ",\"pid\":" << childPid
        << ",\"tid\":" << thread << ",\"target\":" << target
        << ",\"returnAddress\":" << returnAddress << ",\"returnRax\":" << returnValue
        << ",\"processAttach\":" << (lifetime || returnDiagnostic ? "false" : "true")
        << ",\"debugInstrumented\":true";
    if (RizEarlyKernelProfile)
        output << ",\"noInitializationRequired\":" << (module.id == 1 || module.id == 2 ? "true" : "false");
    if (lifetime)
        output << ",\"moduleBytes\":" << module.bytes << ",\"instanceId\":" << module.instanceId
            << ",\"generation\":" << module.generation << ",\"loadSequence\":" << loadSequence
            << ",\"live\":" << (module.live ? "true" : "false")
            << ",\"fileVolumeSerial\":" << module.file.volumeSerial << ",\"fileIndex\":" << module.file.fileIndex
            << ",\"expectedFileVolumeSerial\":" << module.expectedFile.volumeSerial
            << ",\"expectedFileIndex\":" << module.expectedFile.fileIndex << ",\"fileIdentityMatched\":true";
    if (retiredQuery)
        output << ",\"retiredUnloadNotification\":true,\"query\":{\"base\":"
            << reinterpret_cast<std::uint64_t>(retiredQuery->BaseAddress)
            << ",\"allocationBase\":" << reinterpret_cast<std::uint64_t>(retiredQuery->AllocationBase)
            << ",\"regionSize\":" << static_cast<std::uint64_t>(retiredQuery->RegionSize)
            << ",\"state\":" << retiredQuery->State << ",\"type\":" << retiredQuery->Type
            << ",\"protect\":" << retiredQuery->Protect << '}';
    if (returnDiagnostic)
        output << ",\"returnStackMismatch\":true,\"expectedRsp\":" << returnDiagnostic->expectedRsp
            << ",\"actualRsp\":" << returnDiagnostic->actualRsp << ",\"debugSlot\":" << returnDiagnostic->slot
            << ",\"dr6\":" << returnDiagnostic->dr6 << ",\"dr7\":" << returnDiagnostic->dr7
            << ",\"instanceId\":" << module.instanceId << ",\"resumeFlagSet\":"
            << (hardware && (hardware->after.eflags & ResumeFlag) ? "true" : "false");
    if (hardware) {
        Require(hardware->slot < 4, "Invalid hardware evidence slot");
        if (!returnDiagnostic)
            output << ",\"actualRsp\":" << hardware->before.rsp << ",\"expectedRsp\":" << hardware->expectedRsp
                << ",\"debugSlot\":" << hardware->slot << ",\"dr6\":" << hardware->before.dr6
                << ",\"dr7\":" << hardware->before.dr7 << ",\"instanceId\":" << module.instanceId;
        const auto slot = hardware->slot;
        const auto enableMask = 3ull << (slot * 2);
        const bool armed = hardware->after.dr[slot] == returnAddress
            && (hardware->after.dr7 & enableMask) == (1ull << (slot * 2))
            && ((hardware->after.dr7 >> (16 + slot * 4)) & 0xfu) == 0;
        const bool cleared = hardware->after.dr[slot] == 0 && (hardware->after.dr7 & enableMask) == 0;
        output << ",\"resumeFlagRequested\":" << (hardware->resumeFlagRequested ? "true" : "false")
            << ",\"resumeFlagObserved\":" << (hardware->after.eflags & ResumeFlag ? "true" : "false")
            << ",\"slotArmed\":" << (armed ? "true" : "false")
            << ",\"slotCleared\":" << (cleared ? "true" : "false") << ",\"hardwareContextBefore\":";
        WriteHardwareContext(hardware->before);
        output << ",\"hardwareContextAfter\":";
        WriteHardwareContext(hardware->after);
    }
    output << "}\n";
    output.flush();
    Require(output.good(), "Cannot persist debugger observation");
    return static_cast<std::uint64_t>(sequence);
}
void AddEntry(const Module& module, std::uint64_t address, bool tls)
{
    Require(address >= module.base && address < module.base + module.bytes,
        "Observed entry is outside its image");
    Require(entries.size() < 64, "Too many observed initialization entries");
    Require(std::none_of(entries.begin(), entries.end(), [=](const auto& row) { return row.address == address; }),
        "Duplicate observed initialization address");
    const BYTE original = Read<BYTE>(address);
    Require(original != 0xcc, "Unexpected preexisting initialization breakpoint");
    WriteByte(address, 0xcc);
    entries.push_back({address, original, true, tls, module});
}
void Loaded(HANDLE file, std::uint64_t base, DWORD thread)
{
    const auto path = ImagePath(file);
    const auto name = Lower(std::filesystem::path(path).filename().wstring());
    const auto id = ModuleId(name);
    if (!id && name != L"ntdll.dll") return;
    std::fprintf(stderr, "Owned loader load: id=%u base=0x%llx tid=%lu\n", id,
        static_cast<unsigned long long>(base), static_cast<unsigned long>(thread));
    const auto dos = Read<IMAGE_DOS_HEADER>(base);
    Require(dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew > 0 && dos.e_lfanew < 0x100000,
        "Invalid observed DOS header");
    const auto nt = Read<IMAGE_NT_HEADERS64>(base + static_cast<std::uint32_t>(dos.e_lfanew));
    Require(nt.Signature == IMAGE_NT_SIGNATURE && nt.FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64
        && nt.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC && nt.OptionalHeader.SizeOfImage,
        "Invalid observed x64 image");
    if (!id) { ntdllBase = base; ntdllBytes = nt.OptionalHeader.SizeOfImage; return; }
    const bool noInitialization = id == 1 || (RizEarlyKernelProfile && id == 2);
    if (noInitialization) {
        Require(nt.OptionalHeader.AddressOfEntryPoint == 0 && nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS].VirtualAddress == 0,
            "Observed no-entry dependency must have no runtime entry or TLS initialization");
    }
    Module module{};
    module.id = id; module.base = base; module.bytes = nt.OptionalHeader.SizeOfImage;
    module.name = RizEarlyDebugModuleNames[id]; module.live = true;
    if (RizEarlyKernelProfile) {
        module.file = Identify(file); module.expectedFile = ExpectedIdentity(name);
        Require(module.file.volumeSerial == module.expectedFile.volumeSerial
            && module.file.fileIndex == module.expectedFile.fileIndex,
            "Observed module is not the exact expected file beside the owned host");
        Require(modules.size() < 32, "Observed module lifetime history exceeded its finite bound");
        Require(std::none_of(modules.begin(), modules.end(), [=](const auto& row) {
            return row.live && row.base == base;
        }), "A live module base was reported a second time without an unload");
        const auto generations = std::count_if(modules.begin(), modules.end(), [=](const auto& row) { return row.id == id; });
        const auto live = std::count_if(modules.begin(), modules.end(), [=](const auto& row) { return row.id == id && row.live; });
        Require(generations < 8 && (noInitialization || live == 0),
            "Duplicate initialized dependency or excessive no-entry mapping generations");
        module.generation = static_cast<std::uint32_t>(generations) + 1;
        module.instanceId = static_cast<std::uint64_t>(modules.size()) + 1;
    } else {
        Require(std::none_of(modules.begin(), modules.end(), [=](const auto& row) { return row.id == id; }),
            "Duplicate observed dependency name");
    }
    module.loadSequence = Emit(DebugLoad, module, thread, base);
    modules.push_back(module);
    if (!noInitialization) {
        Require(nt.OptionalHeader.AddressOfEntryPoint != 0, "Observed dependency has no expected initialization entry");
        AddEntry(module, base + nt.OptionalHeader.AddressOfEntryPoint, false);
    }
    const auto tls = nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_TLS];
    if (tls.VirtualAddress) {
        Require(tls.VirtualAddress + sizeof(IMAGE_TLS_DIRECTORY64) <= module.bytes,
            "Invalid observed TLS directory");
        const auto directory = Read<IMAGE_TLS_DIRECTORY64>(base + tls.VirtualAddress);
        if (directory.AddressOfCallBacks) {
            bool terminated = false;
            for (unsigned index = 0; index != 16; ++index) {
                const auto callback = Read<std::uint64_t>(directory.AddressOfCallBacks + index * 8ull);
                if (!callback) { terminated = true; break; }
                AddEntry(module, callback, true);
            }
            Require(terminated, "Observed TLS list exceeded finite bound");
        }
    }
}
void Unloaded(std::uint64_t base, DWORD thread)
{
    std::fprintf(stderr, "Owned loader unload: base=0x%llx tid=%lu\n",
        static_cast<unsigned long long>(base), static_cast<unsigned long>(thread));
    if (!RizEarlyKernelProfile) return;
    const auto historical = std::find_if(modules.begin(), modules.end(), [=](const auto& row) { return row.base == base; });
    if (historical == modules.end()) return; // Unobserved system dependency.
    const auto count = std::count_if(modules.begin(), modules.end(), [=](const auto& row) { return row.base == base && row.live; });
    if (count == 0) {
        const auto retiredCount = std::count_if(modules.begin(), modules.end(), [=](const auto& row) {
            return row.base == base && !row.live && (row.id == 1 || row.id == 2);
        });
        Require(retiredCount == 1, "Retired unload notification has ambiguous mapping history");
        const auto retired = std::find_if(modules.begin(), modules.end(), [=](const auto& row) {
            return row.base == base && !row.live && (row.id == 1 || row.id == 2);
        });
        MEMORY_BASIC_INFORMATION query{};
        Require(VirtualQueryEx(process, reinterpret_cast<void*>(base), &query, sizeof(query)) == sizeof(query),
            "Cannot query a retired unload notification");
        Require(!(query.State == MEM_COMMIT && query.Type == MEM_IMAGE
            && reinterpret_cast<std::uint64_t>(query.AllocationBase) == base),
            "Retired unload notification still refers to a mapped image at that base");
        Emit(DebugRetiredUnload, *retired, thread, base, 0, 0, &query);
        return;
    }
    Require(count == 1, "Unload has no exact single live observed mapping");
    auto module = std::find_if(modules.begin(), modules.end(), [=](const auto& row) { return row.base == base && row.live; });
    Require(module->id == 1 || module->id == 2, "Initialized fixture dependency was unloaded");
    Require(std::none_of(entries.begin(), entries.end(), [=](const auto& row) { return row.module.instanceId == module->instanceId; })
        && std::none_of(returns.begin(), returns.end(), [=](const auto& row) { return row.entry.module.instanceId == module->instanceId; }),
        "Unloaded mapping owns an initialization breakpoint or pending return");
    module->live = false;
    Emit(DebugUnload, *module, thread, base);
}
void VerifySelectedModules()
{
    if (!RizEarlyKernelProfile) {
        Require(modules.size() == 5, "Owned debug child did not load every expected dependency");
        return;
    }
    const std::array<std::uint64_t, 6> selected{0, shared->header.publisherModule,
        shared->header.helperModule, shared->header.recorderModule, shared->header.proxyModule,
        shared->header.importerModule};
    for (std::uint32_t id = 1; id <= 5; ++id) {
        const auto count = std::count_if(modules.begin(), modules.end(), [=](const auto& row) { return row.id == id && row.live; });
        Require(count == 1, "Completed child does not have exactly one live mapping for each observed role");
        const auto module = std::find_if(modules.begin(), modules.end(), [=](const auto& row) { return row.id == id && row.live; });
        std::fprintf(stderr, "Owned loader selected: id=%u instance=%llu base=0x%llx header=0x%llx\n", id,
            static_cast<unsigned long long>(module->instanceId), static_cast<unsigned long long>(module->base),
            static_cast<unsigned long long>(selected[id]));
        Require(selected[id] != 0 && module->base == selected[id], "Live mapping differs from actual header-selected dependency");
    }
}
DWORD64& DebugAddress(CONTEXT& context, DWORD slot)
{
    switch (slot) {
    case 0: return context.Dr0;
    case 1: return context.Dr1;
    case 2: return context.Dr2;
    default: Require(slot == 3, "Invalid hardware return slot"); return context.Dr3;
    }
}
void ClearHandledDebugStatus(CONTEXT& context)
{
    context.Dr6 &= ~(DebugBreakpointStatus | DebugOtherStatus);
    context.Dr6 |= DebugStatusSetOnResume; // BLD/RTM clear indications are reset to 1.
}
void CheckDebugRegisters(DWORD thread, CONTEXT& context)
{
    std::array<DWORD64, 4> expected{};
    DWORD64 expectedControl{};
    for (const auto& site : returns) {
        if (site.thread != thread) continue;
        Require(site.hardware && site.debugSlot < 4 && expected[site.debugSlot] == 0,
            "Invalid controlled hardware return table");
        expected[site.debugSlot] = site.address;
        expectedControl |= 1ull << (site.debugSlot * 2);
    }
    Require((context.Dr7 & ~DebugControlFixedOne) == expectedControl
        && !(context.EFlags & TrapFlag), "Preexisting or altered hardware debug control is not owned");
    for (DWORD slot = 0; slot != 4; ++slot)
        Require(DebugAddress(context, slot) == expected[slot], "Preexisting or altered hardware return address is not owned");
}
CONTEXT ReadHardwareContext(HANDLE thread)
{
    CONTEXT observed{}; observed.ContextFlags = CONTEXT_FULL | CONTEXT_DEBUG_REGISTERS;
    Require(GetThreadContext(thread, &observed) != FALSE, "Cannot read back hardware return context");
    return observed;
}
void ArmHardwareReturn(DWORD thread, CONTEXT& context, const Breakpoint& entry,
    std::uint64_t address)
{
    CheckDebugRegisters(thread, context);
    Require(returns.size() < 64, "Too many pending hardware returns");
    DWORD slot = 0;
    for (; slot != 4; ++slot) if (DebugAddress(context, slot) == 0) break;
    Require(slot < 4, "Owned thread exceeded four hardware return slots");
    ReturnSite site{};
    site.address = address; site.stack = context.Rsp + 8; site.thread = thread;
    site.entry = entry; site.hardware = true; site.debugSlot = slot;
    returns.push_back(site);
    DebugAddress(context, slot) = address;
    context.Dr7 |= 1ull << (slot * 2); // Local, execute, one-byte address match.
    ClearHandledDebugStatus(context);
}
bool HardwareReturn(DWORD thread, std::uint64_t address)
{
    if (!RizEarlyKernelProfile) return false;
    const auto foundThread = threads.find(thread);
    Require(foundThread != threads.end(), "Hardware return has no owned thread handle");
    CONTEXT context{}; context.ContextFlags = CONTEXT_FULL | CONTEXT_DEBUG_REGISTERS;
    Require(GetThreadContext(foundThread->second, &context) != FALSE, "Get hardware return context failed");
    if (!(context.Dr6 & DebugBreakpointStatus) || (context.Dr6 & DebugOtherStatus)
        || (context.Dr6 & DebugStatusSetOnResume) != DebugStatusSetOnResume) return false;
    CheckDebugRegisters(thread, context);
    Require(context.Rip == address, "Hardware return exception PC differs from current RIP");
    const auto pending = std::find_if(returns.begin(), returns.end(), [=](const auto& site) {
        return site.hardware && site.thread == thread && site.address == address
            && (context.Dr6 & (1ull << site.debugSlot));
    });
    if (pending == returns.end()) return false;
    const auto exact = std::find_if(returns.begin(), returns.end(), [=](const auto& site) {
        return site.hardware && site.thread == thread && site.address == address && site.stack == context.Rsp
            && (context.Dr6 & (1ull << site.debugSlot));
    });
    if (exact == returns.end()) {
        const ReturnDiagnostic diagnostic{pending->stack, context.Rsp, context.Dr6, context.Dr7, pending->debugSlot};
        const auto before = HardwareSnapshot(context);
        std::fprintf(stderr, "Owned hardware return stack mismatch: tid=%lu pc=0x%llx expected=0x%llx actual=0x%llx slot=%lu\n",
            static_cast<unsigned long>(thread), static_cast<unsigned long long>(address),
            static_cast<unsigned long long>(pending->stack), static_cast<unsigned long long>(context.Rsp),
            static_cast<unsigned long>(pending->debugSlot));
        // An unrelated initializer can pass the same loader return PC. Keep
        // the owned return armed; RF skips this instruction's execute match
        // once. This is not a return from the pending observed initializer.
        context.EFlags |= ResumeFlag; ClearHandledDebugStatus(context);
        Require(SetThreadContext(foundThread->second, &context) != FALSE, "Cannot resume unrelated return-PC pass");
        auto readback = ReadHardwareContext(foundThread->second);
        CheckDebugRegisters(thread, readback);
        Require(readback.Rip == before.rip && readback.Rsp == before.rsp && (readback.EFlags & ResumeFlag),
            "Unrelated return-PC resume did not retain stack/PC/RF in actual readback");
        const HardwareEvidence evidence{before, HardwareSnapshot(readback), pending->stack, pending->debugSlot, true};
        Emit(DebugReturnStackMismatch, pending->entry.module, thread, address, address, context.Rax, nullptr, &diagnostic, &evidence);
        return true;
    }
    const auto site = *exact;
    const auto before = HardwareSnapshot(context);
    if (!site.entry.tls) Require(static_cast<DWORD>(context.Rax) == TRUE,
        "Observed DLL process initialization returned failure");
    DebugAddress(context, site.debugSlot) = 0;
    context.Dr7 &= ~(1ull << (site.debugSlot * 2));
    ClearHandledDebugStatus(context); context.EFlags |= ResumeFlag;
    returns.erase(exact);
    Require(SetThreadContext(foundThread->second, &context) != FALSE, "Cannot release exact hardware return slot");
    auto readback = ReadHardwareContext(foundThread->second);
    CheckDebugRegisters(thread, readback);
    Require(readback.Rip == before.rip && readback.Rsp == site.stack && readback.Rax == context.Rax
        && (readback.EFlags & ResumeFlag), "Exact hardware return cleanup did not preserve actual stack/PC/RAX/RF");
    const HardwareEvidence evidence{before, HardwareSnapshot(readback), site.stack, site.debugSlot, true};
    Emit(site.entry.tls ? DebugTlsReturn : DebugReturn, site.entry.module, thread,
        site.entry.address, address, context.Rax, nullptr, nullptr, &evidence);
    return true;
}
bool Break(DWORD thread, std::uint64_t address)
{
    const auto foundThread = threads.find(thread);
    Require(foundThread != threads.end(), "Breakpoint has no owned thread handle");
    CONTEXT context{};
    context.ContextFlags = CONTEXT_FULL | (RizEarlyKernelProfile ? CONTEXT_DEBUG_REGISTERS : 0u);
    Require(GetThreadContext(foundThread->second, &context) != FALSE, "GetThreadContext failed");
    const auto observedContext = HardwareSnapshot(context);
    auto entry = std::find_if(entries.begin(), entries.end(), [=](const auto& row) {
        return row.active && row.address == address;
    });
    if (entry != entries.end()) {
        Require(context.Rip == address + 1, "Unexpected initialization breakpoint RIP");
        WriteByte(address, entry->original);
        entry->active = false;
        context.Rip = address;
        Require(context.Rdx == DLL_PROCESS_ATTACH, "First observed DLL entry was not process attach");
        const auto returnAddress = Read<std::uint64_t>(context.Rsp);
        if (RizEarlyKernelProfile) ArmHardwareReturn(thread, context, *entry, returnAddress);
        else {
            Require(returns.size() < 64 && std::none_of(returns.begin(), returns.end(), [=](const auto& row) {
                return row.address == returnAddress;
            }), "Overlapping observed initialization return site");
            const BYTE original = Read<BYTE>(returnAddress);
            Require(original != 0xcc, "Unexpected preexisting return breakpoint");
            WriteByte(returnAddress, 0xcc);
            returns.push_back({returnAddress, context.Rsp + 8, thread, original, *entry});
        }
        if (RizEarlyKernelProfile) {
            Require(SetThreadContext(foundThread->second, &context) != FALSE, "SetThreadContext failed");
            auto readback = ReadHardwareContext(foundThread->second);
            CheckDebugRegisters(thread, readback);
            Require(readback.Rip == address && readback.Rsp == observedContext.rsp,
                "Hardware entry arm readback changed the observed stack or requested entry PC");
            const auto& site = returns.back();
            const HardwareEvidence evidence{observedContext, HardwareSnapshot(readback), site.stack, site.debugSlot, false};
            Emit(entry->tls ? DebugTls : DebugEntry, entry->module, thread, address, returnAddress, 0, nullptr, nullptr, &evidence);
        } else {
            Emit(entry->tls ? DebugTls : DebugEntry, entry->module, thread, address, returnAddress);
            Require(SetThreadContext(foundThread->second, &context) != FALSE, "SetThreadContext failed");
        }
        return true;
    }
    if (RizEarlyKernelProfile) return false; // Returns use per-thread DR slots.
    auto returned = std::find_if(returns.begin(), returns.end(), [=](const auto& row) {
        return row.address == address && row.thread == thread;
    });
    if (returned == returns.end()) return false;
    Require(context.Rip == address + 1 && context.Rsp == returned->stack,
        "Observed initialization return had wrong stack identity");
    if (!returned->entry.tls) Require(static_cast<DWORD>(context.Rax) == TRUE,
        "Observed DLL process initialization returned failure");
    WriteByte(address, returned->original);
    context.Rip = address;
    Emit(returned->entry.tls ? DebugTlsReturn : DebugReturn, returned->entry.module, thread,
        returned->entry.address, address, context.Rax);
    returns.erase(returned);
    Require(SetThreadContext(foundThread->second, &context) != FALSE, "SetThreadContext failed");
    return true;
}
}

int wmain(int argc, wchar_t** argv)
{
    PROCESS_INFORMATION child{};
    HANDLE mapping{};
    bool exited = false;
    DWORD exitCode = 1;
    try {
        Require(argc == 3, RizEarlyDebuggerUsage);
        const std::filesystem::path host(argv[1]);
        Require(host.is_absolute() && Lower(host.filename().wstring()) == RizEarlyHostModuleNameLower,
            "Debugger only creates the named owned host at an absolute path");
        hostDirectory = host.parent_path();
        output.open(std::filesystem::path(argv[2]), std::ios::binary | std::ios::trunc);
        Require(output.good(), "Cannot create observer JSONL");
        std::array<wchar_t, 256> mappingName{};
        const DWORD nameLength = GetEnvironmentVariableW(RizEarlyMappingEnvironment,
            mappingName.data(), static_cast<DWORD>(mappingName.size()));
        Require(nameLength && nameLength < mappingName.size(), "Missing named fixture mapping");
        mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, mappingName.data());
        Require(mapping != nullptr, "Cannot open parent fixture mapping");
        shared = static_cast<RizEarlySharedV1*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(RizEarlySharedV1)));
        Require(shared && shared->header.magic == RizEarlyMagic && shared->header.version == RizEarlyVersion,
            "Bad parent fixture mapping");
        std::wstring command = L"\"" + host.wstring() + L"\"";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        startup.dwFlags = STARTF_USESTDHANDLES;
        startup.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
        startup.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
        startup.hStdError = GetStdHandle(STD_ERROR_HANDLE);
        const auto directory = host.parent_path().wstring();
        Require(CreateProcessW(host.c_str(), command.data(), nullptr, nullptr, TRUE,
            DEBUG_ONLY_THIS_PROCESS | CREATE_NO_WINDOW, nullptr, directory.c_str(), &startup, &child) != FALSE,
            "Cannot create owned debug child");
        process = child.hProcess; childPid = child.dwProcessId;
        const auto began = GetTickCount64();
        bool initialBreakpoint = false;
        for (;;) {
            Require(GetTickCount64() - began < 30000, "Owned debugger finite timeout");
            DEBUG_EVENT event{};
            if (!WaitForDebugEvent(&event, 100)) {
                Require(GetLastError() == ERROR_SEM_TIMEOUT, "WaitForDebugEvent failed");
                continue;
            }
            Require(event.dwProcessId == childPid, "Unexpected debug process");
            DWORD continuation = DBG_CONTINUE;
            switch (event.dwDebugEventCode) {
            case CREATE_PROCESS_DEBUG_EVENT:
                threads[event.dwThreadId] = event.u.CreateProcessInfo.hThread;
                if (event.u.CreateProcessInfo.hFile) CloseHandle(event.u.CreateProcessInfo.hFile);
                break;
            case CREATE_THREAD_DEBUG_EVENT:
                threads[event.dwThreadId] = event.u.CreateThread.hThread;
                break;
            case EXIT_THREAD_DEBUG_EVENT:
                threads.erase(event.dwThreadId);
                break;
            case LOAD_DLL_DEBUG_EVENT:
                try { Loaded(event.u.LoadDll.hFile, reinterpret_cast<std::uint64_t>(event.u.LoadDll.lpBaseOfDll), event.dwThreadId); }
                catch (...) { if (event.u.LoadDll.hFile) CloseHandle(event.u.LoadDll.hFile); throw; }
                if (event.u.LoadDll.hFile) CloseHandle(event.u.LoadDll.hFile);
                break;
            case UNLOAD_DLL_DEBUG_EVENT:
                Unloaded(reinterpret_cast<std::uint64_t>(event.u.UnloadDll.lpBaseOfDll), event.dwThreadId);
                break;
            case EXCEPTION_DEBUG_EVENT: {
                const auto& exception = event.u.Exception.ExceptionRecord;
                const auto address = reinterpret_cast<std::uint64_t>(exception.ExceptionAddress);
                if (exception.ExceptionCode == EXCEPTION_SINGLE_STEP && HardwareReturn(event.dwThreadId, address)) break;
                if (exception.ExceptionCode == EXCEPTION_BREAKPOINT && Break(event.dwThreadId, address)) break;
                if (exception.ExceptionCode == EXCEPTION_BREAKPOINT && !initialBreakpoint
                    && address >= ntdllBase && address < ntdllBase + ntdllBytes
                    && event.u.Exception.dwFirstChance) { initialBreakpoint = true; break; }
                // Real original SEH/C++ must reach the host's handlers.
                continuation = DBG_EXCEPTION_NOT_HANDLED;
                break;
            }
            case EXIT_PROCESS_DEBUG_EVENT:
                exitCode = event.u.ExitProcess.dwExitCode; exited = true;
                break;
            default: break;
            }
            Require(ContinueDebugEvent(event.dwProcessId, event.dwThreadId, continuation) != FALSE,
                "ContinueDebugEvent failed");
            if (exited) break;
        }
        Require(exitCode == 0 && returns.empty(),
            "Owned debug child did not complete every expected dependency");
        VerifySelectedModules();
        Require(std::none_of(entries.begin(), entries.end(), [](const auto& row) { return row.active; }),
            "Some observed initialization entries never executed");
    } catch (const std::exception& error) {
        std::fprintf(stderr, "Owned initialization observer: %s\n", error.what());
        if (child.hProcess && !exited) TerminateProcess(child.hProcess, 0xe04c4401u);
        exitCode = 1;
    }
    if (shared) UnmapViewOfFile(shared);
    if (mapping) CloseHandle(mapping);
    if (child.hThread) CloseHandle(child.hThread);
    if (child.hProcess) CloseHandle(child.hProcess);
    return static_cast<int>(exitCode);
}
