// Independent owned L+M+N/Stop integration. N algorithm is derived from the separately frozen WaitManagedFixture.cpp; source is never a production publisher.
#include "../RizToolbox.EngineWaitFixture/WaitOwned.h"
#include <intrin.h>
#include <cstring>
#include <cstdio>
#include <climits>
namespace CancellationOwned { void DuringExecute(WaitOwnedNode*,WaitOwnedObject*); }
extern "C" { void* RtCancellationInlineEntry{}; void RtCancellationStopAndDrop(WaitOwnedNode*); extern unsigned char RtCancellationInlineBeginSite,RtCancellationInlineBeginReturn,RtCancellationInlineFreeSite,RtCancellationInlineFreeReturn,RtCancellationInlineRetainedSite,RtCancellationInlineRetainedReturn; }


// This DLL owns all source instructions and allocations. It only Configures
// the observation kernel. The CLR test adopts and Attaches the actual bridges.
#define EXPORT extern "C" __declspec(dllexport)
extern "C" {
void* RtWaitFixture_CallbackEntry{};
void* RtWaitFixture_ReleaseEntry{};
std::uint64_t RtWaitFixture_RipValue=0x8877665544332211ULL;
RizEventMidFrameV1 RtWaitFixture_Before[18]{},RtWaitFixture_After[18]{};
#define PHASE(n) extern unsigned char RtWaitFixture_Phase##n,RtWaitFixture_After##n;
PHASE(0) PHASE(1) PHASE(2) PHASE(3) PHASE(4) PHASE(5) PHASE(6) PHASE(7) PHASE(8)
PHASE(9) PHASE(10) PHASE(11) PHASE(12) PHASE(13) PHASE(14) PHASE(15) PHASE(16) PHASE(17)
extern unsigned char RtWaitFixture_YieldEnd,RtWaitFixture_DispatchEnd,RtWaitFixture_BatchEnd;
extern unsigned char RtWaitFixture_StartEnd,RtWaitFixture_ReleaseEnd,RtWaitFixture_DetachEnd;
extern unsigned char RtWaitFixture_DispatchCallbackReturn,RtWaitFixture_DispatchCleanupReturn,RtWaitFixture_BatchCleanupReturn;
void RtWaitFixture_SharedReturn(void*);
void* RtWaitFixture_StartPrepare(void*,void*,void*,void*);
std::uint8_t RtWaitFixture_StartFinish(WaitOwnedNode*,void**);
}
namespace {
constexpr unsigned Slot=26,MaximumNodes=2,MaximumWaits=2;
constexpr DWORD InputError=0x12345678,OriginalError=0x76543210;
constexpr std::uint64_t Handle1=0x1234567800000001ULL,Handle2=0x1234567800000002ULL;
constexpr std::uint64_t YieldRaw=0x1122334455667701ULL,DispatchRaw=0x2233445566778802ULL,BatchRaw=0x3344556677889903ULL;
using Carrier=std::uint64_t(__cdecl*)(void*,void*,void*,void*);
using Start=std::uint8_t(__cdecl*)(void*,void*,void*,void*);
using Execute=std::uint32_t(__cdecl*)(void*,void*);
using Release=std::uint8_t(__cdecl*)(void*);
using Detach=void(__cdecl*)(void*);
using Probe=void(__cdecl*)(std::uint32_t,void*,void*,void*);
struct Descriptor {
    std::uint32_t version,size,mode,slot,mainThread,configured,checks,failures;
    void* image;void* kernel;void* relay;void* phaseEntry;void* owner;void* enumerator;void* manager;
    std::uint64_t handle;
    void* nodeTargets[4];void* schedulerTargets[6];void* waitTargets[3];void* waitOriginals[3];
    RizEngineWaitSiteV1 sites[18];RizEngineSchedulerSiteV1 schedulerSites[2];const void* callers[3];
    void* enumerator2;std::uint64_t handle2;
};
struct Receipt {
    std::uint32_t version,size,mode,checks,failures,nodeStarts,nodeExecutes,nodeReleases,nodeDetaches,nodeFreed,waitFreed;
    std::uint32_t publications,dispatches,batches,callbacks,cleanups,sharedReturns,oldReturns,probeCalls,liveNodes,liveWaits,reserved0,reserved1,reserved2;
    void* image;void* relay;void* node;void* wait0;void* wait1;
    std::uint64_t callbackRaw,yieldRaw,dispatchRaw,batchRaw;
    void* secondNode;
};
static_assert(sizeof(Descriptor)==584&&offsetof(Descriptor,sites)==224&&offsetof(Descriptor,enumerator2)==568);
static_assert(sizeof(Receipt)==176&&offsetof(Receipt,callbackRaw)==136&&offsetof(Receipt,secondNode)==168);
struct NodeRow {WaitOwnedNode* value{};void* reservation{};unsigned references{};bool freed{};};
struct WaitRow {WaitOwnedObject* value{};unsigned references{};bool freed{};};
struct Site {
    std::uint64_t site{},continuation{},branch{},relay{},returnPc{};
    unsigned span{},length{};unsigned char original[16]{},replacement[16]{};
    RUNTIME_FUNCTION source{};
};
Descriptor descriptor{};Receipt result{};NodeRow nodes[MaximumNodes];WaitRow waits[MaximumWaits];Site sites[18];
HMODULE self{},kernel{};HANDLE kernelLock=INVALID_HANDLE_VALUE;
unsigned mode{},nodeCount{},waitCount{};bool configured{},ran{},inCallback{},replaced{};
unsigned char ownerValue{},enumeratorValue{},enumerator2Value{},nameValue{};
Carrier waitEntries[3]{},schedulerEntries[6]{};Start startEntry{};Execute executeEntry{};Release releaseEntry{};Detach detachEntry{};
unsigned char* relayBlock{};RUNTIME_FUNCTION* relayFunctions{};void* phaseEntry{};
WaitOwnedManager manager{};WaitOwnedObject* batchObjects[MaximumWaits]{};
WaitOwnedNode* current{};WaitOwnedObject* publishing{};WaitOwnedObject* activeDispatch{};
Probe probe{};void* probeUser{};

void Check(bool okay,const char* text,int line){++result.checks;if(!okay){++result.failures;std::fprintf(stderr,"WaitManagedFixture line %d: %s\n",line,text);std::fflush(stderr);}}
#define CHECK(x) Check(!!(x),#x,__LINE__)
template<class T>T Symbol(const char* name){auto p=GetProcAddress(kernel,name);CHECK(p);return reinterpret_cast<T>(p);}
NodeRow* Node(WaitOwnedNode* value){for(auto& row:nodes)if(row.value==value&&!row.freed)return &row;return nullptr;}
WaitRow* Wait(WaitOwnedObject* value){for(auto& row:waits)if(row.value==value&&!row.freed)return &row;return nullptr;}
void Notify(unsigned stage,WaitOwnedNode* node,WaitOwnedObject* wait){const DWORD error=GetLastError();++result.probeCalls;probe(stage,node,wait,probeUser);SetLastError(error);}
[[noreturn]]void AllocationFailure(){std::fprintf(stderr,"WaitManagedFixture allocation failed, LastError=%lu\n",GetLastError());std::fflush(stderr);ExitProcess(3);}
WaitOwnedNode* NewNode(void* owner,void* cache){
    CHECK(nodeCount<MaximumNodes);if(nodeCount==MaximumNodes)ExitProcess(3);
    auto& row=nodes[nodeCount];auto* value=static_cast<WaitOwnedNode*>(VirtualAlloc(row.reservation,4096,MEM_COMMIT,PAGE_READWRITE));
    if(!value)AllocationFailure();CHECK(value==row.reservation);row.value=value;row.references=1;
    value->enumHandle=nodeCount?Handle2:Handle1;value->enumType=2;value->enumCache=cache;value->owner=owner;value->references=1;
    ++nodeCount;return value;
}
WaitOwnedObject* NewWait(){
    CHECK(waitCount<MaximumWaits);if(waitCount==MaximumWaits)ExitProcess(3);
    auto* value=static_cast<WaitOwnedObject*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));if(!value)AllocationFailure();
    value->sharedReferences=1;auto& row=waits[waitCount++];row.value=value;row.references=1;return value;
}
void FreeNode(WaitOwnedNode* value){auto* row=Node(value);CHECK(row&&row->references==0&&value->wait==nullptr);if(!row)return;
    CHECK(VirtualFree(value,4096,MEM_DECOMMIT));row->freed=true;++result.nodeFreed;}
