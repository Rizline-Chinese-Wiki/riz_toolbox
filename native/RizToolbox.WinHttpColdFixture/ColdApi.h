#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef RIZ_WINHTTP_EMBEDDED_DOORSTOP
#define RIZ_WINHTTP_EMBEDDED_DOORSTOP 1
#endif
#include "../RizToolbox.WinHttpFixture/WinHttpFixtureApi.h"
#include "../RizToolbox.Doorstop/riz_winhttp_doorstop.h"
#include "ColdProxySymbols.h"

namespace RizCold {
constexpr std::uint32_t Version=1, PhaseCount=5, RowCount=12;
struct PhaseProof {
    std::uint32_t phase,threadId,infoResult,lastErrorBefore,lastErrorAfter,reserved;
    std::uint64_t proxyBase,getInfoAddress;
    RizWinHttpInfoV1 info;
    RizWinHttpDoorstopRecordV1 entryRecord;
};
struct Snapshot {
    std::uint32_t version,byteSize,phaseMask,rowCount;
    std::uint64_t checks,failures;
    PhaseProof phases[PhaseCount];
    RizWinFixture::Row rows[RowCount];
};
static_assert(sizeof(PhaseProof)==2592);
static_assert(offsetof(PhaseProof,proxyBase)==24&&offsetof(PhaseProof,info)==40);
static_assert(offsetof(PhaseProof,entryRecord)==1288);
static_assert(offsetof(Snapshot,phases)==32&&offsetof(Snapshot,rows)==12992);
static_assert(sizeof(Snapshot)==16832);

// The internal MAP RVAs are meaningful only for this exact production image.
// This check uses only already imported Kernel32 calls and bounded stack data;
// it is also used before the importer's first TLS observation.
inline bool MatchProxy(const RizWinFixture::Api& api,HMODULE& module)
{
    module=nullptr;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(api.close),&module)||!module)return false;
    const auto base=reinterpret_cast<std::uint64_t>(module);
    IMAGE_DOS_HEADER dos{};IMAGE_NT_HEADERS64 nt{};SIZE_T read{};
    if(!ReadProcessMemory(GetCurrentProcess(),module,&dos,sizeof(dos),&read)||read!=sizeof(dos)
        ||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<=0||dos.e_lfanew>0x100000)return false;
    if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(base+static_cast<std::uint32_t>(dos.e_lfanew)),
        &nt,sizeof(nt),&read)||read!=sizeof(nt)||nt.Signature!=IMAGE_NT_SIGNATURE
        ||nt.FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt.OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC
        ||nt.OptionalHeader.SizeOfImage!=RizColdSymbols::ImageSize||nt.FileHeader.TimeDateStamp!=RizColdSymbols::TimeDateStamp
        ||RizColdSymbols::GetInfoRva>=RizColdSymbols::ImageSize
        ||RizColdSymbols::DoorstopRecordRva>=RizColdSymbols::ImageSize
        ||sizeof(RizWinHttpDoorstopRecordV1)>RizColdSymbols::ImageSize-RizColdSymbols::DoorstopRecordRva)return false;
    const std::uint64_t addresses[4]{reinterpret_cast<std::uint64_t>(api.open),reinterpret_cast<std::uint64_t>(api.proxy),
        reinterpret_cast<std::uint64_t>(api.ie),reinterpret_cast<std::uint64_t>(api.close)};
    const char* const names[4]{"WinHttpOpen","WinHttpGetProxyForUrl","WinHttpGetIEProxyConfigForCurrentUser","WinHttpCloseHandle"};
    for(unsigned k=0;k<4;++k){
        if(RizColdSymbols::ExportRvas[k]>=RizColdSymbols::ImageSize||addresses[k]!=base+RizColdSymbols::ExportRvas[k]
            ||reinterpret_cast<std::uint64_t>(GetProcAddress(module,names[k]))!=addresses[k])return false;
    }
    const std::uint32_t rvas[2]{RizColdSymbols::GetInfoRva,RizColdSymbols::DoorstopRecordRva};
    const std::size_t sizes[2]{1,sizeof(RizWinHttpDoorstopRecordV1)};
    const DWORD protections[2]{PAGE_EXECUTE_READ,PAGE_READWRITE};
    for(unsigned k=0;k<2;++k){
        MEMORY_BASIC_INFORMATION memory{};const auto address=base+rvas[k];
        if(VirtualQuery(reinterpret_cast<void*>(address),&memory,sizeof(memory))!=sizeof(memory)
            ||memory.AllocationBase!=module||memory.State!=MEM_COMMIT||memory.Type!=MEM_IMAGE
            ||memory.Protect!=protections[k]||address<reinterpret_cast<std::uint64_t>(memory.BaseAddress)
            ||address-reinterpret_cast<std::uint64_t>(memory.BaseAddress)>memory.RegionSize
            ||sizes[k]>memory.RegionSize-static_cast<std::size_t>(address-reinterpret_cast<std::uint64_t>(memory.BaseAddress)))return false;
    }
    wchar_t path[32768]{};const auto length=GetModuleFileNameW(module,path,static_cast<DWORD>(sizeof(path)/sizeof(path[0])));
    if(!length||length>=sizeof(path)/sizeof(path[0]))return false;
    const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    RizWinFixture::Sha256 hash;unsigned char bytes[4096]{};std::uint64_t total{};bool okay=true;
    for(;;){DWORD count{};if(!ReadFile(file,bytes,static_cast<DWORD>(sizeof(bytes)),&count,nullptr)){okay=false;break;}
        if(!count)break;total+=count;if(total>64u*1024u*1024u){okay=false;break;}hash.Feed(bytes,count);}
    const auto closed=CloseHandle(file);unsigned char digest[32]{};hash.Finish(digest);
    if(!closed||!okay||!total)return false;
    for(unsigned i=0;i<32;++i)if(digest[i]!=RizColdSymbols::FileSha256[i])return false;
    return true;
}
}

#ifdef RIZ_COLD_IMPORTER_BUILD
#define RIZ_COLD_IMPORTER_API extern "C" __declspec(dllexport)
#else
#define RIZ_COLD_IMPORTER_API extern "C" __declspec(dllimport)
#endif
RIZ_COLD_IMPORTER_API std::uint32_t __cdecl RizColdImporter_RunOutsideLoader();
RIZ_COLD_IMPORTER_API std::uint32_t __cdecl RizColdImporter_GetSnapshot(RizCold::Snapshot* output);
