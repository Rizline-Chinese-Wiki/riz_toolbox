#include "WaitOwned.h"
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <intrin.h>
#include <xmmintrin.h>
#include <limits>

extern "C" {
void* RtWaitFixture_CallbackEntry{};
void* RtWaitFixture_ReleaseEntry{};
std::uint64_t RtWaitFixture_RipValue = 0x123456789abcdef0ULL;
__declspec(align(16)) RizEventMidFrameV1 RtWaitFixture_Before[18]{};
__declspec(align(16)) RizEventMidFrameV1 RtWaitFixture_After[18]{};
#define DECLARE_PHASE(n) extern unsigned char RtWaitFixture_Phase##n, RtWaitFixture_After##n;
DECLARE_PHASE(0) DECLARE_PHASE(1) DECLARE_PHASE(2) DECLARE_PHASE(3)
DECLARE_PHASE(4) DECLARE_PHASE(5) DECLARE_PHASE(6) DECLARE_PHASE(7)
DECLARE_PHASE(8) DECLARE_PHASE(9) DECLARE_PHASE(10) DECLARE_PHASE(11)
DECLARE_PHASE(12) DECLARE_PHASE(13) DECLARE_PHASE(14) DECLARE_PHASE(15)
DECLARE_PHASE(16) DECLARE_PHASE(17)
extern unsigned char RtWaitFixture_YieldEnd, RtWaitFixture_DispatchEnd, RtWaitFixture_BatchEnd;
extern unsigned char RtWaitFixture_StartEnd, RtWaitFixture_ReleaseEnd, RtWaitFixture_DetachEnd;
extern unsigned char RtWaitFixture_YieldDone, RtWaitFixture_DispatchCleanupLoad, RtWaitFixture_BatchSkipCleanup;
extern unsigned char RtWaitFixture_DispatchCallbackReturn, RtWaitFixture_DispatchCleanupReturn;
extern unsigned char RtWaitFixture_BatchCleanupReturn, RtWaitFixture_YieldOldReturn;
extern unsigned char RtWaitFixture_StartSharedReturn, RtWaitFixture_ReleaseSharedReturn;
std::uint64_t RtWaitFixture_TestFlags(std::uint64_t,std::uint64_t);
std::uint64_t RtWaitFixture_CmpFlags(std::uint64_t,std::uint64_t,std::uint64_t);
std::uint64_t RtWaitFixture_IncFlags(std::uint32_t*,std::uint64_t);
void RtWaitFixture_SharedReturn(void*);
}
namespace {
constexpr unsigned Slot = 24, Capacity = 128, ObjectLimit = 24;
constexpr DWORD InputError = 0x12345678, OriginalError = 0x76543210;
constexpr DWORD CallbackException = 0xe0525743, CleanupException = 0xe052574c, ObserverException = 0xe052574f;
constexpr std::uint64_t YieldResult = 0x1122334455667701ULL, DispatchResult = 0x2233445566778802ULL;
constexpr std::uint64_t BatchResult = 0x3344556677889903ULL, CallbackResult = 0xabcdef0100000001ULL;
using Carrier = std::uint64_t(__cdecl*)(void*,void*,void*,void*);
using Start = std::uint8_t(__cdecl*)(void*,void*,void*,void*);
using Execute = std::uint32_t(__cdecl*)(void*,void*);
using Release = std::uint8_t(__cdecl*)(void*);
using Detach = void(__cdecl*)(void*);
struct CppException { std::uint64_t marker; };
struct ObjectReceipt {
    WaitOwnedObject* address{};
    RizEngineWaitTupleV1 initial{};
    std::uint64_t generation{};
    unsigned sharedReferences{}, referenceReturns{}, released{}, decommitted{};
};
struct NodeReceipt {
    WaitOwnedNode* address{}; void* owner{}; void* cache{};
    std::uint64_t generation{}, handle{};
    int references{}; unsigned released{};
};
struct CallReceipt {
    RizEngineWaitFrameV1 frame{};
    RizEngineWaitCompletionV1 completion{};
    unsigned exited{}, reserved{};
};
struct SiteReceipt {
    std::uint64_t site{}, continuation{}, branch{}, relay{}, returnPc{}, imageBase{};
    unsigned span{}, length{};
    unsigned char original[16]{}, replacement[16]{}, code[96]{}, unwind[16]{};
    RUNTIME_FUNCTION source{}, function{};
};
struct Receipt {
    unsigned version = 1, byteSize = sizeof(Receipt), checks{}, failures{};
    unsigned nodeStarts{}, nodeExecutes{}, nodeReleases{}, nodeDetaches{}, nodeFrees{}, waitFrees{};
    unsigned yieldCalls{}, dispatchCalls{}, batchCalls{}, callbacks{}, sharedReturns{}, cleanupCalls{};
    unsigned phaseCount{}, callCount{}, nodeCallCount{}, schedulerCallCount{}, typedExitCount{};
    unsigned originalSeh{}, originalCpp{}, observerFaults{}, maskedCalls{}, reentries{}, workerCalls{};
    unsigned lateAttached{}, unobserved{}, statePairs{}, unwindWalks{}, virtualUnwinds{}, lastErrorChecks{};
    unsigned decommits{}, recommits{}, reservationReleased{}, oldReferenceReturns{}, isDoneCalls{}, occupiedFallbacks{}, isDoneFallbacks{};
    std::uint64_t imageBase{}, relayBase{}, phaseEntry{}, reservation{}, beforeControl{}, afterControl{};
    SiteReceipt sites[18]{};
    RizEngineWaitInfoV1 information{};
    CallReceipt calls[Capacity]{};
    RizEngineWaitPhaseFrameV1 phases[Capacity]{};
    RizEngineNodeFrameV1 nodeFrames[Capacity]{};
    RizEngineNodeCompletionV1 nodeCompletions[Capacity]{};
    RizEngineSchedulerFrameV1 schedulerFrames[Capacity]{};
    RizEngineSchedulerCompletionV1 schedulerCompletions[Capacity]{};
    ObjectReceipt objects[ObjectLimit]{}; NodeReceipt nodes[ObjectLimit]{};
    RizEventMidFrameV1 before[18]{}, after[18]{};
} receipt;
const wchar_t* mode{};
DWORD mainThread{};
HMODULE kernel{};
HANDLE kernelLock = INVALID_HANDLE_VALUE;
unsigned objectCount{}, nodeCount{};
WaitOwnedManager manager{};
WaitOwnedObject* batchObjects[4]{};
WaitOwnedNode* primary{}, *alternate{}, *nested{};
WaitOwnedObject* activeDispatch{};
void* reservation{};
bool reservationCommitted{}, faultDisabled{}, reentered{}, foreignReentry{}, attached{}, duringCallback{}, batchActive{};
unsigned char ownerValue{}, cacheValue{}, nameValue{}, schedulerTree{}, schedulerManager{};
Carrier waitEntries[3]{}, schedulerEntries[6]{};
Execute foreignExecute{};
Start startEntry{}; Execute executeEntry{}; Release releaseEntry{}; Detach detachEntry{};
void* phaseEntry{};
RizEngineWaitObserverV1 waitObserver{};
decltype(&RizEngineWaitContext_Attach) attachWait{};
decltype(&RizEngineWaitContext_GetInfo) waitInfo{};
decltype(&RizEngineNodeContext_GetInfo) nodeInfo{};
decltype(&RizEngineSchedulerContext_GetInfo) schedulerInfo{};
unsigned char* relayBlock{};
PRUNTIME_FUNCTION relayFunctions{};
void Check(bool value,const char* expression,int line) {
    ++receipt.checks;
    if (!value) { ++receipt.failures; std::fprintf(stderr,"FAILED WaitFixture line %d: %s\n",line,expression); std::fflush(stderr); }
}
#define CHECK(x) Check(!!(x),#x,__LINE__)
bool Named(const wchar_t* value) { return std::wcscmp(mode,value)==0; }
template<class T> T Symbol(const char* name) {
    auto value = GetProcAddress(kernel,name); CHECK(value); return reinterpret_cast<T>(value);
}
[[noreturn]] void AllocationFailure(const char* action,DWORD error) {
    std::fprintf(stderr,"Owned allocation failed: %s, actual GetLastError=%lu\n",action,error);
    std::fflush(stderr); ExitProcess(3);
}
NodeReceipt* Node(WaitOwnedNode* address) {
    for (unsigned i=nodeCount;i;--i) if (receipt.nodes[i-1].address==address&&!receipt.nodes[i-1].released) return &receipt.nodes[i-1];
    return nullptr;
}
ObjectReceipt* Object(WaitOwnedObject* address) {
    for (unsigned i=objectCount;i;--i) if (receipt.objects[i-1].address==address&&!receipt.objects[i-1].released&&!receipt.objects[i-1].decommitted) return &receipt.objects[i-1];
    return nullptr;
}
WaitOwnedNode* NewNode(void* owner,void* cache) {
    CHECK(nodeCount<ObjectLimit); if(nodeCount==ObjectLimit) ExitProcess(3);
    auto* value=static_cast<WaitOwnedNode*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    if(!value) AllocationFailure("node MEM_RESERVE|MEM_COMMIT",GetLastError());
    auto& row=receipt.nodes[nodeCount++]; row.address=value; row.owner=owner; row.cache=cache;
    row.generation=nodeCount; row.handle=0x1234567800000000ULL+nodeCount; row.references=1;
    value->enumHandle=row.handle; value->enumType=2; value->enumCache=cache; value->owner=owner; value->references=1;
    return value;
}
std::uint8_t __cdecl IsDone(WaitOwnedObject* wait) {
    CHECK(Object(wait)); ++receipt.isDoneCalls; return static_cast<std::uint8_t>(Named(L"is-done")?1u:0u);
}
void* waitVtable[2]{nullptr,reinterpret_cast<void*>(&IsDone)};
WaitOwnedObject* NewWait(WaitOwnedNode* node,bool callback=true) {
    CHECK(objectCount<ObjectLimit); if(objectCount==ObjectLimit) ExitProcess(3);
    WaitOwnedObject* value{};
    if(Named(L"address-reuse")) {
        if(!reservation) {
            reservation=VirtualAlloc(nullptr,65536,MEM_RESERVE,PAGE_NOACCESS);
            if(!reservation) AllocationFailure("owned wait reservation",GetLastError());
            receipt.reservation=reinterpret_cast<std::uint64_t>(reservation);
        }
        CHECK(!reservationCommitted);
        value=static_cast<WaitOwnedObject*>(VirtualAlloc(reservation,4096,MEM_COMMIT,PAGE_READWRITE));
        if(!value) AllocationFailure("owned wait MEM_COMMIT",GetLastError());
        CHECK(value==reservation); if(objectCount) ++receipt.recommits;
        reservationCommitted=true;
    } else {
        value=static_cast<WaitOwnedObject*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
        if(!value) AllocationFailure("wait MEM_RESERVE|MEM_COMMIT",GetLastError());
    }
    value->vtable=waitVtable; value->sharedReferences=1; value->callback=callback?reinterpret_cast<void*>(&RtWaitFixture_CallbackTarget):nullptr;
    value->cleanup=reinterpret_cast<void*>(&RtWaitFixture_CleanupTarget); value->payload=node; value->ownerId=0x12345678;
    auto& row=receipt.objects[objectCount++]; row.address=value; row.generation=objectCount; row.sharedReferences=1;
    row.initial={value->callback,value->cleanup,value->payload,value->ownerId,0};
    return value;
}
void ReleaseRemaining() {
    // This is fixture teardown, not a cancellation/retirement proof. The actual
    // call receipts above keep every abnormal/missing observation unchanged.
    faultDisabled=true;
    for(unsigned i=0;i<nodeCount;++i) {
        auto& row=receipt.nodes[i];
        while(!row.released&&row.references>0) { SetLastError(InputError); releaseEntry(row.address); }
    }
    for(unsigned i=0;i<objectCount;++i) {
        auto& row=receipt.objects[i];
        while(!row.released&&!row.decommitted&&row.sharedReferences>0)
            RtWaitFixture_SharedReturn(reinterpret_cast<unsigned char*>(row.address)+8);
    }
}
void AttachLate() {
    CHECK(!attached&&attachWait(Slot,receipt.information.epoch,&waitObserver)==0);
    attached=true; ++receipt.lateAttached;
}
void WalkUnwind() {
    CONTEXT context{}; RtlCaptureContext(&context); bool sawRelay=false,sawSource=false;
    for(unsigned i=0;i<64&&context.Rip;++i) {
        DWORD64 image{}; auto* function=RtlLookupFunctionEntry(context.Rip,&image,nullptr);
        if(!function) { context.Rip=*reinterpret_cast<DWORD64*>(context.Rsp); context.Rsp+=8; continue; }
        const bool relay=context.Rip>=receipt.relayBase&&context.Rip<receipt.relayBase+18*128;
        if(relay) {
            CHECK(image==receipt.imageBase); sawRelay=true;
            const auto* unwind=reinterpret_cast<const unsigned char*>(image+function->UnwindData);
            CHECK((unwind[0]>>3)==UNW_FLAG_CHAININFO&&unwind[1]==0&&unwind[2]==0);
        }
        for(const auto& site:receipt.sites)
            if(context.Rip>=site.imageBase+site.source.BeginAddress&&context.Rip<site.imageBase+site.source.EndAddress) sawSource=true;
        PVOID handler{}; DWORD64 establisher{}; KNONVOLATILE_CONTEXT_POINTERS pointers{};
        const auto previous=context.Rsp;
        RtlVirtualUnwind(UNW_FLAG_NHANDLER,image,context.Rip,function,&context,&handler,&establisher,&pointers);
        CHECK(context.Rsp>=previous); if(relay) ++receipt.virtualUnwinds;
        // A relay's CHAININFO unwinds the source in one step. The source need
        // not appear as a second RF frame; its exact metadata is checked below.
        if(sawRelay) break;
    }
    CHECK(sawRelay); (void)sawSource; ++receipt.unwindWalks;
}
std::uintptr_t __cdecl WaitEnter(const RizEngineWaitFrameV1* frame,void* user) {
    CHECK(user==&receipt&&frame&&frame->abiVersion==1&&frame->byteSize==240&&frame->sequence);
    CHECK(receipt.callCount<Capacity); if(receipt.callCount==Capacity) return 0;
    const unsigned index=receipt.callCount++; receipt.calls[index].frame=*frame;
    CHECK(frame->slot==Slot&&frame->epoch==receipt.information.epoch&&frame->threadId==GetCurrentThreadId());
    if(frame->externalSequence) {
        bool found=false;
        if(frame->kind>=RizEngineWaitNodeStart&&frame->kind<=RizEngineWaitNodeDetach)
            for(unsigned i=0;i<receipt.nodeCallCount;++i) if(receipt.nodeFrames[i].sequence==frame->externalSequence) found=true;
        // Wait Enter precedes the old typed Enter. An actual private external
        // scope is therefore recorded now and cross-checked at its Exit below.
        (void)found;
    }
    if(frame->kind==RizEngineWaitNodeRelease&&(frame->flags&(RizEngineWaitSourceDispatch|RizEngineWaitSourceBatch))) {
        CHECK(frame->sourceSequence&&frame->operationSequence&&(frame->flags&RizEngineWaitSnapshotCached));
        CHECK(frame->caller==((frame->flags&RizEngineWaitSourceDispatch)
            ?static_cast<const void*>(&RtWaitFixture_DispatchCleanupReturn)
            :static_cast<const void*>(&RtWaitFixture_BatchCleanupReturn)));
    }
    if(frame->kind==RizEngineWaitSchedulerCallback) {
        if((frame->flags&RizEngineWaitSourceDispatch)!=0) {
            CHECK(frame->sourceSequence&&frame->operationSequence&&frame->caller==&RtWaitFixture_DispatchCallbackReturn);
        } else if(foreignReentry||reentered) {
            CHECK(frame->sourceSequence==0&&(frame->flags&(RizEngineWaitSourceDispatch|RizEngineWaitSourceBatch))==0);
            ++receipt.maskedCalls;
        }
    }
    SetLastError(0xaaaaaaa1); return index+1;
}
void __cdecl WaitExit(const RizEngineWaitFrameV1* frame,std::uintptr_t token,const RizEngineWaitCompletionV1* completion,void* user) {
    CHECK(user==&receipt&&frame&&completion&&completion->abiVersion==1&&completion->byteSize==48);
    CHECK(token&&token<=receipt.callCount); if(!token||token>receipt.callCount) return;
    auto& row=receipt.calls[token-1]; CHECK(!row.exited&&row.frame.sequence==frame->sequence);
    row.completion=*completion; row.exited=1;
    if(frame->kind<3&&(completion->flags&RizEngineWaitReturned)) {
        CHECK((completion->flags&RizEngineWaitRawRaxValid)!=0);
        CHECK(completion->rawRax==(frame->kind==0?YieldResult:frame->kind==1?DispatchResult:BatchResult));
    }
    if(frame->externalSequence) {
        bool found=false;
        if(frame->kind>=RizEngineWaitNodeStart&&frame->kind<=RizEngineWaitNodeDetach) {
            for(unsigned i=0;i<receipt.nodeCallCount;++i)
                if(receipt.nodeFrames[i].sequence==frame->externalSequence) {
                    CHECK(receipt.nodeCompletions[i].abiVersion==1);
                    CHECK(completion->externalFlags==receipt.nodeCompletions[i].flags
                        &&completion->externalRawReturn==receipt.nodeCompletions[i].returnByte); found=true;
                }
        } else {
            for(unsigned i=0;i<receipt.schedulerCallCount;++i)
                if(receipt.schedulerFrames[i].sequence==frame->externalSequence) {
                    CHECK(receipt.schedulerCompletions[i].abiVersion==1
                        &&completion->rawRax==receipt.schedulerCompletions[i].rawRax); found=true;
                }
        }
        CHECK(found);
    }
    SetLastError(0xaaaaaaa2);
}
void __cdecl Phase(const RizEngineWaitPhaseFrameV1* frame,void* user) {
    CHECK(user==&receipt&&frame&&frame->abiVersion==1&&frame->byteSize==288&&frame->eventSequence);
    CHECK(receipt.phaseCount<Capacity); if(receipt.phaseCount==Capacity) return;
    receipt.phases[receipt.phaseCount++]=*frame;
    if(frame->kind<18) {
        const auto& site=receipt.sites[frame->kind];
        CHECK(frame->sitePc==reinterpret_cast<void*>(site.site)&&frame->relayReturnPc==reinterpret_cast<void*>(site.returnPc));
        CHECK(frame->sequence&&frame->operationSequence&&frame->slot==Slot);
        if(GetCurrentThreadId()==mainThread&&!Named(L"nested-foreign")&&!Named(L"observer-reenter")) {
            CHECK((frame->flags&RizEngineWaitParentMatched)!=0);
        }
        WalkUnwind();
        if(frame->kind==RizEngineWaitPublishCommitted) {
            auto* row=Object(static_cast<WaitOwnedObject*>(frame->wait)); CHECK(row);
            if(row) CHECK(row->address->sharedReferences==row->sharedReferences+1);
        }
        if(frame->kind==RizEngineWaitOldReferenceReturned&&frame->oldWait) ++receipt.oldReferenceReturns;
        // originalCaller changes only after an actual child has claimed the
        // operation. A pre-CALL Armed/Decision event must not invent it.
        if(frame->kind==RizEngineWaitDispatchClearReached) {
            // sourceExternalSequence identifies the last actually claimed
            // child. Sticky Unknown can prevent cleanup from claiming after
            // a callback claim; a nonzero source is not cleanup authority.
            const bool cleanupClaimed=(frame->stageFlags&RizEngineWaitCleanupClaimed)!=0;
            const bool callbackClaimed=(frame->stageFlags&RizEngineWaitCallbackClaimed)!=0;
            if(cleanupClaimed||callbackClaimed) {
                const auto expectedKind=cleanupClaimed?RizEngineWaitNodeRelease:RizEngineWaitSchedulerCallback;
                const auto expectedCaller=cleanupClaimed
                    ?static_cast<const void*>(&RtWaitFixture_DispatchCleanupReturn)
                    :static_cast<const void*>(&RtWaitFixture_DispatchCallbackReturn);
                CHECK(frame->sourceExternalSequence&&frame->originalCaller==expectedCaller);
                const CallReceipt* actual=nullptr;
                for(unsigned i=0;i<receipt.callCount;++i) {
                    const auto& child=receipt.calls[i];
                    if(child.frame.externalSequence==frame->sourceExternalSequence&&child.frame.kind==expectedKind) {
                        CHECK(actual==nullptr); actual=&child;
                    }
                }
                CHECK(actual&&actual->exited&&actual->frame.caller==expectedCaller
                    &&actual->frame.sourceSequence==frame->sequence
                    &&actual->frame.operationSequence==frame->operationSequence);
                const auto completedBit=cleanupClaimed?RizEngineWaitCleanupCompleted:RizEngineWaitCallbackCompleted;
                if((frame->stageFlags&completedBit)!=0&&actual)
                    CHECK((actual->completion.flags&(RizEngineWaitReturned|RizEngineWaitAbnormal|RizEngineWaitEnterFaulted
                        |RizEngineWaitRawRaxValid|RizEngineWaitExitUnknown))==(RizEngineWaitReturned|RizEngineWaitRawRaxValid));
                if(!cleanupClaimed) CHECK((frame->stageFlags&RizEngineWaitCleanupCompleted)==0);
            } else CHECK(frame->sourceExternalSequence==0);
        }
        if(frame->kind==RizEngineWaitReleaseReferenceReturned||frame->kind==RizEngineWaitStartReferenceReturned
            ||frame->kind==RizEngineWaitBatchObjectEnd) {
            bool known=false;
            for(unsigned i=0;i<objectCount;++i) if(receipt.objects[i].address==frame->wait) known=true;
            CHECK(known); // cached identity, never dereference a returned wait
        }
        if(Named(L"observer-reenter")&&!reentered&&frame->kind==RizEngineWaitDispatchCallbackArmed) {
            reentered=true;
            schedulerEntries[RizEngineSchedulerCallback](&ownerValue,primary,nullptr,nullptr);
            ++receipt.reentries;
        }
        if(Named(L"observer-seh")&&!faultDisabled&&frame->kind==RizEngineWaitPublishBegin) {
            ++receipt.observerFaults; RaiseException(ObserverException,0,0,nullptr);
        }
    }
    // Only an actual instruction-site call has the ASM state-save wrapper.
    // Abnormal telemetry (kind18) is an ordinary C++ callback and must obey
    // its ABI rather than deliberately corrupting nonvolatile MXCSR control.
    if(frame->kind<18) _mm_setcsr(0x1f80);
    SetLastError(0xaaaaaaa3);
}
std::uintptr_t __cdecl NodeEnter(const RizEngineNodeFrameV1* frame,void*) {
    CHECK(frame&&frame->abiVersion==1&&frame->byteSize==112&&receipt.nodeCallCount<Capacity);
    if(receipt.nodeCallCount==Capacity) return 0;
    const unsigned index=receipt.nodeCallCount++; receipt.nodeFrames[index]=*frame;
    SetLastError(0xaaaaaaa4); return index+1;
}
void __cdecl NodeExit(const RizEngineNodeFrameV1* frame,std::uintptr_t token,const RizEngineNodeCompletionV1* completion,void*) {
    CHECK(frame&&completion&&token&&token<=receipt.nodeCallCount);
    if(token&&token<=receipt.nodeCallCount) { CHECK(receipt.nodeFrames[token-1].sequence==frame->sequence); receipt.nodeCompletions[token-1]=*completion; }
    ++receipt.typedExitCount; SetLastError(0xaaaaaaa5);
}
std::uintptr_t __cdecl SchedulerEnter(const RizEngineSchedulerFrameV1* frame,void*) {
    CHECK(frame&&frame->abiVersion==1&&frame->byteSize==256&&receipt.schedulerCallCount<Capacity);
    if(receipt.schedulerCallCount==Capacity) return 0;
    const unsigned index=receipt.schedulerCallCount++; receipt.schedulerFrames[index]=*frame;
    SetLastError(0xaaaaaaa6); return index+1;
}
void __cdecl SchedulerExit(const RizEngineSchedulerFrameV1* frame,std::uintptr_t token,const RizEngineSchedulerCompletionV1* completion,void*) {
    CHECK(frame&&completion&&token&&token<=receipt.schedulerCallCount);
    if(token&&token<=receipt.schedulerCallCount) { CHECK(receipt.schedulerFrames[token-1].sequence==frame->sequence); receipt.schedulerCompletions[token-1]=*completion; }
    ++receipt.typedExitCount; SetLastError(0xaaaaaaa7);
}
void __cdecl SchedulerRow(const RizEngineSchedulerRowFrameV1*,void*) { CHECK(false); }
void CallDispatch(WaitOwnedObject* wait) {
    activeDispatch=wait; SetLastError(InputError);
    CHECK(waitEntries[1](wait,nullptr,nullptr,nullptr)==DispatchResult);
    CHECK(GetLastError()==OriginalError); ++receipt.lastErrorChecks; activeDispatch=nullptr;
}
WaitOwnedNode* StartOne() {
    void* output{}; SetLastError(InputError);
    const auto result=startEntry(&ownerValue,&cacheValue,&nameValue,&output);
    CHECK(result==(Named(L"start-clear")?0u:1u)&&GetLastError()==OriginalError); ++receipt.lastErrorChecks;
    return static_cast<WaitOwnedNode*>(output);
}
void Publish(WaitOwnedNode* node,WaitOwnedObject* wait) {
    SetLastError(InputError); CHECK(waitEntries[0](node,wait,nullptr,nullptr)==YieldResult);
    CHECK(GetLastError()==OriginalError); ++receipt.lastErrorChecks;
    if(!Named(L"occupied")&&!Named(L"is-done")) {
        auto* row=Object(wait); CHECK(row&&wait->sharedReferences==row->sharedReferences+1);
        if(row) ++row->sharedReferences;
    }
}
}