void Publish(WaitOwnedNode* node,WaitOwnedObject* wait){
    auto* old=node->wait;SetLastError(InputError);const auto raw=waitEntries[0](node,wait,nullptr,nullptr);
    CHECK(raw==YieldRaw&&GetLastError()==OriginalError);result.yieldRaw=raw;++result.publications;
    auto* row=Wait(wait);CHECK(row&&wait->sharedReferences==row->references+1);if(row)++row->references;
    if(old)++result.oldReturns;Notify(2,node,wait);
}
std::uint8_t __cdecl IsDone(WaitOwnedObject*){return 0;}
void* waitVtable[2]{nullptr,reinterpret_cast<void*>(&IsDone)};
void StartSecond(){void* output{};SetLastError(InputError);CHECK(startEntry(&ownerValue,&enumerator2Value,&nameValue,&output)==1);CHECK(output==current&&Node(current));result.secondNode=current;}
std::uint32_t __cdecl ExecuteOriginal(void* value,void* output){
    auto* node=static_cast<WaitOwnedNode*>(value);CHECK(Node(node));++result.nodeExecutes;if(output)*static_cast<unsigned char*>(output)=0;
    if(publishing){auto* wait=publishing;publishing=nullptr;Publish(node,wait);
        // Preserve the real Start inline decision ref<=1. The yielded reference
        // is first returned by a real typed Release (2 -> 1, no final clear).
        if(mode==3)CancellationOwned::DuringExecute(node,wait);}
    SetLastError(OriginalError);return mode==2?0x12340001u:1u;
}
std::uint64_t __cdecl CallbackOriginal(void* owner,void* payload,void*,void*){
    auto* node=static_cast<WaitOwnedNode*>(payload);CHECK(owner==&ownerValue&&Node(node));++result.callbacks;inCallback=true;
    unsigned char error=0xff;const auto raw=executeEntry(node,&error);CHECK(raw==(mode==2?0x12340001u:1u)&&error==0);
    if(mode==5||mode==7){
        // Old node/wait withdrawal uses the actual typed Release and all three
        // source windows; only the retained manager/root wait ref survives.
        while(auto* row=Node(node)){if(!row->references)break;SetLastError(InputError);releaseEntry(node);}
        CHECK(!Node(node));
        if(mode==7){publishing=activeDispatch;StartSecond();}
    }
    inCallback=false;result.callbackRaw=mode==2?0xabcdef0112340001ULL:0xabcdef0100000001ULL;
    SetLastError(OriginalError);return result.callbackRaw;
}
std::uint8_t __cdecl StartOriginal(void* owner,void* cache,void* name,void* output){
    if(mode==3)return RtWaitFixture_Start(owner,cache,name,output);
    auto* node=static_cast<WaitOwnedNode*>(RtWaitFixture_StartPrepare(owner,cache,name,output));
    // The actual Unity Start skips inline final cleanup when refs > 1. The
    // shared ASM is the cleanup branch only, so ordinary retained Starts take
    // this real scalar branch without executing that ASM's clear windows.
    CHECK(Node(node)&&node->references>1);
    return RtWaitFixture_StartFinish(node,static_cast<void**>(output));
}
__declspec(noinline)std::uint8_t __cdecl StartTarget(void*a,void*b,void*c,void*d){const auto r=startEntry(a,b,c,d);_ReadWriteBarrier();return r;}
__declspec(noinline)std::uint32_t __cdecl ExecuteTarget(void*a,void*b){const auto r=executeEntry(a,b);_ReadWriteBarrier();return r;}
__declspec(noinline)void __cdecl DetachTarget(void*a){detachEntry(a);_ReadWriteBarrier();}
template<unsigned Kind>__declspec(noinline)std::uint64_t __cdecl WaitTarget(void*a,void*b,void*c,void*d){const auto r=waitEntries[Kind](a,b,c,d);_ReadWriteBarrier();return r;}
template<unsigned Kind>__declspec(noinline)std::uint64_t __cdecl SchedulerTarget(void*a,void*b,void*c,void*d){const auto r=schedulerEntries[Kind](a,b,c,d);_ReadWriteBarrier();return r;}
template<unsigned Kind>__declspec(noinline)std::uint64_t __cdecl SchedulerOriginal(void*,void*,void*,void*){_ReadWriteBarrier();SetLastError(OriginalError);return 0xaabbccddeeff0000ULL+Kind;}
__declspec(noinline)void DummyDrainBegin(){_ReadWriteBarrier();}
__declspec(noinline)void DummyDrainEnd(){_WriteBarrier();}
__declspec(noinline)void DummyDrainBeginReturn(){_ReadBarrier();}
__declspec(noinline)void DummyDrainEndReturn(){_ReadWriteBarrier();_WriteBarrier();}
bool Jump(unsigned char* at,std::uint64_t pc,std::uint64_t target){const auto delta=static_cast<std::int64_t>(target)-static_cast<std::int64_t>(pc+5);
    if(delta<INT32_MIN||delta>INT32_MAX)return false;at[0]=0xe9;const auto relative=static_cast<std::int32_t>(delta);std::memcpy(at+1,&relative,4);return true;}

