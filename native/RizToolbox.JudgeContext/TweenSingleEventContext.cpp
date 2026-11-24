#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <cstring>
#include <limits>
#include "TweenSingleEventContext.h"
#include "TweenRetirementContext.h"
#if !defined(RIZ_TWEEN_SINGLE_EVENT_FIXTURE)
#include "FirstInstall.h"
#endif

extern "C" void RizTweenSingleEventContext_MidEntry();
extern "C" void RizTweenSingleEventContext_PreEntry();
extern "C" void RizTweenSingleEventContext_PostEntry();

namespace {
constexpr unsigned ParentLimit=128,DepthLimit=32,ChildLimit=256;
struct Parent {
    bool used{},original{},mid{},failed{};
    unsigned childBaseline{};
    unsigned maskBaseline{};
    std::uint64_t sequence{},token{},sourceRsp{};
    const void* list{};
    RizOperationFrameV1 entry{};
    const RizTweenSingleEventObserverV1* observer{};
};
struct Child {
    bool used{},failed{};
    Parent* parent{};
    RizTweenSingleEventFrameV1 frame{};
};
struct Delivery {const RizTweenSingleEventFrameV1* frame{};Delivery* previous{};};
struct Counters {
    volatile LONG64 parentsEntered{},parentsExited{},parentsInFlight{},mids{};
    volatile LONG64 childrenEntered{},childrenExited{},childrenInFlight{},callbackFaults{},rejectedReads{},missingPosts{};
    volatile LONG64 retainedParents{},retainedChildren{},overflow{},childSequence{};
} counters;
SRWLOCK configurationGate=SRWLOCK_INIT;
RizTweenSingleEventConfigV1 config{};
RizTweenSingleEventObserverV1 observer{};
void* volatile publishedObserver{};
volatile LONG configured{},attached{},unknown{};
std::uint64_t epoch{};
thread_local std::array<Parent,ParentLimit> parents{};
thread_local std::array<Parent*,DepthLimit> parentStack{};
thread_local std::array<Child,ChildLimit> children{};
thread_local std::array<Child*,ChildLimit> childStack{};
thread_local unsigned parentDepth{},childDepth{},maskedChildren{};
thread_local Delivery* delivery{};

Parent* Current(){return parentDepth?parentStack[parentDepth-1]:nullptr;}
bool PublicationReady()
{
#if defined(RIZ_TWEEN_SINGLE_EVENT_FIXTURE)
    return true;
#else
    RizFirstInstallInfoV7 receipt{};receipt.publication.common.abiVersion=7;receipt.publication.common.byteSize=sizeof(receipt);
    return RizFirstInstall_GetInfoV7(&receipt)==0&&receipt.publication.common.phase==RizFirstInstallReady
        &&!receipt.publication.common.unknown&&receipt.singleEvent.epoch==epoch&&receipt.singleEvent.parentEpoch==config.parentEpoch
        &&receipt.singleEvent.publishedMask==0x7f&&receipt.singleEvent.completedWrites==7;
#endif
}
void Fault(Parent* parent=nullptr,Child* child=nullptr)
{InterlockedExchange(&unknown,1);RizTweenRetirementFault();if(parent)parent->failed=true;if(child)child->failed=true;}
template<class T> bool Read(const T* pointer,T& value)
{if(!pointer)return false;__try{std::memcpy(&value,pointer,sizeof(value));return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
template<class T> bool Write(T* pointer,const T& value)
{if(!pointer)return false;__try{std::memcpy(pointer,&value,sizeof(value));return true;}__except(EXCEPTION_EXECUTE_HANDLER){return false;}}
std::uint64_t Value(volatile LONG64* value)
{return static_cast<std::uint64_t>(InterlockedCompareExchange64(value,0,0));}
bool Overlap(const void* left,std::size_t leftBytes,const void* right,std::size_t rightBytes)
{
    if(!leftBytes||!rightBytes)return false;
    const auto a=reinterpret_cast<std::uintptr_t>(left),b=reinterpret_cast<std::uintptr_t>(right);
    if(leftBytes>(std::numeric_limits<std::uintptr_t>::max)()-a||rightBytes>(std::numeric_limits<std::uintptr_t>::max)()-b)return true;
    return a<b+rightBytes&&b<a+leftBytes;
}
bool Machine(const RizEventMidFrameV1* frame)
{return frame&&frame->abiVersion==1&&frame->byteSize==sizeof(*frame)&&!frame->reserved0&&!frame->reserved1
    &&frame->entryRsp&&frame->entryRsp<=(std::numeric_limits<std::uint64_t>::max)()-8
    &&frame->entryRsp+8==frame->gpr[RizEventMidRsp]&&(frame->entryRsp&15u)==8;}
bool Matches(Parent* parent,const RizEventMidFrameV1* frame)
{return parent&&parent->used&&parent->original&&Machine(frame)
    &&parent->entry.self==reinterpret_cast<const void*>(frame->gpr[RizEventMidRbp])
    &&parent->entry.method==reinterpret_cast<const void*>(frame->gpr[RizEventMidR13])
    &&static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(parent->entry.arg0))==static_cast<std::uint32_t>(frame->xmm[6].low);}
unsigned Site(const RizEventMidFrameV1* frame,bool post)
{
    if(Machine(frame)&&InterlockedCompareExchange(&configured,0,0)==1)
        for(unsigned i=0;i!=3;++i)if(reinterpret_cast<const void*>(frame->returnPc)==(post?config.postTransportReturnPc[i]:config.sourceReturnPc[i]))return i;
    Fault(Current());return 3;
}
RizTweenSingleEventFrameV1 Frame(Parent* parent,unsigned stage,unsigned site,const RizEventMidFrameV1* machine)
{
    RizTweenSingleEventFrameV1 frame{};frame.abiVersion=1;frame.byteSize=sizeof(frame);frame.stage=stage;frame.site=site;
    frame.threadId=GetCurrentThreadId();frame.epoch=epoch;frame.parentEpoch=config.parentEpoch;
    frame.parentSequence=parent->sequence;frame.parentManagedToken=parent->token;frame.event=parent->entry.self;
    frame.list=parent->list;frame.parentMethod=parent->entry.method;frame.parentCaller=parent->entry.caller;
    frame.valueBits=static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(parent->entry.arg0));
    if(machine){
        frame.argRcx=reinterpret_cast<const void*>(machine->gpr[RizEventMidRcx]);
        frame.argRdx=reinterpret_cast<const void*>(machine->gpr[RizEventMidRdx]);
        frame.argR8=reinterpret_cast<const void*>(machine->gpr[RizEventMidR8]);
        frame.argR9=reinterpret_cast<const void*>(machine->gpr[RizEventMidR9]);
        frame.sourceRsp=machine->gpr[RizEventMidRsp];frame.index=static_cast<std::uint32_t>(machine->gpr[RizEventMidRsi]);
        frame.transportReturnPc=reinterpret_cast<const void*>(machine->returnPc);frame.machineFrame=machine;
    }
    frame.sourceReturnPc=site<3?config.sourceReturnPc[site]:config.midReturnPc;return frame;
}
std::uint32_t InvokeCpp(const RizTweenSingleEventObserverV1* captured,const RizTweenSingleEventFrameV1* frame)
{try{return captured->notify(frame,captured->user);}catch(...){return 5;}}
std::uint32_t InvokeBoundary(const RizTweenSingleEventObserverV1* captured,const RizTweenSingleEventFrameV1* frame)
{__try{return InvokeCpp(captured,frame);}__except(EXCEPTION_EXECUTE_HANDLER){return 5;}}
void Notify(Parent* parent,RizTweenSingleEventFrameV1& frame,Child* child=nullptr)
{
    if(!parent->observer)return;
    Delivery current{&frame,delivery};delivery=&current;
    const auto result=InvokeBoundary(parent->observer,&frame);delivery=current.previous;
    if(result){frame.flags|=RizTweenSingleEventObserverFaulted;InterlockedIncrement64(&counters.callbackFaults);Fault(parent,child);}
    frame.machineFrame=nullptr;
}
// Exactly this raw load replaces the original memory CALL operand. It is
// deliberately outside the observer boundary; its C++/SEH unwind is preserved.
const void* Target(unsigned site,const RizEventMidFrameV1* machine)
{
    if(site==1)return config.directTarget;
    if(site>=3)return nullptr;
    const auto address=static_cast<std::uintptr_t>(machine->gpr[RizEventMidRax])+(site==0?0x198u:0x178u);
    return *reinterpret_cast<const void* const volatile*>(address);
}
void Finish(Child* child,bool returned,const RizEventMidFrameV1* machine,std::uint32_t flags)
{
    auto* parent=child->parent;auto& frame=child->frame;
    frame.stage=returned?RizTweenSingleEventReturned:RizTweenSingleEventAbnormal;frame.flags|=flags;
    frame.machineFrame=machine;
    if(machine)frame.transportReturnPc=reinterpret_cast<const void*>(machine->returnPc);
    Notify(parent,frame,child);
    if(!returned)Fault(parent,child);
    if(childDepth&&childStack[childDepth-1]==child)--childDepth;
    else {Fault(parent,child);return;}
    InterlockedIncrement64(&counters.childrenExited);InterlockedDecrement64(&counters.childrenInFlight);
    if(child->failed)InterlockedIncrement64(&counters.retainedChildren);else *child={};
}
}

