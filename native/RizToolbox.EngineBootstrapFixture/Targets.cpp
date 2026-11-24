#ifndef RIZ_ENGINE_OWNED_TARGETS_BUILD
#define RIZ_ENGINE_OWNED_TARGETS_BUILD
#endif
#include "FixtureApi.h"

// Exact non-NOP instructions and compiler-independent unwind descriptions are
// the preserved FixedTargets.asm implementation in this directory.
#pragma comment(linker,"/EXPORT:RizFixedOwnedMarker,DATA")
#pragma comment(linker,"/EXPORT:RizFixedOwnedStart")
#pragma comment(linker,"/EXPORT:RizFixedOwnedExecute")
#pragma comment(linker,"/EXPORT:RizFixedOwnedRelease")
#pragma comment(linker,"/EXPORT:RizFixedOwnedDetach")
#define EXPORT_LABEL(name) __pragma(comment(linker,"/EXPORT:" #name))
EXPORT_LABEL(RizFixedOwnedStartPrefixEnd)
EXPORT_LABEL(RizFixedOwnedStartPrologEnd)
EXPORT_LABEL(RizFixedOwnedStartBodyReturn)
EXPORT_LABEL(RizFixedOwnedStartEnd)
EXPORT_LABEL(RizFixedOwnedExecutePrefixEnd)
EXPORT_LABEL(RizFixedOwnedExecutePrologEnd)
EXPORT_LABEL(RizFixedOwnedExecuteBodyReturn)
EXPORT_LABEL(RizFixedOwnedExecuteEnd)
EXPORT_LABEL(RizFixedOwnedReleasePrefixEnd)
EXPORT_LABEL(RizFixedOwnedReleasePrologEnd)
EXPORT_LABEL(RizFixedOwnedReleaseBodyReturn)
EXPORT_LABEL(RizFixedOwnedReleaseEnd)
EXPORT_LABEL(RizFixedOwnedDetachPrefixEnd)
EXPORT_LABEL(RizFixedOwnedDetachPrologEnd)
EXPORT_LABEL(RizFixedOwnedDetachBodyReturn)
EXPORT_LABEL(RizFixedOwnedDetachEnd)
#undef EXPORT_LABEL