bool PrepareRelays(){
    const auto image=reinterpret_cast<std::uint64_t>(self);const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(self);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(image+dos->e_lfanew);
    for(auto next=(image+nt->OptionalHeader.SizeOfImage+0xffff)&~std::uint64_t(0xffff);next<image+0x70000000;next+=0x10000){
        MEMORY_BASIC_INFORMATION memory{};if(!VirtualQuery(reinterpret_cast<void*>(next),&memory,sizeof(memory)))break;
        if(memory.State==MEM_FREE&&memory.RegionSize>=8192){relayBlock=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(next),8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));if(relayBlock)break;}}
    CHECK(relayBlock);if(!relayBlock)return false;relayFunctions=reinterpret_cast<RUNTIME_FUNCTION*>(relayBlock+0x1400);
    const void* points[]{
#define SITE(n) &RtWaitFixture_Phase##n
        SITE(0),SITE(1),SITE(2),SITE(3),SITE(4),SITE(5),SITE(6),SITE(7),SITE(8),SITE(9),SITE(10),SITE(11),SITE(12),SITE(13),SITE(14),SITE(15),SITE(16),SITE(17)};
    const void* after[]{
#define AFTER(n) &RtWaitFixture_After##n
        AFTER(0),AFTER(1),AFTER(2),AFTER(3),AFTER(4),AFTER(5),AFTER(6),AFTER(7),AFTER(8),AFTER(9),AFTER(10),AFTER(11),AFTER(12),AFTER(13),AFTER(14),AFTER(15),AFTER(16),AFTER(17)};
    const unsigned spans[]{10,6,8,5,10,10,7,8,8,7,8,9,5,7,5,8,6,10};
    for(unsigned i=0;i<18;++i){auto& site=sites[i];site.site=reinterpret_cast<std::uint64_t>(points[i]);site.continuation=reinterpret_cast<std::uint64_t>(after[i]);site.span=spans[i];
        CHECK(site.continuation-site.site==site.span);DWORD64 sourceImage{};const auto* source=RtlLookupFunctionEntry(site.site,&sourceImage,nullptr);CHECK(source&&sourceImage==image);if(!source)return false;
        const auto begin=i<4?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Yield):i<6?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Detach):i<9?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Release):i<12?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Start):i<16?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Dispatch):reinterpret_cast<std::uint64_t>(&RtWaitFixture_Batch);
        const auto end=i<4?reinterpret_cast<std::uint64_t>(&RtWaitFixture_YieldEnd):i<6?reinterpret_cast<std::uint64_t>(&RtWaitFixture_DetachEnd):i<9?reinterpret_cast<std::uint64_t>(&RtWaitFixture_ReleaseEnd):i<12?reinterpret_cast<std::uint64_t>(&RtWaitFixture_StartEnd):i<16?reinterpret_cast<std::uint64_t>(&RtWaitFixture_DispatchEnd):reinterpret_cast<std::uint64_t>(&RtWaitFixture_BatchEnd);
        CHECK(image+source->BeginAddress==begin&&image+source->EndAddress==end);site.source=*source;site.relay=reinterpret_cast<std::uint64_t>(relayBlock+i*128);site.returnPc=site.relay+6;
        std::memcpy(site.original,points[i],site.span);auto* code=relayBlock+i*128;const unsigned char call[]{0xff,0x15,0x52,0,0,0};std::memcpy(code,call,6);std::memcpy(code+88,&phaseEntry,8);unsigned cursor=6;
        if(i==1||i==12||i==14||i==16){const unsigned prefix=i==1?0u:i==16?4u:3u;if(prefix){std::memcpy(code+cursor,site.original,prefix);cursor+=prefix;}
            CHECK(site.original[prefix]==0x74);site.branch=site.site+prefix+2+static_cast<std::int8_t>(site.original[prefix+1]);code[cursor]=0x0f;code[cursor+1]=0x84;
            const auto delta=static_cast<std::int64_t>(site.branch)-static_cast<std::int64_t>(site.relay+cursor+6);CHECK(delta>=INT32_MIN&&delta<=INT32_MAX);const auto relative=static_cast<std::int32_t>(delta);std::memcpy(code+cursor+2,&relative,4);cursor+=6;
            if(i==1){std::memcpy(code+cursor,site.original+2,4);cursor+=4;}}
        else if(i==3){CHECK(site.original[0]==0xe9);std::int32_t delta{};std::memcpy(&delta,site.original+1,4);site.branch=site.site+5+delta;CHECK(Jump(code+cursor,site.relay+cursor,site.branch));cursor+=5;}
        else{std::memcpy(code+cursor,site.original,site.span);if(i==0||i==4||i==5){CHECK((site.original[5]&0xc7)==5);std::int32_t old{};std::memcpy(&old,site.original+6,4);const auto destination=site.site+10+old;
                const auto delta=static_cast<std::int64_t>(destination)-static_cast<std::int64_t>(site.relay+cursor+10);CHECK(delta>=INT32_MIN&&delta<=INT32_MAX);const auto relative=static_cast<std::int32_t>(delta);std::memcpy(code+cursor+6,&relative,4);}cursor+=site.span;}
        if(i!=3){CHECK(Jump(code+cursor,site.relay+cursor,site.continuation));cursor+=5;}site.length=cursor;
        auto* unwind=relayBlock+0x1000+i*32;unwind[0]=1|(UNW_FLAG_CHAININFO<<3);std::memcpy(unwind+4,source,sizeof(*source));
        relayFunctions[i]={static_cast<DWORD>(site.relay-image),static_cast<DWORD>(site.relay+cursor-image),static_cast<DWORD>(reinterpret_cast<std::uint64_t>(unwind)-image)};
        std::memset(site.replacement,0x90,site.span);CHECK(Jump(site.replacement,site.site,site.relay));descriptor.sites[i]={points[i],reinterpret_cast<void*>(site.returnPc)};
    }
    descriptor.callers[0]=&RtWaitFixture_DispatchCallbackReturn;descriptor.callers[1]=&RtWaitFixture_DispatchCleanupReturn;descriptor.callers[2]=&RtWaitFixture_BatchCleanupReturn;
    CHECK(descriptor.callers[0]==reinterpret_cast<void*>(sites[13].site+0xa));CHECK(descriptor.callers[1]==reinterpret_cast<void*>(sites[14].site+0xb));CHECK(descriptor.callers[2]==reinterpret_cast<void*>(sites[16].site+0xd));
    DWORD previous{};CHECK(VirtualProtect(relayBlock,8192,PAGE_EXECUTE_READ,&previous));CHECK(FlushInstructionCache(GetCurrentProcess(),relayBlock,8192));CHECK(RtlAddFunctionTable(relayFunctions,18,image));return result.failures==0;
}
bool InstallOwnedWindows(){for(const auto& site:sites){DWORD previous{},unused{};auto* target=reinterpret_cast<void*>(site.site);CHECK(std::memcmp(target,site.original,site.span)==0);
    if(!VirtualProtect(target,site.span,PAGE_EXECUTE_READWRITE,&previous)){CHECK(false);return false;}std::memcpy(target,site.replacement,site.span);CHECK(FlushInstructionCache(GetCurrentProcess(),target,site.span));CHECK(VirtualProtect(target,site.span,previous,&unused));}return result.failures==0;}