std::uint32_t RizTweenSingleEventConfigure(const RizTweenSingleEventConfigV1* raw)
{
    RizTweenSingleEventConfigV1 candidate{};
    if(!Read(raw,candidate)||candidate.abiVersion!=1||candidate.byteSize!=sizeof(candidate)
        ||!candidate.parentEpoch||!candidate.midReturnPc||!candidate.directTarget)return 1;
    const void* pcs[7]{candidate.midReturnPc,candidate.sourceReturnPc[0],candidate.sourceReturnPc[1],candidate.sourceReturnPc[2],
        candidate.postTransportReturnPc[0],candidate.postTransportReturnPc[1],candidate.postTransportReturnPc[2]};
    for(unsigned i=0;i!=7;++i){if(!pcs[i])return 1;for(unsigned j=0;j!=i;++j)if(pcs[i]==pcs[j])return 1;}
    RizOperationBootstrapInfoV1 parent{1,sizeof(parent)};
    if(RizOperationBootstrap_GetInfo(RizOperationSingle1Void,31,&parent)||parent.unknown||parent.epoch!=candidate.parentEpoch
        ||!parent.original||!parent.entry)return 3;
    AcquireSRWLockExclusive(&configurationGate);
    const auto result=configured?2u:unknown?5u:0u;
    if(!result){config=candidate;epoch=1;InterlockedExchange(&configured,1);}
    ReleaseSRWLockExclusive(&configurationGate);return result;
}
RizTweenSingleEventEntryCapture RizTweenSingleEventCaptureEntry()
{
    if(InterlockedCompareExchange(&configured,0,0)!=1||!PublicationReady())return {};
    return {InterlockedCompareExchangePointer(&publishedObserver,nullptr,nullptr),epoch};
}
RizTweenSingleEventParent RizTweenSingleEventBegin(const RizTweenSingleEventEntryCapture& captured,
    const RizOperationFrameV1& frame,std::uint64_t parentEpoch)
{
    if(!captured.epoch)return {};
    if(InterlockedCompareExchange(&unknown,0,0))return {};
    if(captured.epoch!=epoch||parentEpoch!=config.parentEpoch||frame.kind!=RizOperationSingle1Void||!frame.sequence||frame.arg1){Fault();return {};}
    if(parentDepth==DepthLimit){InterlockedIncrement64(&counters.overflow);Fault();return {};}
    for(auto& parent:parents)if(!parent.used){
        parent={};parent.used=true;parent.sequence=frame.sequence;parent.entry=frame;parent.childBaseline=childDepth;parent.maskBaseline=maskedChildren;
        parent.observer=static_cast<const RizTweenSingleEventObserverV1*>(captured.observer);parentStack[parentDepth++]=&parent;
        InterlockedIncrement64(&counters.parentsEntered);InterlockedIncrement64(&counters.parentsInFlight);return {&parent,parent.sequence};
    }
    InterlockedIncrement64(&counters.overflow);Fault();return {};
}
void RizTweenSingleEventOriginal(RizTweenSingleEventParent handle,std::uintptr_t managedToken)
{
    if(!handle.record)return;
    auto* parent=static_cast<Parent*>(handle.record);
    if(Current()!=parent||parent->sequence!=handle.sequence||parent->original){Fault(parent);return;}
    parent->token=managedToken;parent->original=true;
}
void RizTweenSingleEventClose(RizTweenSingleEventParent handle,bool returned)
{
    if(!handle.record)return;
    auto* parent=static_cast<Parent*>(handle.record);
    if(Current()!=parent||parent->sequence!=handle.sequence){Fault(parent);return;}
    while(childDepth>parent->childBaseline){
        auto* child=childStack[childDepth-1];
        if(child->parent!=parent){Fault(parent);break;}
        InterlockedIncrement64(&counters.missingPosts);
        Finish(child,false,nullptr,RizTweenSingleEventMissingPost|(returned?0u:RizTweenSingleEventParentAbnormal));
    }
    if(maskedChildren!=parent->maskBaseline){maskedChildren=parent->maskBaseline;Fault(parent);}
    if(!returned||!parent->mid||InterlockedCompareExchange(&unknown,0,0))Fault(parent);
    parent->original=false;--parentDepth;parentStack[parentDepth]=nullptr;
    InterlockedIncrement64(&counters.parentsExited);InterlockedDecrement64(&counters.parentsInFlight);
    if(parent->failed)InterlockedIncrement64(&counters.retainedParents);else *parent={};
}

