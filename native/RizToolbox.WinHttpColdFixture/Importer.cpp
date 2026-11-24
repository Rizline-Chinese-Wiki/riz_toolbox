#define RIZ_COLD_IMPORTER_BUILD
#include "ColdApi.h"

namespace {
using namespace RizWinFixture;
RizCold::Snapshot snapshot{};
Salt salt{};
volatile LONG phaseMask{},rowCount{};
volatile LONG64 checks{},failures{},sequence{};
using InfoFn=std::uint32_t(__cdecl*)(RizWinHttpInfoV1*);
void Check(bool okay){InterlockedIncrement64(&checks);if(!okay)InterlockedIncrement64(&failures);}
Api ImportedApi()
{return {&WinHttpOpen,&WinHttpGetProxyForUrl,&WinHttpGetIEProxyConfigForCurrentUser,&WinHttpCloseHandle};}
std::uint64_t Next(){return static_cast<std::uint64_t>(InterlockedIncrement64(&sequence));}
void Append(const Row& row)
{
    const auto index=InterlockedIncrement(&rowCount)-1;
    if(index<0||static_cast<std::uint32_t>(index)>=RizCold::RowCount){Check(false);return;}
    snapshot.rows[index]=row;Check(row.freesFailed==0);
    for(const auto& text:row.strings)Check(!text.present||text.complete==1);
}
bool Ready(const RizWinHttpInfoV1& info,std::uint64_t proxy)
{
    bool okay=info.version==1&&info.byteSize==sizeof(info)&&info.state==RizWinHttpReady
        &&info.failure==0&&info.phase==RizWinHttpPhaseComplete&&info.flags==63
        &&info.initializeAttempts==1&&info.publishCount==1&&info.resolvedCount==4&&!info.unknown
        &&info.selfModule==proxy&&info.systemModule&&info.systemModule!=proxy
        &&info.activeTable==info.resolvedTable&&info.activeTable!=info.initialTable
        &&info.activeTable!=info.poisonTable&&info.initialTable!=info.poisonTable;
    for(unsigned k=0;k<4;++k)okay=okay&&info.addresses[k]!=0;
    return okay;
}
bool Capture(std::uint32_t phase,const Api& api)
{
    const auto index=phase==ImporterOutside?4u:phase-ImporterTls;
    if(index>=RizCold::PhaseCount){Check(false);return false;}
    auto& proof=snapshot.phases[index];proof.phase=phase;proof.threadId=GetCurrentThreadId();proof.infoResult=UINT32_MAX;
    HMODULE module{};
    const auto matched=RizCold::MatchProxy(api,module);
    proof.proxyBase=reinterpret_cast<std::uint64_t>(module);
    Check(matched);if(!matched)return false;
    const auto* base=reinterpret_cast<const unsigned char*>(module);
    proof.info.version=1;proof.info.byteSize=sizeof(proof.info);
    proof.getInfoAddress=proof.proxyBase+RizColdSymbols::GetInfoRva;
    const auto getInfo=reinterpret_cast<InfoFn>(proof.getInfoAddress);
    proof.lastErrorBefore=0x6c610000u+phase;SetLastError(proof.lastErrorBefore);
    proof.infoResult=getInfo(&proof.info);proof.lastErrorAfter=GetLastError();
    auto* destination=reinterpret_cast<volatile unsigned char*>(&proof.entryRecord);
    const auto* source=reinterpret_cast<const volatile unsigned char*>(base+RizColdSymbols::DoorstopRecordRva);
    for(std::size_t i=0;i<sizeof(proof.entryRecord);++i)destination[i]=source[i];
    const auto& entry=proof.entryRecord;
    const bool infoOkay=proof.infoResult==0&&proof.lastErrorBefore==proof.lastErrorAfter&&Ready(proof.info,proof.proxyBase);
    Check(infoOkay);
    const bool entryOkay=entry.version==1&&entry.byteSize==sizeof(entry)&&entry.stage==RizWinHttpDoorstopReady
        &&entry.attempts==1&&entry.threadId==proof.threadId&&entry.initializeResult==0&&entry.infoResult==0
        &&entry.entryLastError==entry.initializeLastError&&entry.entryLastError==entry.exitLastError
        &&entry.readyChecksPassed==1&&entry.failure==0&&entry.selfModule==proof.proxyBase&&Ready(entry.info,proof.proxyBase);
    Check(entryOkay);
    return infoOkay&&entryOkay;
}
void Run(std::uint32_t phase)
{
    const auto api=ImportedApi();if(phase==ImporterTls)salt=MakeSalt(&snapshot);
    const auto bit=static_cast<LONG>(1u<<phase);Check((InterlockedOr(&phaseMask,bit)&bit)==0);
    if(!Capture(phase,api))return;
    const auto seed=0x6c710000u+phase;
    if(phase==ImporterOutside){
        const auto opened=CallOpen(api,phase,seed,Next());Append(opened);Check(opened.rawReturn!=0);
        const auto handle=reinterpret_cast<HINTERNET>(opened.rawReturn);
        Append(CallIe(api,phase,seed+1,Next(),salt));
        const auto proxy=CallProxy(api,phase,seed+2,Next(),handle,salt);Append(proxy);Check(proxy.rawReturn==0);
        const auto closed=CallClose(api,phase,seed+3,Next(),handle);Append(closed);Check(closed.rawReturn!=0);
    }else{
        const auto closed=CallClose(api,phase,seed,Next(),nullptr);Append(closed);Check(closed.rawReturn==0);
        const auto proxy=CallProxy(api,phase,seed+1,Next(),nullptr,salt);Append(proxy);Check(proxy.rawReturn==0);
    }
}
int __cdecl CInitialize(){Run(ImporterC);return 0;}
void __cdecl CppInitialize(){Run(ImporterCpp);}
}

