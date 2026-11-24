#include "../RizToolbox.EngineSchedulerFixture/SchedulerOwned.h"
#include "../RizToolbox.JudgeContext/EngineSchedulerContext.h"
#include "../RizToolbox.JudgeContext/EngineNodeContext.h"
#include "../RizToolbox.JudgeContext/MidContext.h"
#include <intrin.h>
#include <cstring>
#include <cstdio>
extern "C" {
extern unsigned char RtSchedulerFixture_DrainBeginSite, RtSchedulerFixture_DrainBeginContinuation;
extern unsigned char RtSchedulerFixture_DrainNoCallback, RtSchedulerFixture_DrainEndSite, RtSchedulerFixture_DrainEndContinuation;
extern unsigned char RtSchedulerFixture_DrainEnd;
std::uint64_t RtSchedulerManaged_CallbackTarget(void*,void*,void*,void*);
std::uint8_t RtSchedulerManaged_CleanupTarget(void*);
extern unsigned char RtSchedulerManaged_PumpCallbackReturn, RtSchedulerManaged_RepeatCallbackReturn, RtSchedulerManaged_DrainCallbackReturn;
extern unsigned char RtSchedulerManaged_RemoveCleanupReturn, RtSchedulerManaged_PumpCleanupReturn, RtSchedulerManaged_DrainCleanupReturn;
void* RtSchedulerManaged_CallbackEntry{};
void* RtSchedulerManaged_ReleaseEntry{};
std::uint32_t RtSchedulerManaged_CallbackRoute{}, RtSchedulerManaged_CleanupRoute{};
__declspec(align(16)) unsigned char RtSchedulerFixture_XmmInput[16][16]{};
std::uint32_t RtSchedulerFixture_InputMxcsr = 0x3f80;
std::uint16_t RtSchedulerFixture_InputFpcsr = 0x077f;
}
#define EXPORT extern "C" __declspec(dllexport)
namespace {
constexpr unsigned Slot = 27, MaximumRows = 8;
constexpr std::uint64_t Marker = 0xa1a2a3a4a5a6a7a8ULL, RemoveRaw = 0x823456781234abcdULL,
    ConsumeRaw = 0x934567892345bcdeULL, PumpRaw = 0xa456789a3456cdefULL, DrainRaw = 0xb56789ab4567def0ULL;
constexpr DWORD IncomingError = 0x12345678, OriginalError = 0x76543210;
using Carrier = std::uint64_t(__cdecl*)(void*,void*,void*,void*);
using Start = std::uint8_t(__cdecl*)(void*,void*,void*,void*);
using Execute = std::uint32_t(__cdecl*)(void*,void*);
using Release = std::uint8_t(__cdecl*)(void*);
using Gap = void(__cdecl*)(void*,void*,void*);
struct Descriptor {
    std::uint32_t version,size,scenario,slot,nodeSlot,configured,mainThread,checks,failures,reserved;
    void* image; void* kernel; void* rowEntry; void* relay; void* manager; void* tree;
    void* owner; void* enumerator; void* callback; void* cleanup;
    std::uint64_t handle;
    RizEngineSchedulerSiteV1 sites[2];
    const void* callers[6];
    void* entryReservation;
};
struct RowReceipt {
    std::uint64_t id;
    void* entry; void* payload;
    std::uint32_t registered,consumed,removed,callback,cleanup,storageRetired,repeat,reserved;
};
struct Receipt {
    std::uint32_t version,size,scenario,checks,failures,registrations,consumes,removals,callbacks,cleanups,drains,pumps;
    std::uint32_t gaps,freedEntries,addressReuse,remainingEntries,nodeStarts,nodeExecutes,nodeReleases,nodeFreed,rowChecks,rowCount,reserved0,reserved1;
    void* image; void* relay; void* node;
    RowReceipt rows[MaximumRows];
    std::uint32_t entryDecommits,entryRecommits,entryReservationReleased,entryGapState;
    void* entryReservation;
    std::uint32_t entryGapQuerySucceeded,reserved2;
};
static_assert(sizeof(Descriptor) == 216 && sizeof(RowReceipt) == 56 && sizeof(Receipt) == 600);
struct Row { SchedulerOwnedEntry* entry{}; SchedulerOwnedSource record{}; RowReceipt receipt{}; } rows[MaximumRows];
SchedulerOwnedManager manager{};
Descriptor descriptor{}; Receipt result{};
HMODULE self{},kernel{};
HANDLE kernelLock = INVALID_HANDLE_VALUE;
Carrier entries[6]{};
Start start{}; Execute execute{}; Release release{};
void* node{}; void* nodeOutput{}; void* preferredEntry{};
void* entryReservation{};
std::uint64_t ownerToken = 0xdecafbad12345678ULL, enumToken = 0xcafef00d98765432ULL;
unsigned scenario{},rowCount{}; bool configured{},ran{};
unsigned char* relay{}; RUNTIME_FUNCTION* functions{};
Gap gap{}; void* gapUser{};
void Check(bool okay) { ++result.checks; if (!okay) ++result.failures; }
[[noreturn]] void AllocationFailed(const char* operation,void* requested,DWORD error) {
    std::fprintf(stderr,"Scheduler managed fixture allocation failed: operation=%s requested=%p lastError=%lu\n",operation,requested,error);
    std::fflush(stderr);ExitProcess(3);
}
template<class T> T Symbol(const char* name) {
    const auto address=GetProcAddress(kernel,name);Check(address!=nullptr);T value{};
    static_assert(sizeof(value)==sizeof(address));std::memcpy(&value,&address,sizeof(value));return value;
}
template<class T> void Write(unsigned offset,T value) { std::memcpy(static_cast<unsigned char*>(node)+offset,&value,sizeof(value)); }
template<class T> T Read(unsigned offset) { T value{};std::memcpy(&value,static_cast<unsigned char*>(node)+offset,sizeof(value));return value; }
Row* Find(SchedulerOwnedEntry* entry) {
    for(unsigned i=rowCount;i!=0;--i)if(rows[i-1].entry==entry&&!rows[i-1].receipt.storageRetired)return &rows[i-1];return nullptr;
}
void Erase(Row* row) {
    Check(row&&manager.count&&row->entry);if(!row)return;
    unsigned at=MaximumRows;for(unsigned i=0;i<manager.count;++i)if(manager.rows[i]==row->entry){at=i;break;}
    Check(at!=MaximumRows);if(at==MaximumRows)return;
    for(unsigned i=at;i+1<manager.count;++i)manager.rows[i]=manager.rows[i+1];
    --manager.count;manager.rows[manager.count]=nullptr;
    bool retired=false;
    if(scenario==6){
        Check(row->entry==entryReservation);retired=VirtualFree(row->entry,4096,MEM_DECOMMIT)!=FALSE;
        Check(retired);if(retired)++result.entryDecommits;
    }else{retired=VirtualFree(row->entry,0,MEM_RELEASE)!=FALSE;Check(retired);if(retired)++result.freedEntries;}
    row->receipt.storageRetired=retired?1u:0u;
}
SchedulerOwnedSource Source() {
    SchedulerOwnedSource source{};source.dueTimeBits=0x400921fb54442d18ULL;source.frame=-1234567890123LL;
    source.intervalBits=0x3f123456;source.repeat=scenario==2?1:0;source.payload=node;
    source.callback=reinterpret_cast<void*>(&RtSchedulerManaged_CallbackTarget);
    source.cleanup=reinterpret_cast<void*>(&RtSchedulerManaged_CleanupTarget);
    source.ownerId=0x12345678;source.category=0x40;source.creationTag=0x87654321;return source;
}
Row* RegisterOne(SchedulerOwnedSource* source) {
    SchedulerOwnedEntry* output{};SetLastError(IncomingError);
    const auto raw=entries[0](manager.tree,&output,source,reinterpret_cast<void*>(Marker));
    Check(raw==reinterpret_cast<std::uint64_t>(&output)&&GetLastError()==OriginalError);return Find(output);
}
Row* cleanupRow{};Row* drainRow{};
void Cleanup(Row* row,unsigned route) {
    cleanupRow=row;RtSchedulerManaged_CleanupRoute=route;
    reinterpret_cast<void(__cdecl*)(void*)>(row->record.cleanup)(row->record.payload);
    ++row->receipt.cleanup;++result.cleanups;cleanupRow=nullptr;
}
__declspec(noinline) std::uint8_t __cdecl NodeStart(void* owner,void* enumerator,void*,void* output) {
    Check(owner==&ownerToken&&enumerator==&enumToken&&output==&nodeOutput);++result.nodeStarts;
    node=VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);
    const DWORD allocationError=node?ERROR_SUCCESS:GetLastError();Check(node!=nullptr);
    if(!node)AllocationFailed("node-reserve-commit",nullptr,allocationError);
    Write(0x58,owner);Write(0x10,std::uint64_t(0x1234567890abcdefULL));Write(0x18,std::uint32_t(2));Write(0x20,enumerator);
    Write(0x60,std::int32_t(scenario==3||scenario==4?2:scenario==5?3:1));
    *static_cast<void**>(output)=node;unsigned char error{};Check(execute(node,&error)==0x12340001&&error==0xa5);return 1;
}
__declspec(noinline) std::uint8_t __cdecl NodeStartTarget(void*,void*,void*,void*) { return 0xfe; }
__declspec(noinline) std::uint32_t __cdecl NodeExecute(void* actual,void* error) {
    Check(actual==node);++result.nodeExecutes;*static_cast<unsigned char*>(error)=0xa5;return 0x12340001;
}
__declspec(noinline) std::uint32_t __cdecl NodeExecuteTarget(void*,void*) { return 0xfedc0001; }
__declspec(noinline) std::uint8_t __cdecl NodeRelease(void* actual) {
    Check(actual==node);++result.nodeReleases;auto references=Read<std::int32_t>(0x60);Check(references>0);--references;
    Write(0x60,references);if(references==0){const bool freed=VirtualFree(node,0,MEM_RELEASE)!=FALSE;Check(freed);if(freed)++result.nodeFreed;return 0;}return 1;
}
__declspec(noinline) void __cdecl NodeDetach(void*) { _ReadWriteBarrier(); }
__declspec(noinline) void __cdecl NodeDetachTarget(void*) { _ReadWriteBarrier(); }
}
extern "C" std::uint64_t RtSchedulerFixture_Register(void* tree,void* output,void* sourcePointer,void* fourth) {
    Check(tree==manager.tree&&fourth==reinterpret_cast<void*>(Marker)&&GetLastError()==IncomingError&&rowCount<MaximumRows);
    if(rowCount==MaximumRows){std::fprintf(stderr,"Scheduler managed fixture row capacity exhausted.\n");std::fflush(stderr);ExitProcess(3);}
    const auto requested=scenario==6?entryReservation:preferredEntry;
    if(scenario==6){
        MEMORY_BASIC_INFORMATION memory{};
        Check(VirtualQuery(entryReservation,&memory,sizeof(memory))==sizeof(memory)
            &&memory.State==MEM_RESERVE&&memory.AllocationBase==entryReservation);
    }
    auto* entry=static_cast<SchedulerOwnedEntry*>(VirtualAlloc(requested,4096,scenario==6?MEM_COMMIT:MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    const DWORD allocationError=entry?ERROR_SUCCESS:GetLastError();
    Check(entry&&(!preferredEntry||entry==preferredEntry));
    if(!entry)AllocationFailed(scenario==6?"entry-reservation-commit":"entry-reserve-commit",requested,allocationError);
    if(scenario==6){Check(entry==entryReservation);if(rowCount!=0)++result.entryRecommits;}
    if(preferredEntry){++result.addressReuse;preferredEntry=nullptr;}
    auto& row=rows[rowCount++];row.entry=entry;row.record=*static_cast<SchedulerOwnedSource*>(sourcePointer);
    row.receipt.id=rowCount;row.receipt.entry=entry;row.receipt.payload=row.record.payload;row.receipt.registered=1;row.receipt.repeat=row.record.repeat;
    entry->record=row.record;manager.rows[manager.count++]=entry;*static_cast<void**>(output)=entry;
    ++result.registrations;SetLastError(OriginalError);return reinterpret_cast<std::uint64_t>(output);
}
extern "C" std::uint64_t RtSchedulerFixture_Consume(void* owner,void*,void* entry,void*) {
    auto* row=Find(static_cast<SchedulerOwnedEntry*>(entry));Check(owner==&manager&&row&&GetLastError()==IncomingError);
    if(row){row->receipt.consumed=1;Erase(row);}++result.consumes;SetLastError(OriginalError);return ConsumeRaw;
}
extern "C" std::uint64_t RtSchedulerFixture_Remove(void* owner,void* record,void* entry,void*) {
    auto* row=Find(static_cast<SchedulerOwnedEntry*>(entry));Check(owner==&manager&&row&&record==static_cast<unsigned char*>(entry)+0x20);
    if(row){row->receipt.removed=1;Erase(row);Cleanup(row,1);}++result.removals;SetLastError(OriginalError);return RemoveRaw;
}
extern "C" std::uint64_t RtSchedulerFixture_Callback(void* owner,void* payload,void*,void*) {
    Check(owner==&ownerToken&&payload==node);++result.callbacks;SetLastError(OriginalError);
    return scenario==1||scenario==6?0xabcdef0112340001ULL:0xabcdef0100000001ULL;
}
extern "C" std::uint64_t RtSchedulerFixture_Pump(void* owner,void* mask,void*,void*) {
    Check(owner==&manager&&mask==reinterpret_cast<void*>(0x40)&&manager.count);++result.pumps;
    auto* row=Find(manager.rows[0]);Check(row);if(!row)return PumpRaw;
    if(row->record.repeat){auto* copy=RegisterOne(&row->entry->record);Check(copy&&copy!=row&&row->receipt.consumed==0);}
    SetLastError(IncomingError);Check(entries[2](&manager,reinterpret_cast<void*>(Marker),row->entry,reinterpret_cast<void*>(Marker))==ConsumeRaw);
    MEMORY_BASIC_INFORMATION gapMemory{};
    const bool gapQueried=VirtualQuery(row->entry,&gapMemory,sizeof(gapMemory))==sizeof(gapMemory);
    result.entryGapQuerySucceeded=gapQueried?1u:0u;
    result.entryGapState=gapQueried?gapMemory.State:0;
    // Consume.Exit may already have run CLR allocation/JIT callbacks. A bare
    // released address can be reused immediately; its current state is only a
    // diagnostic. Old-generation release proof is the exact successful
    // VirtualFree call retained in the row and independent release counters.
    if(scenario==6)Check(gapQueried&&gapMemory.State==MEM_RESERVE&&gapMemory.AllocationBase==entryReservation);
    ++result.gaps;if(gap)gap(&manager,row->entry,gapUser);
    RtSchedulerManaged_CallbackRoute=row->record.repeat?1:0;
    const auto raw=reinterpret_cast<Carrier>(row->record.callback)(&ownerToken,row->record.payload,nullptr,nullptr);
    ++row->receipt.callback;
    if(!row->record.repeat&&static_cast<std::uint32_t>(raw)==1)Cleanup(row,0);
    SetLastError(OriginalError);return PumpRaw;
}
extern "C" void* RtSchedulerFixture_DrainNext(void* owner) {
    Check(owner==&manager);if(!manager.count){drainRow=nullptr;return nullptr;}
    drainRow=Find(manager.rows[0]);Check(drainRow);return manager.rows[0];
}
extern "C" void RtSchedulerFixture_DrainBody(void* owner,void* entry,std::uint32_t category) {
    Check(owner==&manager&&drainRow&&drainRow->entry==entry&&category==((drainRow->record.category&0x40)?1u:0u));
    if(!drainRow)return;
    if(category){RtSchedulerManaged_CallbackRoute=2;Check(reinterpret_cast<Carrier>(drainRow->record.callback)(&ownerToken,node,nullptr,nullptr)==0xabcdef0100000001ULL);++drainRow->receipt.callback;}
    Erase(drainRow);Cleanup(drainRow,2);
}
extern "C" std::uint64_t RtSchedulerFixture_DrainFinished(void* owner) {
    Check(owner==&manager&&manager.count==0);++result.drains;SetLastError(OriginalError);return DrainRaw;
}
extern "C" void RtSchedulerFixture_DrainPrepare(void*,std::uint64_t rsp) {
    for(unsigned i=0;i!=4;++i){auto* frame=reinterpret_cast<RizEventMidFrameV1*>(rsp+0x40+i*0x1b0);std::memset(frame,0,sizeof(*frame));frame->abiVersion=1;frame->byteSize=sizeof(*frame);}
}
extern "C" void RtSchedulerFixture_DrainCheck(void*,std::uint64_t rsp) {
    Check(drainRow&&drainRow->receipt.storageRetired);const auto* before=reinterpret_cast<const RizEventMidFrameV1*>(rsp+0x3a0);
    const auto* after=reinterpret_cast<const RizEventMidFrameV1*>(rsp+0x550);Check(before->gpr[3]==after->gpr[3]&&before->gpr[5]==after->gpr[5]);
    ++result.rowChecks;
}
namespace {
__declspec(noinline) std::uint64_t __cdecl Original0(void*a,void*b,void*c,void*d){const auto r=RtSchedulerFixture_Register(a,b,c,d);_ReadWriteBarrier();return r;}
__declspec(noinline) std::uint64_t __cdecl Original1(void*a,void*b,void*c,void*d){const auto r=RtSchedulerFixture_Remove(a,b,c,d);_ReadWriteBarrier();return r;}
__declspec(noinline) std::uint64_t __cdecl Original2(void*a,void*b,void*c,void*d){const auto r=RtSchedulerFixture_Consume(a,b,c,d);_ReadWriteBarrier();return r;}
__declspec(noinline) std::uint64_t __cdecl Original3(void*a,void*b,void*c,void*d){const auto r=RtSchedulerFixture_Pump(a,b,c,d);_ReadWriteBarrier();return r;}
__declspec(noinline) std::uint64_t __cdecl Original4(void*a,void*b,void*c,void*d){const auto r=RtSchedulerFixture_Drain(a,b,c,d);_ReadWriteBarrier();return r;}
__declspec(noinline) std::uint64_t __cdecl Original5(void*a,void*b,void*c,void*d){const auto r=RtSchedulerFixture_Callback(a,b,c,d);_ReadWriteBarrier();return r;}
bool Jump(unsigned char* output,std::uint64_t pc,std::uint64_t target) {
    const auto delta=static_cast<std::int64_t>(target)-static_cast<std::int64_t>(pc+5);if(delta<INT32_MIN||delta>INT32_MAX)return false;
    const auto rel=static_cast<std::int32_t>(delta);output[0]=0xe9;std::memcpy(output+1,&rel,4);return true;
}
bool Relays() {
    const auto base=reinterpret_cast<DWORD64>(self);DWORD64 found{};
    const auto source=RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(&RtSchedulerFixture_Drain),&found,nullptr);
    Check(source&&found==base&&base+source->BeginAddress==reinterpret_cast<DWORD64>(&RtSchedulerFixture_Drain)
        &&base+source->EndAddress==reinterpret_cast<DWORD64>(&RtSchedulerFixture_DrainEnd));if(!source)return false;
    const auto* dos=reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
    const auto* nt=reinterpret_cast<const IMAGE_NT_HEADERS64*>(base+dos->e_lfanew);
    for(auto next=(base+nt->OptionalHeader.SizeOfImage+0xffff)&~std::uint64_t(0xffff);next<base+0x70000000;next+=0x10000){
        MEMORY_BASIC_INFORMATION memory{};if(!VirtualQuery(reinterpret_cast<void*>(next),&memory,sizeof(memory)))break;
        if(memory.State==MEM_FREE&&memory.RegionSize>=4096){relay=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(next),4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));if(relay)break;}
    }
    Check(relay!=nullptr);if(!relay)return false;functions=reinterpret_cast<RUNTIME_FUNCTION*>(relay+0x300);
    unsigned char* sites[]{&RtSchedulerFixture_DrainBeginSite,&RtSchedulerFixture_DrainEndSite};
    const void* continuations[]{&RtSchedulerFixture_DrainBeginContinuation,&RtSchedulerFixture_DrainEndContinuation};
    for(unsigned i=0;i!=2;++i){auto* code=relay+i*64;const unsigned char call[]{0xff,0x15,0x2a,0,0,0};std::memcpy(code,call,6);std::memcpy(code+48,&descriptor.rowEntry,8);
        const unsigned char test[]{0xf6,0x43,0x54,0x40},cmp[]{0x48,0x83,0x7d,0x38,0};unsigned cursor=6;
        if(!i){Check(std::memcmp(sites[i],test,4)==0&&sites[i][4]==0x74);std::memcpy(code+cursor,test,4);cursor+=4;code[cursor]=0x0f;code[cursor+1]=0x84;
            const auto delta=reinterpret_cast<std::intptr_t>(&RtSchedulerFixture_DrainNoCallback)-reinterpret_cast<std::intptr_t>(code+cursor+6);
            Check(delta>=INT32_MIN&&delta<=INT32_MAX);const auto rel=static_cast<std::int32_t>(delta);std::memcpy(code+cursor+2,&rel,4);cursor+=6;
        }else{Check(std::memcmp(sites[i],cmp,5)==0);std::memcpy(code+cursor,cmp,5);cursor+=5;}
        Check(Jump(code+cursor,reinterpret_cast<DWORD64>(code+cursor),reinterpret_cast<DWORD64>(continuations[i])));cursor+=5;
        auto* unwind=relay+0x200+i*32;unwind[0]=1|(UNW_FLAG_CHAININFO<<3);std::memcpy(unwind+4,source,sizeof(*source));
        functions[i]={static_cast<DWORD>(reinterpret_cast<DWORD64>(code)-base),static_cast<DWORD>(reinterpret_cast<DWORD64>(code+cursor)-base),static_cast<DWORD>(reinterpret_cast<DWORD64>(unwind)-base)};
        descriptor.sites[i]={sites[i],code+6};
    }
    DWORD prior{};Check(VirtualProtect(relay,4096,PAGE_EXECUTE_READ,&prior)!=FALSE);Check(FlushInstructionCache(GetCurrentProcess(),relay,4096)!=FALSE);Check(RtlAddFunctionTable(functions,2,base)!=FALSE);
    for(unsigned i=0;i!=2;++i){unsigned char patch[8]{0x90,0x90,0x90,0x90,0x90,0x90,0x90,0x90};const unsigned span=i?5:6;
        Check(Jump(patch,reinterpret_cast<DWORD64>(sites[i]),reinterpret_cast<DWORD64>(relay+i*64)));Check(VirtualProtect(sites[i],span,PAGE_EXECUTE_READWRITE,&prior)!=FALSE);
        std::memcpy(sites[i],patch,span);Check(FlushInstructionCache(GetCurrentProcess(),sites[i],span)!=FALSE);DWORD unused{};Check(VirtualProtect(sites[i],span,prior,&unused)!=FALSE);
        DWORD64 image{};Check(RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(descriptor.sites[i].relayReturnPc),&image,nullptr)==&functions[i]&&image==base);
    }
    return result.failures==0;
}
}
EXPORT std::uint32_t __cdecl RizEngineSchedulerManaged_Configure(const wchar_t* path,std::uint32_t mode,Descriptor* output) {
    if(!path||!output||output->version!=2||output->size!=sizeof(Descriptor)||configured||mode>6)return 1;
    scenario=mode;result.version=2;result.size=sizeof(result);result.scenario=mode;manager.tree=&manager.tree;
    Check(GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&RizEngineSchedulerManaged_Configure),&self)!=FALSE);
    kernelLock=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);Check(kernelLock!=INVALID_HANDLE_VALUE);
    kernel=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);Check(kernel!=nullptr);if(!kernel)return 2;
    auto configure=Symbol<decltype(&RizEngineSchedulerContext_Configure)>("RizEngineSchedulerContext_Configure");
    auto getEntry=Symbol<decltype(&RizEngineSchedulerContext_GetEntry)>("RizEngineSchedulerContext_GetEntry");
    auto getRow=Symbol<decltype(&RizEngineSchedulerContext_GetDrainRowEntry)>("RizEngineSchedulerContext_GetDrainRowEntry");
    auto configureNode=Symbol<decltype(&RizEngineNodeContext_Configure)>("RizEngineNodeContext_Configure");
    auto getNode=Symbol<decltype(&RizEngineNodeContext_GetEntry)>("RizEngineNodeContext_GetEntry");if(result.failures)return 3;
    if(scenario==6){
        entryReservation=VirtualAlloc(nullptr,0x10000,MEM_RESERVE,PAGE_NOACCESS);
        const DWORD allocationError=entryReservation?ERROR_SUCCESS:GetLastError();Check(entryReservation!=nullptr);
        if(!entryReservation)AllocationFailed("entry-address-reservation",nullptr,allocationError);
    }
    descriptor={2,sizeof(descriptor),mode,Slot,Slot,0,GetCurrentThreadId(),0,0,0,self,kernel,getRow(),nullptr,&manager,manager.tree,
        &ownerToken,&enumToken,reinterpret_cast<void*>(&RtSchedulerManaged_CallbackTarget),reinterpret_cast<void*>(&RtSchedulerManaged_CleanupTarget),0x1234567890abcdefULL,{},{},entryReservation};
    descriptor.callers[0]=&RtSchedulerManaged_PumpCallbackReturn;descriptor.callers[1]=&RtSchedulerManaged_RepeatCallbackReturn;descriptor.callers[2]=&RtSchedulerManaged_DrainCallbackReturn;
    descriptor.callers[3]=&RtSchedulerManaged_RemoveCleanupReturn;descriptor.callers[4]=&RtSchedulerManaged_PumpCleanupReturn;descriptor.callers[5]=&RtSchedulerManaged_DrainCleanupReturn;
    if(!Relays())return 4;descriptor.relay=relay;
    Carrier targets[]{&RtSchedulerFixture_Register,&RtSchedulerFixture_Remove,&RtSchedulerFixture_Consume,&RtSchedulerFixture_Pump,&RtSchedulerFixture_Drain,&RtSchedulerManaged_CallbackTarget};
    Carrier originals[]{&Original0,&Original1,&Original2,&Original3,&Original4,&Original5};
    for(unsigned kind=0;kind!=6;++kind){RizEngineSchedulerConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(targets[kind]),reinterpret_cast<void*>(originals[kind]),descriptor.mainThread,0,{}};
        if(kind==4){config.sites[0]=descriptor.sites[0];config.sites[1]=descriptor.sites[1];}Check(configure(kind,Slot,&config)==0);entries[kind]=reinterpret_cast<Carrier>(getEntry(kind,Slot));Check(entries[kind]!=nullptr);}
    RtSchedulerManaged_CallbackEntry=reinterpret_cast<void*>(entries[5]);
    void* nodeTargets[]{reinterpret_cast<void*>(&NodeStartTarget),reinterpret_cast<void*>(&NodeExecuteTarget),reinterpret_cast<void*>(&RtSchedulerManaged_CleanupTarget),reinterpret_cast<void*>(&NodeDetachTarget)};
    void* nodeOriginals[]{reinterpret_cast<void*>(&NodeStart),reinterpret_cast<void*>(&NodeExecute),reinterpret_cast<void*>(&NodeRelease),reinterpret_cast<void*>(&NodeDetach)};
    for(unsigned kind=0;kind!=4;++kind){RizEngineNodeConfigV1 config{1,sizeof(config),nodeTargets[kind],nodeOriginals[kind]};Check(configureNode(kind,Slot,&config)==0);}
    start=reinterpret_cast<Start>(getNode(0,Slot));execute=reinterpret_cast<Execute>(getNode(1,Slot));release=reinterpret_cast<Release>(getNode(2,Slot));
    RtSchedulerManaged_ReleaseEntry=reinterpret_cast<void*>(release);
    for(unsigned i=0;i!=16;++i)for(unsigned j=0;j!=16;++j)RtSchedulerFixture_XmmInput[i][j]=static_cast<unsigned char>(0x10+i*7+j);
    configured=result.failures==0;descriptor.configured=configured?1:0;descriptor.checks=result.checks;descriptor.failures=result.failures;*output=descriptor;return configured?0u:5u;
}
EXPORT std::uint32_t __cdecl RizEngineSchedulerManaged_Run(Gap probe,void* user,Receipt* output) {
    if(!configured||ran||!output||output->version!=2||output->size!=sizeof(Receipt)||!probe)return 1;
    ran=true;gap=probe;gapUser=user;Check(start(&ownerToken,&enumToken,nullptr,&nodeOutput)==1&&nodeOutput==node);
    auto source=Source();auto* first=RegisterOne(&source);Check(first!=nullptr);
    if(scenario==3||scenario==4||scenario==5){RegisterOne(&source);if(scenario==5){source.category=0;RegisterOne(&source);}}
    if(scenario==4){while(manager.count){auto* entry=manager.rows[0];SetLastError(IncomingError);Check(entries[1](&manager,&entry->record,entry,nullptr)==RemoveRaw);}}
    else if(scenario==5){SetLastError(IncomingError);Check(entries[4](&manager,nullptr,nullptr,nullptr)==DrainRaw);}
    else {
        SetLastError(IncomingError);Check(entries[3](&manager,reinterpret_cast<void*>(0x40),nullptr,nullptr)==PumpRaw);
        if(scenario==2){auto* entry=manager.rows[0];SetLastError(IncomingError);Check(entries[1](&manager,&entry->record,entry,nullptr)==RemoveRaw);}
        else if(scenario==3){SetLastError(IncomingError);Check(entries[3](&manager,reinterpret_cast<void*>(0x40),nullptr,nullptr)==PumpRaw);}
        else if(scenario==6){preferredEntry=first->entry;RegisterOne(&source);SetLastError(IncomingError);Check(entries[3](&manager,reinterpret_cast<void*>(0x40),nullptr,nullptr)==PumpRaw);}
    }
    // High-EAX and reuse retire queue rows without claiming cleanup happened.
    // An explicit later real Release outside any scheduler scope frees the
    // still-owned node and remains independently visible in the node ledger.
    if(scenario==1||scenario==6)Check(release(node)==0);
    if(scenario==6){
        // Preserve the owned address throughout every CLR callback. Distinct
        // row lifetimes use real inaccessible decommit/recommit cycles. Only
        // after both actual roots have returned is the reservation released.
        result.entryReservation=entryReservation;
        Check(result.entryDecommits==2&&result.entryRecommits==1&&result.freedEntries==0);
        const bool released=VirtualFree(entryReservation,0,MEM_RELEASE)!=FALSE;Check(released);
        result.entryReservationReleased=released?1u:0u;
    }
    result.remainingEntries=static_cast<std::uint32_t>(manager.count);result.rowCount=rowCount;result.image=self;result.relay=relay;result.node=node;
    for(unsigned i=0;i!=rowCount;++i)result.rows[i]=rows[i].receipt;
    Check(result.remainingEntries==0&&result.nodeFreed==1&&result.nodeStarts==1&&result.nodeExecutes==1);
    *output=result;return result.failures?2u:0u;
}