extern "C" void __cdecl RizTweenSingleEventContext_MidDispatch(const RizEventMidFrameV1* machine)
{
    RizTweenRetirementScope reservation(RizTweenRetirementSingle);auto* parent=Current();
    if(!Matches(parent,machine)||parent->mid||reinterpret_cast<const void*>(machine->returnPc)!=config.midReturnPc
        ||machine->gpr[RizEventMidRsi]||machine->gpr[RizEventMidRcx]||!machine->gpr[RizEventMidR15]){Fault(parent);return;}
    parent->mid=true;parent->list=reinterpret_cast<const void*>(machine->gpr[RizEventMidR15]);parent->sourceRsp=machine->gpr[RizEventMidRsp];
    InterlockedIncrement64(&counters.mids);auto frame=Frame(parent,RizTweenSingleEventMid,3,machine);Notify(parent,frame);
}
extern "C" const void* __cdecl RizTweenSingleEventContext_PreDispatch(const RizEventMidFrameV1* machine)
{
    RizTweenRetirementScope reservation(RizTweenRetirementSingle);const auto site=Site(machine,false);auto* parent=Current();
    if(site>=3)return nullptr;
    if(!Matches(parent,machine)||!parent->mid||parent->list!=reinterpret_cast<const void*>(machine->gpr[RizEventMidR15])
        ||parent->sourceRsp!=machine->gpr[RizEventMidRsp]||maskedChildren||childDepth==ChildLimit){
        if(childDepth==ChildLimit)InterlockedIncrement64(&counters.overflow);
        ++maskedChildren;Fault(parent);return Target(site,machine);
    }
    Child* selected{};for(auto& child:children)if(!child.used){selected=&child;break;}
    if(!selected){++maskedChildren;InterlockedIncrement64(&counters.overflow);Fault(parent);return Target(site,machine);}
    auto* child=selected;*child={};child->used=true;child->parent=parent;child->frame=Frame(parent,RizTweenSingleEventBefore,site,machine);
    const auto sequence=InterlockedIncrement64(&counters.childSequence);
    if(sequence<=0){++maskedChildren;*child={};Fault(parent);return Target(site,machine);}
    child->frame.childSequence=static_cast<std::uint64_t>(sequence);child->frame.invokable=child->frame.argRcx;
    childStack[childDepth++]=child;InterlockedIncrement64(&counters.childrenEntered);InterlockedIncrement64(&counters.childrenInFlight);
    child->frame.flags|=RizTweenSingleEventTargetReadFaulted;
    child->frame.actualTarget=Target(site,machine);child->frame.flags&=~RizTweenSingleEventTargetReadFaulted;
    Notify(parent,child->frame,child);return child->frame.actualTarget;
}
extern "C" void __cdecl RizTweenSingleEventContext_PostDispatch(const RizEventMidFrameV1* machine)
{
    RizTweenRetirementScope reservation(RizTweenRetirementSingle);const auto site=Site(machine,true);
    if(site>=3)return;if(maskedChildren){--maskedChildren;return;}
    auto* parent=Current();
    if(!Matches(parent,machine)||!childDepth){Fault(parent);return;}
    auto* child=childStack[childDepth-1];const auto& frame=child->frame;
    if(child->parent!=parent||frame.site!=site||frame.sourceRsp!=machine->gpr[RizEventMidRsp]
        ||frame.list!=reinterpret_cast<const void*>(machine->gpr[RizEventMidR15])
        ||frame.index!=static_cast<std::uint32_t>(machine->gpr[RizEventMidRsi])){Fault(parent,child);return;}
    Finish(child,true,machine,0);
}