void DispatchWait(WaitOwnedObject* wait){activeDispatch=wait;SetLastError(InputError);const auto raw=waitEntries[1](wait,nullptr,nullptr,nullptr);CHECK(raw==DispatchRaw&&GetLastError()==OriginalError);result.dispatchRaw=raw;++result.dispatches;activeDispatch=nullptr;Notify(3,current,wait);}
void BatchAll(){unsigned count=0;for(auto& row:waits)if(row.value&&!row.freed)batchObjects[count++]=row.value;manager.objects=batchObjects;manager.count=count;
    SetLastError(InputError);const auto raw=waitEntries[2](&manager,nullptr,nullptr,nullptr);CHECK(raw==BatchRaw&&GetLastError()==OriginalError);result.batchRaw=raw;++result.batches;Notify(4,current,nullptr);}
}

extern "C" void* RtWaitFixture_StartPrepare(void* owner,void* cache,void*,void*){
    ++result.nodeStarts;CHECK(owner==&ownerValue&&(cache==&enumeratorValue||cache==&enumerator2Value));current=NewNode(owner,cache);
    if(!publishing){publishing=NewWait();publishing->vtable=waitVtable;if(!result.wait0)result.wait0=publishing;else result.wait1=publishing;}
    unsigned char error=0xff;CHECK(executeEntry(current,&error)==(mode==2?0x12340001u:1u)&&error==0);
    if(mode==3){auto* row=Node(current);CHECK(row&&row->references==1&&current->references==1);
        // Actual original Start work analogous to 7924CD, not an observation
        // receipt. The shared ASM then executes the real three wait clear sites.
        // The independent L Begin probe runs before StopAndDrop returns this final reference.
    }
    return current;
}
extern "C" std::uint8_t RtWaitFixture_StartFinish(WaitOwnedNode* node,void** output){CHECK(Node(node)&&output);
    if(mode==3){auto* row=Node(node);CHECK(node->wait==nullptr&&row&&row->references==0&&node->references==0);FreeNode(node);*output=nullptr;SetLastError(OriginalError);return 0;}
    *output=node;SetLastError(OriginalError);return 1;
}
extern "C" unsigned RtWaitFixture_ReleasePrepare(WaitOwnedNode* node){auto* row=Node(node);CHECK(row&&row->references>0&&node->references==static_cast<std::int32_t>(row->references));++result.nodeReleases;
    if(activeDispatch||inCallback)++result.cleanups;if(!row)return 0;--row->references;--node->references;return row->references==0?1u:0u;
}
extern "C" std::uint8_t RtWaitFixture_ReleaseFinish(WaitOwnedNode* node){auto* row=Node(node);CHECK(row);if(row&&!row->references){FreeNode(node);SetLastError(OriginalError);return 0;}SetLastError(OriginalError);return 1;}
extern "C" void* RtWaitFixture_Nested(void*){return nullptr;}
extern "C" void RtWaitFixture_SharedReturn(void* address){auto* wait=reinterpret_cast<WaitOwnedObject*>(static_cast<unsigned char*>(address)-8);auto* row=Wait(wait);CHECK(row&&row->references&&wait->sharedReferences==row->references);if(!row)return;
    --row->references;--wait->sharedReferences;++result.sharedReturns;if(!row->references){CHECK(VirtualFree(wait,0,MEM_RELEASE));row->freed=true;++result.waitFreed;}SetLastError(OriginalError);
}
extern "C" unsigned RtWaitFixture_YieldPrepare(WaitOwnedNode* node,WaitOwnedObject* wait,void*,void*){auto* row=Node(node);CHECK(row&&Wait(wait)&&wait->callback==nullptr);if(!row)return 0;++row->references;++node->references;return 1;}
extern "C" void* RtWaitFixture_Owner(std::uint32_t* id){CHECK(id);return id&&*id?&ownerValue:nullptr;}
extern "C" std::uint64_t RtWaitFixture_CompleteOriginal(unsigned kind){if(kind==3)++result.nodeDetaches;SetLastError(OriginalError);return kind==0?YieldRaw:kind==1?DispatchRaw:kind==2?BatchRaw:0x445566778899aa04ULL;}

