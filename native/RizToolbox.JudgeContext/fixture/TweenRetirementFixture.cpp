#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <array>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include "../TweenRetirementContext.h"
#include "../OperationContext.h"
#include "../TweenSourceContext.h"
#include "../TweenSourceReferences.h"
#include "../TweenSingleEventContext.h"

namespace {
struct alignas(8) Tween { const void* type; void* monitor{}; const void* cached; };
int classMarker{},fieldMarker{},nativeMarker{},managerMarker{};
volatile LONG checks{},failures{},originals{},beforeCalls{},afterCalls{},blockDomain{-1},workerLane{},throwCallback{},throwExitCallback{},referenceCalls{},blockOriginal{};
bool throwReference{};
bool singleInFlight{},singleInFlightBlocksAcquire{};
HANDLE reached{},resume{};
RizTweenRetirementRequestV1 request{1,sizeof(request),1,7,0,1,0,17};
RizTweenRetirementCutV1 activeCut{};
std::uint32_t copiedWorkerStatus{};
void Check(bool value,const char* expression,int line)
{InterlockedIncrement(&checks);if(!value){InterlockedIncrement(&failures);std::fprintf(stderr,"FAIL retirement %d %s\n",line,expression);}}
#define CHECK(value) Check(!!(value),#value,__LINE__)
RizTweenAdmissionReceiptV1 AdmissionHeader(){return {1,sizeof(RizTweenAdmissionReceiptV1)};}
RizTweenRetirementCutV1 CutHeader(){return {1,sizeof(RizTweenRetirementCutV1)};}
RizTweenRetirementClaimV1 Claim(Tween& object)
{
    auto receipt=AdmissionHeader();CHECK(RizTweenAdmission_Claim(&object,&receipt)==0);
    return {receipt.slot,0,receipt.epoch,&object,&classMarker};
}
void Seal(Tween& object,const RizTweenRetirementClaimV1& claim)
{
    auto receipt=AdmissionHeader();CHECK(RizTweenAdmission_Seal(claim.slot,claim.epoch,&receipt)==0);object.cached=nullptr;
}
std::uintptr_t __cdecl Before(const RizOperationFrameV1* frame,void*)
{
    ++beforeCalls;CHECK(frame&&frame->sequence);
    if(throwCallback)throw std::runtime_error("actual observer exception");
    return frame->sequence;
}
void __cdecl After(const RizOperationFrameV1* frame,std::uintptr_t token,std::uint32_t flags,void*)
{++afterCalls;CHECK(frame&&((flags&RizOperationEnterFaulted)||token==frame->sequence));
 if(throwExitCallback)throw std::runtime_error("actual exit observer exception");}
void OriginalBarrier()
{if(InterlockedCompareExchange(&blockOriginal,0,0)){SetEvent(reached);WaitForSingleObject(resume,10000);}}
void __cdecl OriginalVoid(void*,const void*){++originals;OriginalBarrier();}
void __cdecl OriginalRef(void*,void*,const void*){++originals;}
void __cdecl OriginalSingle(void*,float value,const void*){++originals;CHECK(value==.25f);}
void Invoke(unsigned lane,void* object)
{
    constexpr unsigned kinds[]{0,1,1,0,0,3},slots[]{25,20,19,24,23,31};
    const auto entry=RizOperationContext_GetEntry(kinds[lane],slots[lane]);
    if(kinds[lane]==0)reinterpret_cast<void(__cdecl*)(void*,const void*)>(entry)(object,nullptr);
    else if(kinds[lane]==1)reinterpret_cast<void(__cdecl*)(void*,void*,const void*)>(entry)(&managerMarker,object,nullptr);
    else reinterpret_cast<void(__cdecl*)(void*,float,const void*)>(entry)(object,.25f,nullptr);
}
DWORD WINAPI InvokeWorker(void* object){Invoke(static_cast<unsigned>(workerLane),object);return 0;}
DWORD WINAPI ForeignCutWorker(void*){copiedWorkerStatus=RizTweenRetirement_Verify(&activeCut);return 0;}
void Setup()
{
    constexpr unsigned kinds[]{0,1,1,0,0,3},slots[]{25,20,19,24,23,31};
    for(unsigned i=0;i!=6;++i){
        auto original=kinds[i]==0?reinterpret_cast<void*>(&OriginalVoid):kinds[i]==1?reinterpret_cast<void*>(&OriginalRef):reinterpret_cast<void*>(&OriginalSingle);
        RizOperationBootstrapConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(0x100000ull+i*0x100),original};
        CHECK(RizOperationBootstrap_Configure(kinds[i],slots[i],&config)==0);
        RizOperationBootstrapInfoV1 info{1,sizeof(info)};CHECK(RizOperationBootstrap_GetInfo(kinds[i],slots[i],&info)==0);
        RizOperationBootstrapObserverV1 observer{1,sizeof(observer),Before,After,nullptr};
        CHECK(RizOperationBootstrap_Attach(kinds[i],slots[i],info.epoch,&observer)==0);
    }
    RizTweenAdmissionConfigV1 config{1,sizeof(config),&classMarker,&fieldMarker,16,0};CHECK(RizTweenAdmission_Configure(&config)==0);
}
std::uint32_t __cdecl ReferenceNotify(const RizTweenSourceReferenceNoticeV1* notice,void*)
{++referenceCalls;CHECK(notice&&notice->observerEpoch==1);if(throwReference)throw std::runtime_error("actual reference observer failure");return 0;}
void AttachReferences()
{
    const RizTweenSourceReferencesObserverV1 observer{1,sizeof(observer),ReferenceNotify,nullptr};
    CHECK(RizTweenSourceReferencesAttach(7,&observer)==0);
}
void FreezeWatermark()
{
    RizTweenSourceReferencesInfoV1 info{1,sizeof(info)};CHECK(RizTweenSourceReferences_GetInfo(&info)==0);
    request.referenceObserverEpoch=info.observerEpoch;request.referenceDeliveryCutoff=info.deliveryAttempts;
}
void ReservationRaces()
{
    Tween owned{&classMarker,nullptr,&nativeMarker},foreign{&classMarker,nullptr,&nativeMarker};
    const auto claim=Claim(owned);Seal(owned,claim);
    singleInFlight=true;auto singleCut=CutHeader();
    singleInFlightBlocksAcquire=RizTweenRetirement_Acquire(&request,1,&claim,&singleCut)==3&&singleCut.token==0;
    CHECK(singleInFlightBlocksAcquire);singleInFlight=false;
    reached=CreateEventW(nullptr,TRUE,FALSE,nullptr);resume=CreateEventW(nullptr,TRUE,FALSE,nullptr);CHECK(reached&&resume);
    // The barrier runs after the real common reservation and before the real
    // entry Snapshot. No native lock is held while the worker is paused.
    for(unsigned lane=0;lane!=6;++lane){
        ResetEvent(reached);ResetEvent(resume);blockDomain=static_cast<LONG>(lane);workerLane=static_cast<LONG>(lane);
        const auto worker=CreateThread(nullptr,0,InvokeWorker,&foreign,0,nullptr);
        CHECK(worker&&WaitForSingleObject(reached,10000)==WAIT_OBJECT_0);
        auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==3&&cut.token==0);
        SetEvent(resume);CHECK(WaitForSingleObject(worker,10000)==WAIT_OBJECT_0);CloseHandle(worker);
    }
    blockDomain=-1;
    for(unsigned domain=RizTweenRetirementSourceOperation;domain!=RizTweenRetirementDomainCount;++domain){
        auto reservation=RizTweenRetirementEnter(domain);CHECK(reservation.serial!=0);
        auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==3);
        RizTweenRetirementExit(reservation);
    }
    CHECK(originals==6&&beforeCalls==6&&afterCalls==6);
    auto cut=CutHeader();auto receipt=AdmissionHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==0);
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,1,&claim,&receipt)==0);CHECK(RizTweenRetirement_Release(&cut)==0);
    CloseHandle(reached);CloseHandle(resume);reached=nullptr;resume=nullptr;
}
void ActualReferenceEnd()
{
    RizTweenSourceReferenceOriginV1 origin{1,sizeof(origin),RizTweenSourceUpdate,GetCurrentThreadId(),1,
        RizTweenPlayingTailClear,RizTweenSourceBefore,RizTweenReferenceTween,7,101,0,1,
        reinterpret_cast<void*>(0x10000),reinterpret_cast<void*>(0x20000)};
    RizTweenSourceReferencesTicketV1 ticket{1,sizeof(ticket)};CHECK(RizTweenSourceReferencesBeginFromSource(&origin,&ticket)==0);
    const RizTweenSourceBorrowedReferenceV1 reference{&nativeMarker,RizTweenReferenceTween,0};
    CHECK(RizTweenSourceReferencesDeliverFromSource(&ticket,RizTweenReferences,&origin,&reference,1)==0);
    CHECK(RizTweenSourceReferencesDeliverFromSource(&ticket,RizTweenReferencesBeforeCommitted,&origin,nullptr,0)==0);
    origin.stage=RizTweenSourceAfter;
    CHECK(RizTweenSourceReferencesDeliverFromSource(&ticket,RizTweenReferences,&origin,&reference,1)==0);
    CHECK(RizTweenSourceReferencesDeliverFromSource(&ticket,RizTweenReferencesAfterCommitted,&origin,nullptr,0)==0);
    FreezeWatermark();auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==3);
    CHECK(RizTweenSourceReferencesEndFromSource(&ticket,true)==0);FreezeWatermark();
    cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==0);
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,0,nullptr,nullptr)==0);
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,0,nullptr,nullptr)==6);
    CHECK(RizTweenRetirement_VerifyReferenceRoots(&cut)==0);CHECK(RizTweenRetirement_Release(&cut)==0);
}
void AtomicBatch()
{
    Tween first{&classMarker,nullptr,&nativeMarker},second{&classMarker,nullptr,&nativeMarker},foreign{&classMarker,nullptr,&nativeMarker};
    std::array<RizTweenRetirementClaimV1,2> claims{Claim(first),Claim(second)};Seal(first,claims[0]);Seal(second,claims[1]);
    FreezeWatermark();auto cut=CutHeader();auto bad=claims;bad[1].epoch++;
    CHECK(RizTweenRetirement_Acquire(&request,2,bad.data(),&cut)==2&&cut.token==0);
    CHECK(RizTweenRetirement_Acquire(&request,2,claims.data(),&cut)==0);activeCut=cut;
    const auto thread=CreateThread(nullptr,0,ForeignCutWorker,nullptr,0,nullptr);CHECK(thread&&WaitForSingleObject(thread,10000)==WAIT_OBJECT_0);
    CloseHandle(thread);CHECK(copiedWorkerStatus==2);
    // New foreign generations really execute while a cut is borrowed.
    const auto prior=originals;for(unsigned lane=0;lane!=6;++lane)Invoke(lane,&foreign);CHECK(originals==prior+6);
    CHECK(RizTweenRetirement_Verify(&cut)==0);
    std::array<RizTweenAdmissionReceiptV1,2> receipts{AdmissionHeader(),AdmissionHeader()};
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,2,bad.data(),receipts.data())==2);
    second.cached=&nativeMarker;CHECK(RizTweenRetirement_UnregisterBatch(&cut,2,claims.data(),receipts.data())==3);
    auto observed=AdmissionHeader();CHECK(RizTweenAdmission_Read(claims[0].slot,claims[0].epoch,&observed)==0&&observed.phase==RizTweenSealed&&observed.object==&first);
    // A later foreign original is physically inside its real call while this
    // older cut verifies and commits. Its independent generation cannot turn
    // this owner cut into a suspension gate or require waiting for the game.
    second.cached=nullptr;reached=CreateEventW(nullptr,TRUE,FALSE,nullptr);resume=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    CHECK(reached&&resume);workerLane=0;blockOriginal=1;
    const auto foreignThread=CreateThread(nullptr,0,InvokeWorker,&foreign,0,nullptr);
    CHECK(foreignThread&&WaitForSingleObject(reached,10000)==WAIT_OBJECT_0);
    RizOperationBootstrapInfoV1 lane{1,sizeof(lane)};
    CHECK(RizOperationBootstrap_GetInfo(0,25,&lane)==0&&lane.inFlight==1);
    CHECK(RizTweenRetirement_Verify(&cut)==0);
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,2,claims.data(),receipts.data())==0);
    SetEvent(resume);CHECK(WaitForSingleObject(foreignThread,10000)==WAIT_OBJECT_0);CloseHandle(foreignThread);blockOriginal=0;
    CloseHandle(reached);CloseHandle(resume);reached=nullptr;resume=nullptr;
    CHECK(originals==prior+7);
    for(unsigned i=0;i!=2;++i)CHECK(receipts[i].phase==RizTweenRetired&&receipts[i].object==claims[i].object
        &&receipts[i].slot==claims[i].slot&&receipts[i].epoch==claims[i].epoch&&!receipts[i].cachedNative&&!receipts[i].activeEntries);
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,2,claims.data(),receipts.data())==6);
    CHECK(RizTweenRetirement_Verify(&cut)==0);CHECK(RizTweenRetirement_Release(&cut)==0);
    CHECK(RizTweenRetirement_Verify(&cut)==2&&RizTweenRetirement_Release(&cut)==2);
    // Slot reuse changes epoch and cannot revive the earlier cut or group.
    first.cached=&nativeMarker;const auto recycled=Claim(first);CHECK(recycled.epoch!=claims[0].epoch);
}
void ObserverFault(unsigned lane,bool exitFault)
{
    Tween object{&classMarker,nullptr,&nativeMarker},foreign{&classMarker,nullptr,&nativeMarker};
    const auto claim=Claim(object);Seal(object,claim);
    auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==0);
    if(exitFault)throwExitCallback=1;else throwCallback=1;
    Invoke(lane,&foreign);throwCallback=throwExitCallback=0;
    CHECK(RizTweenRetirement_Verify(&cut)==5);
    auto receipt=AdmissionHeader();CHECK(RizTweenRetirement_UnregisterBatch(&cut,1,&claim,&receipt)==5);
    CHECK(RizTweenRetirement_Release(&cut)==5);
    cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==5&&cut.token==0);
    CHECK(originals==1&&beforeCalls==1&&afterCalls==1);
}
void AdmissionFault()
{
    Tween object{&classMarker,nullptr,&nativeMarker};const auto claim=Claim(object);Seal(object,claim);
    auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==0);
    object.type=&fieldMarker;auto receipt=AdmissionHeader();
    CHECK(RizTweenAdmission_Read(claim.slot,claim.epoch,&receipt)==5&&receipt.unknown==1);
    CHECK(RizTweenRetirement_Verify(&cut)==5);
    CHECK(RizTweenRetirement_UnregisterBatch(&cut,1,&claim,&receipt)==5);
    CHECK(RizTweenRetirement_Release(&cut)==5);
    cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,1,&claim,&cut)==5&&cut.token==0);
}
void ReferenceFault()
{
    auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==0);
    RizTweenSourceReferenceOriginV1 origin{1,sizeof(origin),RizTweenSourceUpdate,GetCurrentThreadId(),1,
        RizTweenPlayingTailClear,RizTweenSourceBefore,RizTweenReferenceTween,7,101,0,1,
        reinterpret_cast<void*>(0x10000),reinterpret_cast<void*>(0x20000)};
    RizTweenSourceReferencesTicketV1 ticket{1,sizeof(ticket)};CHECK(RizTweenSourceReferencesBeginFromSource(&origin,&ticket)==0);
    const RizTweenSourceBorrowedReferenceV1 reference{&nativeMarker,RizTweenReferenceTween,0};
    throwReference=true;CHECK(RizTweenSourceReferencesDeliverFromSource(&ticket,RizTweenReferences,&origin,&reference,1)==5);
    CHECK(RizTweenRetirement_Verify(&cut)==5);
    CHECK(RizTweenSourceReferencesEndFromSource(&ticket,false)==5);FreezeWatermark();
    CHECK(RizTweenRetirement_VerifyReferenceRoots(&cut)==5&&RizTweenRetirement_Release(&cut)==5);
    cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==5&&cut.token==0);
    RizTweenSourceReferencesInfoV1 references{1,sizeof(references)};CHECK(RizTweenSourceReferences_GetInfo(&references)==0
        &&references.unknown&&references.activeScopes==0&&references.retainedScopes==1);
}
void ReferenceReadFault()
{
    auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==0);
    RizTweenSourceReferenceNoticeV1 notice{1,sizeof(notice)};
    RizTweenSourceReferenceOriginV1 origin{1,sizeof(origin)};
    CHECK(RizTweenSourceReferences_ReadCurrentNotice(nullptr,1,1,1,&notice,&origin,nullptr,0)==3);
    CHECK(RizTweenRetirement_Verify(&cut)==5);
    CHECK(RizTweenRetirement_VerifyReferenceRoots(&cut)==5&&RizTweenRetirement_Release(&cut)==5);
    cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==5&&cut.token==0);
}
void Unconfigured()
{
    Tween object{&classMarker,nullptr,&nativeMarker};Invoke(0,&object);
    RizOperationBootstrapInfoV1 info{1,sizeof(info)};
    CHECK(RizOperationBootstrap_GetInfo(0,25,&info)==3&&info.unknown==1&&info.entry&&!info.original&&!info.epoch);
    CHECK(info.entered==0&&info.exited==0&&info.inFlight==0&&info.enterFaults==1);
    const RizOperationBootstrapConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(0x100000ull),reinterpret_cast<void*>(&OriginalVoid)};
    CHECK(RizOperationBootstrap_Configure(0,25,&config)==2);
    const RizOperationBootstrapObserverV1 observer{1,sizeof(observer),Before,After,nullptr};
    CHECK(RizOperationBootstrap_Attach(0,25,1,&observer)==5);
    auto cut=CutHeader();CHECK(RizTweenRetirement_Acquire(&request,0,nullptr,&cut)==5&&cut.token==0);
    CHECK(originals==0&&beforeCalls==0&&afterCalls==0);
}
}
extern "C" bool RizTweenAdmissionFixture_Ready(){return true;}
extern "C" bool RizTweenAdmissionFixture_Validate(const RizTweenAdmissionConfigV1* config)
{return config&&config->exactTweenClass==&classMarker&&config->cachedPointerField==&fieldMarker;}
extern "C" void RizTweenRetirementFixture_AfterEnter(std::uint32_t domain)
{
    if(reached&&resume&&static_cast<LONG>(domain)==InterlockedCompareExchange(&blockDomain,0,0)){
        SetEvent(reached);WaitForSingleObject(resume,10000);
    }
}
// The aggregate fixture executes actual entry/admission/reference code. Only the
// standalone source metadata/counters are controlled; preserving source relays
// are separately exercised by the unchanged real source fixture build.
std::uint32_t __cdecl RizTweenSourceContext_GetInfo(RizTweenSourceInfoV1* info)
{if(!info||info->abiVersion!=1||info->byteSize!=sizeof(*info))return 1;
 *info={1,sizeof(*info),1,0,43,8192,0,1,7,0,0,0,0,0,0,0};return 0;}
