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
#include "EarlyLoaderShared.h"

// Test observer only. Creates its own child, never attaches to another process.
// Breakpoints briefly change owned-child TLS/CRT entry/return instructions in
// memory. Main fixture runs remain uninstrumented; no game permission is implied.
namespace {
constexpr DWORD ObserverFlag = 0x80000000u;
constexpr DWORD DebugLoad = 1001, DebugEntry = 1002, DebugReturn = 1003,
    DebugTls = 1004, DebugTlsReturn = 1005;
struct Module { std::uint32_t id{}; std::uint64_t base{}, bytes{}; std::string name; };
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
    if (name == L"dobby.dll") return 1;
    if (name == L"riztoolbox.judgecontext.dll") return 2;
    if (name == L"rizownedrecorder.dll") return 3;
    if (name == L"rizownedproxy.dll") return 4;
    if (name == L"rizownedimporter.dll") return 5;
    return 0;
}
void Emit(DWORD type, const Module& module, DWORD thread, std::uint64_t target,
    std::uint64_t returnAddress = 0, std::uint64_t returnValue = 0)
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
    shared->events[index] = row;
    InterlockedExchange64(&shared->events[index].sequence, sequence);
    output << "{\"sequence\":" << sequence << ",\"type\":" << type
        << ",\"moduleId\":" << module.id << ",\"module\":\"" << module.name
        << "\",\"base\":" << module.base << ",\"pid\":" << childPid
        << ",\"tid\":" << thread << ",\"target\":" << target
        << ",\"returnAddress\":" << returnAddress << ",\"returnRax\":" << returnValue
        << ",\"processAttach\":true,\"debugInstrumented\":true}\n";
    output.flush();
    Require(output.good(), "Cannot persist debugger observation");
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
    const auto dos = Read<IMAGE_DOS_HEADER>(base);
    Require(dos.e_magic == IMAGE_DOS_SIGNATURE && dos.e_lfanew > 0 && dos.e_lfanew < 0x100000,
        "Invalid observed DOS header");
    const auto nt = Read<IMAGE_NT_HEADERS64>(base + static_cast<std::uint32_t>(dos.e_lfanew));
    Require(nt.Signature == IMAGE_NT_SIGNATURE && nt.FileHeader.Machine == IMAGE_FILE_MACHINE_AMD64
        && nt.OptionalHeader.Magic == IMAGE_NT_OPTIONAL_HDR64_MAGIC && nt.OptionalHeader.SizeOfImage,
        "Invalid observed x64 image");
    if (!id) { ntdllBase = base; ntdllBytes = nt.OptionalHeader.SizeOfImage; return; }
    Require(std::none_of(modules.begin(), modules.end(), [=](const auto& row) { return row.id == id; }),
        "Duplicate observed dependency name");
    constexpr const char* observedNames[]{"", "dobby.dll", "riztoolbox.judgecontext.dll",
        "rizownedrecorder.dll", "rizownedproxy.dll", "rizownedimporter.dll"};
    Module module{id, base, nt.OptionalHeader.SizeOfImage, observedNames[id]};
    modules.push_back(module);
    Emit(DebugLoad, module, thread, base);
    Require(nt.OptionalHeader.AddressOfEntryPoint != 0, "Observed DLL has no CRT entry");
    AddEntry(module, base + nt.OptionalHeader.AddressOfEntryPoint, false);
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
bool Break(DWORD thread, std::uint64_t address)
{
    const auto foundThread = threads.find(thread);
    Require(foundThread != threads.end(), "Breakpoint has no owned thread handle");
    CONTEXT context{}; context.ContextFlags = CONTEXT_FULL;
    Require(GetThreadContext(foundThread->second, &context) != FALSE, "GetThreadContext failed");
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
        Require(returns.size() < 64 && std::none_of(returns.begin(), returns.end(), [=](const auto& row) {
            return row.address == returnAddress;
        }), "Overlapping observed initialization return site");
        const BYTE original = Read<BYTE>(returnAddress);
        Require(original != 0xcc, "Unexpected preexisting return breakpoint");
        WriteByte(returnAddress, 0xcc);
        returns.push_back({returnAddress, context.Rsp + 8, thread, original, *entry});
        Emit(entry->tls ? DebugTls : DebugEntry, entry->module, thread, address, returnAddress);
        Require(SetThreadContext(foundThread->second, &context) != FALSE, "SetThreadContext failed");
        return true;
    }
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
        Require(argc == 3, "Usage: RizOwnedDebugger.exe <absolute-owned-host> <debug-events.jsonl>");
        const std::filesystem::path host(argv[1]);
        Require(host.is_absolute() && Lower(host.filename().wstring()) == L"rizownedhost.exe",
            "Debugger only creates the named owned host at an absolute path");
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
            case EXCEPTION_DEBUG_EVENT: {
                const auto& exception = event.u.Exception.ExceptionRecord;
                const auto address = reinterpret_cast<std::uint64_t>(exception.ExceptionAddress);
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
        Require(exitCode == 0 && returns.empty() && modules.size() == 5,
            "Owned debug child did not complete every expected dependency");
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
