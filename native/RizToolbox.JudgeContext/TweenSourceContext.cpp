#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <algorithm>
#include <array>
#include <cstring>
#include <initializer_list>
#include <limits>
#include "TweenSourceContext.h"
#include "TweenSourceProfile.h"
#include "TweenSourceReferences.h"
#include "TweenSourceContext.References.h"
#include "MidContext.h"
#include "TweenAdmission.h"
#include "TweenRetirementContext.h"

extern "C" void RizTweenSourceContext_Entry();
#if defined(RIZ_TWEEN_SOURCE_FIXTURE)
extern "C" bool RizTweenSourceFixture_Metadata(const RizTweenSourceMetadataV1*);
#endif
namespace {
namespace P = RizTweenSourceProfile;
using SourceFault = RizTweenSourceFault;
constexpr unsigned JournalLimit=8192, ContextLimit=32, DepthLimit=16, PendingLimit=128, CapacityLimit=4096, BindingLimit=43;
SRWLOCK lock=SRWLOCK_INIT;
struct Guard {
    bool held=true;
    Guard(){AcquireSRWLockExclusive(&lock);}
    ~Guard(){if(held)ReleaseSRWLockExclusive(&lock);}
    void Unlock(){ReleaseSRWLockExclusive(&lock);held=false;}
    void Relock(){AcquireSRWLockExclusive(&lock);held=true;}
};
struct Snapshot {
    void* container{};void* array{};std::int32_t count{},capacity{},version{};bool collection{};
    std::array<void*,CapacityLimit> values{};
};
struct Pending {
    RizTweenRetirementReservation retirement{};
    bool used{};unsigned context{},site{};
    RizTweenSourceRecordV1 before{};
    Snapshot snapshot{};void* outputSlot{};
    bool validBefore{},beforeAppended{},actualAfter{},referencesFailed{},referencesEnded{},publishing{},beforeChanged{};
    const void* decisionCached{};
    std::uint64_t sourceRsp{};
    // A source samples attachment once. An unattached old Pending never gains
    // a newly attached observer at its After. Failed deliveries retain both
    // the actual source snapshot and their last borrowed reference identities.
    unsigned referencesState{}; // 0 not sampled, 1 absent, 2 captured, 3 failed reservation
    RizTweenSourceReferencesTicketV1 referencesTicket{1,sizeof(RizTweenSourceReferencesTicketV1)};
    RizTweenSourceReferenceOriginV1 referencesOrigin{};
    std::array<RizTweenSourceBorrowedReferenceV1,16> lastReferences{};
    unsigned lastReferenceCount{};
};
struct Context {
    RizTweenRetirementReservation retirement{};
    bool used{};RizTweenSourceOperationTicketV1 ticket{};void* manager{};
    unsigned pendingDepth{};std::array<unsigned,DepthLimit> pending{};
    // These are borrowed stack-call outputs, not roots or globally keyed Tween
    // ownership. Duplicate pointer occurrences remain separate transactions.
    void* popped[2]{};std::uint64_t poppedTransaction[2]{};
    bool movePending{};void* moveArray{};void* moveTween{};int moveSource=-1,moveDestination=-1;
};
std::array<RizTweenSourceSiteBindingV1,BindingLimit> bindings{};
std::array<RizTweenSourceRecordV1,JournalLimit> journal{};
std::array<Context,ContextLimit> contexts{};
std::array<Pending,PendingLimit> pending{};
thread_local std::array<unsigned,DepthLimit> threadContexts{};
thread_local unsigned threadDepth=0;
const unsigned char* moduleBase{};const void* tweenClass{};const void* managerClass{};const void* cachedPointerField{};
std::uint32_t configured=0,metadataBound=0,unknown=0,firstFaultDetail=0;
std::uint64_t epoch=0,events=0,transactions=0,completed=0,redirects=0,faults=0,pendingCount=0;
unsigned active=0;
void Fault(SourceFault reason, unsigned site=RizTweenSourceSiteCount, unsigned stage=RizTweenSourceBefore)
{
    if (!unknown) {
        firstFaultDetail = 0x01000000u | (static_cast<std::uint32_t>(reason) << 16);
        if (site < RizTweenSourceSiteCount) firstFaultDetail |= ((stage + 1) << 8) | (site + 1);
    }
    unknown=1;RizTweenRetirementFault();if(faults!=(std::numeric_limits<std::uint64_t>::max)())++faults;
}
bool Next(std::uint64_t& source)
{
    if(source==(std::numeric_limits<std::uint64_t>::max)()){Fault(SourceFault::SequenceExhausted);return false;}
    ++source;return true;
}
template<class T> bool Read(const void* pointer,T& output)
{
    if(!pointer)return false;
    __try{std::memcpy(&output,pointer,sizeof(output));return true;}
    __except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
template<class T> bool Field(const void* object,std::size_t offset,T& output)
{
    const auto start=reinterpret_cast<std::uintptr_t>(object);
    return start && offset<=(std::numeric_limits<std::uintptr_t>::max)()-start && Read(reinterpret_cast<const void*>(start+offset),output);
}
bool Exact(void* object,const void* cls){const void* actual{};return object && (reinterpret_cast<std::uintptr_t>(object)&7u)==0 && Read(object,actual)&&actual==cls;}
bool Dead(void* object,bool& dead,const void** cachedObserved=nullptr)
{
    dead=false;if(cachedObserved)*cachedObserved=nullptr;if(!object)return true;
    const void* actual{};if(!Read(object,actual))return false;
    // A foreign class has no exact-class death policy. Keep its original
    // machine branch and retain Unknown instead of guessing it is live/dead.
    if(actual!=tweenClass)return false;
    const void* cached{};if(!Field(object,16,cached))return false;
    if(cachedObserved)*cachedObserved=cached;
    dead=cached==nullptr;return true;
}
bool SnapshotArray(void* array,Snapshot& output)
{
    std::uint64_t count{};
    if(!array || !Field(array,24,count) || count>CapacityLimit)return false;
    output.array=array;output.capacity=static_cast<int>(count);
    for(unsigned i=0;i<count;++i)if(!Field(array,32+i*sizeof(void*),output.values[i]))return false;
    return true;
}
bool Capture(void* container,bool collection,Snapshot& output,void* publishedArray=nullptr)
{
    output={};output.container=container;output.collection=collection;
    if(!collection){if(!SnapshotArray(container,output))return false;output.count=output.capacity;return true;}
    void* array{};
    if(!Field(container,16,array)||(publishedArray&&array!=publishedArray)||!Field(container,24,output.count)||!Field(container,28,output.version)
        || output.count<0 || !SnapshotArray(array,output) || output.count>output.capacity)return false;
    return true;
}
void CopySnapshot(RizTweenSourceRecordV1& record,const Snapshot& before,const Snapshot& after)
{
    record.container=before.container;record.arrayBefore=before.array;record.arrayAfter=after.array;
    record.countBefore=before.count;record.countAfter=after.count;record.capacityBefore=before.capacity;record.capacityAfter=after.capacity;
    record.versionBefore=before.version;record.versionAfter=after.version;
}
bool Append(RizTweenSourceRecordV1& record)
{
    // Normal Update traffic must not exhaust a process-lifetime journal. Only
    // completed rows are reusable: overwriting an unfinished Before would lose
    // the actual source pair, so retain that pending snapshot and fail closed.
    if(events>=JournalLimit){
        const auto evicted=events-JournalLimit+1;
        for(const auto& work:pending)if(work.used&&work.before.eventSequence==evicted){
            Fault(SourceFault::JournalPendingEviction,work.site,work.before.stage);return false;
        }
    }
    if(!Next(events))return false;
    record.eventSequence=events;record.epoch=epoch;if(unknown)record.flags|=RizTweenSourceUnknown;
    journal[static_cast<std::size_t>((events-1)%JournalLimit)]=record;return true;
}
bool FinalizeRecord(RizTweenSourceRecordV1& record)
{
    // Committed callbacks run while the actual native source remains in-flight.
    // They cannot issue a retirement receipt. Finalize this same retained row
    // after the terminal observer return, clearing any abandoned selector claim.
    if(!record.eventSequence||record.eventSequence>events){Fault(SourceFault::JournalRecordMismatch,record.site,record.stage);return false;}
    auto& row=journal[static_cast<std::size_t>((record.eventSequence-1)%JournalLimit)];
    if(row.epoch!=record.epoch||row.eventSequence!=record.eventSequence||row.transaction!=record.transaction
        ||row.site!=record.site||row.stage!=record.stage||row.threadId!=record.threadId){
        Fault(SourceFault::JournalRecordMismatch,record.site,record.stage);return false;
    }
    if(unknown)record.flags|=RizTweenSourceUnknown;
    row=record;return true;
}
Context* Current()
{
    if(threadDepth==0 || threadDepth>DepthLimit)return nullptr;
    auto& context=contexts[threadContexts[threadDepth-1]];
    return context.used && context.ticket.threadId==GetCurrentThreadId()?&context:nullptr;
}
unsigned ExpectedOperation(unsigned site){return site==19||site==20?RizTweenSourcePlay:site==21?RizTweenSourceExecute:RizTweenSourceUpdate;}
bool EventSite(unsigned site){return site>=17;}
bool CollectionSite(unsigned site){return site<=1 || (site>=7&&site<=12);}
bool SnapshotSite(unsigned site){return site<=12;}
void* Manager(const RizEventMidFrameV1& frame,Context& context)
{
    if(context.ticket.operation==RizTweenSourceUpdate)return context.ticket.receiver;
    static_cast<void>(frame);return context.manager;
}
unsigned ReferenceRoles(unsigned site)
{
    unsigned roles=RizTweenReferenceManager|RizTweenReferenceTween;
    if(EventSite(site))return roles|RizTweenReferenceEvent;
    roles|=RizTweenReferenceArray;
    if(CollectionSite(site))roles|=RizTweenReferenceContainer;
    if((site>=2&&site<=6)||(site>=9&&site<=12))roles|=RizTweenReferencePrior;
    return roles;
}
bool WorkCurrent(const Pending& work,Context& context)
{
    return Current()==&context&&work.used&&threadDepth&&work.context==threadContexts[threadDepth-1]
        &&context.pendingDepth&& &pending[context.pending[context.pendingDepth-1]]==&work;
}
bool BeginReferences(Pending& work,Context& context,Guard& guard)
{
    const auto& row=work.before;
    work.referencesOrigin={1,sizeof(work.referencesOrigin),row.operation,row.threadId,row.depth,row.site,row.stage,
        ReferenceRoles(row.site),row.epoch,row.updateSequence,row.parentUpdateSequence,row.transaction,row.sourcePc,row.relayReturnPc};
    work.publishing=true;guard.Unlock();
    const auto result=RizTweenSourceReferencesBeginFromSource(&work.referencesOrigin,&work.referencesTicket);
    guard.Relock();work.publishing=false;
    if(!WorkCurrent(work,context)){work.referencesFailed=true;Fault(SourceFault::ReferencesParentChanged,row.site,row.stage);return false;}
    if(result==2){work.referencesState=1;return true;}
    if(result==0){work.referencesState=2;return true;}
    work.referencesState=3;work.referencesFailed=true;Fault(SourceFault::ReferencesScopeRejected,row.site,row.stage);return false;
}
bool PublishReferences(Pending& work,Context& context,Guard& guard,unsigned stage,
    std::initializer_list<RizTweenSourceBorrowedReferenceV1> references)
{
    if(work.referencesState==1)return true;
    if(work.referencesState!=2||references.size()>work.lastReferences.size())return false;
    work.lastReferenceCount=static_cast<unsigned>(references.size());
    unsigned at=0;for(const auto& reference:references)work.lastReferences[at++]=reference;
    auto origin=work.referencesOrigin;origin.stage=stage;
    work.publishing=true;guard.Unlock();
    const auto result=RizTweenSourceReferencesDeliverFromSource(&work.referencesTicket,RizTweenReferences,
        &origin,work.lastReferences.data(),work.lastReferenceCount);
    guard.Relock();work.publishing=false;
    const bool current=WorkCurrent(work,context);
    if(!current||result!=0){
        work.referencesFailed=true;
        Fault(current?SourceFault::ReferencesDeliveryRejected:SourceFault::ReferencesParentChanged,work.site,stage);
        return false;
    }
    return true;
}
bool ReferenceNotice(Pending& work,Context& context,Guard& guard,unsigned phase,unsigned stage)
{
    if(work.referencesState==1)return true;
    if(work.referencesState!=2||work.referencesEnded)return false;
    auto origin=work.referencesOrigin;origin.stage=stage;
    work.publishing=true;guard.Unlock();
    const auto result=RizTweenSourceReferencesDeliverFromSource(&work.referencesTicket,phase,&origin,nullptr,0);
    guard.Relock();work.publishing=false;
    const bool current=WorkCurrent(work,context);
    if(!current||result!=0){
        work.referencesFailed=true;
        Fault(current?SourceFault::ReferencesNoticeRejected:SourceFault::ReferencesParentChanged,work.site,stage);
        return false;
    }
    return true;
}
bool EndReferences(Pending& work,Context& context,Guard& guard,unsigned stage,bool returned)
{
    if(work.referencesState==1||work.referencesEnded)return !work.referencesFailed;
    if(work.referencesState!=2)return false;
    // End(false) emits the actual abnormal responsibility when no source After
    // completed. Already failed observer scopes still receive this notification.
    work.publishing=true;guard.Unlock();
    const auto result=RizTweenSourceReferencesEndFromSource(&work.referencesTicket,returned);
    guard.Relock();work.publishing=false;work.referencesEnded=true;
    const bool current=WorkCurrent(work,context);
    if(!current||result!=0){
        work.referencesFailed=true;
        Fault(current?SourceFault::ReferencesCloseRejected:SourceFault::ReferencesParentChanged,work.site,stage);
        return false;
    }
    return true;
}
bool PublishedCapture(Pending& work,Context& context,Guard& guard,unsigned stage,
    void* container,bool collection,Snapshot& output)
{
    void* array=container;
    if(collection&&!Field(container,16,array))return false;
    // Container was independently rooted before loading its backing reference.
    // Publish that reference immediately; length/class/vector reads follow it.
    if(!PublishReferences(work,context,guard,stage,{{array,RizTweenReferenceArray,-1}}))return false;
    if(!array)return false;
    return Capture(container,collection,output,array);
}
bool SameSnapshot(const Snapshot& left,const Snapshot& right)
{
    if(left.container!=right.container||left.array!=right.array||left.collection!=right.collection
        ||left.count!=right.count||left.capacity!=right.capacity||left.version!=right.version)return false;
    for(int i=0;i<left.capacity;++i)if(left.values[static_cast<unsigned>(i)]!=right.values[static_cast<unsigned>(i)])return false;
    return true;
}
bool StillPublishedBefore(const Pending& work)
{
    if(work.referencesState!=2)return true;
    if(EventSite(work.site)){
        void* actualEvent{};
        return Exact(work.before.tween,tweenClass)
            &&Field(work.before.tween,work.site==RizTweenPlayOnComplete||work.site==RizTweenExecuteOnComplete?0x90u:0x88u,actualEvent)
            &&actualEvent==work.before.container;
    }
    if(!work.snapshot.array)return true;
    if(work.site>=RizTweenTailDeadDecision&&work.site<=RizTweenPlayingExecuteAppend){
        void* actualArray{};
        if(!Field(work.before.manager,32,actualArray)||actualArray!=work.snapshot.array)return false;
    }
    Snapshot actual{};
    return Capture(work.snapshot.container,work.snapshot.collection,actual,work.snapshot.array)&&SameSnapshot(work.snapshot,actual);
}
bool InitializeBeforeRecord(const RizEventMidFrameV1& frame,const RizTweenSourceSiteBindingV1& binding,Context& context,Pending& work)
{
    const auto site=binding.site;auto& record=work.before;work.sourceRsp=frame.gpr[RizEventMidRsp];
    record={};record.abiVersion=1;record.byteSize=sizeof(record);record.site=site;record.stage=binding.stage;
    record.flags=RizTweenSourceParentMatched;record.threadId=context.ticket.threadId;record.depth=context.ticket.depth;
    record.operation=context.ticket.operation;record.epoch=epoch;record.updateSequence=context.ticket.operationSequence;
    record.parentUpdateSequence=context.ticket.parentOperationSequence;record.sourcePc=binding.sourcePc;record.relayReturnPc=binding.relayReturnPc;
    record.manager=Manager(frame,context);record.sourceIndex=-1;record.destinationIndex=-1;
    record.countBefore=record.countAfter=record.capacityBefore=record.capacityAfter=record.versionBefore=record.versionAfter=-1;
    if(!Next(transactions))return false;record.transaction=transactions;record.occurrence=transactions;
    if(site==2||site==3){const unsigned pop=site==2?0u:1u;if(context.poppedTransaction[pop])record.occurrence=context.poppedTransaction[pop];}
    return true;
}
bool BeforeRecord(const RizEventMidFrameV1& frame,const RizTweenSourceSiteBindingV1& binding,Context& context,Pending& work,Guard& guard)
{
    const auto site=binding.site;auto& record=work.before;
    if(!BeginReferences(work,context,guard))return false;
    auto reg=[&](unsigned index){return reinterpret_cast<void*>(frame.gpr[index]);};
    if(context.ticket.operation==RizTweenSourceUpdate && reg(RizEventMidR14)!=context.ticket.receiver)return false;
    if(context.ticket.operation!=RizTweenSourceUpdate && reg(RizEventMidRsi)!=context.ticket.receiver)return false;
    const auto esi=static_cast<std::int32_t>(frame.gpr[RizEventMidRsi]);
    const auto ebx=static_cast<std::int32_t>(frame.gpr[RizEventMidRbx]);
    void* container{};bool collection=CollectionSite(site);
    if(site<=1){container=reg(RizEventMidRcx);work.outputSlot=reg(RizEventMidRdx);}
    else if(site==2||site==3||site==4){container=reg(RizEventMidRbx);record.destinationIndex=esi;record.tween=reg(RizEventMidRdi);if(site==4)record.sourceIndex=static_cast<int>(frame.gpr[RizEventMidR15]);}
    else if(site==5){container=reg(RizEventMidRdx);record.sourceIndex=static_cast<int>(frame.gpr[RizEventMidRcx]);}
    else if(site==6){container=reg(RizEventMidRcx);record.sourceIndex=static_cast<int>(frame.gpr[RizEventMidRax]);}
    else if(site==7||site==8){container=reg(RizEventMidRcx);record.tween=reg(RizEventMidRdx);record.sourceIndex=site==7?esi:ebx;}
    else if(site==9){container=reg(RizEventMidRcx);record.tween=reg(RizEventMidR8);record.sourceIndex=ebx;record.destinationIndex=static_cast<int>(frame.gpr[RizEventMidRdx]);}
    else if(site>=10&&site<=12){container=reg(RizEventMidRcx);record.sourceIndex=static_cast<int>(frame.gpr[RizEventMidRdx]);}
    else if(site==RizTweenTailDeadDecision||(site>=14&&site<=16)){
        record.tween=site==RizTweenTailDeadDecision?reg(RizEventMidRbx):reg(RizEventMidRdx);
        if(!PublishReferences(work,context,guard,binding.stage,{{record.manager,RizTweenReferenceManager,-1},
            {record.tween,RizTweenReferenceTween,-1}}))return false;
        void* array{};
        if(!record.manager||!Field(record.manager,32,array)
            ||!PublishedCapture(work,context,guard,binding.stage,array,false,work.snapshot))return false;
        CopySnapshot(record,work.snapshot,work.snapshot);
        record.sourceIndex=site==RizTweenTailDeadDecision||site==RizTweenPlayingExecuteExisting
            ?static_cast<int>(frame.gpr[RizEventMidR15]):esi;
        if(record.sourceIndex<0||record.sourceIndex>=work.snapshot.capacity
            ||work.snapshot.values[static_cast<unsigned>(record.sourceIndex)]!=record.tween)return false;
        // Execute's real RCX is the manager, not the playing array. Preserve
        // that identity while publishing the exact backing/source slot too.
        if(site!=RizTweenTailDeadDecision){record.container=reg(RizEventMidRcx);if(record.container!=record.manager)return false;}
    }
    else if(EventSite(site)){
        record.container=reg(RizEventMidRcx);
        if(site==17||site==18){
            const auto lane=site==17?0u:1u;const auto offset=site==17?0x67u:0x77u;
            if(!Field(reg(RizEventMidRbp),offset,record.tween)||!context.poppedTransaction[lane]
                ||context.popped[lane]!=record.tween)return false;
            record.occurrence=context.poppedTransaction[lane];
        }
        else record.tween=context.ticket.receiver;
        // All direct event inputs are adopted together, before inspecting
        // either receiver. Loading the Tween from the actual caller stack is
        // scalar borrowing, not an object metadata/field read.
        if(!PublishReferences(work,context,guard,binding.stage,{{record.manager,RizTweenReferenceManager,-1},
            {record.tween,RizTweenReferenceTween,-1},{record.container,RizTweenReferenceEvent,-1}}))return false;
        void* actualEvent{};
        if(!record.tween||!Field(record.tween,site==20||site==21?0x90u:0x88u,actualEvent)
            ||!PublishReferences(work,context,guard,binding.stage,{{actualEvent,RizTweenReferenceEvent,-1}})
            ||actualEvent!=record.container)return false;
    }
    if(SnapshotSite(site)){
        record.container=container;
        if(!PublishReferences(work,context,guard,binding.stage,{{record.manager,RizTweenReferenceManager,-1},
            {container,collection?RizTweenReferenceContainer:RizTweenReferenceArray,-1},
            {record.tween,RizTweenReferenceTween,record.sourceIndex}}))return false;
        if(!PublishedCapture(work,context,guard,binding.stage,container,collection,work.snapshot))return false;
        CopySnapshot(record,work.snapshot,work.snapshot);
        if(site<=1){record.sourceIndex=work.snapshot.count-1;if(work.snapshot.count)record.tween=work.snapshot.values[static_cast<unsigned>(record.sourceIndex)];}
        else if(site==7||site==8){record.destinationIndex=work.snapshot.count;}
        else {
            const auto index=site==2||site==3||site==4||site==9?record.destinationIndex:record.sourceIndex;
            if(index<0||index>=work.snapshot.count)return false;record.prior=work.snapshot.values[static_cast<unsigned>(index)];
            if(site==5||site==6||(site>=10&&site<=12))record.tween=record.prior;
        }
        if(!PublishReferences(work,context,guard,binding.stage,{{record.tween,RizTweenReferenceTween,record.sourceIndex}}))return false;
        if(ReferenceRoles(site)&RizTweenReferencePrior)
            if(!PublishReferences(work,context,guard,binding.stage,{{record.prior,RizTweenReferencePrior,record.sourceIndex}}))return false;
    }
    if(record.tween&&!Exact(record.tween,tweenClass))return false;
    if(!StillPublishedBefore(work))return false;
    record.flags|=RizTweenSourceKnown;return true;
}
bool AfterReferences(Pending& work,Context& context,Guard& guard,void*& publishedArray,void*& actualOutput)
{
    publishedArray=nullptr;actualOutput=work.before.tween;
    if(work.referencesState==1)return true;
    if(work.referencesState!=2)return false;
    const auto site=work.site;const auto& row=work.before;
    if(!PublishReferences(work,context,guard,RizTweenSourceAfter,{{row.manager,RizTweenReferenceManager,-1},
        {row.tween,RizTweenReferenceTween,row.sourceIndex}}))return false;
    if(EventSite(site))return PublishReferences(work,context,guard,RizTweenSourceAfter,{{row.container,RizTweenReferenceEvent,-1}});
    if(!SnapshotSite(site))return true;
    const bool collection=CollectionSite(site);
    if(!PublishReferences(work,context,guard,RizTweenSourceAfter,
        {{row.container,collection?RizTweenReferenceContainer:RizTweenReferenceArray,-1}}))return false;
    publishedArray=row.container;
    if(collection&&!Field(row.container,16,publishedArray))return false;
    if(!PublishReferences(work,context,guard,RizTweenSourceAfter,{{publishedArray,RizTweenReferenceArray,-1}}))return false;
    if(!publishedArray)return false;
    if(site<=1){
        // A successful TryPop has already removed its slot. Publish the actual
        // stack-call output while the physical After still borrows that value.
        if(!Read(work.outputSlot,actualOutput)
            ||!PublishReferences(work,context,guard,RizTweenSourceAfter,{{actualOutput,RizTweenReferenceTween,row.sourceIndex}}))return false;
    }
    if(ReferenceRoles(site)&RizTweenReferencePrior)
        if(!PublishReferences(work,context,guard,RizTweenSourceAfter,{{row.prior,RizTweenReferencePrior,row.sourceIndex}}))return false;
    return true;
}
bool VersionStep(const Snapshot& before,const Snapshot& after)
{
    // List/Stack use unchecked Int32 version increments. Compare raw bits so a
    // legitimate MaxValue -> MinValue transition is not a false read fault.
    return static_cast<std::uint32_t>(after.version)==static_cast<std::uint32_t>(before.version)+1u;
}
bool SameExcept(const Snapshot& before,const Snapshot& after,int changed,void* expected)
{
    if(before.array!=after.array||before.capacity!=after.capacity||before.count!=after.count)return false;
    for(int i=0;i<before.capacity;++i)if(after.values[static_cast<unsigned>(i)]!=(i==changed?expected:before.values[static_cast<unsigned>(i)]))return false;
    return true;
}
bool VerifyAfter(Pending& work,const RizEventMidFrameV1& frame,Context& context,RizTweenSourceRecordV1& record,void* publishedArray=nullptr)
{
    const auto site=work.site;record=work.before;record.stage=RizTweenSourceAfter;
    record.flags|=RizTweenSourcePaired;record.rawResult=frame.gpr[RizEventMidRax];
    if(!SnapshotSite(site))return true;
    Snapshot after{};const auto& before=work.snapshot;
    if(!Capture(before.container,before.collection,after,publishedArray))return false;
    CopySnapshot(record,before,after);
    if(site<=1){
        const bool success=(record.rawResult&0xffu)!=0;void* returned{};
        if(!Read(work.outputSlot,returned))return false;
        record.tween=returned;
        if(success){
            if(before.count<=0 || returned!=before.values[static_cast<unsigned>(before.count-1)]
                || after.array!=before.array || after.capacity!=before.capacity || after.count!=before.count-1 || !VersionStep(before,after))return false;
            for(int i=0;i<before.capacity;++i)if(after.values[static_cast<unsigned>(i)]!=(i==before.count-1?nullptr:before.values[static_cast<unsigned>(i)]))return false;
            record.flags|=RizTweenSourcePopSucceeded;
        }else if(before.count!=0||returned||before.version!=after.version||!SameExcept(before,after,-1,nullptr))return false;
    }else if(site==2||site==3||site==4){
        if(!SameExcept(before,after,record.destinationIndex,record.tween))return false;
        if(site==4){
            if(record.sourceIndex<0||record.sourceIndex>=before.count||before.values[static_cast<unsigned>(record.sourceIndex)]!=record.tween||context.movePending)return false;
        }else{
            const unsigned pop=site==2?0u:1u;
            if(context.popped[pop]!=record.tween||context.poppedTransaction[pop]==0)return false;
        }
    }else if(site==5||site==6){
        if(!SameExcept(before,after,record.sourceIndex,nullptr))return false;
        if(site==5){
            if(!context.movePending||context.moveArray!=before.array||context.moveSource!=record.sourceIndex||context.moveTween!=record.prior
                ||context.moveDestination<0||context.moveDestination>=before.capacity
                ||before.values[static_cast<unsigned>(context.moveDestination)]!=context.moveTween)return false;
        }
    }else if(site==7||site==8){
        if(after.count!=before.count+1||!VersionStep(before,after)||after.capacity<after.count)return false;
        if(after.array==before.array){
            if(after.capacity!=before.capacity)return false;
            for(int i=0;i<after.capacity;++i){
                const auto expected=i==before.count?record.tween:before.values[static_cast<unsigned>(i)];
                if(after.values[static_cast<unsigned>(i)]!=expected)return false;
            }
        }else{
            if(after.capacity<=before.capacity)return false;
            for(int i=0;i<after.capacity;++i){
                const auto expected=i<before.count?before.values[static_cast<unsigned>(i)]:i==before.count?record.tween:nullptr;
                if(after.values[static_cast<unsigned>(i)]!=expected)return false;
            }
        }
    }else if(site==9){
        if(!VersionStep(before,after)||!SameExcept(before,after,record.destinationIndex,record.tween))return false;
        if(record.sourceIndex<0||record.sourceIndex>=before.count||before.values[static_cast<unsigned>(record.sourceIndex)]!=record.tween)return false;
    }else{
        if(after.count!=before.count-1 || after.array!=before.array||after.capacity!=before.capacity||!VersionStep(before,after))return false;
        for(int i=0;i<before.capacity;++i){
            void* expected=before.values[static_cast<unsigned>(i)];
            if(i>=record.sourceIndex&&i<before.count-1)expected=before.values[static_cast<unsigned>(i+1)];
            else if(i==before.count-1)expected=nullptr;
            if(after.values[static_cast<unsigned>(i)]!=expected)return false;
        }
    }
    return true;
}
struct AfterWitness { void* event{};void* managerArray{}; };
bool CaptureAfterWitness(const Pending& work,AfterWitness& witness)
{
    if(work.referencesState!=2)return true;
    if(EventSite(work.site))return Field(work.before.tween,
        work.site==RizTweenPlayOnComplete||work.site==RizTweenExecuteOnComplete?0x90u:0x88u,witness.event);
    if(work.site>=RizTweenPlayingExecuteFront&&work.site<=RizTweenPlayingExecuteAppend)
        return Field(work.before.manager,32,witness.managerArray);
    return true;
}
bool StableAfterCallbacks(Pending& work,const RizEventMidFrameV1& frame,Context& context,
    void* publishedArray,const AfterWitness& witness,bool checkCached,bool expectedDead,const void* expectedCached)
{
    if(work.referencesState!=2)return true;
    RizTweenSourceRecordV1 rechecked{};
    // A collection may have acquired a different backing in any of the batched
    // callbacks. VerifyAfter compares the published identity BEFORE reading its
    // capacity/vector, so no new unrooted array can enter this terminal check.
    if(!VerifyAfter(work,frame,context,rechecked,publishedArray))return false;
    if(EventSite(work.site)){
        void* currentEvent{};
        if(!Exact(work.before.tween,tweenClass)||!Field(work.before.tween,
            work.site==RizTweenPlayOnComplete||work.site==RizTweenExecuteOnComplete?0x90u:0x88u,currentEvent)
            ||currentEvent!=witness.event)return false;
    }else if(work.site>=RizTweenPlayingExecuteFront&&work.site<=RizTweenPlayingExecuteAppend){
        void* currentArray{};
        if(!Field(work.before.manager,32,currentArray)||currentArray!=witness.managerArray)return false;
    }
    if(checkCached){
        bool currentDead=false;const void* currentCached{};
        if(!Dead(rechecked.tween,currentDead,&currentCached)||currentDead!=expectedDead||currentCached!=expectedCached)return false;
    }
    return true;
}
void CommitActualAfter(Pending& work,Context& context,const RizTweenSourceRecordV1& actual,bool stable,bool redirect)
{
    const auto site=work.site;
    if(site<=1&&(actual.flags&RizTweenSourcePopSucceeded)){
        // The actual successful pop is never rewritten to false or forgotten
        // because a terminal observer changed its death classification.
        context.popped[site]=actual.tween;context.poppedTransaction[site]=actual.transaction;
        if(redirect){context.popped[site]=nullptr;context.poppedTransaction[site]=0;}
        return;
    }
    if(site==RizTweenPlayingMoveWrite){
        context.movePending=true;context.moveArray=actual.arrayBefore;context.moveTween=actual.tween;
        context.moveSource=actual.sourceIndex;context.moveDestination=actual.destinationIndex;return;
    }
    if(!stable)return;
    if(site==RizTweenPlayingInsertFront||site==RizTweenPlayingInsertAppend){
        const auto lane=site==RizTweenPlayingInsertFront?0u:1u;
        context.popped[lane]=nullptr;context.poppedTransaction[lane]=0;
    }else if(site==RizTweenPlayingMoveClear){
        context.movePending=false;context.moveArray=nullptr;context.moveTween=nullptr;
    }
}
#if !defined(RIZ_TWEEN_SOURCE_FIXTURE)
template<class T>T Api(const char* name){auto proc=GetProcAddress(reinterpret_cast<HMODULE>(const_cast<unsigned char*>(moduleBase)),name);T fn{};static_assert(sizeof(fn)==sizeof(proc));std::memcpy(&fn,&proc,sizeof(fn));return fn;}
bool TextEqual(const char* value,const char* expected){if(!value)return false;for(unsigned i=0;i<128;++i){if(value[i]!=expected[i])return false;if(!expected[i])return true;}return false;}
bool Metadata(const RizTweenSourceMetadataV1& metadata)
{
    using Ptr=const void*(__cdecl*)(const void*);using Text=const char*(__cdecl*)(const void*);
    using Find=const void*(__cdecl*)(const void*,const char*,const char*);using FieldFn=const void*(__cdecl*)(const void*,const char*);using Offset=std::size_t(__cdecl*)(const void*);
    using Flags=std::uint32_t(__cdecl*)(const void*);
    auto image=Api<Ptr>("il2cpp_class_get_image");auto name=Api<Text>("il2cpp_class_get_name");auto ns=Api<Text>("il2cpp_class_get_namespace");
    auto imageName=Api<Text>("il2cpp_image_get_name");auto find=Api<Find>("il2cpp_class_from_name");auto field=Api<FieldFn>("il2cpp_class_get_field_from_name");
    auto parent=Api<Ptr>("il2cpp_field_get_parent");auto offset=Api<Offset>("il2cpp_field_get_offset");auto flags=Api<Flags>("il2cpp_field_get_flags");
    if(!image||!name||!ns||!imageName||!find||!field||!parent||!offset||!flags)return false;
    RizTweenAdmissionInfoV1 admission{1,sizeof(admission)};
    if(RizTweenAdmission_GetInfo(&admission)!=0||!admission.configured||admission.unknown||admission.exactTweenClass!=metadata.exactTweenClass
        ||admission.cachedPointerField!=metadata.cachedPointerField||admission.cachedPointerOffset!=16)return false;
    auto cls=metadata.exactManagerClass;auto assembly=image(cls);
    if(!TextEqual(name(cls),"TweenManager")||!TextEqual(ns(cls),"")||!TextEqual(imageName(assembly),"Assembly-CSharp.dll")
        ||find(assembly,"","TweenManager")!=cls||image(metadata.exactTweenClass)!=assembly)return false;
    const char* fields[]{"playingTweens","scheduledTweens","readyTweens","_workingTweenTrackSize"};const std::size_t offsets[]{32,40,48,60};
    for(unsigned i=0;i<4;++i){auto value=field(cls,fields[i]);if(!value||parent(value)!=cls||offset(value)!=offsets[i]||(flags(value)&0x10u))return false;}
    return true;
}
#endif
bool ValidateMetadata(const RizTweenSourceMetadataV1& metadata)
{
    __try {
#if defined(RIZ_TWEEN_SOURCE_FIXTURE)
        return RizTweenSourceFixture_Metadata(&metadata);
#else
        return Metadata(metadata);
#endif
    }__except(EXCEPTION_EXECUTE_HANDLER){return false;}
}
void FlushPendingReferences(Context& context,Guard& guard)
{
    // This is the real operation-finally path, while its actual managed parent
    // is still current. Flush in physical LIFO order, retaining every Pending
    // snapshot/root responsibility after an abnormal or missing source After.
    while(context.pendingDepth){
        auto& work=pending[context.pending[context.pendingDepth-1]];
        if(!work.used||work.context!=threadContexts[threadDepth-1]||work.publishing){Fault(SourceFault::OperationPendingMismatch);return;}
        Fault(SourceFault::OperationUnfinishedSource,work.site,work.before.stage);
        auto row=work.before;row.flags|=RizTweenSourceUnknown|RizTweenSourceAbnormal;Append(row);
        if(work.referencesState==2&&!work.referencesEnded)EndReferences(work,context,guard,work.before.stage,false);
        if(Current()!=&context){Fault(SourceFault::OperationPendingMismatch);return;}
        --context.pendingDepth;
    }
}
}
std::uint32_t RizTweenSourceConfigure(const RizTweenSourceConfigV1* config)
{
    if(!config||config->abiVersion!=1||config->byteSize!=sizeof(*config)||!config->moduleBase||config->reserved
        ||config->reservedTweenClass||config->reservedManagerClass||config->siteBindingCount!=BindingLimit||!config->sites)return 1;
    std::array<RizTweenSourceSiteBindingV1,BindingLimit> copy{};
    for(unsigned i=0;i<BindingLimit;++i){if(!Read(config->sites+i,copy[i]))return 1;
        auto& value=copy[i];if(value.site>=P::SiteCount||value.stage>RizTweenSourceDecision||!value.relayReturnPc)return 1;
        const auto& site=P::Sites[value.site];
        if(value.sourcePc!=static_cast<const unsigned char*>(config->moduleBase)+site.beginRva
            ||(value.site==RizTweenTailDeadDecision?value.stage!=RizTweenSourceDecision:value.stage==RizTweenSourceDecision))return 1;
        const bool canRedirect=(value.site<=1&&value.stage==RizTweenSourceAfter)||value.site==RizTweenTailDeadDecision;
        const void* expected=canRedirect?static_cast<const unsigned char*>(config->moduleBase)+site.deadContinuationRva:nullptr;
        if(value.deadContinuation!=expected)return 1;
        for(unsigned j=0;j<i;++j)if(copy[j].relayReturnPc==value.relayReturnPc||(copy[j].site==value.site&&copy[j].stage==value.stage))return 1;
    }
    Guard guard;if(configured)return 4;if(unknown)return 5;bindings=copy;moduleBase=static_cast<const unsigned char*>(config->moduleBase);epoch=1;configured=1;return 0;
}
std::uint32_t RizTweenSourceBindMetadata(const RizTweenSourceMetadataV1* metadata)
{
    if(!metadata||metadata->abiVersion!=1||metadata->byteSize!=sizeof(*metadata)||!metadata->exactTweenClass||!metadata->exactManagerClass||!metadata->cachedPointerField)return 1;
    auto same=[&]{return tweenClass==metadata->exactTweenClass&&managerClass==metadata->exactManagerClass&&cachedPointerField==metadata->cachedPointerField;};
    {Guard guard;if(!configured)return 2;if(unknown)return 5;if(metadataBound)return same()?4u:1u;}
    if(!ValidateMetadata(*metadata))return 1;
    Guard guard;if(unknown)return 5;if(metadataBound)return same()?4u:1u;
    tweenClass=metadata->exactTweenClass;managerClass=metadata->exactManagerClass;cachedPointerField=metadata->cachedPointerField;metadataBound=1;return 0;
}
std::uint32_t RizTweenSourceOperationEnter(std::uint32_t operation,void* receiver,void* actualManager,std::uint64_t sequence,RizTweenSourceOperationTicketV1* ticket)
{
    RizTweenRetirementScope retirement(RizTweenRetirementSourceOperation);
    if(!ticket||ticket->abiVersion!=1||ticket->byteSize!=sizeof(*ticket)||ticket->reserved||operation>RizTweenSourceExecute||!receiver||!sequence)return 1;
    Guard guard;if(!configured||!metadataBound)return 2;if(unknown)return 5;
    auto* parent=Current();
    void* manager=actualManager;
    if(operation==RizTweenSourcePlay){if(actualManager)return 1;manager=parent?parent->manager:nullptr;}
    if(threadDepth>=DepthLimit){Fault(SourceFault::OperationDepthLimit);return 5;}
    if(!Exact(receiver,operation==RizTweenSourceUpdate?managerClass:tweenClass)){
        Fault(SourceFault::OperationReceiverMismatch);return 5;
    }
    if((operation==RizTweenSourceUpdate&&actualManager!=receiver)
        ||(operation==RizTweenSourceExecute&&!actualManager)||(manager&&!Exact(manager,managerClass))){
        Fault(SourceFault::OperationManagerMismatch);return 5;
    }
    for(const auto& value:contexts)if(value.used&&value.ticket.operationSequence==sequence){Fault(SourceFault::OperationSequenceReused);return 5;}
    for(unsigned i=0;i<ContextLimit;++i)if(!contexts[i].used){
        auto& value=contexts[i];value={};value.used=true;value.manager=manager;value.retirement=retirement.Take();
        value.ticket={1,sizeof(value.ticket),GetCurrentThreadId(),threadDepth+1,operation,0,epoch,sequence,parent?parent->ticket.operationSequence:0,receiver};
        threadContexts[threadDepth++]=i;++active;*ticket=value.ticket;return 0;
    }
    Fault(SourceFault::OperationCapacity);return 5;
}
std::uint32_t RizTweenSourceOperationExit(const RizTweenSourceOperationTicketV1* ticket,bool returned)
{
    if(!ticket||ticket->abiVersion!=1||ticket->byteSize!=sizeof(*ticket)||ticket->reserved)return 1;
    Guard guard;auto* value=Current();
    if(!value||std::memcmp(&value->ticket,ticket,sizeof(*ticket))!=0){Fault(SourceFault::OperationTicketMismatch);return 5;}
    if(!returned)Fault(SourceFault::OperationAbnormalExit);
    else if(value->pendingDepth)Fault(SourceFault::OperationUnfinishedSource);
    else if(value->movePending)Fault(SourceFault::OperationUnfinishedMove);
    else if(value->poppedTransaction[0]||value->poppedTransaction[1])Fault(SourceFault::OperationUnfinishedPop);
    FlushPendingReferences(*value,guard);
    // A fault retains its context/unfinished transfer records. Never normalize
    // it into a successful empty interval or discard an abnormal source ticket.
    if(!unknown){value->used=false;--active;auto retirement=value->retirement;value->retirement={};--threadDepth;
        guard.Unlock();RizTweenRetirementExit(retirement);return 0;}
    --threadDepth;return 5;
}
std::uint32_t RizTweenSourceOperationPrepareExitReferences(const RizTweenSourceOperationTicketV1* ticket)
{
    if(!ticket||ticket->abiVersion!=1||ticket->byteSize!=sizeof(*ticket)||ticket->reserved)return 1;
    Guard guard;auto* context=Current();
    if(!context||std::memcmp(&context->ticket,ticket,sizeof(*ticket))!=0){Fault(SourceFault::OperationTicketMismatch);return 5;}
    FlushPendingReferences(*context,guard);return unknown?5u:0u;
}
std::uint32_t RizTweenSourceUpdateEnter(void* manager,std::uint64_t sequence,RizTweenSourceUpdateTicketV1* ticket)
{
    if(!ticket||ticket->abiVersion!=1||ticket->byteSize!=sizeof(*ticket))return 1;
    RizTweenSourceOperationTicketV1 actual{1,sizeof(actual)};const auto result=RizTweenSourceOperationEnter(RizTweenSourceUpdate,manager,manager,sequence,&actual);
    if(!result)*ticket={1,sizeof(*ticket),actual.threadId,actual.depth,actual.epoch,actual.operationSequence,actual.parentOperationSequence,actual.receiver};return result;
}
std::uint32_t RizTweenSourceUpdateExit(const RizTweenSourceUpdateTicketV1* ticket,bool returned)
{
    if(!ticket||ticket->abiVersion!=1||ticket->byteSize!=sizeof(*ticket))return 1;
    const RizTweenSourceOperationTicketV1 actual{1,sizeof(actual),ticket->threadId,ticket->depth,RizTweenSourceUpdate,0,ticket->epoch,ticket->updateSequence,ticket->parentUpdateSequence,ticket->manager};
    return RizTweenSourceOperationExit(&actual,returned);
}
extern "C" std::uintptr_t __cdecl RizTweenSourceContext_Dispatch(const RizEventMidFrameV1* raw)
{
    RizTweenRetirementScope relayRetirement(RizTweenRetirementSourceRelay);
    // Reserve a potential Pending before taking the source lock. If this is an
    // After/unmatched entry the local scope releases it on return.
    RizTweenRetirementScope pendingRetirement(RizTweenRetirementSourcePending);
    RizEventMidFrameV1 frame{};
    if(!Read(raw,frame)||frame.abiVersion!=1||frame.byteSize!=sizeof(frame)||frame.reserved0||frame.reserved1
        ||frame.entryRsp> (std::numeric_limits<std::uint64_t>::max)()-8||frame.entryRsp+8!=frame.gpr[RizEventMidRsp]
        ||(frame.entryRsp&15u)!=8){Guard guard;if(configured)Fault(SourceFault::InvalidSourceFrame);return 0;}
    Guard guard;if(!configured||!metadataBound)return 0;
    const RizTweenSourceSiteBindingV1* selected=nullptr;
    for(const auto& binding:bindings)if(reinterpret_cast<std::uint64_t>(binding.relayReturnPc)==frame.returnPc){selected=&binding;break;}
    if(!selected){Fault(SourceFault::UnboundSourceReturn);return 0;}
    const auto& binding=*selected;auto* context=Current();
    if(!context){Fault(SourceFault::SourceParentMissing,binding.site,binding.stage);return 0;}
    if(context->ticket.operation!=ExpectedOperation(binding.site)){
        Fault(SourceFault::SourceOperationMismatch,binding.site,binding.stage);return 0;
    }
    if(binding.stage!=RizTweenSourceAfter){
        if(unknown)return 0;
        if(context->pendingDepth>=DepthLimit){Fault(SourceFault::SourceDepthLimit,binding.site,binding.stage);return 0;}
        for(unsigned i=0;i<PendingLimit;++i)if(!pending[i].used){
            auto& work=pending[i];work={};work.used=true;work.site=binding.site;work.context=threadContexts[threadDepth-1];
            work.retirement=pendingRetirement.Take();
            // Register responsibility before the first source read. A failed
            // capture must remain visible as unfinished native work as well.
            context->pending[context->pendingDepth++]=i;++pendingCount;
            if(binding.site<=1){
                // The next real TryPop proves that the previous uninstalled
                // activation took the original reject/managed-null loop edge.
                context->popped[binding.site]=nullptr;context->poppedTransaction[binding.site]=0;
            }
            const bool initialized=InitializeBeforeRecord(frame,binding,*context,work);
            work.validBefore=initialized&&BeforeRecord(frame,binding,*context,work,guard);
            bool dead=false;
            if(work.validBefore&&binding.stage==RizTweenSourceDecision&&!Dead(work.before.tween,dead,&work.decisionCached))work.validBefore=false;
            if(!work.validBefore){
                work.before.flags|=RizTweenSourceReadFault;Fault(SourceFault::SourceBeforeMismatch,binding.site,binding.stage);
            }
            if(dead)work.before.flags|=RizTweenSourceExactDead|RizTweenSourceRedirected;
            work.beforeAppended=Append(work.before);
            if(work.beforeAppended)ReferenceNotice(work,*context,guard,RizTweenReferencesBeforeCommitted,binding.stage);
            // BeforeCommitted is itself an unlocked observer boundary. A child
            // call there can change a destination/prior or event after the last
            // References scan. Recheck under the source lock before the replay
            // resumes; a stale image never earns source/selector authority.
            if(work.referencesState==2&&work.validBefore){
                bool unchanged=StillPublishedBefore(work);
                if(binding.stage==RizTweenSourceDecision){
                    bool currentDead=false;const void* currentCached{};
                    unchanged=unchanged&&Dead(work.before.tween,currentDead,&currentCached)
                        &&currentDead==dead&&currentCached==work.decisionCached;
                }
                if(!unchanged){
                    work.validBefore=false;work.beforeChanged=true;dead=false;
                    work.before.flags|=RizTweenSourceUnknown|RizTweenSourceReadFault;
                    Fault(SourceFault::SourceBeforeChanged,binding.site,binding.stage);
                }
            }
            if(binding.stage!=RizTweenSourceDecision)return 0;
            // The selector is exclusively the native exact-class cached-pointer
            // policy. No observer return is interpreted as a continuation.
            const bool refsEnded=EndReferences(work,*context,guard,binding.stage,work.beforeAppended&&!work.beforeChanged);
            work.actualAfter=true;
            const bool redirect=dead&&work.validBefore&&work.beforeAppended&&!work.referencesFailed&&refsEnded&&!unknown;
            if(!redirect)work.before.flags&=~static_cast<unsigned>(RizTweenSourceRedirected);
            if(work.beforeChanged)work.before.flags&=~static_cast<unsigned>(RizTweenSourceExactDead);
            if(work.beforeAppended)FinalizeRecord(work.before);
            const bool keep=!work.validBefore||!work.beforeAppended||work.referencesFailed||!refsEnded||unknown;
            if(!keep){work.used=false;--pendingCount;}
            if(Current()==context&&context->pendingDepth&&context->pending[context->pendingDepth-1]==i)--context->pendingDepth;
            else Fault(SourceFault::OperationPendingMismatch,binding.site,binding.stage);
            const auto continuation=redirect?reinterpret_cast<std::uintptr_t>(binding.deadContinuation):0;
            if(redirect)++redirects;
            if(!keep){auto retirement=work.retirement;work.retirement={};guard.Unlock();RizTweenRetirementExit(retirement);}
            if(redirect)return continuation;
            return 0;
        }
        Fault(SourceFault::SourceCapacity,binding.site,binding.stage);return 0;
    }
    if(!context->pendingDepth){Fault(SourceFault::SourceAfterMissingBefore,binding.site,binding.stage);return 0;}
    auto& work=pending[context->pending[context->pendingDepth-1]];
    if(!work.used||work.site!=binding.site||work.context!=threadContexts[threadDepth-1]){
        Fault(SourceFault::SourceAfterPairMismatch,binding.site,binding.stage);return 0;
    }
    // Before and After execute at the same actual outgoing stack pointer. A
    // nested operation that failed to acquire its own parent after Unknown must
    // never finish an outer Pending merely because it names the same site.
    if(work.referencesState==2&&frame.gpr[RizEventMidRsp]!=work.sourceRsp){
        Fault(SourceFault::SourceAfterStackMismatch,binding.site,binding.stage);return 0;
    }
    if(unknown&&work.referencesState!=2)return 0; // Preserve the unattached path.
    RizTweenSourceRecordV1 after{};
    const auto receiverRegister=context->ticket.operation==RizTweenSourceUpdate?RizEventMidR14:RizEventMidRsi;
    void* publishedArray{};void* actualOutput{};
    const bool rooted=AfterReferences(work,*context,guard,publishedArray,actualOutput);
    const bool bodyVerified=rooted&&work.validBefore
        &&frame.gpr[receiverRegister]==reinterpret_cast<std::uint64_t>(context->ticket.receiver)
        &&VerifyAfter(work,frame,*context,after,publishedArray);
    bool valid=bodyVerified,dead=false;const void* cached{};AfterWitness witness{};
    const bool checkCached=bodyVerified&&binding.site<=1&&(after.flags&RizTweenSourcePopSucceeded);
    if(checkCached){
        if(!Dead(after.tween,dead,&cached))valid=false;
        if(dead)after.flags|=RizTweenSourceExactDead|RizTweenSourceRedirected;
    }
    if(valid&&!CaptureAfterWitness(work,witness))valid=false;
    if(!valid){
        if(!bodyVerified){
            after=work.before;after.stage=RizTweenSourceAfter;after.rawResult=frame.gpr[RizEventMidRax];
            if(binding.site<=1)after.tween=actualOutput;
        }
        after.flags|=RizTweenSourceReadFault;Fault(SourceFault::SourceAfterMismatch,binding.site,binding.stage);
    }
    after.relayReturnPc=binding.relayReturnPc;const bool appended=Append(after);
    const auto actual=after;
    if(appended&&work.beforeAppended)ReferenceNotice(work,*context,guard,RizTweenReferencesAfterCommitted,RizTweenSourceAfter);
    bool afterChanged=false;
    if(valid&&!StableAfterCallbacks(work,frame,*context,publishedArray,witness,checkCached,dead,cached)){
        valid=false;afterChanged=true;dead=false;
        after.flags|=RizTweenSourceUnknown|RizTweenSourceReadFault;
        after.flags&=~static_cast<unsigned>(RizTweenSourceExactDead|RizTweenSourceRedirected);
        Fault(SourceFault::SourceAfterChanged,binding.site,binding.stage);
    }
    const bool refsEnded=EndReferences(work,*context,guard,binding.stage,appended&&work.beforeAppended&&!work.beforeChanged&&!afterChanged);
    work.actualAfter=true;
    // End(false) may itself deliver the terminal Abnormal callback. Commit
    // physical transfer tokens and the selector only after ALL observer calls.
    // Never modify frame.gpr[RAX], its real output slot or the real return PC.
    const bool redirect=valid&&dead&&appended&&!work.referencesFailed&&refsEnded&&!unknown;
    if(!redirect)after.flags&=~static_cast<unsigned>(RizTweenSourceRedirected);
    if(appended)FinalizeRecord(after);
    if(bodyVerified&&WorkCurrent(work,*context))CommitActualAfter(work,*context,actual,valid&&!work.referencesFailed&&refsEnded&&!unknown,redirect);
    // An attached failed observer retains the concrete Pending even though the
    // physical source CALL returned. Only the active LIFO frame is removed.
    const bool retain=!valid||!appended||work.referencesFailed||!refsEnded||unknown;
    if(!retain){work.used=false;--pendingCount;++completed;}
    if(Current()==context&&context->pendingDepth&& &pending[context->pending[context->pendingDepth-1]]==&work)--context->pendingDepth;
    else Fault(SourceFault::OperationPendingMismatch,binding.site,binding.stage);
    const auto continuation=redirect?reinterpret_cast<std::uintptr_t>(binding.deadContinuation):0;
    if(redirect)++redirects;
    if(!retain){auto retirement=work.retirement;work.retirement={};guard.Unlock();RizTweenRetirementExit(retirement);}
    if(redirect)return continuation;
    return 0;
}
RIZTS_EXPORT void* __cdecl RizTweenSourceContext_GetEntry(){return reinterpret_cast<void*>(&RizTweenSourceContext_Entry);}
RIZTS_EXPORT std::uint32_t __cdecl RizTweenSourceContext_GetInfo(RizTweenSourceInfoV1* info)
{
    if(!info||info->abiVersion!=1||info->byteSize!=sizeof(*info))return 1;
    Guard guard;*info={1,sizeof(*info),configured,unknown,configured?BindingLimit:0,JournalLimit,active,metadataBound,
        epoch,events,transactions,completed,redirects,faults,pendingCount,(std::min)(events,static_cast<std::uint64_t>(JournalLimit))};return 0;
}
std::uint32_t RizTweenSourceFirstFaultDetail()
{
    Guard guard;return firstFaultDetail;
}
RIZTS_EXPORT std::uint32_t __cdecl RizTweenSourceContext_ReadRecord(std::uint64_t expectedEpoch,std::uint64_t event,RizTweenSourceRecordV1* record)
{
    if(!record||record->abiVersion!=1||record->byteSize!=sizeof(*record)||record->reserved1||!event)return 1;
    Guard guard;if(!configured||expectedEpoch!=epoch)return 2;
    const auto retained=(std::min)(events,static_cast<std::uint64_t>(JournalLimit));
    if(event>events||event<=events-retained)return 3;
    const auto& row=journal[static_cast<std::size_t>((event-1)%JournalLimit)];
    if(row.eventSequence!=event||row.epoch!=epoch){Fault(SourceFault::JournalRecordMismatch);return 5;}
    *record=row;if(unknown)record->flags|=RizTweenSourceUnknown;return unknown?5u:0u;
}