EXPORT std::uint32_t __cdecl RizEngineWaitManaged_GetDescriptorSize(){return sizeof(Descriptor);}
EXPORT std::uint32_t __cdecl RizEngineWaitManaged_GetReceiptSize(){return sizeof(Receipt);}
EXPORT std::uint32_t __cdecl RizEngineWaitManaged_Configure(const wchar_t* path,std::uint32_t scenario,Descriptor* output){
    if(!path||!output||output->version!=1||output->size!=sizeof(Descriptor)||configured||scenario>7)return 1;
    mode=scenario;result.version=1;result.size=sizeof(result);result.mode=mode;
    CHECK(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,reinterpret_cast<LPCWSTR>(&RizEngineWaitManaged_Configure),&self));
    kernelLock=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);CHECK(kernelLock!=INVALID_HANDLE_VALUE);
    kernel=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);CHECK(kernel);if(!kernel)return 2;
    descriptor.version=1;descriptor.size=sizeof(descriptor);descriptor.mode=mode;descriptor.slot=Slot;descriptor.mainThread=GetCurrentThreadId();descriptor.image=self;descriptor.kernel=kernel;
    descriptor.owner=&ownerValue;descriptor.enumerator=&enumeratorValue;descriptor.enumerator2=&enumerator2Value;descriptor.handle=Handle1;descriptor.handle2=Handle2;descriptor.manager=&manager;
    auto getPhase=Symbol<decltype(&RizEngineWaitContext_GetPhaseEntry)>("RizEngineWaitContext_GetPhaseEntry");auto configureWait=Symbol<decltype(&RizEngineWaitContext_Configure)>("RizEngineWaitContext_Configure");
    auto getWait=Symbol<decltype(&RizEngineWaitContext_GetEntry)>("RizEngineWaitContext_GetEntry");auto configureNode=Symbol<decltype(&RizEngineNodeContext_Configure)>("RizEngineNodeContext_Configure");auto getNode=Symbol<decltype(&RizEngineNodeContext_GetEntry)>("RizEngineNodeContext_GetEntry");
    auto configureScheduler=Symbol<decltype(&RizEngineSchedulerContext_Configure)>("RizEngineSchedulerContext_Configure");auto getScheduler=Symbol<decltype(&RizEngineSchedulerContext_GetEntry)>("RizEngineSchedulerContext_GetEntry");if(result.failures)return 3;
    phaseEntry=getPhase();descriptor.phaseEntry=phaseEntry;CHECK(phaseEntry);if(!PrepareRelays())return 4;descriptor.relay=relayBlock;
    void* nodeTargets[]{reinterpret_cast<void*>(&StartTarget),reinterpret_cast<void*>(&ExecuteTarget),reinterpret_cast<void*>(&RtWaitFixture_CleanupTarget),reinterpret_cast<void*>(&DetachTarget)};
    void* nodeOriginals[]{reinterpret_cast<void*>(&StartOriginal),reinterpret_cast<void*>(&ExecuteOriginal),reinterpret_cast<void*>(&RtWaitFixture_Release),reinterpret_cast<void*>(&RtWaitFixture_Detach)};
    for(unsigned kind=0;kind<4;++kind){descriptor.nodeTargets[kind]=nodeTargets[kind];RizEngineNodeConfigV1 config{1,sizeof(config),nodeTargets[kind],nodeOriginals[kind]};CHECK(configureNode(kind,Slot,&config)==0);}
    startEntry=reinterpret_cast<Start>(getNode(0,Slot));executeEntry=reinterpret_cast<Execute>(getNode(1,Slot));releaseEntry=reinterpret_cast<Release>(getNode(2,Slot));detachEntry=reinterpret_cast<Detach>(getNode(3,Slot));RtWaitFixture_ReleaseEntry=reinterpret_cast<void*>(releaseEntry);
    Carrier schedulerTargets[]{&SchedulerTarget<0>,&SchedulerTarget<1>,&SchedulerTarget<2>,&SchedulerTarget<3>,&SchedulerTarget<4>,&RtWaitFixture_CallbackTarget};
    Carrier schedulerOriginals[]{&SchedulerOriginal<0>,&SchedulerOriginal<1>,&SchedulerOriginal<2>,&SchedulerOriginal<3>,&SchedulerOriginal<4>,&CallbackOriginal};
    // These distinct owned descriptor identities belong to an unused Drain
    // channel. No Drain/row is invoked or claimed by this wait integration.
    descriptor.schedulerSites[0]={reinterpret_cast<void*>(&DummyDrainBegin),reinterpret_cast<void*>(&DummyDrainBeginReturn)};descriptor.schedulerSites[1]={reinterpret_cast<void*>(&DummyDrainEnd),reinterpret_cast<void*>(&DummyDrainEndReturn)};
    for(unsigned kind=0;kind<6;++kind){descriptor.schedulerTargets[kind]=reinterpret_cast<void*>(schedulerTargets[kind]);RizEngineSchedulerConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(schedulerTargets[kind]),reinterpret_cast<void*>(schedulerOriginals[kind]),descriptor.mainThread,0,{}};
        if(kind==4){config.sites[0]=descriptor.schedulerSites[0];config.sites[1]=descriptor.schedulerSites[1];}CHECK(configureScheduler(kind,Slot,&config)==0);schedulerEntries[kind]=reinterpret_cast<Carrier>(getScheduler(kind,Slot));CHECK(schedulerEntries[kind]);}
    RtWaitFixture_CallbackEntry=reinterpret_cast<void*>(schedulerEntries[5]);
    RizEngineWaitConfigV1 config{};config.abiVersion=1;config.byteSize=sizeof(config);config.mainThreadId=descriptor.mainThread;
    Carrier targets[]{&WaitTarget<0>,&WaitTarget<1>,&WaitTarget<2>};Carrier originals[]{&RtWaitFixture_Yield,&RtWaitFixture_Dispatch,&RtWaitFixture_Batch};
    for(unsigned i=0;i<3;++i){descriptor.waitTargets[i]=config.targets[i]=reinterpret_cast<void*>(targets[i]);descriptor.waitOriginals[i]=config.originals[i]=reinterpret_cast<void*>(originals[i]);}
    for(unsigned i=0;i<4;++i)config.nodeTargets[i]=descriptor.nodeTargets[i];for(unsigned i=0;i<6;++i)config.schedulerTargets[i]=descriptor.schedulerTargets[i];for(unsigned i=0;i<18;++i)config.sites[i]=descriptor.sites[i];
    CHECK(configureWait(Slot,&config)==0);for(unsigned i=0;i<3;++i){waitEntries[i]=reinterpret_cast<Carrier>(getWait(i,Slot));CHECK(waitEntries[i]);}
    for(auto& row:nodes){row.reservation=VirtualAlloc(nullptr,65536,MEM_RESERVE,PAGE_NOACCESS);CHECK(row.reservation);if(!row.reservation)AllocationFailure();}
    if(!InstallOwnedWindows())return 5;configured=result.failures==0;descriptor.configured=configured?1u:0u;descriptor.checks=result.checks;descriptor.failures=result.failures;*output=descriptor;return configured?0u:6u;
}
EXPORT std::uint32_t __cdecl RizEngineWaitManaged_Run(Probe notify,void* user,Receipt* output){
    if(!configured||ran||!notify||!output||output->version!=1||output->size!=sizeof(Receipt))return 1;
    ran=true;probe=notify;probeUser=user;void* nodeOutput{};SetLastError(InputError);const auto started=startEntry(&ownerValue,&enumeratorValue,&nameValue,&nodeOutput);
    CHECK(started==(mode==3?0u:1u)&&GetLastError()==OriginalError);result.node=current;Notify(1,current,static_cast<WaitOwnedObject*>(result.wait0));
    if(mode!=3){CHECK(nodeOutput==current&&Node(current));auto* first=static_cast<WaitOwnedObject*>(result.wait0);
        if(mode==6){auto* next=NewWait();next->vtable=waitVtable;result.wait1=next;publishing=next;unsigned char error=0xff;CHECK(executeEntry(current,&error)==1&&error==0);replaced=true;}
        if(mode==1){SetLastError(InputError);detachEntry(current);CHECK(first->ownerId==0&&GetLastError()==OriginalError);}
        if(mode==4){
            // Observe a callback-null Batch while cleanup/payload are still
            // present, then final Release owns the remaining shared reference.
            DispatchWait(first);BatchAll();
        }else if(mode==6){
            DispatchWait(first);
            // The first publication is retired, but the manager/root still
            // retains this wait object. Reenter its actual callback-null guard
            // and complete the whole physical Dispatch without borrowing node.
            DispatchWait(first);
            DispatchWait(static_cast<WaitOwnedObject*>(result.wait1));
        }
        else DispatchWait(first);
        if(auto* row=Node(current)){while(row->references){SetLastError(InputError);releaseEntry(current);if(row->freed)break;}}
        if(mode!=4)BatchAll();
    }
    else BatchAll();
    for(const auto& row:nodes)if(row.value&&!row.freed)++result.liveNodes;for(const auto& row:waits)if(row.value&&!row.freed)++result.liveWaits;
    CHECK(result.liveNodes==0&&result.liveWaits==0&&result.nodeFreed==(mode==7?2u:1u)&&result.waitFreed==(mode==6?2u:1u));
    if(mode==2)CHECK(result.callbackRaw==0xabcdef0112340001ULL);if(mode==1)CHECK(result.callbacks==0);if(mode==3)CHECK(result.publications==1&&result.callbacks==0&&result.nodeReleases==1);
    if(mode==7)CHECK(result.publications==2&&result.nodeStarts==2&&result.secondNode&&result.secondNode!=result.node);
    if(mode==6)CHECK(replaced&&result.publications==2&&result.oldReturns==1);
    result.image=self;result.relay=relayBlock;Notify(5,current,nullptr);*output=result;return result.failures?2u:0u;
}

