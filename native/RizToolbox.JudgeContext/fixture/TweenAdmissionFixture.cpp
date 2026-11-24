#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../TweenAdmission.h"
#include "../OperationContext.h"

namespace {
struct alignas(8) Tween { const void* type{}; void* monitor{}; void* cached{}; };
int classMarker{}, fieldMarker{}, nativeMarker{}, foreignMarker{};
volatile LONG ready{}, checks{}, failures{}, originalCount{}, beforeCount{}, afterCount{}, skipCount{}, callbackFault{}, abnormal{};
volatile LONG physicalLane{};
void* blockedObject{}; HANDLE reached{}, resume{};
void Check(bool value, const char* expression, int line)
{ InterlockedIncrement(&checks); if (!value) { InterlockedIncrement(&failures); std::fprintf(stderr,"FAIL tween gate %d %s\n",line,expression); } }
#define CHECK(x) Check(!!(x),#x,__LINE__)
using Void = void(__cdecl*)(void*,const void*);
using Ref = void(__cdecl*)(void*,void*,const void*);
RizTweenAdmissionReceiptV1 Header() { RizTweenAdmissionReceiptV1 value{}; value.abiVersion=1;value.byteSize=sizeof(value);return value; }
RizTweenAdmissionInfoV1 Info() { RizTweenAdmissionInfoV1 value{1,sizeof(value)};CHECK(RizTweenAdmission_GetInfo(&value)==0);return value; }
RizTweenAdmissionConfigV1 Config() { return {1,sizeof(RizTweenAdmissionConfigV1),&classMarker,&fieldMarker,16,0}; }
std::uintptr_t __cdecl Before(const RizOperationFrameV1* frame,void*)
{
    InterlockedIncrement(&beforeCount); CHECK(frame && frame->sequence!=0);
    if(InterlockedCompareExchange(&callbackFault,0,0)) RaiseException(0xe0545702,0,0,nullptr);
    return static_cast<std::uintptr_t>(frame->sequence);
}
void __cdecl After(const RizOperationFrameV1* frame,std::uintptr_t token,std::uint32_t flags,void*)
{
    InterlockedIncrement(&afterCount); CHECK(token==frame->sequence || (flags&RizOperationEnterFaulted));
    if(flags&RizOperationSkipped) { CHECK((flags&(RizOperationReturned|RizOperationAbnormal))==0);InterlockedIncrement(&skipCount); }
    else CHECK((flags&(RizOperationReturned|RizOperationAbnormal))!=0);
}
void Original(void* value)
{
    InterlockedIncrement(&originalCount);
    if(value==blockedObject && reached && resume) { SetEvent(reached);WaitForSingleObject(resume,10000); }
    if(!value || InterlockedCompareExchange(&abnormal,0,0)) RaiseException(0xe0545701,0,0,nullptr);
}
void __cdecl OriginalVoid(void* self,const void*) { Original(self); }
void __cdecl OriginalRef(void*,void* arg,const void*) { Original(arg); }
void Play(void* object) { reinterpret_cast<Void>(RizOperationContext_GetEntry(0,25))(object,nullptr); }
void Add(void* object) { reinterpret_cast<Ref>(RizOperationContext_GetEntry(1,20))(&nativeMarker,object,nullptr); }
void Execute(void* object) { reinterpret_cast<Ref>(RizOperationContext_GetEntry(1,19))(&nativeMarker,object,nullptr); }
void Update(void* object) { reinterpret_cast<Void>(RizOperationContext_GetEntry(0,24))(object,nullptr); }
void Awake(void* object) { reinterpret_cast<Void>(RizOperationContext_GetEntry(0,23))(object,nullptr); }
bool Catches(void(*operation)(void*),void* object)
{ __try { operation(object);return false; } __except(EXCEPTION_EXECUTE_HANDLER) { return true; } }
DWORD WINAPI Worker(void* object) { Play(object);return 0; }
DWORD WINAPI PhysicalWorker(void* object)
{
    switch(InterlockedCompareExchange(&physicalLane,0,0)) {
        case 0:Play(object);break;case 1:Add(object);break;case 2:Execute(object);break;
        case 3:Update(object);break;default:Awake(object);break;
    }
    return 0;
}
void Bootstrap()
{
    constexpr unsigned kinds[]{0,1,1,0,0}, slots[]{25,20,19,24,23};
    for(unsigned i=0;i!=5;++i) {
        void* original=kinds[i]==0?reinterpret_cast<void*>(&OriginalVoid):reinterpret_cast<void*>(&OriginalRef);
        const auto target = std::uintptr_t{0x100000} + static_cast<std::uintptr_t>(i) * std::uintptr_t{0x100};
        RizOperationBootstrapConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(target),original};
        CHECK(RizOperationBootstrap_Configure(kinds[i],slots[i],&config)==0);
        RizOperationBootstrapInfoV1 lane{1,sizeof(lane)};CHECK(RizOperationBootstrap_GetInfo(kinds[i],slots[i],&lane)==0);
        RizOperationBootstrapObserverV1 observer{1,sizeof(observer),Before,After,nullptr};
        CHECK(RizOperationBootstrap_Attach(kinds[i],slots[i],lane.epoch,&observer)==0);
    }
}
void Configure()
{
    auto config=Config();CHECK(RizTweenAdmission_Configure(&config)==2);
    InterlockedExchange(&ready,1);auto invalid=config;invalid.cachedPointerOffset=24;CHECK(RizTweenAdmission_Configure(&invalid)==1);
    invalid=config;invalid.exactTweenClass=&foreignMarker;CHECK(RizTweenAdmission_Configure(&invalid)==1);
    CHECK(RizTweenAdmission_Configure(&config)==0);CHECK(RizTweenAdmission_Configure(&config)==4);
    auto info=Info();CHECK(info.configured==1 && info.unknown==0 && info.exactTweenClass==&classMarker && info.cachedPointerOffset==16);
}
void Normal()
{
    Tween object{&classMarker,nullptr,&nativeMarker}, peer{&classMarker,nullptr,&nativeMarker};
    // Actual dispatch remains observation-only before policy configuration.
    Play(&object);CHECK(originalCount==1);Configure();
    auto owner=Header();CHECK(RizTweenAdmission_Claim(&object,&owner)==0);
    CHECK(owner.phase==RizTweenOpen && owner.object==&object && owner.actualClass==&classMarker && owner.cachedNative==&nativeMarker && owner.epoch!=0);
    auto duplicate=Header();CHECK(RizTweenAdmission_Claim(&object,&duplicate)==3);
    Play(&object);Add(&object);Execute(&object);Play(&peer);CHECK(originalCount==5);
    Tween foreign{&foreignMarker,nullptr,nullptr};Play(&foreign);CHECK(originalCount==6);
    CHECK(Catches(Add,nullptr));CHECK(originalCount==7);
    Play(reinterpret_cast<void*>(std::uintptr_t{1}));CHECK(originalCount==8 && Info().unknown==0);
    reached=CreateEventW(nullptr,TRUE,FALSE,nullptr);resume=CreateEventW(nullptr,TRUE,FALSE,nullptr);blockedObject=&object;
    const auto worker=CreateThread(nullptr,0,Worker,&object,0,nullptr);CHECK(worker!=nullptr && WaitForSingleObject(reached,10000)==WAIT_OBJECT_0);
    auto pendingClaim=Header();CHECK(RizTweenAdmission_Claim(&peer,&pendingClaim)==6 && pendingClaim.epoch==0);
    CHECK(RizTweenAdmission_Seal(owner.slot,owner.epoch,&owner)==3 && owner.phase==RizTweenClosing && owner.issuersInFlight==1);
    const auto before=originalCount;Play(&object);Add(&object);CHECK(originalCount==before && skipCount==2);
    blockedObject=nullptr;Execute(&object);CHECK(originalCount==before+1);
    SetEvent(resume);CHECK(WaitForSingleObject(worker,10000)==WAIT_OBJECT_0);CloseHandle(worker);CloseHandle(reached);CloseHandle(resume);reached=nullptr;resume=nullptr;
    CHECK(RizTweenAdmission_Seal(owner.slot,owner.epoch,&owner)==0 && owner.phase==RizTweenSealed && owner.issuersInFlight==0 && owner.activeEntries==0);
    CHECK(RizTweenAdmission_Unregister(owner.slot,owner.epoch,&owner)==3); // Actual native object still live.
    object.cached=nullptr;
    // A real physical Update original keeps retirement pending even when the
    // owner has no active issuer or Execute entry of its own.
    for(LONG lane=0;lane!=5;++lane) {
        reached=CreateEventW(nullptr,TRUE,FALSE,nullptr);resume=CreateEventW(nullptr,TRUE,FALSE,nullptr);blockedObject=&peer;
        InterlockedExchange(&physicalLane,lane);
        const auto physical=CreateThread(nullptr,0,PhysicalWorker,&peer,0,nullptr);
        CHECK(physical!=nullptr && WaitForSingleObject(reached,10000)==WAIT_OBJECT_0);
        CHECK(RizTweenAdmission_Unregister(owner.slot,owner.epoch,&owner)==3);
        SetEvent(resume);CHECK(WaitForSingleObject(physical,10000)==WAIT_OBJECT_0);
        CloseHandle(physical);CloseHandle(reached);CloseHandle(resume);blockedObject=nullptr;reached=nullptr;resume=nullptr;
    }
    CHECK(RizTweenAdmission_Unregister(owner.slot,owner.epoch,&owner)==0 && owner.phase==RizTweenRetired && owner.object==&object && owner.cachedNative==nullptr);
    const auto retiredEpoch=owner.epoch;const auto retiredSlot=owner.slot;const auto count=originalCount;
    Play(&object);Add(&object);Execute(&object);CHECK(originalCount==count && skipCount==5);
    CHECK(Info().activeOwners==0);Play(&peer);CHECK(originalCount==count+1);
    object.cached=&nativeMarker;auto successor=Header();CHECK(RizTweenAdmission_Claim(&object,&successor)==0 && successor.epoch>retiredEpoch && successor.slot==retiredSlot);
    auto stale=Header();CHECK(RizTweenAdmission_Read(retiredSlot,retiredEpoch,&stale)==2);
    Play(&object);CHECK(originalCount==count+2);
    const auto info=Info();CHECK(info.skippedDead==3 && info.skippedSealed==2 && info.unknownReads==1 && info.unknown==0
        && info.originalCalls==static_cast<std::uint64_t>(originalCount-3) && info.calls==info.originalCalls+5);
}
void UnknownRead()
{
    Configure();auto* object=static_cast<Tween*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));CHECK(object!=nullptr);
    *object={&classMarker,nullptr,&nativeMarker};auto owner=Header();CHECK(RizTweenAdmission_Claim(object,&owner)==0);
    Tween peer{&classMarker,nullptr,&nativeMarker};auto other=Header();CHECK(RizTweenAdmission_Claim(&peer,&other)==0);
    DWORD old{};CHECK(VirtualProtect(object,4096,PAGE_NOACCESS,&old)!=FALSE);const auto count=originalCount;
    Play(object);CHECK(originalCount==count+1);CHECK(RizTweenAdmission_Read(owner.slot,owner.epoch,&owner)==5 && owner.phase==RizTweenUnknown && owner.object==object);
    CHECK(Info().unknown==1 && Info().activeOwners==2);CHECK(VirtualProtect(object,4096,old,&old)!=FALSE);
    CHECK(RizTweenAdmission_Unregister(owner.slot,owner.epoch,&owner)==5); // Retained until process exit; no retry/free proof.
    CHECK(RizTweenAdmission_Read(other.slot,other.epoch,&other)==5);
    CHECK(RizTweenAdmission_Seal(other.slot,other.epoch,&other)==5 && other.phase==RizTweenOpen);
}
void Abnormal()
{
    Configure();Tween object{&classMarker,nullptr,&nativeMarker};auto owner=Header();CHECK(RizTweenAdmission_Claim(&object,&owner)==0);
    InterlockedExchange(&abnormal,1);CHECK(Catches(Play,&object));InterlockedExchange(&abnormal,0);
    CHECK(RizTweenAdmission_Read(owner.slot,owner.epoch,&owner)==5 && owner.phase==RizTweenUnknown && owner.activeEntries==0 && owner.issuersInFlight==0);
    CHECK(Info().activeOwners==1 && Info().unknown==1);CHECK(RizTweenAdmission_Seal(owner.slot,owner.epoch,&owner)==5);
}
void Capacity()
{
    Configure();Tween objects[129]{};RizTweenAdmissionReceiptV1 first{};
    for(unsigned i=0;i!=129;++i) {
        objects[i]={&classMarker,nullptr,&nativeMarker};auto owner=Header();const auto result=RizTweenAdmission_Claim(&objects[i],&owner);
        CHECK(result==(i<128?0u:4u));if(i==0)first=owner;
    }
    CHECK(Info().activeOwners==128);CHECK(RizTweenAdmission_Seal(first.slot,first.epoch,&first)==0);
    objects[0].cached=nullptr;CHECK(RizTweenAdmission_Unregister(first.slot,first.epoch,&first)==0);
    auto reused=Header();CHECK(RizTweenAdmission_Claim(&objects[128],&reused)==0 && reused.slot==first.slot && reused.epoch>first.epoch);
    CHECK(Info().activeOwners==128);
}
}
extern "C" bool RizTweenAdmissionFixture_Ready() { return InterlockedCompareExchange(&ready,0,0)!=0; }
extern "C" bool RizTweenAdmissionFixture_Validate(const RizTweenAdmissionConfigV1* config)
{ return config && config->exactTweenClass==&classMarker && config->cachedPointerField==&fieldMarker && config->cachedPointerOffset==16; }
int wmain(int argc,wchar_t** argv)
{
    if(argc!=2)return 2;Bootstrap();
    if(std::wcscmp(argv[1],L"normal")==0)Normal();
    else if(std::wcscmp(argv[1],L"unknown-read")==0)UnknownRead();
    else if(std::wcscmp(argv[1],L"abnormal")==0)Abnormal();
    else if(std::wcscmp(argv[1],L"capacity")==0)Capacity();else return 2;
    const bool normal=std::wcscmp(argv[1],L"normal")==0;
    std::printf("{\"kind\":\"typed-tween-admission-native-fixture-mode\",\"mode\":\"%ls\",\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"originalCalls\":%ld,\"beforeCalls\":%ld,\"afterCalls\":%ld,\"skippedCalls\":%ld,\"actualNativeGateExecuted\":true,\"actualOperationDispatchExecuted\":true,\"controlledReadyAndMetadata\":true,\"productionMetadataVerified\":false,\"productionHelperExecuted\":false,\"gameExecuted\":false,\"specificIssuanceOnly\":true,",
        argv[1],failures?"false":"true",checks,failures,originalCount,beforeCount,afterCount,skipCount);
    std::printf("\"workerClosingWithoutEarlySeal\":%s,\"allFivePhysicalLanesBlockUnregister\":%s,\"deadUnownedSkipped\":%s,\"aliveUnownedForwarded\":%s,\"nullArgumentOriginalException\":%s,\"skippedFlagsWithoutReturned\":%s,\"exactOriginalCounters\":%s,\"retiredSlotNewEpoch\":%s}\n",
        normal?"true":"false",normal?"true":"false",normal?"true":"false",normal?"true":"false",
        normal?"true":"false",normal?"true":"false",normal?"true":"false",normal?"true":"false");
    return failures?1:0;
}