std::uint32_t __cdecl RizTweenSingleEventContext_GetInfo(RizTweenSingleEventInfoV1* info)
{
    if(!info||info->abiVersion!=1||info->byteSize!=sizeof(*info))return 1;
    *info={1,sizeof(*info)};info->configured=info->attached=1;info->epoch=1;info->parentEpoch=6;
    info->parentsEntered=info->parentsInFlight=singleInFlight?1u:0u;return 0;
}
int main(int argc,char** argv)
{
    const bool fault=argc==2&&std::strcmp(argv[1],"observer-fault")==0;
    const bool referenceFault=argc==2&&std::strcmp(argv[1],"reference-fault")==0;
    const bool admissionFault=argc==2&&std::strcmp(argv[1],"admission-fault")==0;
    const bool referenceReadFault=argc==2&&std::strcmp(argv[1],"reference-read-fault")==0;
    const bool unconfigured=argc==2&&std::strcmp(argv[1],"unconfigured")==0;
    const bool enterFault=argc==2&&std::strlen(argv[1])==16&&std::strncmp(argv[1],"observer-enter-",15)==0&&argv[1][15]>='0'&&argv[1][15]<='5';
    const bool exitFault=argc==2&&std::strlen(argv[1])==15&&std::strncmp(argv[1],"observer-exit-",14)==0&&argv[1][14]>='0'&&argv[1][14]<='5';
    if(argc!=2||(!fault&&!referenceFault&&!admissionFault&&!referenceReadFault&&!unconfigured&&!enterFault&&!exitFault&&std::strcmp(argv[1],"happy"))){
        std::fprintf(stderr,"Use happy, observer-fault, observer-enter-[0-5], observer-exit-[0-5], reference-fault, admission-fault, reference-read-fault or unconfigured\n");return 2;}
    if(unconfigured)Unconfigured();
    else{
        Setup();AttachReferences();
        if(fault||enterFault||exitFault)ObserverFault(fault?5u:static_cast<unsigned>(argv[1][exitFault?14:15]-'0'),exitFault);
        else if(referenceFault)ReferenceFault();else if(admissionFault)AdmissionFault();else if(referenceReadFault)ReferenceReadFault();
        else{ReservationRaces();ActualReferenceEnd();AtomicBatch();}
    }
    RizTweenRetirementInfoV1 info{1,sizeof(info)};CHECK(RizTweenRetirement_GetInfo(&info)==0);
    CHECK(info.reservations==(referenceFault?1u:0u)&&info.entered==info.exited+info.reservations&&!info.activeCut);
    CHECK(info.unknown==(std::strcmp(argv[1],"happy")?1u:0u));
    const bool happy=std::strcmp(argv[1],"happy")==0;
    if(happy)CHECK(singleInFlightBlocksAcquire);
    std::printf("{\"singleCountersControlled\":true,\"singleInFlightBlocksAcquire\":%s,",happy&&singleInFlightBlocksAcquire?"true":"false");
    std::printf("\"kind\":\"owned-tween-retirement-native-fixture\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"originals\":%ld,\"referenceCalls\":%ld,\"cuts\":%llu,\"commits\":%llu,\"actualEntryReservations\":true,\"actualAdmissionBatch\":%s,\"actualReferenceEnd\":%s,\"allSixBeforeSnapshotRaces\":%s,\"foreignOriginalInFlightDuringCommit\":%s,\"sourceCountersControlled\":true,\"gameExecuted\":false}\n",
        argv[1],failures?"false":"true",checks,failures,originals,referenceCalls,info.cuts,info.commits,
        unconfigured?"false":"true",happy||referenceFault?"true":"false",happy?"true":"false",happy?"true":"false");
    return failures?1:0;
}
