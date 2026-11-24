#define RIZ_PRODUCTION_COLD_IMPORTER_BUILD
#include "Api.h"

namespace {
using namespace RizProductionCold;
Snapshot snapshot{};
volatile LONG phaseMask{},callCount{};
volatile LONG64 checks{},failures{};
void Check(bool value){InterlockedIncrement64(&checks);if(!value)InterlockedIncrement64(&failures);}
RizWinFixture::Api Imported()
{return {&WinHttpOpen,&WinHttpGetProxyForUrl,&WinHttpGetIEProxyConfigForCurrentUser,&WinHttpCloseHandle};}
bool NoWrites(const RizDoorstopEnginePublicationV4& v)
{
    if(v.version!=4||v.byteSize!=4096||v.state!=5||!v.failure||v.profile!=2||v.claimed!=1
        ||v.configuredMask||v.boundMask||v.committedMask||v.sourceWriteAttempts||v.sourceWriteCompletions||v.protectionOutstanding)return false;
    for(const auto& row:v.rows)if(row.entry||row.helperEpoch||row.helperSlot||row.protectAttempts||row.protectSucceeded
        ||row.sourceWriteAttempts||row.sourceBytesWritten||row.readbackAttempts||row.readbackVerified||row.restoreAttempts
        ||row.restoreSucceeded||row.flushAttempts||row.flushSucceeded||row.protectionOutstanding)return false;
    return true;
}
bool Capture(unsigned phase,const RizWinFixture::Api& api)
{
    auto& proof=snapshot.phases[phase-1];proof.phase=phase;proof.threadId=GetCurrentThreadId();
    proof.lastErrorBefore=0x6c810000u+phase;SetLastError(proof.lastErrorBefore);
    proof.matched=Match(api,proof)?1u:0u;Check(proof.matched==1);
    if(!proof.matched){proof.lastErrorAfter=GetLastError();return false;}
    Check(ReadReference(proof));if(!proof.referenceMatched)return false;
    // Read only the precisely generated private MAP record. Calling even
    // GetInfoV4 here would alter the observation this fixture must measure.
    auto* out=reinterpret_cast<volatile unsigned char*>(&proof.record);
    const auto* in=reinterpret_cast<const volatile unsigned char*>(proof.recordAddress);
    for(std::size_t i=0;i<sizeof(proof.record);++i)out[i]=in[i];
    proof.recordReadBytes=sizeof(proof.record);proof.lastErrorAfter=GetLastError();
    Check(proof.lastErrorAfter==proof.lastErrorBefore);
    const auto& r=proof.record;
    const bool ready=r.version==1&&r.byteSize==12448&&r.stage==RizEngineDoorstopComplete&&r.attempts==1
        &&r.processId==GetCurrentProcessId()&&r.threadId==proof.threadId&&r.attachReason==DLL_PROCESS_ATTACH&&r.startupStatic==1
        &&r.nativeVersion==4&&!r.infoResult&&!r.afterInfoResult&&r.admission==RizEngineDoorstopNoWriteAdmission&&r.result==1
        &&!r.failure&&!r.abortAttempts&&!r.rejected&&!r.retainedResponsibility&&!r.verifyAttempts&&r.infoReads==3
        &&r.selfModule==proof.proxyBase&&NoWrites(r.before)&&Equal(&r.before,&r.after,sizeof(r.before));
    Check(ready);return ready;
}
void Append(const Call& call)
{
    const auto index=InterlockedIncrement(&callCount)-1;
    if(index<0||static_cast<unsigned>(index)>=CallCount){Check(false);return;}snapshot.calls[index]=call;
}
void Close(const RizWinFixture::Api& api,unsigned phase,HINTERNET handle)
{
    Call call{};call.phase=phase;call.kind=Kind::Close;call.threadId=GetCurrentThreadId();call.apiAddress=reinterpret_cast<std::uint64_t>(api.close);
    call.handle=reinterpret_cast<std::uint64_t>(handle);call.lastErrorBefore=0x6c820000u+phase;SetLastError(call.lastErrorBefore);
    call.rawReturn=static_cast<std::uint32_t>(api.close(handle));call.lastErrorAfter=GetLastError();
    if(phase!=Main){
        const auto& proof=snapshot.phases[phase-1];Check(proof.referenceMatched==1);
        call.systemAddress=proof.resolver.addresses[3];const auto direct=reinterpret_cast<RizWinFixture::CloseFn>(call.systemAddress);
        SetLastError(call.lastErrorBefore);call.systemRawReturn=static_cast<std::uint32_t>(direct(nullptr));call.systemLastError=GetLastError();
        Check(call.rawReturn==call.systemRawReturn&&call.lastErrorAfter==call.systemLastError);
    }
    Append(call);
    Check(handle?call.rawReturn!=0:call.rawReturn==0);
}
void Run(unsigned phase)
{
    Check(phase>=Tls&&phase<=Main);const auto bit=static_cast<LONG>(1u<<phase);Check((InterlockedOr(&phaseMask,bit)&bit)==0);
    const auto api=Imported();if(!Capture(phase,api))return;
    if(phase!=Main){Close(api,phase,nullptr);return;}
    Call call{};call.phase=phase;call.kind=Kind::Open;call.threadId=GetCurrentThreadId();call.apiAddress=reinterpret_cast<std::uint64_t>(api.open);
    call.lastErrorBefore=0x6c830000u+phase;SetLastError(call.lastErrorBefore);
    const auto session=api.open(L"RizToolbox.OwnedProductionCold",WINHTTP_ACCESS_TYPE_NO_PROXY,
        WINHTTP_NO_PROXY_NAME,WINHTTP_NO_PROXY_BYPASS,0);
    call.rawReturn=reinterpret_cast<std::uint64_t>(session);call.lastErrorAfter=GetLastError();Append(call);Check(session!=nullptr);
    if(session)Close(api,phase,session);
}
}
extern "C" void NTAPI RizProductionColdTls(PVOID,DWORD reason,PVOID){if(reason==DLL_PROCESS_ATTACH)Run(RizProductionCold::Tls);}
#pragma section(".CRT$XLB",long,read)
extern "C" __declspec(allocate(".CRT$XLB")) PIMAGE_TLS_CALLBACK const RizProductionColdTlsSlot=&RizProductionColdTls;
#pragma comment(linker,"/INCLUDE:_tls_used")
#pragma comment(linker,"/INCLUDE:RizProductionColdTlsSlot")
BOOL WINAPI DllMain(HINSTANCE,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)Run(RizProductionCold::Dll);return TRUE;}
RIZ_PRODUCTION_COLD_API std::uint32_t __cdecl RizProductionCold_Run()
{if(InterlockedCompareExchange(&phaseMask,0,0)!=6)return 1;Run(RizProductionCold::Main);return InterlockedCompareExchange64(&failures,0,0)?1u:0u;}
RIZ_PRODUCTION_COLD_API std::uint32_t __cdecl RizProductionCold_GetSnapshot(RizProductionCold::Snapshot* output)
{
    if(!output||output->version!=RizProductionCold::Version||output->byteSize!=sizeof(*output))return 1;
    snapshot.version=RizProductionCold::Version;snapshot.byteSize=sizeof(snapshot);snapshot.phaseCount=RizProductionCold::PhaseCount;
    snapshot.phaseMask=static_cast<std::uint32_t>(InterlockedCompareExchange(&phaseMask,0,0));
    snapshot.callCount=static_cast<std::uint32_t>(InterlockedCompareExchange(&callCount,0,0));
    snapshot.checks=static_cast<std::uint64_t>(InterlockedCompareExchange64(&checks,0,0));
    snapshot.failures=static_cast<std::uint64_t>(InterlockedCompareExchange64(&failures,0,0));*output=snapshot;return 0;
}
