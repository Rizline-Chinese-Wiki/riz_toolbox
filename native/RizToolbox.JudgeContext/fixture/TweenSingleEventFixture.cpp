#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <intrin.h>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <limits>
#include <stdexcept>
#include "../TweenSingleEventContext.h"
#include "../TweenAdmission.h"
#include "../TweenRetirementContext.h"

extern "C" {
void __cdecl RtSingleFixture_Original(void*,float,const void*);
void __cdecl RtSingleFixture_TypedTarget(void*,float,const void*);
void __cdecl RtSingleFixture_VoidTarget(void*,const void*);
void __cdecl RtSingleFixture_ArrayTarget(void*,void*,const void*);
void* RtSingleFixture_CppTargets[3]{};
unsigned RtSingleFixture_MidPolicy{};
RizEventMidFrameV1 RtSingleFixture_MidBeforeState{},RtSingleFixture_MidAfterState{},RtSingleFixture_TargetState{},RtSingleFixture_LoopState{};
extern char RtSingleFixture_MidReturn,RtSingleFixture_Return0,RtSingleFixture_Return1,RtSingleFixture_Return2;
extern char RtSingleFixture_Loop;
extern char RtSingleFixture_PrimaryEnd,RtSingleFixture_Continuation,RtSingleFixture_ContinuationEnd;
extern char RizTweenSingleEventContext_MidCall,RizTweenSingleEventContext_MidEpilog;
extern char RizTweenSingleEventContext_PreCall,RizTweenSingleEventContext_PreEpilog;
extern char RizTweenSingleEventContext_PostCall,RizTweenSingleEventContext_PostEpilog;
}
namespace {
constexpr DWORD OriginalSeh=0xe0525345u,ObserverSeh=0xe052534fu;
struct Class {
    std::array<unsigned char,0x178> pad{};
    const void* arrayTarget{};const void* arrayMethod{};
    std::array<unsigned char,0x10> gap{};
    const void* singleTarget{};const void* singleMethod{};
};
struct Invokable {const Class* type{};unsigned site{},id{};};
struct List {const void* type{};void* monitor{};Invokable** items{};unsigned count{},version{};};
struct Box {const void* type{};void* monitor{};std::uint32_t bits{};};
struct Arguments {const void* type{};void* monitor{};void* bounds{};std::uint64_t length{1};Box* value{};};
struct Event {const void* type{};void* monitor{};List* list{};std::uint64_t unused{};Box* box{};Arguments* arguments{};};
static_assert(offsetof(Class,arrayTarget)==0x178&&offsetof(Class,singleTarget)==0x198&&offsetof(Class,singleMethod)==0x1a0);
static_assert(offsetof(List,items)==0x10&&offsetof(List,count)==0x18&&offsetof(Event,arguments)==0x28);
struct CppPayload {std::uint64_t value;};
struct CppUnwind {~CppUnwind();};
std::uint64_t checks{},failures{},originals{},listeners{},mids{},befores{},afters{},abnormal{},cppUnwinds{},workerRejects{},nativeBefore{},nativeAfter{},helperUnwinds{},stubUnwinds{},caughtOriginalExceptions{},machineStateComparisons{};
thread_local RizEventMidFrameV1 lastPreMachine{},lastPostMachine{};
thread_local bool lastPostValid{};
thread_local std::array<RizOperationFrameV1,128> actualEntries{};
thread_local unsigned operationDepth{},callbackDepth{};
unsigned maximumCallbackDepth{};
const char* mode{};
bool armed=true,nestedDone{},attachedLate{};
const RizTweenSingleEventFrameV1* saved{};
Class invokableClass{};int eventType{},listType{},boxType{},arrayType{},singleMethod{},arrayMethod{},parentMethod{};
Invokable typed{&invokableClass,0,10},plain{&invokableClass,1,20},array{&invokableClass,2,30};
std::array<Invokable*,4> rows{&typed,&plain,&array,&typed};
std::array<Invokable*,257> manyRows{};
List empty{&listType,nullptr,rows.data(),0,1},mixed{&listType,nullptr,rows.data(),4,2};
List many{&listType,nullptr,manyRows.data(),257,3};
Box box{&boxType},nestedBox{&boxType};Arguments arguments{&arrayType},nestedArguments{&arrayType};
Event event{&eventType,nullptr,&mixed,0,&box,&arguments},nestedEvent{&eventType,nullptr,&empty,0,&nestedBox,&nestedArguments};
thread_local std::uint32_t expectedBits{};
thread_local Event* expectedEvent{};
std::uint32_t workerStatus{};
unsigned char* postBlock{};
CppUnwind::~CppUnwind(){++cppUnwinds;}
void Check(bool value,const char* expression,int line)
{++checks;if(!value){++failures;std::fprintf(stderr,"FAIL Single %d %s\n",line,expression);}}
#define CHECK(value) Check(!!(value),#value,__LINE__)
bool Is(const char* name){return std::strcmp(mode,name)==0;}
void CompareMachine(const RizEventMidFrameV1& expected,const RizEventMidFrameV1& actual)
{
    for(unsigned index=0;index!=16;++index)CHECK(expected.gpr[index]==actual.gpr[index]);
    for(unsigned index=0;index!=16;++index)CHECK(std::memcmp(expected.xmm+index,actual.xmm+index,sizeof(M128A))==0);
    CHECK(expected.rflags==actual.rflags&&expected.mxcsr==actual.mxcsr&&expected.fpcsr==actual.fpcsr);++machineStateComparisons;
}
void CheckPostReplay()
{
    if(!Is("machine-state")||!lastPostValid)return;
    auto expected=lastPostMachine;const auto prior=static_cast<std::uint32_t>(expected.gpr[RizEventMidRsi]);
    const auto next=prior+1u;expected.gpr[RizEventMidRsi]=expected.gpr[RizEventMidRcx]=next;
    unsigned parity{};for(unsigned bits=next&0xffu;bits;bits>>=1)parity^=bits&1u;
    expected.rflags&=~0x8d4ull;
    expected.rflags|=(!parity?4ull:0ull)|((prior&15u)==15u?0x10ull:0ull)|(!next?0x40ull:0ull)
        |(next&0x80000000u?0x80ull:0ull)|(prior==0x7fffffffu?0x800ull:0ull);
    CompareMachine(expected,RtSingleFixture_LoopState);lastPostValid=false;
}
void CheckPreState()
{
    if(!Is("machine-state"))return;
    auto actual=RtSingleFixture_TargetState;actual.gpr[RizEventMidRsp]+=8;CompareMachine(lastPreMachine,actual);
}
bool ObserverFaultMode(){return Is("observer-cpp")||Is("observer-seh")||Is("observer-nonzero")||Is("mid-cpp")||Is("mid-seh")||Is("mid-nonzero")
    ||Is("returned-cpp")||Is("returned-seh")||Is("returned-nonzero");}
bool CppMode(){return Is("original-cpp")||Is("original-cpp-1")||Is("original-cpp-2")||Is("before-call-cpp")||Is("abnormal-cpp");}
bool SehMode(){return Is("original-seh")||Is("original-seh-1")||Is("original-seh-2")||Is("before-call-seh")||Is("target-seh")||Is("target-seh-2")
    ||Is("abnormal-seh")||Is("abnormal-nonzero")||Is("null-list");}
bool MissingMode(){return Is("missing-post-0")||Is("missing-post-1")||Is("missing-post-2")||Is("child-overflow");}
RizTweenSingleEventInfoV1 Info()
{RizTweenSingleEventInfoV1 info{1,sizeof(info)};CHECK(RizTweenSingleEventContext_GetInfo(&info)==0);return info;}
RizTweenRetirementInfoV1 Retirement()
{RizTweenRetirementInfoV1 info{1,sizeof(info)};CHECK(RizTweenRetirement_GetInfo(&info)==0);return info;}
void Invoke(Event& self,std::uint32_t bits)
{
    expectedBits=bits;expectedEvent=&self;float value{};std::memcpy(&value,&bits,4);
    const auto entry=RizOperationContext_GetEntry(RizOperationSingle1Void,31);
    reinterpret_cast<void(__cdecl*)(void*,float,const void*)>(entry)(&self,value,&parentMethod);
}
void ActualOriginalFailure(unsigned site)
{
    const unsigned failedSite=Is("original-cpp-1")||Is("original-seh-1")?1u:Is("original-cpp-2")||Is("original-seh-2")?2u:0u;
    if(site!=failedSite)return;
    if(CppMode()&&!Is("before-call-cpp")){CppUnwind unwind;throw CppPayload{0x1234aabbccdd7788ull};}
    if(SehMode()&&!Is("before-call-seh")&&!Is("target-seh")&&!Is("target-seh-2")&&!Is("null-list")){
        const ULONG_PTR payload[]{0x1234aabbccdd7788ull,site};RaiseException(OriginalSeh,0,2,payload);
    }
}
void __cdecl TypedOriginal(void* self,float value,const void* method)
{
    ++listeners;std::uint32_t bits{};std::memcpy(&bits,&value,4);
    CheckPreState();
    CHECK(self==&typed&&method==&singleMethod&&bits==expectedBits);
    CHECK(_ReturnAddress()==&RtSingleFixture_Return0);ActualOriginalFailure(0);
}
void __cdecl VoidOriginal(void* self,const void* method)
{++listeners;CheckPreState();CHECK(self==&plain&&method==nullptr);CHECK(_ReturnAddress()==&RtSingleFixture_Return1);ActualOriginalFailure(1);}
void __cdecl ArrayOriginal(void* self,Arguments* input,const void* method)
{++listeners;CheckPreState();CHECK(expectedEvent&&self==&array&&method==&arrayMethod&&input==expectedEvent->arguments&&input->length==1
    &&input->value==expectedEvent->box&&input->value->bits==expectedBits);CHECK(_ReturnAddress()==&RtSingleFixture_Return2);ActualOriginalFailure(2);}
std::uint32_t __cdecl Notify(const RizTweenSingleEventFrameV1*,void*);
void Attach()
{const RizTweenSingleEventObserverV1 observer{1,sizeof(observer),Notify,nullptr};CHECK(RizTweenSingleEventContext_Attach(1,&observer)==0);}
std::uintptr_t __cdecl OperationBefore(const RizOperationFrameV1* frame,void*)
{
    ++nativeBefore;CHECK(frame&&frame->sequence&&frame->kind==RizOperationSingle1Void&&operationDepth<actualEntries.size());
    if(operationDepth<actualEntries.size())actualEntries[operationDepth++]=*frame;
    if(Is("late-attach")&&!attachedLate){Attach();attachedLate=true;}
    return frame->sequence+0x1000;
}
void __cdecl OperationAfter(const RizOperationFrameV1* frame,std::uintptr_t token,std::uint32_t flags,void*)
{
    ++nativeAfter;CHECK(operationDepth&&actualEntries[operationDepth-1].sequence==frame->sequence&&token==frame->sequence+0x1000);
    if(Is("machine-state")){CompareMachine(RtSingleFixture_MidBeforeState,RtSingleFixture_MidAfterState);CheckPostReplay();}
    CHECK(flags==RizOperationReturned||flags==RizOperationAbnormal);if(operationDepth)--operationDepth;
}
DWORD WINAPI WorkerRead(void*)
{
    RizTweenSingleEventFrameV1 copy{1,sizeof(copy)};RizEventMidFrameV1 machine{1,sizeof(machine)};
    workerStatus=RizTweenSingleEventContext_ReadCurrent(saved,&copy,&machine);return 0;
}
void DirectInvoke(Event& self,std::uint32_t bits)
{
    const auto priorBits=expectedBits;auto* priorEvent=expectedEvent;expectedBits=bits;expectedEvent=&self;
    float value{};std::memcpy(&value,&bits,4);RtSingleFixture_Original(&self,value,&parentMethod);
    expectedBits=priorBits;expectedEvent=priorEvent;
}
DWORD WINAPI WorkerInvoke(void*)
{if(Is("worker-owned"))Invoke(nestedEvent,0x80000000u);else DirectInvoke(nestedEvent,0x80000000u);return 0;}
void MachineContext(CONTEXT& context,const RizEventMidFrameV1& frame)
{
    context={};context.ContextFlags=CONTEXT_ALL;
    context.Rax=frame.gpr[0];context.Rcx=frame.gpr[1];context.Rdx=frame.gpr[2];context.Rbx=frame.gpr[3];
    context.Rsp=frame.gpr[4];context.Rbp=frame.gpr[5];context.Rsi=frame.gpr[6];context.Rdi=frame.gpr[7];
    context.R8=frame.gpr[8];context.R9=frame.gpr[9];context.R10=frame.gpr[10];context.R11=frame.gpr[11];
    context.R12=frame.gpr[12];context.R13=frame.gpr[13];context.R14=frame.gpr[14];context.R15=frame.gpr[15];
    context.EFlags=static_cast<DWORD>(frame.rflags);context.MxCsr=frame.mxcsr;
    std::memcpy(&context.Xmm0,frame.xmm,sizeof(frame.xmm));
}
void CheckHelperUnwind(const RizEventMidFrameV1& machine,unsigned stage)
{
    const void* entries[]{RizTweenSingleEventContext_GetMidEntry(),RizTweenSingleEventContext_GetPreEntry(),RizTweenSingleEventContext_GetPostEntry()};
    const void* calls[]{&RizTweenSingleEventContext_MidCall,&RizTweenSingleEventContext_PreCall,&RizTweenSingleEventContext_PostCall};
    const void* epilogs[]{&RizTweenSingleEventContext_MidEpilog,&RizTweenSingleEventContext_PreEpilog,&RizTweenSingleEventContext_PostEpilog};
    CHECK(stage<3);if(stage>=3)return;
    DWORD64 imageBase{};auto* function=RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(entries[stage]),&imageBase,nullptr);
    CHECK(function&&imageBase+function->BeginAddress==reinterpret_cast<DWORD64>(entries[stage]));if(!function)return;
    const auto begin=imageBase+function->BeginAddress,epilog=reinterpret_cast<DWORD64>(epilogs[stage]);
    const unsigned char prefix[]{0x41,0x55,0x9c,0x48,0x81,0xec,0xe8,0x01,0,0,0x4c,0x8d,0xac,0x24,0x80,0,0,0};
    const unsigned char restore[]{0x49,0x8d,0xa5,0x70,0x01,0,0,0x41,0x5d};
    CHECK(std::memcmp(reinterpret_cast<const void*>(begin),prefix,sizeof(prefix))==0
        &&std::memcmp(reinterpret_cast<const void*>(epilog),restore,sizeof(restore))==0);
    const auto* unwind=reinterpret_cast<const unsigned char*>(imageBase+function->UnwindData);
    CHECK((unwind[0]&7)==1&&unwind[3]==0x8d);
    const auto entry=machine.entryRsp,body=entry-0x1f8;
    struct Boundary{DWORD64 pc,rsp,r13;};
    const Boundary boundaries[]{
        {begin,entry,machine.gpr[13]},{begin+2,entry-8,machine.gpr[13]},
        {begin+3,entry-16,machine.gpr[13]},{begin+10,body,machine.gpr[13]},
        {begin+18,body,body+0x80},{reinterpret_cast<DWORD64>(calls[stage]),body,body+0x80},
        {reinterpret_cast<DWORD64>(calls[stage])+5,body,body+0x80},
        {epilog,body,body+0x80},{epilog+7,entry-8,body+0x80},{epilog+9,entry,machine.gpr[13]}};
    for(const auto& boundary:boundaries){
        CONTEXT context{};MachineContext(context,machine);context.Rip=boundary.pc;context.Rsp=boundary.rsp;context.R13=boundary.r13;
        void* handler{};DWORD64 establisher{};
        RtlVirtualUnwind(UNW_FLAG_NHANDLER,imageBase,context.Rip,function,&context,&handler,&establisher,nullptr);
        CHECK(context.Rip==machine.returnPc&&context.Rsp==machine.gpr[4]&&context.R13==machine.gpr[13]);
        CHECK(context.Rbx==machine.gpr[3]&&context.Rbp==machine.gpr[5]&&context.Rsi==machine.gpr[6]&&context.Rdi==machine.gpr[7]
            &&context.R12==machine.gpr[12]&&context.R14==machine.gpr[14]&&context.R15==machine.gpr[15]);
        CHECK(std::memcmp(&context.Xmm6,machine.xmm+6,10*sizeof(M128A))==0);++helperUnwinds;
    }
}
std::uint32_t __cdecl Notify(const RizTweenSingleEventFrameV1* actual,void*)
{
    ++callbackDepth;if(callbackDepth>maximumCallbackDepth)maximumCallbackDepth=callbackDepth;
    RizTweenSingleEventFrameV1 frame{1,sizeof(frame)};RizEventMidFrameV1 machine{1,sizeof(machine)};
    CHECK(RizTweenSingleEventContext_ReadCurrent(actual,&frame,&machine)==0);
    CHECK(actual&&operationDepth&&frame.parentSequence==actualEntries[operationDepth-1].sequence
        &&frame.event==actualEntries[operationDepth-1].self&&frame.parentMethod==actualEntries[operationDepth-1].method
        &&frame.parentCaller==actualEntries[operationDepth-1].caller&&frame.parentManagedToken==frame.parentSequence+0x1000
        &&frame.valueBits==static_cast<std::uint32_t>(reinterpret_cast<std::uintptr_t>(actualEntries[operationDepth-1].arg0)));
    CHECK(frame.epoch==1&&frame.parentEpoch==1&&frame.threadId==GetCurrentThreadId());
    if(frame.machineFrame)CheckHelperUnwind(machine,frame.stage);
    if(frame.stage==RizTweenSingleEventMid){
        ++mids;CHECK(frame.site==3&&!frame.childSequence&&frame.list==static_cast<const Event*>(frame.event)->list&&frame.index==0);
        CHECK(frame.sourceReturnPc==&RtSingleFixture_MidReturn&&frame.machineFrame==&machine
            &&frame.sourceRsp==machine.entryRsp+8&&machine.gpr[RizEventMidR15]==reinterpret_cast<std::uint64_t>(frame.list));
    }else{
        const auto* list=static_cast<const List*>(frame.list);
        CHECK(frame.site<3&&frame.childSequence&&list&&frame.index<list->count
            &&frame.invokable==list->items[static_cast<unsigned>(frame.index)]);
        const void* returns[]{&RtSingleFixture_Return0,&RtSingleFixture_Return1,&RtSingleFixture_Return2};
        CHECK(frame.sourceReturnPc==returns[frame.site]);
        if(frame.stage==RizTweenSingleEventBefore){
            CheckPostReplay();lastPreMachine=machine;
            ++befores;CHECK(frame.machineFrame==&machine&&frame.argRcx==frame.invokable);
            const void* targets[]{reinterpret_cast<const void*>(&RtSingleFixture_TypedTarget),reinterpret_cast<const void*>(&RtSingleFixture_VoidTarget),reinterpret_cast<const void*>(&RtSingleFixture_ArrayTarget)};
            CHECK(frame.actualTarget==targets[frame.site]);
            if(frame.site==0)CHECK(frame.argR8==&singleMethod&&static_cast<std::uint32_t>(machine.xmm[1].low)==frame.valueBits);
            if(frame.site==1)CHECK(!frame.argRdx);
            if(frame.site==2){const auto* self=static_cast<const Event*>(frame.event);
                CHECK(frame.argR8==&arrayMethod&&frame.argRdx==self->arguments&&self->box->bits==frame.valueBits
                    &&machine.gpr[RizEventMidR14]==reinterpret_cast<std::uintptr_t>(frame.invokable));}
        }else if(frame.stage==RizTweenSingleEventReturned){++afters;CHECK(frame.machineFrame==&machine);lastPostMachine=machine;lastPostValid=true;}
        else {++abnormal;CHECK(frame.stage==RizTweenSingleEventAbnormal&&!frame.machineFrame&&(frame.flags&RizTweenSingleEventMissingPost));
            CHECK(!!(frame.flags&RizTweenSingleEventParentAbnormal)==!MissingMode());
            CHECK(!!(frame.flags&RizTweenSingleEventTargetReadFaulted)==(Is("target-seh")||Is("target-seh-2")));}
    }
    saved=actual;
    if((Is("nested")||Is("nested-same"))&&frame.stage==RizTweenSingleEventBefore&&!nestedDone){
        nestedDone=true;const auto priorBits=expectedBits;auto* priorEvent=expectedEvent;
        Invoke(Is("nested-same")?event:nestedEvent,0x80000000u);expectedBits=priorBits;expectedEvent=priorEvent;
        RizTweenSingleEventFrameV1 restored{1,sizeof(restored)};RizEventMidFrameV1 restoredMachine{1,sizeof(restoredMachine)};
        CHECK(RizTweenSingleEventContext_ReadCurrent(actual,&restored,&restoredMachine)==0&&restored.parentSequence==frame.parentSequence);
    }
    if(Is("parent-overflow")&&frame.stage==RizTweenSingleEventMid&&operationDepth<33){
        const auto priorBits=expectedBits;auto* priorEvent=expectedEvent;Invoke(nestedEvent,0x80000000u);
        expectedBits=priorBits;expectedEvent=priorEvent;
    }
    if(Is("foreign")&&frame.stage==RizTweenSingleEventMid){
        const auto priorCallbacks=mids+befores+afters+abnormal;DirectInvoke(nestedEvent,0x80000000u);
        CHECK(mids+befores+afters+abnormal==priorCallbacks);
    }
    if((Is("worker-foreign")||Is("worker-owned"))&&frame.event==&event&&frame.stage==RizTweenSingleEventMid){
        const auto priorBits=expectedBits;auto* priorEvent=expectedEvent;
        const auto worker=CreateThread(nullptr,0,WorkerInvoke,nullptr,0,nullptr);
        CHECK(worker&&WaitForSingleObject(worker,10000)==WAIT_OBJECT_0);if(worker)CloseHandle(worker);
        CHECK(expectedBits==priorBits&&expectedEvent==priorEvent);
        RizTweenSingleEventFrameV1 restored{1,sizeof(restored)};RizEventMidFrameV1 restoredMachine{1,sizeof(restoredMachine)};
        CHECK(RizTweenSingleEventContext_ReadCurrent(actual,&restored,&restoredMachine)==0&&restored.parentSequence==frame.parentSequence);
    }
    if(Is("worker-read")&&frame.stage==RizTweenSingleEventMid){
        const auto worker=CreateThread(nullptr,0,WorkerRead,nullptr,0,nullptr);
        CHECK(worker&&WaitForSingleObject(worker,10000)==WAIT_OBJECT_0&&workerStatus==3);CloseHandle(worker);++workerRejects;
    }
    if(Is("alias-read")&&frame.stage==RizTweenSingleEventMid)
        CHECK(RizTweenSingleEventContext_ReadCurrent(actual,const_cast<RizTweenSingleEventFrameV1*>(actual),&machine)==1);
    if(Is("copied-read")&&frame.stage==RizTweenSingleEventMid){
        const auto copied=frame;RizTweenSingleEventFrameV1 output{1,sizeof(output)};RizEventMidFrameV1 copiedMachine{1,sizeof(copiedMachine)};
        CHECK(RizTweenSingleEventContext_ReadCurrent(&copied,&output,&copiedMachine)==3);
    }
    const bool failMid=(Is("mid-cpp")||Is("mid-seh")||Is("mid-nonzero"))&&frame.stage==RizTweenSingleEventMid;
    const bool failBefore=(Is("observer-cpp")||Is("observer-seh")||Is("observer-nonzero"))&&frame.stage==RizTweenSingleEventBefore;
    const bool failReturned=(Is("returned-cpp")||Is("returned-seh")||Is("returned-nonzero"))&&frame.stage==RizTweenSingleEventReturned;
    const bool failAbnormal=(Is("abnormal-cpp")||Is("abnormal-seh")||Is("abnormal-nonzero"))&&frame.stage==RizTweenSingleEventAbnormal;
    --callbackDepth;
    if(armed&&(failMid||failBefore||failReturned||failAbnormal)){
        armed=false;
        if(Is("observer-cpp")||Is("mid-cpp")||Is("returned-cpp")||Is("abnormal-cpp")){CppUnwind unwind;throw CppPayload{0x99887766};}
        if(Is("observer-seh")||Is("mid-seh")||Is("returned-seh")||Is("abnormal-seh"))RaiseException(ObserverSeh,0,0,nullptr);
        return 27;
    }
    return 0;
}
void Setup()
{
    manyRows.fill(&typed);
    if(Is("no-mid"))RtSingleFixture_MidPolicy=1;
    if(Is("duplicate-mid"))RtSingleFixture_MidPolicy=2;
    if(Is("foreign")||Is("worker-foreign")||Is("worker-owned"))nestedEvent.list=&mixed;
    if(Is("parent-overflow"))event.list=&empty;
    if(Is("child-overflow"))event.list=&many;
    if(Is("null-list"))event.list=nullptr;
    RtSingleFixture_CppTargets[0]=reinterpret_cast<void*>(&TypedOriginal);RtSingleFixture_CppTargets[1]=reinterpret_cast<void*>(&VoidOriginal);
    RtSingleFixture_CppTargets[2]=reinterpret_cast<void*>(&ArrayOriginal);
    invokableClass.singleTarget=reinterpret_cast<void*>(&RtSingleFixture_TypedTarget);invokableClass.singleMethod=&singleMethod;
    invokableClass.arrayTarget=reinterpret_cast<void*>(&RtSingleFixture_ArrayTarget);invokableClass.arrayMethod=&arrayMethod;
    const RizOperationBootstrapConfigV1 parent{1,sizeof(parent),reinterpret_cast<void*>(0x100000),reinterpret_cast<void*>(&RtSingleFixture_Original)};
    CHECK(RizOperationBootstrap_Configure(RizOperationSingle1Void,31,&parent)==0);
    RizOperationBootstrapInfoV1 info{1,sizeof(info)};CHECK(RizOperationBootstrap_GetInfo(RizOperationSingle1Void,31,&info)==0&&info.epoch==1);
    const RizOperationBootstrapObserverV1 observer{1,sizeof(observer),OperationBefore,OperationAfter,nullptr};
    CHECK(RizOperationBootstrap_Attach(RizOperationSingle1Void,31,1,&observer)==0);
    const auto imageBase=reinterpret_cast<std::uintptr_t>(GetModuleHandleW(nullptr));
    for(unsigned i=0;i!=32&&!postBlock;++i)postBlock=static_cast<unsigned char*>(VirtualAlloc(
        reinterpret_cast<void*>(imageBase+0x4000000ull+i*0x10000ull),4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    CHECK(postBlock!=nullptr);if(!postBlock)throw std::runtime_error("No owned near post block.");
    DWORD64 actualBase{};const auto* actualParent=RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(&RtSingleFixture_Original),&actualBase,nullptr);
    CHECK(actualParent&&actualBase==imageBase);if(!actualParent)throw std::runtime_error("No actual owned original RF.");
    CHECK(actualParent->EndAddress==reinterpret_cast<std::uintptr_t>(&RtSingleFixture_PrimaryEnd)-imageBase);
    auto* functions=reinterpret_cast<RUNTIME_FUNCTION*>(postBlock+0x380);
    auto Relative=[](unsigned char* at,unsigned char opcode,const void* target){
        const auto delta=reinterpret_cast<std::intptr_t>(target)-reinterpret_cast<std::intptr_t>(at+5);
        CHECK(delta>=(std::numeric_limits<std::int32_t>::min)()&&delta<=(std::numeric_limits<std::int32_t>::max)());
        const auto displacement=static_cast<std::int32_t>(delta);at[0]=opcode;std::memcpy(at+1,&displacement,4);
    };
    // This exact five-register chain is required by the real Single CHAIN0.
    // The owned continuation executes these saves in the same stack slots.
    unsigned char extraUnwind[]{0x21,25,10,0,25,0xe4,7,0,20,0x74,15,0,15,0x34,13,0,10,0xc4,8,0,5,0x64,14,0};
    for(unsigned site=0;site!=3;++site){
        auto* stub=postBlock+site*0x100;auto* unwind=stub+0x40;
        Relative(stub,0xe8,RizTweenSingleEventContext_GetPostEntry());
        const unsigned char replay[]{0xff,0xc6,0x8b,0xce};std::memcpy(stub+5,replay,4);
        // Either branch returns to the loop without changing flags/registers.
        // An external E9 looks like a completed epilog to RtlVirtualUnwind
        // because its target lies outside the chained primary RF.
        for(unsigned jump=9;jump!=21;jump+=6){
            const auto delta=reinterpret_cast<std::intptr_t>(&RtSingleFixture_Loop)-reinterpret_cast<std::intptr_t>(stub+jump+6);
            CHECK(delta>=(std::numeric_limits<std::int32_t>::min)()&&delta<=(std::numeric_limits<std::int32_t>::max)());
            const auto displacement=static_cast<std::int32_t>(delta);
            stub[jump]=0x0f;stub[jump+1]=jump==9?0x84:0x85;std::memcpy(stub+jump+2,&displacement,4);
        }
        std::memcpy(unwind,extraUnwind,sizeof(extraUnwind));unwind[1]=0;
        for(unsigned code=4;code!=24;code+=4)unwind[code]=0;
        std::memcpy(unwind+24,actualParent,sizeof(*actualParent));
        const auto codeRva=reinterpret_cast<std::uintptr_t>(stub)-imageBase;
        const auto unwindRva=reinterpret_cast<std::uintptr_t>(unwind)-imageBase;
        CHECK(codeRva<MAXDWORD-21&&unwindRva<MAXDWORD);
        functions[site]={static_cast<DWORD>(codeRva),static_cast<DWORD>(codeRva+21),static_cast<DWORD>(unwindRva)};
        unsigned char* returns[]{reinterpret_cast<unsigned char*>(&RtSingleFixture_Return0),
            reinterpret_cast<unsigned char*>(&RtSingleFixture_Return1),reinterpret_cast<unsigned char*>(&RtSingleFixture_Return2)};
        DWORD savedProtection{};CHECK(VirtualProtect(returns[site],9,PAGE_EXECUTE_READWRITE,&savedProtection));
        const bool skip=Is("child-overflow")||(site==0&&Is("missing-post-0"))||(site==1&&Is("missing-post-1"))||(site==2&&Is("missing-post-2"));
        Relative(returns[site],0xe9,stub+(skip?5:0));std::memset(returns[site]+5,0x90,4);
        DWORD ignored{};CHECK(VirtualProtect(returns[site],9,savedProtection,&ignored));
        CHECK(FlushInstructionCache(GetCurrentProcess(),returns[site],9));
    }
    DWORD previous{};CHECK(VirtualProtect(postBlock,4096,PAGE_EXECUTE_READ,&previous));
    CHECK(FlushInstructionCache(GetCurrentProcess(),postBlock,4096));CHECK(RtlAddFunctionTable(functions,3,imageBase));
    DWORD64 continuationBase{};const auto* registered=RtlLookupFunctionEntry(
        reinterpret_cast<DWORD64>(&RtSingleFixture_Continuation),&continuationBase,nullptr);
    CHECK(registered&&continuationBase==imageBase&&registered->BeginAddress==
        reinterpret_cast<DWORD64>(&RtSingleFixture_Continuation)-imageBase&&registered->EndAddress==
        reinterpret_cast<DWORD64>(&RtSingleFixture_ContinuationEnd)-imageBase);
    const RizTweenSingleEventConfigV1 config{1,sizeof(config),1,&RtSingleFixture_MidReturn,
        {&RtSingleFixture_Return0,&RtSingleFixture_Return1,&RtSingleFixture_Return2},
        {postBlock+5,postBlock+0x105,postBlock+0x205},reinterpret_cast<void*>(&RtSingleFixture_VoidTarget)};
    CHECK(RizTweenSingleEventConfigure(&config)==0&&RizTweenSingleEventConfigure(&config)==2);
    if(!Is("late-attach"))Attach();
}
LONG OriginalException(EXCEPTION_POINTERS* exception)
{
    const auto* record=exception->ExceptionRecord;
    const auto expected=Is("target-seh")||Is("target-seh-2")||Is("null-list")?EXCEPTION_ACCESS_VIOLATION:OriginalSeh;
    CHECK(record->ExceptionCode==expected);if(record->ExceptionCode!=expected)return EXCEPTION_CONTINUE_SEARCH;
    if(expected==OriginalSeh&&!Is("before-call-seh")){
        const unsigned site=Is("original-seh-1")?1u:Is("original-seh-2")?2u:0u;
        CHECK(record->NumberParameters==2&&record->ExceptionInformation[0]==0x1234aabbccdd7788ull&&record->ExceptionInformation[1]==site);
    }
    ++caughtOriginalExceptions;return EXCEPTION_EXECUTE_HANDLER;
}
void InvokeSeh(Event* self)
{
    __try{Invoke(*self,0x7fc01234u);CHECK(false);}
    __except(OriginalException(GetExceptionInformation())){CHECK(true);}
}
void CheckUnwind()
{
    const std::uintptr_t stubs[]{reinterpret_cast<std::uintptr_t>(postBlock),reinterpret_cast<std::uintptr_t>(postBlock+0x100),reinterpret_cast<std::uintptr_t>(postBlock+0x200)};
    for(const auto stub:stubs)for(const unsigned offset:{0u,5u,7u,9u,15u}){
        DWORD64 base{};auto* function=RtlLookupFunctionEntry(stub+offset,&base,nullptr);CHECK(function!=nullptr);if(!function)continue;
        alignas(16) std::array<DWORD64,32> stack{};
        stack[0x58/8]=0x123456789ull;stack[0x50/8]=0x1300130013001300ull;stack[0x48/8]=0x5500550055005500ull;
        stack[0x30/8]=0x1500150015001500ull;stack[0x38/8]=0x1400140014001400ull;stack[0x40/8]=0x1200120012001200ull;
        stack[0x68/8]=0x3300330033003300ull;stack[0x70/8]=0x6600660066006600ull;stack[0x78/8]=0x7700770077007700ull;
        std::memset(reinterpret_cast<unsigned char*>(stack.data())+0x20,0x6a,16);
        CONTEXT context{};context.ContextFlags=CONTEXT_ALL;context.Rip=stub+offset;context.Rsp=reinterpret_cast<DWORD64>(stack.data());
        void* handler{};DWORD64 establisher{};
        RtlVirtualUnwind(UNW_FLAG_NHANDLER,base,context.Rip,function,&context,&handler,&establisher,nullptr);
        if(context.Rip!=stack[0x58/8]||context.Rsp!=reinterpret_cast<DWORD64>(stack.data())+0x60)
            std::fprintf(stderr,"Single post unwind offset=%u RIP=%llx expected=%llx RSP=%llx expected=%llx\n",offset,
                context.Rip,stack[0x58/8],context.Rsp,reinterpret_cast<DWORD64>(stack.data())+0x60);
        CHECK(context.Rip==stack[0x58/8]&&context.Rsp==reinterpret_cast<DWORD64>(stack.data())+0x60
            &&context.Rbp==stack[0x50/8]&&context.R13==stack[0x48/8]&&context.R15==stack[0x30/8]
            &&context.R14==stack[0x38/8]&&context.R12==stack[0x40/8]&&context.Rbx==stack[0x68/8]
            &&context.Rsi==stack[0x70/8]&&context.Rdi==stack[0x78/8]);
        const auto* restored=reinterpret_cast<const unsigned char*>(&context.Xmm6);
        bool xmmRestored=true;for(unsigned byte=0;byte!=16;++byte)xmmRestored=xmmRestored&&restored[byte]==0x6a;
        CHECK(xmmRestored);++stubUnwinds;
    }
}
}
extern "C" bool RizTweenAdmissionFixture_Ready(){return true;}
extern "C" bool RizTweenAdmissionFixture_Validate(const RizTweenAdmissionConfigV1*){return false;}
extern "C" void __cdecl RtSingleFixture_OriginalBegin(){++originals;}
extern "C" void __cdecl RtSingleFixture_BeforeCalls(Event*)
{
    if(Is("before-call-cpp")){CppUnwind unwind;throw CppPayload{0x1234aabbccdd7788ull};}
    if(Is("before-call-seh"))RaiseException(OriginalSeh,0,0,nullptr);
}
extern "C" void __cdecl RtSingleFixture_Box(Event* self,std::uint32_t bits)
{self->box->bits=bits;self->arguments->value=self->box;}
int main(int argc,char** argv)
{
    if(argc!=2)return 2;mode=argv[1];
    const bool happy=Is("happy"),machineState=Is("machine-state"),late=Is("late-attach"),nested=Is("nested")||Is("nested-same");
    const bool cpp=CppMode(),seh=SehMode(),observerFault=ObserverFaultMode();
    const bool readFault=Is("worker-read")||Is("stale-read")||Is("alias-read")||Is("copied-read");
    const bool foreign=Is("foreign")||Is("worker-foreign"),workerOwned=Is("worker-owned");
    const bool overflow=Is("parent-overflow")||Is("child-overflow");
    const bool midFault=Is("no-mid")||Is("duplicate-mid");
    if(!(happy||machineState||late||nested||cpp||seh||observerFault||readFault||foreign||workerOwned||overflow||MissingMode()||midFault))return 2;
    Setup();CheckUnwind();
    if(happy){
        for(const auto bits:{0u,0x80000000u,0x7f800000u,0xff800000u,0x7fc01234u,0xffc0abcdu,1u,0x3e800000u}){
            event.list=&empty;Invoke(event,bits);event.list=&mixed;Invoke(event,bits);
        }
        CHECK(mids==16&&listeners==32&&befores==32&&afters==32&&originals==16);
    }else if(late){Invoke(event,0x3e800000u);CHECK(mids==0&&befores==0&&afters==0&&listeners==4);
        Invoke(event,0x80000000u);CHECK(mids==1&&befores==4&&afters==4&&listeners==8);}
    else if(cpp){try{Invoke(event,0x7fc01234u);CHECK(false);}catch(const CppPayload& error){CHECK(error.value==0x1234aabbccdd7788ull);++caughtOriginalExceptions;}}
    else if(seh){
        void* pages{};
        if(Is("target-seh")||Is("target-seh-2")){
            pages=VirtualAlloc(nullptr,8192,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE);CHECK(pages!=nullptr);
            const bool arrayFault=Is("target-seh-2");auto* method=static_cast<unsigned char*>(pages)+4096;
            *reinterpret_cast<const void**>(method)=arrayFault?&arrayMethod:&singleMethod;
            DWORD old{};CHECK(VirtualProtect(pages,4096,PAGE_NOACCESS,&old));
            if(arrayFault)array.type=reinterpret_cast<Class*>(method-0x180);else typed.type=reinterpret_cast<Class*>(method-0x1a0);
        }
        InvokeSeh(&event);if(pages){typed.type=array.type=&invokableClass;CHECK(VirtualFree(pages,0,MEM_RELEASE));}
    }else{Invoke(event,0x7fc01234u);if(observerFault){const auto callbacks=mids+befores+afters+abnormal;
            Invoke(event,0x80000000u);CHECK(listeners==8&&mids+befores+afters+abnormal==callbacks);}}
    if(Is("stale-read")){RizTweenSingleEventFrameV1 copy{1,sizeof(copy)};RizEventMidFrameV1 machine{1,sizeof(machine)};
        CHECK(RizTweenSingleEventContext_ReadCurrent(saved,&copy,&machine)==3);}
    const auto info=Info();const auto common=Retirement();
    CHECK(!operationDepth&&nativeBefore==nativeAfter&&info.parentsInFlight==0&&info.childrenInFlight==0
        &&info.parentsEntered==info.parentsExited&&info.childrenEntered==info.childrenExited&&common.reservations==0&&common.entered==common.exited);
    CHECK(info.unknown==static_cast<unsigned>(!(happy||machineState||late||nested||workerOwned))&&common.unknown==info.unknown);
    CHECK(machineStateComparisons==(machineState?9u:0u));
    CHECK(helperUnwinds==10*(mids+befores+afters)&&stubUnwinds==15);
    CHECK(caughtOriginalExceptions==static_cast<unsigned>(cpp||seh));
    if(nested)CHECK(originals==2&&mids==2&&listeners==(Is("nested-same")?8u:4u)&&maximumCallbackDepth==2);
    if(cpp||seh)CHECK(originals==1&&mids==static_cast<unsigned>(!Is("null-list"))&&info.retainedParents==1);
    if((cpp||seh)&&!Is("before-call-cpp")&&!Is("before-call-seh")&&!Is("null-list"))CHECK(abnormal==1&&info.missingPosts==1&&info.retainedChildren==1);
    if(Is("before-call-cpp")||Is("before-call-seh"))CHECK(befores==0&&abnormal==0&&listeners==0);
    if(observerFault)CHECK(info.callbackFaults==1&&info.retainedParents==1);
    if(Is("abnormal-cpp")||Is("abnormal-seh")||Is("abnormal-nonzero"))CHECK(info.callbackFaults==1&&info.retainedParents==1);
    if(foreign)CHECK(originals==2&&mids==1&&listeners==8&&befores==4&&afters==4&&info.parentsEntered==1&&info.retainedParents==1);
    if(workerOwned)CHECK(originals==2&&mids==2&&listeners==8&&befores==8&&afters==8&&info.parentsEntered==2&&!info.retainedParents);
    if(Is("parent-overflow"))CHECK(originals==33&&mids==32&&!listeners&&info.parentsEntered==32&&info.retainedParents==32&&info.overflow==1);
    if(MissingMode()&&!Is("child-overflow"))CHECK(originals==1&&listeners==4&&befores==4&&afters+abnormal==4
        &&abnormal==(Is("missing-post-0")?2u:1u)&&info.missingPosts==abnormal&&info.retainedChildren==abnormal);
    if(Is("child-overflow"))CHECK(originals==1&&listeners==257&&mids==1&&befores==256&&!afters&&abnormal==256
        &&info.retainedParents==1&&info.retainedChildren==256&&info.missingPosts==256&&info.overflow==1);
    if(midFault)CHECK(originals==1&&listeners==4&&mids==static_cast<unsigned>(Is("duplicate-mid"))
        &&befores==(Is("duplicate-mid")?4u:0u)&&afters==befores&&info.retainedParents==1&&!info.retainedChildren);
    const auto expectedCppUnwinds=Is("abnormal-cpp")?2u:CppMode()||Is("observer-cpp")||Is("mid-cpp")||Is("returned-cpp")?1u:0u;
    CHECK(cppUnwinds==expectedCppUnwinds);
    std::printf("{\"kind\":\"owned-tween-single-event-machine-fixture\",\"mode\":\"%s\",\"passed\":%s,\"checks\":%llu,\"failures\":%llu,\"originals\":%llu,\"listeners\":%llu,\"mids\":%llu,\"before\":%llu,\"after\":%llu,\"abnormal\":%llu,\"retainedParents\":%llu,\"retainedChildren\":%llu,\"unknown\":%u,\"missingPosts\":%llu,\"overflow\":%llu,\"helperSyntheticBoundaryUnwinds\":%llu,\"stubSyntheticBoundaryUnwinds\":%llu,\"caughtOriginalExceptions\":%llu,\"cppDestructors\":%llu,\"machineStateComparisons\":%llu,\"actualSingleEntry\":true,\"actualMachineMidAndCalls\":true,\"privateNativeParentBinding\":true,\"controlledPublication\":true,\"postStubBytes\":21,\"postStubConditionalJumps\":2,\"staticOwnedContinuationRF\":true,\"officialMetadataVerified\":false,\"productionHelperExecuted\":false,\"gameExecuted\":false}\n",
        mode,failures?"false":"true",checks,failures,originals,listeners,mids,befores,afters,abnormal,info.retainedParents,info.retainedChildren,info.unknown,
        info.missingPosts,info.overflow,helperUnwinds,stubUnwinds,caughtOriginalExceptions,cppUnwinds,machineStateComparisons);
    return failures?1:0;
}