namespace CancellationOwned {
using Stop = void(__cdecl*)(void*,void*,const void*);
struct SourceObject {
    std::uint64_t generation;
    void* cachedOwner;
    std::uint32_t role,state,references,producerReferences,openScopes,withdrawAttempts,rootReleaseAttempts,reserved;
    std::uint64_t withdrawn,dead;
};
struct SourceRow {
    std::uint64_t generation;void* object;void* cachedOwner;
    std::uint32_t role,state,references,producerReferences,openScopes,withdrawAttempts,rootReleaseAttempts,reserved;
    std::uint64_t withdrawn,dead;
};
struct Extension {
    std::uint32_t version,size,configured,ran,sourceCount,stopCalls,stopOriginalCalls,closed;
    std::uint64_t eventSequence;
    void* stopOriginal;void* stopEntry;void* receivers[2];void* nativeOwners[2];
    RizEngineInlineSiteV1 inlineSites[3];SourceRow sources[5];
};
static_assert(sizeof(SourceObject)==64&&sizeof(SourceRow)==72&&sizeof(Extension)==496);
SourceObject* objects[5]{};SourceRow finalRows[5]{};
std::uint64_t eventSequence=0;unsigned stopCalls=0,stopOriginalCalls=0;
Stop stopEntry{};Detach actualDetach{};bool closed=false;
unsigned char secondNativeOwner{},stopMethod{};
void* pendingNode{};RizEngineInlineSiteV1 inlineSites[3]{};

SourceRow ReadSource(unsigned index){
    const auto* value=objects[index];if(!value)return finalRows[index];
    return {value->generation,const_cast<SourceObject*>(value),value->cachedOwner,value->role,value->state,value->references,
        value->producerReferences,value->openScopes,value->withdrawAttempts,value->rootReleaseAttempts,0,value->withdrawn,value->dead};
}
void Copy(Extension* output){
    Extension value{};value.version=1;value.size=sizeof(value);value.configured=configured?1u:0u;value.ran=ran?1u:0u;
    value.sourceCount=5;value.stopCalls=stopCalls;value.stopOriginalCalls=stopOriginalCalls;value.closed=closed?1u:0u;
    value.eventSequence=eventSequence;value.stopEntry=reinterpret_cast<void*>(stopEntry);
    value.receivers[0]=objects[0]?static_cast<void*>(objects[0]):finalRows[0].object;
    value.receivers[1]=objects[2]?static_cast<void*>(objects[2]):finalRows[2].object;
    value.nativeOwners[0]=&ownerValue;value.nativeOwners[1]=&secondNativeOwner;
    for(unsigned i=0;i<3;++i)value.inlineSites[i]=inlineSites[i];for(unsigned i=0;i<5;++i)value.sources[i]=ReadSource(i);
    *output=value;
}
__declspec(noinline)void __cdecl StopOriginal(void* receiver,void* enumerator,const void* method){
    CHECK(receiver==objects[0]&&enumerator==&enumeratorValue&&method==&stopMethod&&pendingNode&&actualDetach);
    CHECK(objects[0]&&objects[0]->state==0&&objects[0]->openScopes==1);++stopOriginalCalls;
    actualDetach(pendingNode);_ReadWriteBarrier();
}
bool Idle(){
    auto nodeInfo=Symbol<decltype(&RizEngineNodeContext_GetInfo)>("RizEngineNodeContext_GetInfo");
    auto sideInfo=Symbol<decltype(&RizEngineNodeContext_GetInlineInfo)>("RizEngineNodeContext_GetInlineInfo");
    auto schedulerInfo=Symbol<decltype(&RizEngineSchedulerContext_GetInfo)>("RizEngineSchedulerContext_GetInfo");
    auto waitInfo=Symbol<decltype(&RizEngineWaitContext_GetInfo)>("RizEngineWaitContext_GetInfo");
    for(unsigned kind=0;kind<4;++kind){RizEngineNodeInfoV1 value{1,sizeof(value)};if(nodeInfo(kind,Slot,&value)||value.attached!=1||value.unknown||value.inFlight||value.entered!=value.exited)return false;}
    RizEngineInlineInfoV1 side{1,sizeof(side)};if(sideInfo(Slot,&side)||side.attached!=1||side.unknown||side.startsInFlight||side.probesInFlight||side.startsEntered!=side.startsExited)return false;
    for(unsigned kind=0;kind<6;++kind){RizEngineSchedulerInfoV1 value{1,sizeof(value)};if(schedulerInfo(kind,Slot,&value)||value.attached!=1||value.unknown||value.inFlight||value.rowsInFlight||value.entered!=value.exited)return false;}
    RizEngineWaitInfoV1 wait{1,sizeof(wait)};return waitInfo(Slot,&wait)==0&&wait.attached==1&&!wait.unknown&&!wait.inFlight&&wait.entered==wait.exited;
}
}