extern "C" void* RtWaitFixture_StartPrepare(void* owner,void* cache,void*,void*) {
    ++receipt.nodeStarts; CHECK(owner==&ownerValue&&cache==&cacheValue);
    auto* node=NewNode(owner,cache);
    if(Named(L"start-clear")) { node->wait=NewWait(node); }
    return node;
}
extern "C" std::uint8_t RtWaitFixture_StartFinish(WaitOwnedNode* node,void** output) {
    CHECK(node&&output); auto* row=Node(node); CHECK(row);
    if(Named(L"start-clear")) {
        CHECK(node->wait==nullptr&&row&&row->references==1);
        const auto freed=VirtualFree(node,0,MEM_RELEASE); CHECK(freed);
        if(freed&&row) { row->released=1; row->references=0; ++receipt.nodeFrees; }
        *output=nullptr; SetLastError(OriginalError); return 0;
    }
    *output=node; SetLastError(OriginalError); return 1;
}
extern "C" unsigned RtWaitFixture_ReleasePrepare(WaitOwnedNode* node) {
    ++receipt.nodeReleases; auto* row=Node(node); CHECK(row&&node->references>0);
    if(!faultDisabled&&(Named(L"cleanup-seh")||Named(L"cleanup-cpp"))&&activeDispatch) {
        ++receipt.cleanupCalls;
        if(Named(L"cleanup-cpp")) throw CppException{0xc1ea0c1ea0ULL};
        RaiseException(CleanupException,0,0,nullptr);
    }
    if(row) { CHECK(row->references==node->references); --row->references; --node->references; }
    if(activeDispatch||batchActive) ++receipt.cleanupCalls;
    return row&&row->references==0?1u:0u;
}
extern "C" std::uint8_t RtWaitFixture_ReleaseFinish(WaitOwnedNode* node) {
    auto* row=Node(node); CHECK(row);
    if(row&&row->references==0) {
        CHECK(node->wait==nullptr); const auto freed=VirtualFree(node,0,MEM_RELEASE); CHECK(freed);
        if(freed) { row->released=1; ++receipt.nodeFrees; }
        SetLastError(OriginalError); return 0;
    }
    SetLastError(OriginalError); return 1;
}
extern "C" void* RtWaitFixture_Nested(void* node) {
    CHECK(Node(static_cast<WaitOwnedNode*>(node))); return Named(L"detach-nested")?nested:nullptr;
}
extern "C" void RtWaitFixture_SharedReturn(void* address) {
    auto* wait=reinterpret_cast<WaitOwnedObject*>(static_cast<unsigned char*>(address)-8);
    auto* row=Object(wait); CHECK(row&&row->sharedReferences>0);
    if(!row) return;
    CHECK(wait->sharedReferences==row->sharedReferences);
    --wait->sharedReferences; --row->sharedReferences; ++row->referenceReturns; ++receipt.sharedReturns;
    if(!row->sharedReferences) {
        if(Named(L"address-reuse")) {
            CHECK(wait==reservation&&reservationCommitted);
            const auto result=VirtualFree(wait,4096,MEM_DECOMMIT); CHECK(result);
            if(result) { row->decommitted=1; reservationCommitted=false; ++receipt.decommits; }
            MEMORY_BASIC_INFORMATION memory{};
            CHECK(VirtualQuery(wait,&memory,sizeof(memory))==sizeof(memory)&&memory.State==MEM_RESERVE
                &&memory.AllocationBase==reservation&&(memory.Protect==0||memory.Protect==PAGE_NOACCESS));
        } else {
            const auto result=VirtualFree(wait,0,MEM_RELEASE); CHECK(result);
            if(result) { row->released=1; ++receipt.waitFrees; }
        }
    }
    SetLastError(OriginalError);
}
extern "C" unsigned RtWaitFixture_YieldPrepare(WaitOwnedNode* node,WaitOwnedObject* wait,void*,void*) {
    ++receipt.yieldCalls; CHECK(Node(node)&&Object(wait));
    if(Named(L"late-attach")&&!attached) { AttachLate(); ++receipt.unobserved; }
    auto* row=Node(node); ++node->references; if(row) ++row->references;
    using Done=std::uint8_t(__cdecl*)(WaitOwnedObject*);
    const auto done=reinterpret_cast<Done*>(wait->vtable)[1](wait);
    if(done) {
        ++receipt.isDoneFallbacks; CHECK(releaseEntry(node)==1); return 0;
    }
    if(wait->callback) {
        ++receipt.occupiedFallbacks; CHECK(releaseEntry(node)==1); return 0;
    }
    return 1;
}
extern "C" void* RtWaitFixture_Owner(std::uint32_t* ownerId) {
    ++receipt.dispatchCalls; CHECK(ownerId); return ownerId&&*ownerId?&ownerValue:nullptr;
}
extern "C" std::uint64_t RtWaitFixture_CompleteOriginal(unsigned kind) {
    if(kind==2) ++receipt.batchCalls;
    if(kind==3) ++receipt.nodeDetaches;
    SetLastError(OriginalError);
    return kind==0?YieldResult:kind==1?DispatchResult:kind==2?BatchResult:0x445566778899aa04ULL;
}
namespace {
std::uint32_t __cdecl ExecuteOriginal(void* address,void* output) {
    auto* node=static_cast<WaitOwnedNode*>(address); CHECK(Node(node)); ++receipt.nodeExecutes;
    if(output) *static_cast<unsigned char*>(output)=0;
    SetLastError(OriginalError); return Named(L"high-eax")?0x12340001u:1u;
}
std::uint64_t __cdecl CallbackOriginal(void* owner,void* payload,void*,void*) {
    auto* node=static_cast<WaitOwnedNode*>(payload); CHECK(owner==&ownerValue&&Node(node)); ++receipt.callbacks;
    if(!faultDisabled&&activeDispatch) {
        if(Named(L"callback-seh")) RaiseException(CallbackException,0,0,nullptr);
        if(Named(L"callback-cpp")) throw CppException{0xcabca11bacULL};
    }
    if(Named(L"detach-inflight")&&!duringCallback&&activeDispatch) {
        duringCallback=true; detachEntry(node); CHECK(activeDispatch->ownerId==0&&activeDispatch->callback
            &&activeDispatch->cleanup&&activeDispatch->payload==node); duringCallback=false;
    }
    if(Named(L"callback-mutate")&&activeDispatch) {
        CHECK(alternate); activeDispatch->cleanup=reinterpret_cast<void*>(&RtWaitFixture_CleanupTarget);
        activeDispatch->payload=alternate;
    }
    if(Named(L"nested-foreign")&&!foreignReentry&&activeDispatch) {
        foreignReentry=true; foreignExecute(node,nullptr); foreignReentry=false; ++receipt.reentries;
    }
    unsigned char exception{}; const auto result=executeEntry(node,&exception);
    CHECK(result==(Named(L"high-eax")?0x12340001u:1u)&&exception==0);
    SetLastError(OriginalError); return Named(L"high-eax")?0xabcdef0112340001ULL:CallbackResult;
}
std::uint32_t __cdecl ForeignExecuteOriginal(void* node,void*) {
    schedulerEntries[RizEngineSchedulerCallback](&ownerValue,node,nullptr,nullptr);
    SetLastError(OriginalError); return 0x12340001u;
}
__declspec(noinline) std::uint32_t __cdecl ForeignExecuteTarget(void* a,void* b) {
    const auto result=ForeignExecuteOriginal(a,b); _ReadWriteBarrier(); return result^1u;
}
__declspec(noinline) std::uint8_t __cdecl StartTarget(void* a,void* b,void* c,void* d) {
    const auto result=startEntry(a,b,c,d); _ReadWriteBarrier(); return result;
}
__declspec(noinline) std::uint32_t __cdecl ExecuteTarget(void* a,void* b) {
    const auto result=executeEntry(a,b); _ReadWriteBarrier(); return result;
}
__declspec(noinline) void __cdecl DetachTarget(void* a) { detachEntry(a); _ReadWriteBarrier(); }
template<unsigned Kind> __declspec(noinline) std::uint64_t __cdecl WaitTarget(void* a,void* b,void* c,void* d) {
    const auto result=waitEntries[Kind](a,b,c,d); _ReadWriteBarrier(); return result;
}
template<unsigned Kind> __declspec(noinline) std::uint64_t __cdecl SchedulerTarget(void* a,void* b,void* c,void* d) {
    const auto result=schedulerEntries[Kind](a,b,c,d); _ReadWriteBarrier(); return result;
}
struct SchedulerRecord { unsigned char bytes[0x40]; } schedulerRecord{};
struct SchedulerRecordEntry { unsigned char links[0x20]; SchedulerRecord record; } schedulerRecordEntry{};
template<unsigned Kind> __declspec(noinline) std::uint64_t __cdecl SchedulerOriginal(void* a,void* b,void* c,void*) {
    if constexpr(Kind==0) {
        CHECK(a==&schedulerTree&&b&&c==&schedulerRecord);
        std::memcpy(&schedulerRecordEntry.record,c,sizeof(schedulerRecord));
        *static_cast<void**>(b)=&schedulerRecordEntry; SetLastError(OriginalError); return reinterpret_cast<std::uint64_t>(b);
    } else { SetLastError(OriginalError); return 0xaabbccddeeff0000ULL+Kind; }
}
__declspec(noinline) void DummyDrainBegin() { _ReadWriteBarrier(); }
__declspec(noinline) void DummyDrainEnd() { _WriteBarrier(); }
bool Jump(unsigned char* at,std::uint64_t pc,std::uint64_t target) {
    const auto delta=static_cast<std::int64_t>(target)-static_cast<std::int64_t>(pc+5);
    if(delta<INT32_MIN||delta>INT32_MAX) return false;
    at[0]=0xe9; const auto rel=static_cast<std::int32_t>(delta); std::memcpy(at+1,&rel,4); return true;
}
bool PrepareRelays() {
    receipt.imageBase=reinterpret_cast<std::uint64_t>(GetModuleHandleW(nullptr));
    receipt.phaseEntry=reinterpret_cast<std::uint64_t>(phaseEntry);
    auto next=(receipt.imageBase+0x400000+0xffff)&~std::uint64_t(0xffff);
    for(;next<receipt.imageBase+0x70000000;next+=0x10000) {
        MEMORY_BASIC_INFORMATION memory{};
        if(!VirtualQuery(reinterpret_cast<void*>(next),&memory,sizeof(memory))) break;
        if(memory.State==MEM_FREE&&memory.RegionSize>=8192) {
            relayBlock=static_cast<unsigned char*>(VirtualAlloc(reinterpret_cast<void*>(next),8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
            if(relayBlock) break;
        }
    }
    CHECK(relayBlock); if(!relayBlock) return false;
    receipt.relayBase=reinterpret_cast<std::uint64_t>(relayBlock);
    relayFunctions=reinterpret_cast<PRUNTIME_FUNCTION>(relayBlock+0x1400);
    const void* sites[]{
#define SITE(n) &RtWaitFixture_Phase##n
        SITE(0),SITE(1),SITE(2),SITE(3),SITE(4),SITE(5),SITE(6),SITE(7),SITE(8),
        SITE(9),SITE(10),SITE(11),SITE(12),SITE(13),SITE(14),SITE(15),SITE(16),SITE(17)};
    const void* continuations[]{
#define AFTER(n) &RtWaitFixture_After##n
        AFTER(0),AFTER(1),AFTER(2),AFTER(3),AFTER(4),AFTER(5),AFTER(6),AFTER(7),AFTER(8),
        AFTER(9),AFTER(10),AFTER(11),AFTER(12),AFTER(13),AFTER(14),AFTER(15),AFTER(16),AFTER(17)};
    const unsigned spans[]{10,6,8,5,10,10,7,8,8,7,8,9,5,7,5,8,6,10};
    for(unsigned i=0;i<18;++i) {
        auto& site=receipt.sites[i]; site.site=reinterpret_cast<std::uint64_t>(sites[i]);
        site.continuation=reinterpret_cast<std::uint64_t>(continuations[i]); site.span=spans[i];
        CHECK(site.continuation-site.site==site.span);
        DWORD64 image{}; const auto* source=RtlLookupFunctionEntry(site.site,&image,nullptr);
        CHECK(source&&image==receipt.imageBase); if(!source) return false;
        const auto expectedBegin=i<4?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Yield)
            :i<6?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Detach)
            :i<9?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Release)
            :i<12?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Start)
            :i<16?reinterpret_cast<std::uint64_t>(&RtWaitFixture_Dispatch)
            :reinterpret_cast<std::uint64_t>(&RtWaitFixture_Batch);
        const auto expectedEnd=i<4?reinterpret_cast<std::uint64_t>(&RtWaitFixture_YieldEnd)
            :i<6?reinterpret_cast<std::uint64_t>(&RtWaitFixture_DetachEnd)
            :i<9?reinterpret_cast<std::uint64_t>(&RtWaitFixture_ReleaseEnd)
            :i<12?reinterpret_cast<std::uint64_t>(&RtWaitFixture_StartEnd)
            :i<16?reinterpret_cast<std::uint64_t>(&RtWaitFixture_DispatchEnd)
            :reinterpret_cast<std::uint64_t>(&RtWaitFixture_BatchEnd);
        CHECK(image+source->BeginAddress==expectedBegin&&image+source->EndAddress==expectedEnd);
        site.source=*source; site.imageBase=image;
        site.relay=receipt.relayBase+i*128; site.returnPc=site.relay+6;
        std::memcpy(site.original,sites[i],site.span);
        auto* code=relayBlock+i*128; const unsigned char call[]{0xff,0x15,0x52,0,0,0};
        std::memcpy(code,call,6); std::memcpy(code+88,&phaseEntry,8); unsigned cursor=6;
        const bool conditional=i==1||i==12||i==14||i==16;
        if(conditional) {
            const unsigned prefix=i==1?0u:i==16?4u:3u;
            if(prefix) { std::memcpy(code+cursor,site.original,prefix); cursor+=prefix; }
            CHECK(site.original[prefix]==0x74);
            site.branch=site.site+prefix+2+static_cast<std::int8_t>(site.original[prefix+1]);
            code[cursor]=0x0f; code[cursor+1]=0x84;
            const auto delta=static_cast<std::int64_t>(site.branch)-static_cast<std::int64_t>(site.relay+cursor+6);
            CHECK(delta>=INT32_MIN&&delta<=INT32_MAX); const auto relative=static_cast<std::int32_t>(delta);
            std::memcpy(code+cursor+2,&relative,4); cursor+=6;
            if(i==1) { std::memcpy(code+cursor,site.original+2,4); cursor+=4; }
        } else if(i==3) {
            CHECK(site.original[0]==0xe9); std::int32_t delta{}; std::memcpy(&delta,site.original+1,4);
            site.branch=site.site+5+delta; CHECK(Jump(code+cursor,site.relay+cursor,site.branch)); cursor+=5;
        } else {
            std::memcpy(code+cursor,site.original,site.span);
            // Three source windows contain RIP-relative LEA/MOV. The actual
            // operand address, not its old displacement, is preserved.
            if(i==0||i==4||i==5) {
                CHECK((site.original[5]&0xc7)==5); std::int32_t old{}; std::memcpy(&old,site.original+6,4);
                const auto destination=site.site+10+old;
                const auto delta=static_cast<std::int64_t>(destination)-static_cast<std::int64_t>(site.relay+cursor+10);
                CHECK(delta>=INT32_MIN&&delta<=INT32_MAX); const auto relative=static_cast<std::int32_t>(delta);
                std::memcpy(code+cursor+6,&relative,4);
            }
            cursor+=site.span;
        }
        if(i!=3) { CHECK(Jump(code+cursor,site.relay+cursor,site.continuation)); cursor+=5; }
        site.length=cursor; std::memcpy(site.code,code,96);
        auto* unwind=relayBlock+0x1000+i*32;
        unwind[0]=1|(UNW_FLAG_CHAININFO<<3); std::memcpy(unwind+4,source,sizeof(*source));
        relayFunctions[i]={static_cast<DWORD>(site.relay-image),static_cast<DWORD>(site.relay+cursor-image),
            static_cast<DWORD>(reinterpret_cast<std::uint64_t>(unwind)-image)};
        site.function=relayFunctions[i]; std::memcpy(site.unwind,unwind,16);
        std::memset(site.replacement,0x90,site.span); CHECK(Jump(site.replacement,site.site,site.relay));
    }
    CHECK(reinterpret_cast<std::uint64_t>(&RtWaitFixture_DispatchCallbackReturn)==receipt.sites[13].site+0xa);
    CHECK(reinterpret_cast<std::uint64_t>(&RtWaitFixture_DispatchCleanupReturn)==receipt.sites[14].site+0xb);
    CHECK(reinterpret_cast<std::uint64_t>(&RtWaitFixture_BatchCleanupReturn)==receipt.sites[16].site+0xd);
    DWORD previous{}; CHECK(VirtualProtect(relayBlock,8192,PAGE_EXECUTE_READ,&previous));
    CHECK(FlushInstructionCache(GetCurrentProcess(),relayBlock,8192));
    CHECK(RtlAddFunctionTable(relayFunctions,18,receipt.imageBase));
    return receipt.failures==0;
}
bool Initialize(const wchar_t* path) {
    kernelLock=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    CHECK(kernelLock!=INVALID_HANDLE_VALUE);
    kernel=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    CHECK(kernel); if(!kernel) return false;
    auto waitVersion=Symbol<decltype(&RizEngineWaitContext_GetAbiVersion)>("RizEngineWaitContext_GetAbiVersion");
    auto nodeVersion=Symbol<decltype(&RizEngineNodeContext_GetAbiVersion)>("RizEngineNodeContext_GetAbiVersion");
    auto nodeFrameSize=Symbol<decltype(&RizEngineNodeContext_GetFrameSize)>("RizEngineNodeContext_GetFrameSize");
    auto nodeCompletionSize=Symbol<decltype(&RizEngineNodeContext_GetCompletionSize)>("RizEngineNodeContext_GetCompletionSize");
    auto schedulerVersion=Symbol<decltype(&RizEngineSchedulerContext_GetAbiVersion)>("RizEngineSchedulerContext_GetAbiVersion");
    auto schedulerFrameSize=Symbol<decltype(&RizEngineSchedulerContext_GetFrameSize)>("RizEngineSchedulerContext_GetFrameSize");
    auto schedulerCompletionSize=Symbol<decltype(&RizEngineSchedulerContext_GetCompletionSize)>("RizEngineSchedulerContext_GetCompletionSize");
    auto frameSize=Symbol<decltype(&RizEngineWaitContext_GetFrameSize)>("RizEngineWaitContext_GetFrameSize");
    auto completionSize=Symbol<decltype(&RizEngineWaitContext_GetCompletionSize)>("RizEngineWaitContext_GetCompletionSize");
    auto phaseSize=Symbol<decltype(&RizEngineWaitContext_GetPhaseFrameSize)>("RizEngineWaitContext_GetPhaseFrameSize");
    auto getPhase=Symbol<decltype(&RizEngineWaitContext_GetPhaseEntry)>("RizEngineWaitContext_GetPhaseEntry");
    auto configureWait=Symbol<decltype(&RizEngineWaitContext_Configure)>("RizEngineWaitContext_Configure");
    auto waitGetEntry=Symbol<decltype(&RizEngineWaitContext_GetEntry)>("RizEngineWaitContext_GetEntry");
    attachWait=Symbol<decltype(attachWait)>("RizEngineWaitContext_Attach");
    waitInfo=Symbol<decltype(waitInfo)>("RizEngineWaitContext_GetInfo");
    auto configureNode=Symbol<decltype(&RizEngineNodeContext_Configure)>("RizEngineNodeContext_Configure");
    auto nodeGetEntry=Symbol<decltype(&RizEngineNodeContext_GetEntry)>("RizEngineNodeContext_GetEntry");
    auto nodeAttach=Symbol<decltype(&RizEngineNodeContext_Attach)>("RizEngineNodeContext_Attach");
    nodeInfo=Symbol<decltype(nodeInfo)>("RizEngineNodeContext_GetInfo");
    auto configureScheduler=Symbol<decltype(&RizEngineSchedulerContext_Configure)>("RizEngineSchedulerContext_Configure");
    auto schedulerGetEntry=Symbol<decltype(&RizEngineSchedulerContext_GetEntry)>("RizEngineSchedulerContext_GetEntry");
    auto schedulerAttach=Symbol<decltype(&RizEngineSchedulerContext_Attach)>("RizEngineSchedulerContext_Attach");
    schedulerInfo=Symbol<decltype(schedulerInfo)>("RizEngineSchedulerContext_GetInfo");
    if(receipt.failures) return false;
    CHECK(waitVersion()==1&&frameSize()==240&&completionSize()==48&&phaseSize()==288);
    CHECK(nodeVersion()==2&&nodeFrameSize()==112&&nodeCompletionSize()==32);
    CHECK(schedulerVersion()==1&&schedulerFrameSize()==256&&schedulerCompletionSize()==48);
    phaseEntry=getPhase(); CHECK(phaseEntry); if(!PrepareRelays()) return false;
    void* nodeTargets[]{reinterpret_cast<void*>(&StartTarget),reinterpret_cast<void*>(&ExecuteTarget),
        reinterpret_cast<void*>(&RtWaitFixture_CleanupTarget),reinterpret_cast<void*>(&DetachTarget)};
    void* nodeOriginals[]{reinterpret_cast<void*>(&RtWaitFixture_Start),reinterpret_cast<void*>(&ExecuteOriginal),
        reinterpret_cast<void*>(&RtWaitFixture_Release),reinterpret_cast<void*>(&RtWaitFixture_Detach)};
    for(unsigned kind=0;kind<4;++kind) {
        RizEngineNodeConfigV1 config{1,sizeof(config),nodeTargets[kind],nodeOriginals[kind]};
        CHECK(configureNode(kind,Slot,&config)==0);
        RizEngineNodeInfoV1 info{}; info.abiVersion=1; info.byteSize=sizeof(info); CHECK(nodeInfo(kind,Slot,&info)==0);
        RizEngineNodeObserverV1 observer{1,sizeof(observer),&NodeEnter,&NodeExit,&receipt}; CHECK(nodeAttach(kind,Slot,info.epoch,&observer)==0);
    }
    startEntry=reinterpret_cast<Start>(nodeGetEntry(0,Slot)); executeEntry=reinterpret_cast<Execute>(nodeGetEntry(1,Slot));
    releaseEntry=reinterpret_cast<Release>(nodeGetEntry(2,Slot)); detachEntry=reinterpret_cast<Detach>(nodeGetEntry(3,Slot));
    RtWaitFixture_ReleaseEntry=reinterpret_cast<void*>(releaseEntry);
    Carrier schedulerTargets[]{&SchedulerTarget<0>,&SchedulerTarget<1>,&SchedulerTarget<2>,&SchedulerTarget<3>,&SchedulerTarget<4>,&RtWaitFixture_CallbackTarget};
    Carrier schedulerOriginals[]{&SchedulerOriginal<0>,&SchedulerOriginal<1>,&SchedulerOriginal<2>,&SchedulerOriginal<3>,&SchedulerOriginal<4>,&CallbackOriginal};
    for(unsigned kind=0;kind<6;++kind) {
        RizEngineSchedulerConfigV1 config{1,sizeof(config),reinterpret_cast<void*>(schedulerTargets[kind]),
            reinterpret_cast<void*>(schedulerOriginals[kind]),mainThread,0,{}};
        if(kind==4) { config.sites[0]={reinterpret_cast<void*>(&DummyDrainBegin),reinterpret_cast<void*>(&DummyDrainBegin)};
            config.sites[1]={reinterpret_cast<void*>(&DummyDrainEnd),reinterpret_cast<void*>(&DummyDrainEnd)}; }
        CHECK(configureScheduler(kind,Slot,&config)==0);
        RizEngineSchedulerInfoV1 info{}; info.abiVersion=1; info.byteSize=sizeof(info); CHECK(schedulerInfo(kind,Slot,&info)==0);
        RizEngineSchedulerObserverV1 observer{1,sizeof(observer),&SchedulerEnter,&SchedulerExit,kind==4?&SchedulerRow:nullptr,&receipt};
        CHECK(schedulerAttach(kind,Slot,info.epoch,&observer)==0);
        schedulerEntries[kind]=reinterpret_cast<Carrier>(schedulerGetEntry(kind,Slot)); CHECK(schedulerEntries[kind]);
    }
    RtWaitFixture_CallbackEntry=reinterpret_cast<void*>(schedulerEntries[5]);
    RizEngineWaitConfigV1 config{}; config.abiVersion=1; config.byteSize=sizeof(config); config.mainThreadId=mainThread;
    Carrier targets[]{&WaitTarget<0>,&WaitTarget<1>,&WaitTarget<2>};
    Carrier originals[]{&RtWaitFixture_Yield,&RtWaitFixture_Dispatch,&RtWaitFixture_Batch};
    for(unsigned i=0;i<3;++i) { config.targets[i]=reinterpret_cast<void*>(targets[i]); config.originals[i]=reinterpret_cast<void*>(originals[i]); }
    for(unsigned i=0;i<4;++i) config.nodeTargets[i]=nodeTargets[i];
    for(unsigned i=0;i<6;++i) config.schedulerTargets[i]=reinterpret_cast<void*>(schedulerTargets[i]);
    for(unsigned i=0;i<18;++i) config.sites[i]={reinterpret_cast<void*>(receipt.sites[i].site),reinterpret_cast<void*>(receipt.sites[i].returnPc)};
    CHECK(configureWait(Slot,&config)==0);
    receipt.information.abiVersion=1; receipt.information.byteSize=sizeof(receipt.information); CHECK(waitInfo(Slot,&receipt.information)==0);
    waitObserver={1,sizeof(waitObserver),&WaitEnter,&WaitExit,&Phase,&receipt};
    if(!Named(L"late-attach")) { CHECK(attachWait(Slot,receipt.information.epoch,&waitObserver)==0); attached=true; }
    for(unsigned kind=0;kind<3;++kind) { waitEntries[kind]=reinterpret_cast<Carrier>(waitGetEntry(kind,Slot)); CHECK(waitEntries[kind]); }
    if(Named(L"nested-foreign")) {
        // This is a real unobserved foreign Node Execute boundary. Its target
        // is not in the Wait family; its native physical mask still prevents
        // the nested known callback from borrowing the outer Dispatch.
        RizEngineNodeConfigV1 foreign{1,sizeof(foreign),reinterpret_cast<void*>(&ForeignExecuteTarget),
            reinterpret_cast<void*>(&ForeignExecuteOriginal)};
        CHECK(configureNode(RizEngineNodeExecute,Slot+1,&foreign)==0);
        foreignExecute=reinterpret_cast<Execute>(nodeGetEntry(RizEngineNodeExecute,Slot+1)); CHECK(foreignExecute);
    }
    for(unsigned i=0;i<18;++i) {
        const auto& site=receipt.sites[i]; DWORD previous{},unused{}; auto* pointer=reinterpret_cast<void*>(site.site);
        CHECK(std::memcmp(pointer,site.original,site.span)==0&&VirtualProtect(pointer,site.span,PAGE_EXECUTE_READWRITE,&previous));
        std::memcpy(pointer,site.replacement,site.span); CHECK(FlushInstructionCache(GetCurrentProcess(),pointer,site.span));
        CHECK(VirtualProtect(pointer,site.span,previous,&unused));
    }
    return receipt.failures==0;
}
void SchedulerSmoke() {
    void* output{};
    CHECK(schedulerEntries[0](&schedulerTree,&output,&schedulerRecord,nullptr)==reinterpret_cast<std::uint64_t>(&output));
    CHECK(output==&schedulerRecordEntry);
    for(unsigned kind=1;kind<5;++kind) {
        void* arg0=&schedulerManager;
        void* arg1=kind==1?&schedulerRecordEntry.record:nullptr;
        void* arg2=kind==1||kind==2?&schedulerRecordEntry:nullptr;
        CHECK(schedulerEntries[kind](arg0,arg1,arg2,nullptr)==0xaabbccddeeff0000ULL+kind);
    }
}
void RunBody() {
    primary=StartOne();
    if(Named(L"start-clear")) { CHECK(!primary); return; }
    CHECK(primary); if(!primary) return;
    SchedulerSmoke();
    if(Named(L"callback-mutate")) alternate=NewNode(&ownerValue,&cacheValue);
    if(Named(L"batch")) {
        for(unsigned i=0;i<3;++i) {
            auto* node=i?NewNode(&ownerValue,&cacheValue):primary;
            batchObjects[i]=NewWait(node,i!=1);
        }
        manager.objects=batchObjects; manager.count=3; batchActive=true; SetLastError(InputError);
        CHECK(waitEntries[2](&manager,nullptr,nullptr,nullptr)==BatchResult&&GetLastError()==OriginalError); ++receipt.lastErrorChecks; batchActive=false;
        return;
    }
    if(Named(L"release-clear")) {
        primary->wait=NewWait(primary); SetLastError(InputError);
        CHECK(releaseEntry(primary)==0&&GetLastError()==OriginalError); ++receipt.lastErrorChecks; return;
    }
    auto* wait=NewWait(primary,Named(L"occupied"));
    if(Named(L"replace")) {
        auto* old=NewWait(primary); primary->wait=old;
        // This old wait has exactly the node's shared reference. A successful
        // replacement really frees it before OldReferenceReturned executes.
    }
    Publish(primary,wait);
    if(Named(L"occupied")||Named(L"is-done")) { CHECK(primary->wait==nullptr&&wait->sharedReferences==1); return; }
    if(Named(L"late-attach")) {
        auto* second=NewWait(primary,false); Publish(primary,second); wait=second;
    }
    if(Named(L"callback-mutate")) wait->cleanup=nullptr;
    if(Named(L"owner-null")) wait->ownerId=0;
    if(Named(L"detach-nested")) {
        nested=NewNode(&ownerValue,&cacheValue); nested->wait=NewWait(nested);
        detachEntry(primary); CHECK(wait->ownerId==0&&nested->wait->ownerId==0&&wait->callback&&wait->cleanup&&wait->payload==primary);
    }
    CallDispatch(wait);
    CHECK(wait->callback==nullptr&&wait->cleanup==reinterpret_cast<void*>(&RtWaitFixture_CleanupTarget));
    if(Named(L"address-reuse")) {
        ReleaseRemaining(); CHECK(!reservationCommitted&&receipt.decommits==1);
        auto* secondNode=StartOne(); auto* second=NewWait(secondNode,false);
        CHECK(second==wait&&receipt.objects[1].generation>receipt.objects[0].generation);
        Publish(secondNode,second); CallDispatch(second);
    }
}
void RunSeh() {
    __try { RunBody(); }
    __except(GetExceptionCode()==CallbackException||GetExceptionCode()==CleanupException?EXCEPTION_EXECUTE_HANDLER:EXCEPTION_CONTINUE_SEARCH) {
        ++receipt.originalSeh;
    }
}
DWORD WINAPI Worker(void*) { RunSeh(); ++receipt.workerCalls; return 0; }
void ValidateStatePairs() {
    // Nine windows can be snapshotted immediately on both sides without
    // shifting the original callback/cleanup CALL identities. The remaining
    // conditional/call-adjacent windows are checked by actual route/caller
    // receipts, not falsely claimed as full-register state pairs.
    const unsigned selected[]{0,4,5,6,8,9,11,15,17};
    for(const auto phase:selected) {
        const auto& before=RtWaitFixture_Before[phase];
        const auto& after=RtWaitFixture_After[phase];
        if(!before.gpr[RizEventMidRsp]) continue;
        CHECK(after.gpr[RizEventMidRsp]&&before.abiVersion==1&&after.abiVersion==1
            &&before.byteSize==432&&after.byteSize==432);
        for(unsigned reg=0;reg<16;++reg) {
            auto expected=before.gpr[reg];
            if(phase==0&&reg==RizEventMidRax) expected=reinterpret_cast<std::uint64_t>(&RtWaitFixture_CallbackTarget);
            if((phase==4||phase==5)&&reg==RizEventMidRcx) expected=RtWaitFixture_RipValue;
            if(phase==17&&reg==RizEventMidRdi) ++expected;
            CHECK(after.gpr[reg]==expected);
            CHECK(std::memcmp(&before.xmm[reg],&after.xmm[reg],16)==0);
        }
        auto flags=before.rflags;
        if(phase==8||phase==11) {
            std::uint64_t handle{};
            for(unsigned i=0;i<nodeCount;++i)
                if(reinterpret_cast<std::uint64_t>(receipt.nodes[i].address)==before.gpr[RizEventMidRbx])
                    handle=receipt.nodes[i].handle;
            CHECK(handle); flags=RtWaitFixture_CmpFlags(handle,0,before.rflags);
        } else if(phase==17)
            flags=RtWaitFixture_CmpFlags(before.gpr[RizEventMidRdi]+1,manager.count,before.rflags);
        CHECK(after.rflags==flags&&after.mxcsr==before.mxcsr&&after.fpcsr==before.fpcsr);
        ++receipt.statePairs;
    }
}
void Validate() {
    ValidateStatePairs();
    for(unsigned i=0;i<receipt.callCount;++i) CHECK(receipt.calls[i].exited==1);
    bool expectedFault=Named(L"worker")||Named(L"observer-seh")||Named(L"callback-seh")||Named(L"cleanup-seh")
        ||Named(L"callback-cpp")||Named(L"cleanup-cpp");
    receipt.information.abiVersion=1; receipt.information.byteSize=sizeof(receipt.information);
    CHECK(waitInfo(Slot,&receipt.information)==0&&receipt.information.inFlight==0&&receipt.information.entered==receipt.information.exited);
    CHECK(receipt.information.unknown==(expectedFault?1u:0u));
    CHECK(receipt.nodeStarts>0&&receipt.nodeFrees==nodeCount&&objectCount==receipt.waitFrees+receipt.decommits);
    if(Named(L"start-clear")) CHECK(receipt.nodeStarts==1&&receipt.nodeExecutes==0);
    else CHECK(receipt.schedulerCallCount>=5);
    if(Named(L"occupied")||Named(L"is-done")) CHECK(receipt.phaseCount==0&&receipt.callbacks==0);
    if(Named(L"occupied")) CHECK(receipt.isDoneCalls==1&&receipt.occupiedFallbacks==1&&receipt.isDoneFallbacks==0);
    if(Named(L"is-done")) CHECK(receipt.isDoneCalls==1&&receipt.isDoneFallbacks==1&&receipt.occupiedFallbacks==0);
    if(Named(L"replace")) CHECK(receipt.oldReferenceReturns==1);
    if(Named(L"owner-null")||Named(L"detach-nested")) CHECK(receipt.callbacks==0&&receipt.cleanupCalls>=1);
    if(Named(L"callback-mutate")) CHECK(receipt.callbacks==1&&receipt.nodes[1].released==1);
    if(Named(L"batch")) CHECK(receipt.batchCalls==1&&receipt.callbacks==0&&receipt.waitFrees==3&&receipt.cleanupCalls==2);
    if(Named(L"callback-seh")||Named(L"cleanup-seh")) CHECK(receipt.originalSeh==1);
    if(Named(L"callback-cpp")||Named(L"cleanup-cpp")) CHECK(receipt.originalCpp==1);
    if(Named(L"observer-seh")) CHECK(receipt.observerFaults==1);
    if(Named(L"observer-reenter")||Named(L"nested-foreign")) CHECK(receipt.maskedCalls>0&&receipt.reentries==1);
    if(Named(L"late-attach")) CHECK(receipt.lateAttached==1&&receipt.unobserved==1);
    if(Named(L"address-reuse")) CHECK(receipt.decommits==2&&receipt.recommits==1&&receipt.waitFrees==0&&receipt.reservationReleased==1);
    if(!Named(L"occupied")&&!Named(L"is-done")) CHECK(receipt.unwindWalks>0&&receipt.virtualUnwinds==receipt.unwindWalks);
    std::memcpy(receipt.before,RtWaitFixture_Before,sizeof(receipt.before));
    std::memcpy(receipt.after,RtWaitFixture_After,sizeof(receipt.after));
}
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=4) { std::fwprintf(stderr,L"Usage: EngineWait.Fixture.exe ABS_KERNEL MODE ABS_RAW\n"); return 2; }
    mode=argv[2]; mainThread=GetCurrentThreadId();
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);
    const bool initialized=Initialize(argv[1]);
    for(unsigned i=0;i<18;++i) {
        RtWaitFixture_Before[i].abiVersion=RtWaitFixture_After[i].abiVersion=1;
        RtWaitFixture_Before[i].byteSize=RtWaitFixture_After[i].byteSize=sizeof(RizEventMidFrameV1);
    }
    _mm_setcsr(0x3f80); receipt.beforeControl=_mm_getcsr();
    if(initialized) {
        try {
            if(Named(L"worker")) {
                HANDLE worker=CreateThread(nullptr,0,&Worker,nullptr,0,nullptr); CHECK(worker);
                if(worker) { CHECK(WaitForSingleObject(worker,10000)==WAIT_OBJECT_0); CloseHandle(worker); }
            } else RunSeh();
        } catch(const CppException&) { ++receipt.originalCpp; }
        activeDispatch=nullptr; batchActive=false; ReleaseRemaining();
        if(reservation) {
            CHECK(!reservationCommitted); const auto freed=VirtualFree(reservation,0,MEM_RELEASE); CHECK(freed);
            if(freed) receipt.reservationReleased=1;
        }
        receipt.afterControl=_mm_getcsr(); CHECK(receipt.beforeControl==receipt.afterControl);
        Validate();
    }
    FILE* file{}; CHECK(_wfopen_s(&file,argv[3],L"wb")==0&&file);
    // Raw bytes are a snapshot taken before the three actual persistence
    // checks. Keep that count explicit instead of claiming later I/O checks
    // were already present in the bytes written.
    const auto rawChecks=receipt.checks, rawFailures=receipt.failures;
    if(file) { CHECK(std::fwrite(&receipt,1,sizeof(receipt),file)==sizeof(receipt)); CHECK(std::fflush(file)==0); CHECK(std::fclose(file)==0); }
    std::printf("{\"schemaVersion\":1,\"kind\":\"native-engine-wait-fixture\",\"mode\":\"%ls\",\"passed\":%s,\"initialized\":%s,"
        "\"checks\":%u,\"failures\":%u,\"rawChecks\":%u,\"rawFailures\":%u,\"persistenceChecks\":%u,\"rawBytes\":%zu,\"typedEntryInvoked\":%s,\"physicalKindsConfigured\":13,\"phaseSitesConfigured\":18,"
        "\"observedCalls\":%u,\"observedPhases\":%u,\"nodeCalls\":%u,\"schedulerCalls\":%u,\"callbacks\":%u,\"sharedReturns\":%u,"
        "\"nodeFrees\":%u,\"waitMemReleaseSuccesses\":%u,\"waitDecommitSuccesses\":%u,\"ownedReservationRecommits\":%u,\"reservationReleased\":%s,"
        "\"unwindWalks\":%u,\"virtualUnwinds\":%u,\"lastErrorChecks\":%u,\"statePairs\":%u,\"unknown\":%s,"
        "\"pageStateIsReleaseEvidence\":false,\"productionPatchInstallationVerified\":false,\"allWritersProven\":false,\"allDispatchersProven\":false,\"futureSourcesClosed\":false,"
        "\"gameRuntimeVerified\":false,\"productionAdmissionVerified\":false,\"engineCancellationCompleted\":false}\n",
        mode,initialized&&!receipt.failures?"true":"false",initialized?"true":"false",receipt.checks,receipt.failures,rawChecks,rawFailures,receipt.checks-rawChecks,sizeof(receipt),
        initialized&&receipt.nodeStarts?"true":"false",receipt.callCount,receipt.phaseCount,receipt.nodeCallCount,receipt.schedulerCallCount,
        receipt.callbacks,receipt.sharedReturns,receipt.nodeFrees,receipt.waitFrees,receipt.decommits,receipt.recommits,
        receipt.reservationReleased?"true":"false",receipt.unwindWalks,receipt.virtualUnwinds,receipt.lastErrorChecks,receipt.statePairs,
        receipt.information.unknown?"true":"false");
    std::fflush(stdout);
    // Permanent configured callbacks/relays stay alive until this child exits.
    return initialized&&!receipt.failures?0:1;
}
