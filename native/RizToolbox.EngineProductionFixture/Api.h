#pragma once
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef RIZ_WINHTTP_EMBEDDED_DOORSTOP
#define RIZ_WINHTTP_EMBEDDED_DOORSTOP 1
#endif
#include "../RizToolbox.WinHttpFixture/WinHttpFixtureApi.h"
#include "../RizToolbox.Doorstop/riz_engine_bootstrap.h"
#include "../RizToolbox.Doorstop/riz_winhttp.h"
#include "ProductionColdProfile.h"

namespace RizProductionCold {
constexpr std::uint32_t Version=2,PhaseCount=3,CallCount=4;
enum Phase : std::uint32_t {Tls=1,Dll=2,Main=3};
enum Kind : std::uint32_t {Open=1,Close=2};
struct ModuleProof {
    std::uint64_t base,fileBytes;
    std::uint32_t imageSize,timeDateStamp,matched,reserved;
    unsigned char sha256[32];
};
struct Call {
    std::uint32_t phase,kind,threadId,lastErrorBefore,lastErrorAfter,reserved;
    std::uint64_t apiAddress,handle,rawReturn;
    std::uint64_t systemAddress;
    std::uint32_t systemRawReturn,systemLastError;
};
struct PhaseProof {
    std::uint32_t phase,threadId,lastErrorBefore,lastErrorAfter,matched,reserved;
    std::uint64_t proxyBase,bootstrapBase,kernelBase,apiAddresses[4],recordAddress;
    ModuleProof modules[3];
    std::uint32_t recordReadBytes,readError;
    RizEngineDoorstopRecordV1 record;
    std::uint64_t proxyGetInfoAddress;
    std::uint32_t resolverInfoResult,resolverLastErrorBefore,resolverLastErrorAfter,referenceMatched;
    ModuleProof systemModule;
    RizWinHttpInfoV1 resolver;
};
struct Snapshot {
    std::uint32_t version,byteSize,phaseMask,phaseCount,callCount,reserved;
    std::uint64_t checks,failures;
    PhaseProof phases[PhaseCount];
    Call calls[CallCount];
};
static_assert(sizeof(ModuleProof)==64&&sizeof(Call)==64);
static_assert(offsetof(PhaseProof,modules)==88&&offsetof(PhaseProof,record)==288);
static_assert(offsetof(PhaseProof,proxyGetInfoAddress)==12736&&offsetof(PhaseProof,systemModule)==12760);
static_assert(offsetof(PhaseProof,resolver)==12824&&sizeof(PhaseProof)==14072&&offsetof(Snapshot,phases)==40);
static_assert(offsetof(Snapshot,calls)==42256&&sizeof(Snapshot)==42512);

inline bool Equal(const void* a,const void* b,std::size_t bytes)
{
    const auto* left=static_cast<const volatile unsigned char*>(a);
    const auto* right=static_cast<const volatile unsigned char*>(b);
    unsigned difference{};for(std::size_t i=0;i<bytes;++i)difference|=left[i]^right[i];
    return difference==0;
}
inline bool ModuleIdentity(HMODULE module,unsigned index,ModuleProof& proof)
{
    if(!module||index>=4)return false;
    proof.base=reinterpret_cast<std::uint64_t>(module);
    IMAGE_DOS_HEADER dos{};IMAGE_NT_HEADERS64 nt{};SIZE_T count{};
    const auto process=GetCurrentProcess();
    if(!ReadProcessMemory(process,module,&dos,sizeof(dos),&count)||count!=sizeof(dos)
        ||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<=0||dos.e_lfanew>0x100000)return false;
    if(!ReadProcessMemory(process,reinterpret_cast<const void*>(proof.base+static_cast<std::uint32_t>(dos.e_lfanew)),
        &nt,sizeof(nt),&count)||count!=sizeof(nt)||nt.Signature!=IMAGE_NT_SIGNATURE
        ||nt.FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt.OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC)return false;
    proof.imageSize=nt.OptionalHeader.SizeOfImage;proof.timeDateStamp=nt.FileHeader.TimeDateStamp;
    const auto& expected=RizEngineProductionColdContract::Images[index];
    if(proof.imageSize!=expected.imageSize||proof.timeDateStamp!=expected.timeDateStamp)return false;
    wchar_t path[32768]{};const auto length=GetModuleFileNameW(module,path,static_cast<DWORD>(sizeof(path)/sizeof(path[0])));
    if(!length||length>=sizeof(path)/sizeof(path[0]))return false;
    const auto file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    RizWinFixture::Sha256 hash;unsigned char buffer[4096]{};bool okay=true;
    for(;;){DWORD read{};if(!ReadFile(file,buffer,static_cast<DWORD>(sizeof(buffer)),&read,nullptr)){okay=false;break;}
        if(!read)break;proof.fileBytes+=read;if(proof.fileBytes>64ull*1024*1024){okay=false;break;}hash.Feed(buffer,read);}
    const auto closed=CloseHandle(file);hash.Finish(proof.sha256);
    proof.matched=closed&&okay&&proof.fileBytes==expected.fileBytes&&Equal(proof.sha256,expected.sha256,32)?1u:0u;
    return proof.matched==1;
}
inline bool Match(const RizWinFixture::Api& api,PhaseProof& proof)
{
    const DWORD incoming=GetLastError();bool okay=false;HMODULE proxy{};
    if(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(api.close),&proxy)&&proxy){
        proof.proxyBase=reinterpret_cast<std::uint64_t>(proxy);
        const HMODULE modules[3]{proxy,GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll"),GetModuleHandleW(L"RizToolbox.EngineNodeKernel.dll")};
        proof.bootstrapBase=reinterpret_cast<std::uint64_t>(modules[1]);proof.kernelBase=reinterpret_cast<std::uint64_t>(modules[2]);
        proof.apiAddresses[0]=reinterpret_cast<std::uint64_t>(api.open);proof.apiAddresses[1]=reinterpret_cast<std::uint64_t>(api.proxy);
        proof.apiAddresses[2]=reinterpret_cast<std::uint64_t>(api.ie);proof.apiAddresses[3]=reinterpret_cast<std::uint64_t>(api.close);
        okay=true;
        for(unsigned i=0;i<3;++i){if(!ModuleIdentity(modules[i],i,proof.modules[i]))okay=false;
            for(unsigned j=0;j<i;++j)if(modules[i]==modules[j])okay=false;}
        for(unsigned i=0;i<4;++i)if(proof.apiAddresses[i]!=proof.proxyBase+RizEngineProductionColdContract::ExportRvas[i])okay=false;
        proof.recordAddress=proof.proxyBase+RizEngineProductionColdContract::RecordRva;
        MEMORY_BASIC_INFORMATION memory{};
        if(VirtualQuery(reinterpret_cast<const void*>(proof.recordAddress),&memory,sizeof(memory))!=sizeof(memory)
            ||memory.AllocationBase!=proxy||memory.State!=MEM_COMMIT||memory.Type!=MEM_IMAGE||memory.Protect!=PAGE_READWRITE
            ||proof.recordAddress<reinterpret_cast<std::uint64_t>(memory.BaseAddress)
            ||proof.recordAddress-reinterpret_cast<std::uint64_t>(memory.BaseAddress)>memory.RegionSize
            ||sizeof(proof.record)>memory.RegionSize-static_cast<std::size_t>(proof.recordAddress-reinterpret_cast<std::uint64_t>(memory.BaseAddress)))okay=false;
    }
    SetLastError(incoming);return okay;
}
inline bool ImageRange(std::uint64_t address,std::size_t size,HMODULE module,DWORD protection)
{
    MEMORY_BASIC_INFORMATION memory{};
    return VirtualQuery(reinterpret_cast<const void*>(address),&memory,sizeof(memory))==sizeof(memory)
        &&memory.AllocationBase==module&&memory.State==MEM_COMMIT&&memory.Type==MEM_IMAGE&&memory.Protect==protection
        &&address>=reinterpret_cast<std::uint64_t>(memory.BaseAddress)
        &&address-reinterpret_cast<std::uint64_t>(memory.BaseAddress)<=memory.RegionSize
        &&size<=memory.RegionSize-static_cast<std::size_t>(address-reinterpret_cast<std::uint64_t>(memory.BaseAddress));
}
inline bool ReadReferenceInner(PhaseProof& proof)
{
    const auto proxy=reinterpret_cast<HMODULE>(proof.proxyBase);
    proof.proxyGetInfoAddress=proof.proxyBase+RizEngineProductionColdContract::ProxyGetInfoRva;
    if(!ImageRange(proof.proxyGetInfoAddress,1,proxy,PAGE_EXECUTE_READ))return false;
    using GetInfoFn=std::uint32_t(__cdecl*)(RizWinHttpInfoV1*);
    const auto getInfo=reinterpret_cast<GetInfoFn>(proof.proxyGetInfoAddress);
    auto& info=proof.resolver;info.version=1;info.byteSize=sizeof(info);
    proof.resolverLastErrorBefore=GetLastError();SetLastError(proof.resolverLastErrorBefore);
    proof.resolverInfoResult=getInfo(&info);proof.resolverLastErrorAfter=GetLastError();
    if(proof.resolverInfoResult||proof.resolverLastErrorBefore!=proof.resolverLastErrorAfter
        ||info.version!=1||info.byteSize!=1248||info.state!=RizWinHttpReady||info.failure||info.phase!=RizWinHttpPhaseComplete
        ||info.flags!=63||info.initializeAttempts!=1||info.publishCount!=1||info.resolvedCount!=4||info.lastWin32Error||info.unknown
        ||info.faultStage||info.faultIndex||info.selfModule!=proof.proxyBase||!info.systemModule||info.systemModule==proof.proxyBase
        ||!info.heldSystemFile||!info.fileIndex||!info.fileVolumeSerial||!info.activeTable||info.activeTable!=info.resolvedTable
        ||info.activeTable==info.initialTable||info.activeTable==info.poisonTable||info.initialTable==info.poisonTable)return false;
    const auto system=reinterpret_cast<HMODULE>(info.systemModule);
    if(!ModuleIdentity(system,3,proof.systemModule)||info.fileBytes!=proof.systemModule.fileBytes
        ||info.peMachine!=IMAGE_FILE_MACHINE_AMD64||info.peImageSize!=proof.systemModule.imageSize
        ||info.peTimeDateStamp!=proof.systemModule.timeDateStamp||info.peExportRva!=RizEngineProductionColdContract::SystemExportDirectoryRva
        ||info.peExportBytes!=RizEngineProductionColdContract::SystemExportDirectoryBytes||info.loadFlags!=LOAD_LIBRARY_SEARCH_SYSTEM32)return false;
    if(!info.systemPathChars||info.systemPathChars>=260||info.systemPathChars!=info.loadedPathChars
        ||info.systemPath[info.systemPathChars]||info.loadedPath[info.loadedPathChars]
        ||CompareStringOrdinal(info.systemPath,static_cast<int>(info.systemPathChars),info.loadedPath,static_cast<int>(info.loadedPathChars),TRUE)!=CSTR_EQUAL)return false;
    wchar_t expectedPath[260]{};auto length=GetSystemDirectoryW(expectedPath,260);
    constexpr wchar_t suffix[]=L"\\winhttp.dll";
    if(length<3||length>=260)return false;if(expectedPath[length-1]==L'\\')--length;
    if(length+sizeof(suffix)/sizeof(suffix[0])>260)return false;
    for(std::size_t i=0;i<sizeof(suffix)/sizeof(suffix[0]);++i)expectedPath[length+i]=suffix[i];
    length+=static_cast<DWORD>(sizeof(suffix)/sizeof(suffix[0])-1);
    if(length!=info.systemPathChars||CompareStringOrdinal(expectedPath,static_cast<int>(length),info.systemPath,static_cast<int>(length),TRUE)!=CSTR_EQUAL)return false;
    wchar_t actualPath[260]{};const auto actualLength=GetModuleFileNameW(system,actualPath,260);
    if(actualLength!=length||CompareStringOrdinal(actualPath,static_cast<int>(actualLength),expectedPath,static_cast<int>(length),TRUE)!=CSTR_EQUAL)return false;
    BY_HANDLE_FILE_INFORMATION file{};
    if(!GetFileInformationByHandle(reinterpret_cast<HANDLE>(info.heldSystemFile),&file)
        ||((std::uint64_t(file.nFileIndexHigh)<<32)|file.nFileIndexLow)!=info.fileIndex||file.dwVolumeSerialNumber!=info.fileVolumeSerial
        ||((std::uint64_t(file.nFileSizeHigh)<<32)|file.nFileSizeLow)!=info.fileBytes)return false;
    const char* const names[4]{"WinHttpOpen","WinHttpGetProxyForUrl","WinHttpGetIEProxyConfigForCurrentUser","WinHttpCloseHandle"};
    for(unsigned i=0;i<4;++i){
        const auto address=info.systemModule+RizEngineProductionColdContract::SystemExportRvas[i];HMODULE actual{};
        if(info.addresses[i]!=address||info.protections[i]!=PAGE_EXECUTE_READ
            ||reinterpret_cast<std::uint64_t>(GetProcAddress(system,names[i]))!=address||!ImageRange(address,1,system,PAGE_EXECUTE_READ)
            ||!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCWSTR>(address),&actual)||actual!=system)return false;
    }
    std::uint64_t slots[4]{};SIZE_T read{};
    return ImageRange(info.activeTable,sizeof(slots),proxy,PAGE_READWRITE)
        &&ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<const void*>(info.activeTable),slots,sizeof(slots),&read)
        &&read==sizeof(slots)&&Equal(slots,info.addresses,sizeof(slots));
}
inline bool ReadReference(PhaseProof& proof)
{
    const DWORD incoming=GetLastError();const bool okay=ReadReferenceInner(proof);
    proof.referenceMatched=okay?1u:0u;SetLastError(incoming);return okay;
}
}

#ifdef RIZ_PRODUCTION_COLD_IMPORTER_BUILD
#define RIZ_PRODUCTION_COLD_API extern "C" __declspec(dllexport)
#else
#define RIZ_PRODUCTION_COLD_API extern "C" __declspec(dllimport)
#endif
RIZ_PRODUCTION_COLD_API std::uint32_t __cdecl RizProductionCold_Run();
RIZ_PRODUCTION_COLD_API std::uint32_t __cdecl RizProductionCold_GetSnapshot(RizProductionCold::Snapshot* output);