EXPORT std::uint32_t __cdecl RizEngineCancellationManaged_Configure(const wchar_t* path,Descriptor* output){
    using namespace CancellationOwned;
    const auto status=RizEngineWaitManaged_Configure(path,3,output);if(status)return status;
    auto nodeInfo=Symbol<decltype(&RizEngineNodeContext_GetInfo)>("RizEngineNodeContext_GetInfo");
    auto configureInline=Symbol<decltype(&RizEngineNodeContext_ConfigureInline)>("RizEngineNodeContext_ConfigureInline");
    RizEngineNodeInfoV1 start{1,sizeof(start)};CHECK(nodeInfo(0,Slot,&start)==0&&start.attached==0);
    inlineSites[0]={&RtCancellationInlineBeginSite,&RtCancellationInlineBeginReturn};
    inlineSites[1]={&RtCancellationInlineFreeSite,&RtCancellationInlineFreeReturn};
    inlineSites[2]={&RtCancellationInlineRetainedSite,&RtCancellationInlineRetainedReturn};
    RizEngineInlineConfigV1 side{1,sizeof(side),Slot,descriptor.mainThread,start.epoch,start.target,0,{}};
    for(unsigned i=0;i<3;++i)side.sites[i]=inlineSites[i];CHECK(configureInline(Slot,&side)==0);
    for(unsigned i=0;i<5;++i){
        auto* value=static_cast<SourceObject*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));if(!value)AllocationFailure();
        value->generation=++eventSequence;value->role=i;value->references=2;value->producerReferences=1;
        if(i==0)value->cachedOwner=&ownerValue;else if(i==2)value->cachedOwner=&secondNativeOwner;objects[i]=value;
    }
    actualDetach=detachEntry;RtCancellationInlineEntry=Symbol<decltype(&RizEngineNodeContext_GetInlineEntry)>("RizEngineNodeContext_GetInlineEntry")();return result.failures?7u:0u;
}
EXPORT void* __cdecl RizEngineCancellationManaged_GetStopOriginal(){return reinterpret_cast<void*>(&CancellationOwned::StopOriginal);}
EXPORT std::uint32_t __cdecl RizEngineCancellationManaged_SetStopEntry(void* entry){
    using namespace CancellationOwned;
    if(!configured||ran||!entry||stopEntry)return 1;MEMORY_BASIC_INFORMATION memory{};
    if(VirtualQuery(entry,&memory,sizeof(memory))!=sizeof(memory)||memory.Type!=MEM_IMAGE||memory.State!=MEM_COMMIT
        ||(memory.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE))==0||memory.AllocationBase==self)return 2;
    stopEntry=reinterpret_cast<Stop>(entry);return 0;
}
EXPORT std::uint32_t __cdecl RizEngineCancellationManaged_GetInfo(CancellationOwned::Extension* output){
    if(!output||output->version!=1||output->size!=sizeof(*output))return 1;CancellationOwned::Copy(output);
    output->stopOriginal=reinterpret_cast<void*>(&CancellationOwned::StopOriginal);return 0;
}
EXPORT std::uint32_t __cdecl RizEngineCancellationManaged_Run(Probe notify,void* user,Receipt* output){
    using namespace CancellationOwned;
    if(!configured||ran||!stopEntry||!notify||!output||output->version!=1||output->size!=sizeof(*output))return 1;
    ran=true;probe=notify;probeUser=user;void* nodeOutput{};
    SetLastError(InputError);CHECK(startEntry(&ownerValue,&enumeratorValue,&nameValue,&nodeOutput)==0&&GetLastError()==OriginalError);
    result.node=current;CHECK(nodeOutput==nullptr&&!Node(current));Notify(1,current,static_cast<WaitOwnedObject*>(result.wait0));
    BatchAll();
    for(const auto& row:nodes)if(row.value&&!row.freed)++result.liveNodes;
    for(const auto& row:waits)if(row.value&&!row.freed)++result.liveWaits;
    CHECK(result.liveNodes==0&&result.liveWaits==0&&result.nodeFreed==1&&result.waitFreed==1
        &&result.callbacks==1&&result.nodeDetaches==1&&stopCalls==1&&stopOriginalCalls==1);
    result.image=self;result.relay=relayBlock;Notify(5,current,nullptr);*output=result;return result.failures?2u:0u;
}
EXPORT std::uint32_t __cdecl RizEngineCancellationManaged_WithdrawSources(std::uint64_t generation){
    using namespace CancellationOwned;
    if(!ran||closed||!objects[0]||objects[0]->generation!=generation||!Idle()||result.liveNodes||result.liveWaits
        ||stopCalls!=1||stopOriginalCalls!=1)return 1;
    for(const auto* value:objects)if(!value||value->state||value->references!=2||value->producerReferences!=1||value->openScopes)return 2;
    for(auto* value:objects){++value->withdrawAttempts;value->state=1;value->withdrawn=++eventSequence;
        --value->producerReferences;--value->references;value->cachedOwner=nullptr;value->state=2;value->dead=++eventSequence;}
    return 0;
}
EXPORT std::uint32_t __cdecl RizEngineCancellationManaged_ReleaseSources(std::uint64_t generation){
    using namespace CancellationOwned;
    if(closed||!objects[0]||objects[0]->generation!=generation||!Idle())return 1;
    for(const auto* value:objects)if(!value||value->state!=2||value->references!=1||value->producerReferences||value->openScopes||value->rootReleaseAttempts)return 2;
    // Freeze scalar receipt before each actual independent free. No source
    // Target/read occurs after its own successful release.
    for(unsigned i=0;i<5;++i){auto* value=objects[i];++value->rootReleaseAttempts;--value->references;value->state=3;
        finalRows[i]=ReadSource(i);if(!VirtualFree(value,0,MEM_RELEASE))return 3;objects[i]=nullptr;}
    closed=true;return 0;
}

namespace CancellationOwned {
void DuringExecute(WaitOwnedNode* node,WaitOwnedObject* wait){CHECK(node==current&&Wait(wait));DispatchWait(wait);CHECK(Node(node)&&node->references==1);}
}
extern "C" void RtCancellationStopAndDrop(WaitOwnedNode* node){
    using namespace CancellationOwned;
    auto* source=objects[0];CHECK(node==current&&Node(node)&&source&&source->state==0&&source->references==2&&source->producerReferences==1);
    ++source->openScopes;++stopCalls;pendingNode=node;stopEntry(source,&enumeratorValue,&stopMethod);pendingNode=nullptr;--source->openScopes;
    auto* row=Node(node);CHECK(row&&row->references==1&&node->references==1);if(row)--row->references;--node->references;
}
