#include "riz_engine_transport.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportGameProfile.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportAdmissionFacts.h"
#include "../RizToolbox.EngineBootstrap/EngineTransportAdmissionSha256.h"
#include <intrin.h>

#if defined(RIZ_ENGINE_TRANSPORT_DOORSTOP_OWNED) && RIZ_ENGINE_TRANSPORT_DOORSTOP_OWNED != 1
#error Owned Doorstop must be omitted or exactly one.
#endif
#pragma comment(linker,"/INCLUDE:RizEngineTransport_GetVersion")
#pragma comment(linker,"/INCLUDE:RizEngineTransport_GetInfoV5")
#pragma comment(linker,"/INCLUDE:RizEngineTransport_VerifyV5")
#pragma comment(linker,"/INCLUDE:RizEngineTransport_AbortAfterPublication")
extern "C" { RizEngineTransportDoorstopRecordV5 RizEngineTransportDoorstopRecord{}; }
namespace {
namespace Layout=RizEngineTransportPublicationLayout;
namespace Facts=RizEngineTransportGameProfile;
using U8=std::uint8_t;using U32=std::uint32_t;using U64=std::uint64_t;
auto& record=RizEngineTransportDoorstopRecord;
volatile LONG claimed{},abortState{},retainedResponsibility{};
constexpr U32 ReadyAdmission=1,NoWriteAdmission=2;
#ifdef RIZ_ENGINE_TRANSPORT_DOORSTOP_OWNED
constexpr U32 ExpectedProfile=1,ExpectedMainProof=1;
#else
constexpr U32 ExpectedProfile=2,ExpectedMainProof=2;
#endif
bool Equal(const void* a,const void* b,std::size_t count){
    const auto* left=static_cast<const volatile U8*>(a);const auto* right=static_cast<const volatile U8*>(b);
    for(std::size_t i=0;i<count;++i)if(left[i]!=right[i])return false;return true;
}
bool Empty(const void* data,std::size_t count){const auto* bytes=static_cast<const volatile U8*>(data);
    for(std::size_t i=0;i<count;++i)if(bytes[i])return false;return true;}
void Stage(U32 stage){InterlockedExchange(reinterpret_cast<volatile LONG*>(&record.stage),static_cast<LONG>(stage));}
[[noreturn]] void Fatal(){Stage(RizTransportProxyFatal);RaiseFailFastException(nullptr,nullptr,0);
    TerminateProcess(GetCurrentProcess(),0xe052495c);for(;;)__fastfail(7);}
bool Responsibility(const RizEngineTransportPublicationV5& value){
    if(value.configuredFamilies||value.boundFamilies||value.retainedFamilies||value.writeAttemptedWindows
        ||value.committedWindows||value.outstandingProtectionWindows||value.retainedResources
        ||value.sourceWriteAttempts||value.sourceWriteCompletions||value.protectionOutstanding)return true;
    // Inspect all fixed-capacity rows even if a malformed count would hide one.
    for(const auto& row:value.families)if(row.configureAttempts||row.configureSucceeded||row.retainedClaim||row.flags)return true;
    for(const auto& row:value.windows)if(row.sourceWriteAttempts||row.sourceBytesWritten||row.protectionOutstanding
        ||row.protectSucceeded||row.restoreAttempts||row.flushAttempts)return true;
    for(const auto& row:value.resources)if(row.allocationBase||row.tableAddress||row.allocationSucceeded
        ||row.tableRegisterAttempts||row.tableRegisterSucceeded||row.rootRetained||row.protectionOutstanding
        ||row.tableRegistered||row.allocationOwned||row.flags)return true;
    return false;
}
void Remember(const RizEngineTransportPublicationV5& value){if(Responsibility(value))InterlockedExchange(&retainedResponsibility,1);
    record.retainedResponsibility=static_cast<U32>(InterlockedCompareExchange(&retainedResponsibility,0,0));}
U32 Read(RizEngineTransportPublicationV5* value,DWORD incoming){
    __stosb(reinterpret_cast<U8*>(value),0,sizeof(*value));value->version=5;value->byteSize=sizeof(*value);
    ++record.infoReads;SetLastError(incoming);const U32 result=RizEngineTransport_GetInfoV5(value);Remember(*value);return result;
}
bool Envelope(const RizEngineTransportPublicationV5& value){
    if(value.version!=5||value.byteSize!=sizeof(value)||value.schemaMagic!=Layout::Magic||value.profile!=ExpectedProfile
        ||value.claimed!=1||value.processId!=GetCurrentProcessId()||!value.ownerThread||!value.producerThread
        ||value.attachReason!=DLL_PROCESS_ATTACH||value.startupStatic>1||value.state>RizEngineTransportUnknownV5
        ||value.moduleCount!=5||value.typedCount!=13||value.siteCount!=23||value.windowCount!=36||value.familyCount!=12
        ||value.runtimeFunctionCount>128||value.resourceCount>23||value.eventCapacity!=1024||!value.eventCount
        ||value.eventCount>1024||value.eventOverflow||value.verifyFailures||value.reserved0)return false;
    if((value.configuredFamilies|value.boundFamilies|value.retainedFamilies)&~Layout::AllFamilies)return false;
    if((value.preparedWindows|value.writeAttemptedWindows|value.committedWindows|value.outstandingProtectionWindows)&~Layout::AllWindows)return false;
    if(value.retainedResources&~((U64{1}<<23)-1))return false;
    for(U32 i=0;i<value.eventCount;++i){const auto& e=value.events[i];if(e.sequence!=U64{i}+1||!e.stage
        ||e.stage>RizEngineTransportEventsOverflowedV5||e.objectKind>RizEngineTransportResourceObjectV5
        ||e.state>RizEngineTransportUnknownV5)return false;}
    return Empty(value.events+value.eventCount,(1024-value.eventCount)*sizeof(value.events[0]));
}
bool NoWrite(const RizEngineTransportPublicationV5& value){return Envelope(value)
    &&value.state==RizEngineTransportDisabledNoSourceWriteV5&&value.failure>RizEngineTransportNoFailureV5
    &&value.failure<=RizEngineTransportInvalidSnapshotV5&&!Responsibility(value)
    &&!InterlockedCompareExchange(&retainedResponsibility,0,0);}
bool Within(U64 address,U64 bytes,const RizEngineTransportModuleV5& module){return address>=module.module
    &&address-module.module<module.imageSize&&bytes<=module.imageSize-(address-module.module);}
bool Rx(U64 at,U64 bytes,U64 allocation,DWORD type){if(!at||!bytes||at>~U64{0}-bytes)return false;
    const U64 end=at+bytes;while(at<end){MEMORY_BASIC_INFORMATION m{};
        if(VirtualQuery(reinterpret_cast<void*>(at),&m,sizeof(m))!=sizeof(m)||m.State!=MEM_COMMIT
            ||m.Protect!=PAGE_EXECUTE_READ||m.Type!=type||reinterpret_cast<U64>(m.AllocationBase)!=allocation)return false;
        const U64 next=reinterpret_cast<U64>(m.BaseAddress)+m.RegionSize;if(next<=at)return false;at=next<end?next:end;}return true;}
bool Ready(const RizEngineTransportPublicationV5& value){
    if(!Envelope(value)||value.state!=RizEngineTransportReadyV5||value.failure||!value.startupStatic||!value.epoch
        ||!value.mainThread||value.mainThreadProof!=ExpectedMainProof||!value.snapshotSequence
        ||value.mainThread!=record.threadId||value.producerThread!=record.threadId||value.ownerThread!=record.threadId
        ||value.configuredFamilies!=Layout::AllFamilies||value.boundFamilies!=Layout::AllFamilies||value.retainedFamilies!=Layout::AllFamilies
        ||value.preparedWindows!=Layout::AllWindows||value.writeAttemptedWindows!=Layout::AllWindows||value.committedWindows!=Layout::AllWindows
        ||value.sourceWriteAttempts!=36||value.sourceWriteCompletions!=36||value.protectionOutstanding||value.outstandingProtectionWindows
        ||value.runtimeFunctionCount!=67||value.resourceCount!=1||value.retainedResources!=1
        ||Empty(value.fixedContractSha256,32)||Empty(value.dependencyGraphSha256,32))return false;
    if constexpr(ExpectedProfile==2){
        namespace AF=RizEngineTransportAdmissionFacts;
        namespace AC=RizEngineTransportAdmissionCrypto;
        if constexpr(!AF::InteriorEntryClosureVerified||!AF::ProductionColdTraceVerified||!AF::ProductionAdmissionVerified)return false;
        if(value.admission.version!=5||value.admission.byteSize!=sizeof(RizEngineTransportAdmissionReceiptV5)
            ||(value.admission.witnessFlags&RizEngineTransportWitnessAllV5)!=RizEngineTransportWitnessAllV5
            ||value.admission.earliestThreadId!=value.mainThread||!value.admission.preWriteThreadCount
            ||value.admission.nonMainInteriorRipCount||!value.admission.postCommitThreadCount)return false;
        U8 expectedFacts[32]{};
        if(!AC::ParseHex32(Facts::FactsSha256,expectedFacts)||!Equal(value.fixedContractSha256,expectedFacts,32))return false;
        U8 expectedAuthority[32]{},expectedExclusivity[32]{};
        if(!AC::ComputeAdmissionAuthoritySha256(value,AF::InteriorClosureSha256,AF::ColdTraceSha256,expectedAuthority))return false;
        AC::ComputeExclusivitySha256(value,value.admission.witnessFlags,expectedExclusivity);
        if(!Equal(value.admission.admissionAuthoritySha256,expectedAuthority,32)
            ||!Equal(value.admission.exclusivitySha256,expectedExclusivity,32))return false;
    }
    const U64 modules[]{value.bootstrapModule,value.kernelModule,value.targetModule,value.executableModule,value.doorstopModule};
    for(U32 i=0;i<5;++i){const auto& m=value.modules[i];if(m.kind!=i||m.module!=modules[i]||!m.module
        ||m.machine!=0x8664||!m.imageSize||!m.fileBytes||!m.heldFile||m.flags!=(i==0?7u:15u)||Empty(m.sha256,32))return false;
        for(U32 prior=0;prior<i;++prior)if(modules[prior]==modules[i])return false;}
    if(value.doorstopModule!=record.selfModule)return false;
    for(U32 i=0;i<13;++i){const auto& row=value.typed[i];const auto& fact=Facts::Functions[i];
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=i||row.flags!=7||row.moduleIndex!=2||row.windowIndex!=i
            ||row.rva!=fact.beginRva||row.endRva!=fact.endRva||row.prefixBytes!=fact.prefixBytes||row.sourceBodyBytes!=fact.bodyBytes
            ||row.module!=value.targetModule||row.target!=value.targetModule+fact.beginRva||row.continuation!=row.target+fact.prefixBytes
            ||!row.helperEpoch||row.helperSlot!=28||row.originalModuleIndex!=0||row.originalRfFirst!=27+i||row.originalRfCount!=1
            ||!Within(row.original,row.originalBytes,value.modules[0])||!Within(row.entry,1,value.modules[1])
            ||!Rx(row.target,row.prefixBytes,value.targetModule,MEM_IMAGE)||!Rx(row.original,row.originalBytes,value.bootstrapModule,MEM_IMAGE)
            ||!Rx(row.entry,1,value.kernelModule,MEM_IMAGE))return false;}
    for(U32 i=0;i<12;++i){const auto& row=value.families[i];if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=i
        ||row.module!=value.kernelModule||row.slot!=28||row.flags!=7||row.configureAttempts!=1||row.configureSucceeded!=1||row.retainedClaim!=1
        ||!row.epoch||row.helperUnknown||row.lastWin32Error||row.reserved0||Empty(row.configSha256,32)
        ||(i>=4&&row.mainOsThread!=value.mainThread))return false;}
    const auto& resource=value.resources[0];if(resource.version!=5||resource.byteSize!=sizeof(resource)||resource.index||resource.flags!=15
        ||!resource.allocationBase||resource.allocationBytes<8192||resource.tableBase!=value.targetModule
        ||resource.tableAddress!=resource.allocationBase||resource.tableCount!=23||resource.rfFirst!=40||resource.rfCount!=27
        ||resource.allocationSucceeded!=1||resource.rxProtectSucceeded!=1||resource.tableRegisterSucceeded!=1
        ||resource.rootRetained!=1||resource.tableRegistered!=1||resource.allocationOwned!=1||resource.protectionOutstanding
        ||resource.observedProtection!=PAGE_EXECUTE_READ||!Rx(resource.allocationBase,resource.allocationBytes,resource.allocationBase,MEM_PRIVATE))return false;
    for(U32 i=0;i<36;++i){const auto& row=value.windows[i];const auto& fixed=Facts::PhysicalWindows[i];
        const U32 expanded=i>=13&&Facts::Sites[i-13].expandedPhysicalBlock?192u:0u;
        if(row.version!=5||row.byteSize!=sizeof(row)||row.index!=i||row.flags!=(63u|expanded)||row.moduleIndex!=2
            ||row.physicalRva!=fixed.beginRva||row.physicalBytes!=fixed.byteCount||row.source!=value.targetModule+fixed.beginRva
            ||row.protectAttempts!=1||row.protectSucceeded!=1||row.sourceWriteAttempts!=1||row.sourceBytesWritten!=fixed.byteCount
            ||row.readbackAttempts!=1||row.readbackVerified!=1||row.restoreAttempts!=1||row.restoreSucceeded!=1||row.flushAttempts!=1||row.flushSucceeded!=1
            ||row.protectionOutstanding||row.lastWin32Error||row.observedProtection!=PAGE_EXECUTE_READ
            ||!row.writeSequence||!row.commitSequence||!row.readbackSequence||row.readbackBytes!=fixed.byteCount
            ||!Equal(row.expectedBytes,fixed.bytes,fixed.byteCount)||!Equal(row.patchBytes,row.observedBytes,fixed.byteCount)
            ||!Rx(row.source,fixed.byteCount,value.targetModule,MEM_IMAGE)||!Equal(reinterpret_cast<const void*>(row.source),row.patchBytes,fixed.byteCount))return false;
        for(U32 prior=0;prior<i;++prior){const auto& p=value.windows[prior];if(row.source<p.source+p.physicalBytes&&p.source<row.source+row.physicalBytes)return false;}}
    return true;
}
bool Verified(const RizEngineTransportPublicationV5& before,RizEngineTransportPublicationV5* after,DWORD incoming){
    ++record.verifyAttempts;SetLastError(incoming);record.verifyResult=RizEngineTransport_VerifyV5();record.verifyLastError=GetLastError();
    if(record.verifyResult||record.verifyLastError!=incoming)return false;
    record.afterInfoResult=Read(after,incoming);record.afterInfoLastError=GetLastError();
    return !record.afterInfoResult&&record.afterInfoLastError==incoming&&Equal(&before,after,sizeof(before));
}
}
extern "C" BOOL __cdecl RizEngineDoorstopOnFailure(std::uint32_t failure,DWORD incoming){
    InterlockedExchange(reinterpret_cast<volatile LONG*>(&record.rejected),1);
    InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(&record.failure),static_cast<LONG>(failure),0);
    record.result=FALSE;record.bodyLastError=incoming;Stage(RizTransportProxyRejected);
    __try {record.abortInfoResult=Read(&record.terminal,incoming);record.abortInfoLastError=GetLastError();
        const bool safe=record.nativeVersion==5&&!record.abortInfoResult&&record.abortInfoLastError==incoming&&NoWrite(record.terminal);
        const LONG previous=InterlockedCompareExchange(&abortState,1,0);if(previous==1)Fatal();
        if(!previous){Stage(RizTransportProxyAborting);++record.abortAttempts;SetLastError(incoming);
            RizEngineTransport_AbortAfterPublication();record.abortLastError=GetLastError();
            record.abortInfoResult=Read(&record.terminal,incoming);record.abortInfoLastError=GetLastError();InterlockedExchange(&abortState,2);}
        if(!safe||record.proxyIatResponsibility||(!previous&&record.abortLastError!=incoming)||record.abortInfoResult
            ||record.abortInfoLastError!=incoming||!NoWrite(record.terminal))Fatal();
    }__except(EXCEPTION_EXECUTE_HANDLER){record.exceptionCode=GetExceptionCode();Fatal();}
    Stage(RizTransportProxyAborted);record.exitLastError=incoming;SetLastError(incoming);return FALSE;
}
extern "C" BOOL __cdecl RizEngineDoorstopBeforeAttach(HINSTANCE module,DWORD reason,LPVOID reserved){
    const DWORD incoming=GetLastError();InterlockedIncrement(reinterpret_cast<volatile LONG*>(&record.attempts));
    if(InterlockedCompareExchange(&claimed,1,0))return RizEngineDoorstopOnFailure(RizTransportProxyRepeatedAttach,incoming);
    record.version=5;record.byteSize=sizeof(record);record.selfModule=reinterpret_cast<U64>(module);record.reservedPointer=reinterpret_cast<U64>(reserved);
    record.attachReason=reason;record.startupStatic=reserved?1u:0u;record.threadId=GetCurrentThreadId();record.processId=GetCurrentProcessId();
    record.entryLastError=incoming;record.verifyResult=0xffffffffu;Stage(RizTransportProxyEntered);
    __try {SetLastError(incoming);record.nativeVersion=RizEngineTransport_GetVersion();record.versionLastError=GetLastError();
        if(record.nativeVersion!=5)return RizEngineDoorstopOnFailure(RizTransportProxyBadVersion,incoming);
        if(record.versionLastError!=incoming)return RizEngineDoorstopOnFailure(RizTransportProxyLastErrorChanged,incoming);
        record.infoResult=Read(&record.before,incoming);record.infoLastError=GetLastError();Stage(RizTransportProxySnapshot);
        if(record.infoResult)return RizEngineDoorstopOnFailure(RizTransportProxyUnreadable,incoming);
        if(record.infoLastError!=incoming)return RizEngineDoorstopOnFailure(RizTransportProxyLastErrorChanged,incoming);
        if(reason!=DLL_PROCESS_ATTACH||record.before.ownerThread!=record.threadId)return RizEngineDoorstopOnFailure(RizTransportProxyBadPublication,incoming);
        if(Ready(record.before)) {HMODULE pinned{};
            if(!reserved||!Verified(record.before,&record.after,incoming))return RizEngineDoorstopOnFailure(RizTransportProxyVerifyFailed,incoming);
            if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN|GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<LPCWSTR>(module),&pinned)
                ||pinned!=module)return RizEngineDoorstopOnFailure(RizTransportProxyPinFailed,incoming);
            record.selfPinned=1;record.admission=ReadyAdmission;Stage(RizTransportProxyVerified);
        }else if(NoWrite(record.before)){record.admission=NoWriteAdmission;Stage(RizTransportProxyDisabled);}
        else return RizEngineDoorstopOnFailure(RizTransportProxyBadPublication,incoming);
        record.afterInfoResult=Read(&record.after,incoming);record.afterInfoLastError=GetLastError();
        if(record.afterInfoResult||record.afterInfoLastError!=incoming||!Equal(&record.before,&record.after,sizeof(record.before)))
            return RizEngineDoorstopOnFailure(RizTransportProxyChangedPublication,incoming);
    }__except(EXCEPTION_EXECUTE_HANDLER){record.exceptionCode=GetExceptionCode();return RizEngineDoorstopOnFailure(RizTransportProxyNativeException,incoming);}
    record.result=TRUE;record.exitLastError=incoming;Stage(RizTransportProxyBody);SetLastError(incoming);return TRUE;
}
extern "C" BOOL __cdecl RizEngineDoorstopAfterAttach(BOOL bodyResult,DWORD incoming){record.bodyLastError=incoming;
    if(!bodyResult||InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(&record.rejected),0,0))return RizEngineDoorstopOnFailure(RizTransportProxyBodyFailed,incoming);
    __try {record.afterInfoResult=Read(&record.after,incoming);record.afterInfoLastError=GetLastError();
        if(record.afterInfoResult||record.afterInfoLastError!=incoming||!Equal(&record.before,&record.after,sizeof(record.before)))
            return RizEngineDoorstopOnFailure(RizTransportProxyChangedPublication,incoming);
        if(record.admission==ReadyAdmission){if(!Ready(record.after)||!Verified(record.before,&record.terminal,incoming))
                return RizEngineDoorstopOnFailure(RizTransportProxyVerifyFailed,incoming);}
        else if(record.admission!=NoWriteAdmission||!NoWrite(record.after))return RizEngineDoorstopOnFailure(RizTransportProxyBadPublication,incoming);
    }__except(EXCEPTION_EXECUTE_HANDLER){record.exceptionCode=GetExceptionCode();return RizEngineDoorstopOnFailure(RizTransportProxyNativeException,incoming);}
    record.result=TRUE;record.exitLastError=incoming;Stage(RizTransportProxyComplete);SetLastError(incoming);return TRUE;
}
extern "C" BOOL __cdecl RizEngineDoorstopBeforeIat(){const DWORD incoming=GetLastError();++record.proxyIatAttempts;
    if(InterlockedCompareExchange(reinterpret_cast<volatile LONG*>(&record.rejected),0,0)){SetLastError(incoming);return FALSE;}
    if(!record.selfPinned){HMODULE pinned{};if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN|GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
            reinterpret_cast<LPCWSTR>(record.selfModule),&pinned)||reinterpret_cast<U64>(pinned)!=record.selfModule)
            return RizEngineDoorstopOnFailure(RizTransportProxyPinFailed,incoming);record.selfPinned=1;}
    InterlockedExchange(reinterpret_cast<volatile LONG*>(&record.proxyIatResponsibility),1);SetLastError(incoming);return TRUE;
}
extern "C" void __cdecl RizEngineDoorstopOnDetach(LPVOID reserved){const DWORD incoming=GetLastError();++record.detachAttempts;
    record.processTermination=reserved?1u:0u;Stage(RizTransportProxyDetaching);
    if(!reserved)(void)RizEngineDoorstopOnFailure(RizTransportProxyDynamicDetach,incoming);SetLastError(incoming);
}