namespace {
using namespace RizEngineOwned;
Snapshot evidence{};
volatile LONG nextRow{},phaseMask{},access{},captureGate{};
volatile LONG64 checks{},failures{},bodyCalls[4]{};
__declspec(thread) RizFixedCase* current;

void Check(bool okay)
{InterlockedIncrement64(&checks);if(!okay)InterlockedIncrement64(&failures);}
void Copy(void* output,const void* input,std::size_t bytes)
{auto* to=static_cast<unsigned char*>(output);const auto* from=static_cast<const unsigned char*>(input);for(std::size_t i=0;i<bytes;++i)to[i]=from[i];}
std::uint32_t ReadCount(const RizFixedCase& test)
{std::uint32_t count{};Copy(&count,test.node+0x60,sizeof(count));return count;}
void Counters(std::uint64_t* output)
{for(unsigned i=0;i<4;++i)output[i]=static_cast<std::uint64_t>(InterlockedCompareExchange64(&bodyCalls[i],0,0));}
bool BeginCall()
{
    LONG seen=InterlockedCompareExchange(&access,0,0);
    for(unsigned n=0;n<32;++n){if(seen<0)return false;const auto actual=InterlockedCompareExchange(&access,seen+1,seen);if(actual==seen)return true;seen=actual;}
    return false;
}
Row* Reserve()
{
    LONG seen=InterlockedCompareExchange(&nextRow,0,0);
    for(;;){if(seen<0||static_cast<std::uint32_t>(seen)>=Capacity)return nullptr;
        const auto actual=InterlockedCompareExchange(&nextRow,seen+1,seen);if(actual==seen)return &evidence.rows[seen];seen=actual;}
}
bool Ready(const RizEnginePublicationV4& info)
{
    return info.version==4&&info.byteSize==sizeof(info)&&info.state==RizEnginePublicationReady&&info.failure==0
        &&info.profile==RizEngineOwnedProfile&&info.startupStatic==1&&info.claimed==1&&info.epoch!=0
        &&info.processId==GetCurrentProcessId()&&info.configuredMask==15&&info.boundMask==15&&info.committedMask==15
        &&info.sourceWriteAttempts==4&&info.sourceWriteCompletions==4&&info.protectionOutstanding==0&&info.overflow==0;
}
void Capture(Row& row,bool after)
{
    // The workload calls targets concurrently, while these read-only evidence
    // copies are serialized so the owner's documented nonwaiting copy gate is
    // not deliberately made busy by the fixture's own other readers.
    const auto deadline=GetTickCount64()+5000;
    while(InterlockedCompareExchange(&captureGate,1,0)!=0){if(GetTickCount64()>=deadline)RaiseFailFastException(nullptr,nullptr,0);Sleep(1);}
    auto& value=after?row.after:row.before;value.version=4;value.byteSize=sizeof(value);
    const auto seed=0x69700000u+row.phase*0x10000u+row.kind*0x100u+row.mode*2u+(after?1u:0u);
    SetLastError(seed);auto result=RizEngineFirstInstall_GetInfoV4(&value);auto error=GetLastError();
    const auto verified=RizEngineFirstInstall_VerifyV4();Check(GetLastError()==error);
    // A failed verification can permanently record Unknown. Capture that real
    // final state rather than serializing the earlier pre-verification copy.
    if(verified){value.version=4;value.byteSize=sizeof(value);SetLastError(seed);result=RizEngineFirstInstall_GetInfoV4(&value);error=GetLastError();}
    if(after){row.infoAfterResult=result;row.verifyAfter=verified;row.infoAfterErrorBefore=seed;row.infoAfterErrorAfter=error;}
    else{row.infoBeforeResult=result;row.verifyBefore=verified;row.infoBeforeErrorBefore=seed;row.infoBeforeErrorAfter=error;}
    Check(result==0);Check(error==seed);
    // Abnormal-call children retain and expose any permanent Unknown result.
    // Normal calls and all true loader initializer observations require Ready.
    if(row.mode==0){Check(verified==0);Check(Ready(value));}
    InterlockedExchange(&captureGate,0);
}
RizFixedCase* Enter(unsigned kind)
{
    if(!current||kind>=4)RaiseFailFastException(nullptr,nullptr,0);
    ++current->calls[kind];InterlockedIncrement64(&bodyCalls[kind]);
    if(!current->nested&&current->throwKind==kind){
        if(current->throwMode==1){ULONG_PTR values[]{static_cast<ULONG_PTR>(RizFixedPayload),kind};RaiseException(RizFixedSehCode,0,2,values);}
        if(current->throwMode==2)throw RizFixedOriginalException{RizFixedPayload,kind};
    }
    return current;
}
void Call(std::uint32_t kind,RizFixedCase* test)
{
    switch(kind){
        case 0:test->rawReturn=RizFixedOwnedStart(test->owner,test->enumerator,test->name,&test->outputNode);break;
        case 1:test->rawReturn=RizFixedOwnedExecute(test->node,&test->outputByte);break;
        case 2:test->rawReturn=RizFixedOwnedRelease(test->node);break;
        default:RizFixedOwnedDetach(test->node);break;
    }
    test->returned=1;
}
int Filter(EXCEPTION_POINTERS* pointers,RizFixedCase* test)
{
    const auto& value=*pointers->ExceptionRecord;
    if((test->throwMode==1&&value.ExceptionCode==RizFixedSehCode)||(test->throwMode==2&&value.ExceptionCode==0xe06d7363u)){
        test->exceptionCode=value.ExceptionCode;test->exceptionFlags=value.ExceptionFlags;test->exceptionParameters=value.NumberParameters;
        if(test->throwMode==1){
            if(value.NumberParameters==2){test->exceptionPayload=value.ExceptionInformation[0];test->exceptionKind=value.ExceptionInformation[1];}
            else ++test->diagnosticsFailed;
            return EXCEPTION_EXECUTE_HANDLER;
        }
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
void CallSeh(std::uint32_t kind,RizFixedCase* test)
{__try{Call(kind,test);}__except(Filter(GetExceptionInformation(),test)){} }
void Invoke(std::uint32_t kind,RizFixedCase& test,Row& row)
{
    try{CallSeh(kind,&test);if(test.exceptionCode==RizFixedSehCode)row.caught=1;}
    catch(const RizFixedOriginalException& error){row.caught=2;test.exceptionPayload=error.payload;test.exceptionKind=error.kind;}
    catch(...){row.caught=3;++test.diagnosticsFailed;}
}
std::uint32_t Run(std::uint32_t phase,std::uint32_t kind,std::uint32_t mode,Row* output)
{
    if(kind>=4||mode>2||!BeginCall())return 2;
    auto* selected=Reserve();if(!selected){InterlockedDecrement(&access);return 2;}
    auto& row=*selected;row.version=Version;row.byteSize=sizeof(row);row.phase=phase;row.kind=kind;row.mode=mode;
    row.threadId=GetCurrentThreadId();row.sequence=static_cast<std::uint64_t>(selected-evidence.rows)+1;
    row.target=reinterpret_cast<std::uint64_t>(RizEngineOwned_GetTarget(kind));
    Capture(row,false);Counters(row.bodyBefore);
    RizFixedCase test{};test.throwKind=kind;test.throwMode=mode;
    const void* owner=test.owner;const void* cache=test.enumerator;const std::uint64_t handle=0xf123456789abcdefull;
    const std::uint32_t type=2,references=kind==2?1u:2u;
    Copy(test.node+0x10,&handle,sizeof(handle));Copy(test.node+0x18,&type,sizeof(type));Copy(test.node+0x20,&cache,sizeof(cache));
    Copy(test.node+0x58,&owner,sizeof(owner));Copy(test.node+0x60,&references,sizeof(references));row.nodeRefBefore=references;
    row.args[0]=reinterpret_cast<std::uint64_t>(kind==0?test.owner:test.node);
    if(kind==0){row.args[1]=reinterpret_cast<std::uint64_t>(test.enumerator);row.args[2]=reinterpret_cast<std::uint64_t>(test.name);row.args[3]=reinterpret_cast<std::uint64_t>(&test.outputNode);}
    if(kind==1)row.args[1]=reinterpret_cast<std::uint64_t>(&test.outputByte);
    auto* previous=RizFixedOwnedSetCase(&test);Invoke(kind,test,row);Check(RizFixedOwnedSetCase(previous)==&test);
    row.returned=test.returned;row.rawReturn=test.rawReturn;row.exceptionCode=test.exceptionCode;row.exceptionFlags=test.exceptionFlags;
    row.exceptionParameters=test.exceptionParameters;row.exceptionPayload=test.exceptionPayload;row.exceptionKind=test.exceptionKind;
    row.releaseCount=test.releaseCount;row.releaseZeroFlag=test.releaseZeroFlag;row.detachZero=test.detachZero;row.diagnosticsFailed=test.diagnosticsFailed;
    row.outputByte=test.outputByte;row.outputNodeMatches=test.outputNode==test.node?1u:0u;row.nodeRefAfter=ReadCount(test);
    for(unsigned i=0;i<4;++i){row.calls[i]=test.calls[i];const auto expected=(i==kind?1u:0u)+(!mode&&kind==0&&(i==1||i==2)?1u:0u);Check(row.calls[i]==expected);}
    Check(row.diagnosticsFailed==0);Check(row.returned==(mode?0u:1u));Check(row.caught==mode);
    if(mode){Check(row.exceptionPayload==RizFixedPayload&&row.exceptionKind==kind);Check(row.exceptionCode==(mode==1?RizFixedSehCode:0xe06d7363u));}
    else{const std::uint32_t returns[4]{0x81,0xa5c3fe81u,0xe7,0};Check(row.rawReturn==returns[kind]);
        if(kind==0)Check(row.outputNodeMatches==1&&row.outputByte==0x9d);if(kind==1)Check(row.outputByte==0x9d);
        if(kind==2||kind==0)Check(row.releaseCount==references-1&&row.releaseZeroFlag==((references-1)==0?1u:0u));}
    Check(row.nodeRefAfter==references-((kind==2||(!mode&&kind==0))?1u:0u));
    Counters(row.bodyAfter);Capture(row,true);
    if(output)*output=row;
    InterlockedDecrement(&access);return row.diagnosticsFailed==0&&row.returned==(mode?0u:1u)&&row.caught==mode?0u:1u;
}
}

RIZ_FIXED_TARGET_API RizFixedCase* __cdecl RizFixedOwnedSetCase(RizFixedCase* value)
{auto* previous=current;current=value;return previous;}
extern "C" std::uint8_t __cdecl RizFixedBodyStart(void* owner,void* enumerator,void* name,void** output)
{
    auto* test=Enter(0);if(owner!=test->owner||enumerator!=test->enumerator||name!=test->name||output!=&test->outputNode)++test->diagnosticsFailed;
    test->nested=1;if(RizFixedOwnedExecute(test->node,&test->outputByte)!=0xa5c3fe81u)++test->diagnosticsFailed;
    if(RizFixedOwnedRelease(test->node)!=0xe7u)++test->diagnosticsFailed;test->nested=0;*output=test->node;return 0x81;
}
extern "C" std::uint32_t __cdecl RizFixedBodyExecute(void* node,unsigned char* output)
{auto* test=Enter(1);if(node!=test->node||output!=&test->outputByte)++test->diagnosticsFailed;*output=0x9d;return 0xa5c3fe81u;}
extern "C" std::uint8_t __cdecl RizFixedBodyRelease(void* node,std::uint32_t afterCount,unsigned char zeroFlag)
{auto* test=Enter(2);test->releaseCount=afterCount;test->releaseZeroFlag=zeroFlag;if(node!=test->node||afterCount!=ReadCount(*test)||zeroFlag!=(afterCount==0?1u:0u))++test->diagnosticsFailed;return 0xe7;}
extern "C" void __cdecl RizFixedBodyDetach(void* node,std::uint32_t clearedEbp)
{auto* test=Enter(3);test->detachZero=clearedEbp;if(node!=test->node||clearedEbp)++test->diagnosticsFailed;}

RIZ_ENGINE_FIXTURE_API void* __cdecl RizEngineOwned_GetTarget(std::uint32_t kind)
{const void* const targets[4]{reinterpret_cast<const void*>(&RizFixedOwnedStart),reinterpret_cast<const void*>(&RizFixedOwnedExecute),reinterpret_cast<const void*>(&RizFixedOwnedRelease),reinterpret_cast<const void*>(&RizFixedOwnedDetach)};return kind<4?const_cast<void*>(targets[kind]):nullptr;}
RIZ_ENGINE_FIXTURE_API std::uint32_t __cdecl RizEngineOwned_Run(std::uint32_t kind,std::uint32_t mode,Row* output)
{if(!output||output->version!=Version||output->byteSize!=sizeof(*output)||kind>=4||mode>2)return 1;InterlockedOr(&phaseMask,static_cast<LONG>(1u<<Outside));return Run(Outside,kind,mode,output);}
RIZ_ENGINE_FIXTURE_API std::uint32_t __cdecl RizEngineOwned_GetSnapshot(Snapshot* output)
{
    if(!output||output->version!=Version||output->byteSize!=sizeof(*output))return 1;
    if(InterlockedCompareExchange(&access,-1,0)!=0)return 2;
    evidence.version=Version;evidence.byteSize=sizeof(evidence);evidence.phaseMask=static_cast<std::uint32_t>(InterlockedCompareExchange(&phaseMask,0,0));
    evidence.rowCount=static_cast<std::uint32_t>(InterlockedCompareExchange(&nextRow,0,0));
    evidence.checks=static_cast<std::uint64_t>(InterlockedCompareExchange64(&checks,0,0));evidence.failures=static_cast<std::uint64_t>(InterlockedCompareExchange64(&failures,0,0));
    Counters(evidence.bodyCounters);*output=evidence;InterlockedExchange(&access,0);return 0;
}
RIZ_ENGINE_FIXTURE_API std::uint32_t __cdecl RizEngineOwned_GetBodyCounters(std::uint64_t* output)
{if(!output)return 1;Counters(output);return 0;}
extern "C" void __cdecl RizEngineOwned_RunPhase(std::uint32_t phase)
{
    if(phase<Tls||phase>Dll){Check(false);return;}
    const auto bit=static_cast<LONG>(1u<<phase);Check((InterlockedOr(&phaseMask,bit)&bit)==0);
    Check(RizEngineFirstInstall_GetVersion()==4);
    for(std::uint32_t kind=0;kind<4;++kind)Check(Run(phase,kind,0,nullptr)==0);
}