extern "C" void NTAPI RizColdTls(PVOID,DWORD reason,PVOID){if(reason==DLL_PROCESS_ATTACH)Run(RizWinFixture::ImporterTls);}
#pragma section(".CRT$XLB",long,read)
#pragma section(".CRT$XIC",long,read)
#pragma section(".CRT$XCU",long,read)
extern "C" __declspec(allocate(".CRT$XLB")) PIMAGE_TLS_CALLBACK const RizColdTlsSlot=&RizColdTls;
extern "C" __declspec(allocate(".CRT$XIC")) int(__cdecl* const RizColdCSlot)()=&CInitialize;
extern "C" __declspec(allocate(".CRT$XCU")) void(__cdecl* const RizColdCppSlot)()=&CppInitialize;
#pragma comment(linker,"/INCLUDE:_tls_used")
#pragma comment(linker,"/INCLUDE:RizColdTlsSlot")
#pragma comment(linker,"/INCLUDE:RizColdCSlot")
#pragma comment(linker,"/INCLUDE:RizColdCppSlot")
BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)Run(RizWinFixture::ImporterDll);return TRUE;}
RIZ_COLD_IMPORTER_API std::uint32_t __cdecl RizColdImporter_RunOutsideLoader()
{if(InterlockedCompareExchange(&phaseMask,0,0)!=0xf0)return 1;Run(RizWinFixture::ImporterOutside);return InterlockedCompareExchange64(&failures,0,0)==0?0u:1u;}
RIZ_COLD_IMPORTER_API std::uint32_t __cdecl RizColdImporter_GetSnapshot(RizCold::Snapshot* output)
{
    if(!output||output->version!=RizCold::Version||output->byteSize!=sizeof(*output))return 1;
    snapshot.version=RizCold::Version;snapshot.byteSize=sizeof(snapshot);
    snapshot.phaseMask=static_cast<std::uint32_t>(InterlockedCompareExchange(&phaseMask,0,0));
    snapshot.rowCount=static_cast<std::uint32_t>(InterlockedCompareExchange(&rowCount,0,0));
    snapshot.checks=static_cast<std::uint64_t>(InterlockedCompareExchange64(&checks,0,0));
    snapshot.failures=static_cast<std::uint64_t>(InterlockedCompareExchange64(&failures,0,0));
    *output=snapshot;return 0;
}