RIZTSE_EXPORT void* __cdecl RizTweenSingleEventContext_GetMidEntry(){return reinterpret_cast<void*>(&RizTweenSingleEventContext_MidEntry);}
RIZTSE_EXPORT void* __cdecl RizTweenSingleEventContext_GetPreEntry(){return reinterpret_cast<void*>(&RizTweenSingleEventContext_PreEntry);}
RIZTSE_EXPORT void* __cdecl RizTweenSingleEventContext_GetPostEntry(){return reinterpret_cast<void*>(&RizTweenSingleEventContext_PostEntry);}
RIZTSE_EXPORT std::uint32_t __cdecl RizTweenSingleEventContext_Attach(std::uint64_t actualEpoch,const RizTweenSingleEventObserverV1* raw)
{
    RizTweenSingleEventObserverV1 candidate{};
    if(!Read(raw,candidate)||candidate.abiVersion!=1||candidate.byteSize!=sizeof(candidate)||!candidate.notify)return 1;
    if(!PublicationReady())return 3;
    AcquireSRWLockExclusive(&configurationGate);const auto result=!configured||actualEpoch!=epoch?3u:attached?2u:unknown?5u:0u;
    if(!result){observer=candidate;InterlockedExchange(&attached,1);InterlockedExchangePointer(&publishedObserver,&observer);}
    ReleaseSRWLockExclusive(&configurationGate);return result;
}
RIZTSE_EXPORT std::uint32_t __cdecl RizTweenSingleEventContext_GetInfo(RizTweenSingleEventInfoV1* raw)
{
    RizTweenSingleEventInfoV1 header{};if(!Read(raw,header)||header.abiVersion!=1||header.byteSize!=sizeof(header))return 1;
    const RizTweenSingleEventInfoV1 info{1,sizeof(info),static_cast<unsigned>(InterlockedCompareExchange(&configured,0,0)),
        static_cast<unsigned>(InterlockedCompareExchange(&attached,0,0)),static_cast<unsigned>(InterlockedCompareExchange(&unknown,0,0)),ParentLimit,
        epoch,config.parentEpoch,Value(&counters.parentsEntered),Value(&counters.parentsExited),Value(&counters.parentsInFlight),Value(&counters.mids),
        Value(&counters.childrenEntered),Value(&counters.childrenExited),Value(&counters.childrenInFlight),Value(&counters.callbackFaults),
        Value(&counters.rejectedReads),Value(&counters.missingPosts),Value(&counters.retainedParents),Value(&counters.retainedChildren),Value(&counters.overflow)};
    return Write(raw,info)?0u:1u;
}
RIZTSE_EXPORT std::uint32_t __cdecl RizTweenSingleEventContext_ReadCurrent(const RizTweenSingleEventFrameV1* actual,
    RizTweenSingleEventFrameV1* output,RizEventMidFrameV1* machineCopy)
{
    // Reject arbitrary addresses BEFORE dereferencing them. No pointer from a
    // caller DTO may authorize a native read or manufacture a parent record.
    if(!delivery||actual!=delivery->frame||actual->threadId!=GetCurrentThreadId()){
        InterlockedIncrement64(&counters.rejectedReads);Fault(Current());return 3;}
    RizTweenSingleEventFrameV1 header{};RizEventMidFrameV1 machineHeader{};
    const bool alias=Overlap(output,sizeof(*output),actual,sizeof(*actual))
        ||actual->machineFrame&&(Overlap(output,sizeof(*output),actual->machineFrame,sizeof(*actual->machineFrame))
            ||Overlap(machineCopy,sizeof(*machineCopy),actual,sizeof(*actual))
            ||Overlap(machineCopy,sizeof(*machineCopy),actual->machineFrame,sizeof(*actual->machineFrame))
            ||Overlap(machineCopy,sizeof(*machineCopy),output,sizeof(*output)));
    if(alias||!Read(output,header)||header.abiVersion!=1||header.byteSize!=sizeof(header)
        ||actual->machineFrame&&(!Read(machineCopy,machineHeader)
            ||machineHeader.abiVersion!=1||machineHeader.byteSize!=sizeof(machineHeader))){
        InterlockedIncrement64(&counters.rejectedReads);Fault(Current());return 1;}
    auto copied=*actual;copied.machineFrame=nullptr;
    if(actual->machineFrame){if(!Write(machineCopy,*actual->machineFrame)){Fault(Current());return 1;}copied.machineFrame=machineCopy;}
    if(!Write(output,copied)){Fault(Current());return 1;}return 0;
}
