#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <atomic>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <vector>
#include "PlaybackSourceContext.h"
#include "../PlaybackLogicalHandoff.h"

// This POD fixture exercises the actual helper entries and their native finally
// receipts. Its stores model argument delivery, not a Unity engine or mixer.
namespace {
std::atomic<unsigned> checks{}, failures{};
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; std::fprintf(stderr,"line %d: %s\n",__LINE__,#x); } } while(0)
int playerClass, audioClass, otherClass;
struct Audio { void* klass; std::uint64_t value; void* method; std::uint32_t role, calls; };
struct Player { void* klass; void* monitor; Audio* source; };
Audio audioA{&audioClass,0x101,nullptr,0,0}, audioB{&audioClass,0x202,nullptr,0,0}, foreign{&audioClass,0x303,nullptr,0,0};
Player playerA{&playerClass,nullptr,&audioA}, playerB{&playerClass,nullptr,&audioB};
void* owner; void* initialWatch; void* gate; HMODULE library;
decltype(&RtPlaybackSourceCreateOwner) createOwner;
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceGetEntry) getEntry;
decltype(&RtPlaybackSourceSetArmed) arm;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadCurrent) readCurrent;
decltype(&RtPlaybackSourceVerifyCurrent) verify;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
decltype(&RtPlaybackSourceConfigurePlayerWatch) configureWatch;
decltype(&RtPlaybackSourceStagePlayerWatch) stageWatch;
decltype(&RtPlaybackSourceReadPlayerWatch) readWatch;
decltype(&RtPlaybackSourceActivateInitialPlayerWatch) activateWatch;
decltype(&RtPlaybackSourceRetirePlayerWatch) retireWatch;
using BindGate=void*(__cdecl*)(void*,RizLogicalCommit,void*);
using GateAction=std::uint32_t(__cdecl*)(void*,std::uint32_t,void*);
BindGate bindGate; GateAction gateAction;
using Void0=void(__cdecl*)(void*,void*);
using UInt64_1=void(__cdecl*)(void*,std::uint64_t,void*);
using Double1=void(__cdecl*)(void*,double,void*);
using Bool1=void(__cdecl*)(void*,unsigned char,void*);
const std::uint32_t kinds[]={RtSourceUInt64_1,RtSourceDouble1,RtSourceVoid0,RtSourceVoid0,RtSourceBool1,RtSourceBool1};
const std::uint32_t roles[]={RtSourceAudioPlayHelper,RtSourceAudioPlayDelayed,RtSourceAudioPlay,RtSourceAudioStop,RtSourceAudioStopBoolean,RtSourceAudioSetLoop};
const std::uint32_t slots[]={16,17,18,29,30,31};
void* entries[6]{};
std::vector<RtPlaybackSourceFrame> before, returned, abnormal;
bool throwObserver{}, throwOriginal{}, reenter{}, didReenter{}, withdrawInOriginal{};
HANDLE enteredEvent, continueEvent;
RtPlaybackSourceInfo lastOriginalHealth{};
void* expectedMethod=reinterpret_cast<void*>(std::uintptr_t{0xAABC1234});

template<class T> T Symbol(const char* name) {
    auto* value=GetProcAddress(library,name); CHECK(value!=nullptr); return reinterpret_cast<T>(value);
}
RtPlaybackSourceInfo Health() { RtPlaybackSourceInfo value{}; CHECK(readOwner(owner,&value)==1); return value; }
RtPlaybackPlayerWatchInfoV2 Watch(void* receipt) {
    RtPlaybackPlayerWatchInfoV2 value{2,sizeof(RtPlaybackPlayerWatchInfoV2)};
    CHECK(readWatch(owner,receipt,&value)==1); return value;
}
void NoScope() {
    RtPlaybackSourceFrame frame{}; std::memset(&frame,0xA5,sizeof(frame)); const auto expected=frame;
    for(unsigned i=0;i<6;++i) CHECK(readCurrent(kinds[i],slots[i],&frame)==0);
    CHECK(std::memcmp(&frame,&expected,sizeof(frame))==0);
}
__declspec(noinline) void Store(void* self,std::uint32_t role,std::uint64_t value,void* method) {
    auto* audio=static_cast<Audio*>(self); NoScope(); lastOriginalHealth=Health();
    ++audio->calls; CHECK(lastOriginalHealth.InFlight>0 || audio==&foreign || audio==&audioA);
    if(withdrawInOriginal) {
        const auto& actual=before.back(); RtPlaybackSourceFrame receipt{};
        CHECK(lastOriginalHealth.InFlight==1 && readReceipt(owner,actual.Sequence,actual.Token,&receipt)==0);
        CHECK(arm(owner,lastOriginalHealth.OwnerEpoch,0)==1);
        CHECK(retireWatch(owner,initialWatch)==0);
    }
    if(enteredEvent) { SetEvent(enteredEvent); CHECK(WaitForSingleObject(continueEvent,10000)==WAIT_OBJECT_0); }
    if(throwOriginal) RaiseException(0xE2420011,0,0,nullptr);
    audio->role=role; audio->value=value; audio->method=method;
}
__declspec(noinline) void __cdecl OriginalUInt64(void* s,std::uint64_t v,void* m) { Store(s,roles[0],v,m); }
__declspec(noinline) void __cdecl OriginalDouble(void* s,double v,void* m) { std::uint64_t b;std::memcpy(&b,&v,sizeof(b));Store(s,roles[1],b,m); }
__declspec(noinline) void __cdecl OriginalPlay(void* s,void* m) { Store(s,roles[2],0,m); }
__declspec(noinline) void __cdecl OriginalStop(void* s,void* m) { Store(s,roles[3],0,m); }
__declspec(noinline) void __cdecl OriginalStopBool(void* s,unsigned char v,void* m) { Store(s,roles[4],v,m); }
__declspec(noinline) void __cdecl OriginalLoop(void* s,unsigned char v,void* m) { Store(s,roles[5],v,m); }
__declspec(noinline) void __cdecl OriginalCtor(void* s,void* v,void*) { static_cast<Player*>(s)->source=static_cast<Audio*>(v); }
__declspec(noinline) void __cdecl DummyCtor(void*,void*,void*) {}
__declspec(noinline) void __cdecl DummyPause(void*,void*) {}
void* originals[]={reinterpret_cast<void*>(&OriginalUInt64),reinterpret_cast<void*>(&OriginalDouble),reinterpret_cast<void*>(&OriginalPlay),
    reinterpret_cast<void*>(&OriginalStop),reinterpret_cast<void*>(&OriginalStopBool),reinterpret_cast<void*>(&OriginalLoop)};
unsigned char targetMarkers[32];
unsigned selectedIndex;
std::uint64_t TestBits(unsigned i) { return i==0?0x8000000000000001ull:i==1?0x7FF8000000001234ull:i<4?0ull:0xFFull; }
int Invoke(unsigned i,Audio* self,std::uint64_t bits,void* method,std::uint32_t* code) {
    *code=0;
    __try {
        if(i==0) reinterpret_cast<UInt64_1>(entries[i])(self,bits,method);
        else if(i==1) {double value;std::memcpy(&value,&bits,sizeof(value));reinterpret_cast<Double1>(entries[i])(self,value,method);}
        else if(i<4) reinterpret_cast<Void0>(entries[i])(self,method);
        else reinterpret_cast<Bool1>(entries[i])(self,static_cast<unsigned char>(bits),method);
        return 1;
    } __except(EXCEPTION_EXECUTE_HANDLER) { *code=GetExceptionCode();return 2; }
}
void Observe(void*,const RtPlaybackSourceFrame* f) {
    RtPlaybackSourceFrame current{}; CHECK(readCurrent(f->Kind,f->Slot,&current)==1);
    CHECK(std::memcmp(&current,f,sizeof(current))==0 && verify(owner,f)==1);
    auto copy=*f;copy.ArgBits^=1;CHECK(verify(owner,&copy)==0);
    RtPlaybackSourceFrame receipt{};CHECK(readReceipt(owner,f->Sequence,f->Token,&receipt)==0);
    CHECK(f->Role>=13 && f->Role<=18 && f->Kind==kinds[f->Role-13]);
    if(f->Stage==RtSourceBefore) {
        before.push_back(*f);
        auto* self=reinterpret_cast<Audio*>(f->Self);
        CHECK(self==&audioA || self==&audioB);
        CHECK(f->InFlight>0 && f->SchedulingSequence>1);
        if(reenter && !didReenter) {
            didReenter=true;std::uint32_t code;const auto parent=f->Sequence;
            CHECK(Invoke(5,self,0x80,expectedMethod,&code)==1 && code==0);
            CHECK(before.back().ParentSequence==parent && before.back().Depth==f->Depth+1);
        }
        if(throwObserver) RaiseException(0xE2420012,0,0,nullptr);
    } else if(f->Stage==RtSourceOriginalReturned) returned.push_back(*f);
    else if(f->Stage==RtSourceAbnormal) abnormal.push_back(*f);
    else CHECK(false);
}
std::uint32_t __cdecl Commit(void*,void*,std::uint32_t,const RizLogicalSourceSnapshot* value,void*) {
    CHECK(value && value->owner==reinterpret_cast<std::uint64_t>(owner) && value->inFlight==0);return 0;
}
bool Load() {
#define LOAD(field,name) field=Symbol<decltype(field)>(name)
    LOAD(createOwner,"RtPlaybackSourceCreateOwner");LOAD(configure,"RtPlaybackSourceConfigure");LOAD(getEntry,"RtPlaybackSourceGetEntry");
    LOAD(arm,"RtPlaybackSourceSetArmed");LOAD(readOwner,"RtPlaybackSourceReadOwner");LOAD(readCurrent,"RtPlaybackSourceReadCurrent");
    LOAD(verify,"RtPlaybackSourceVerifyCurrent");LOAD(readReceipt,"RtPlaybackSourceReadReceipt");LOAD(releaseReceipt,"RtPlaybackSourceReleaseReceipt");
    LOAD(configureWatch,"RtPlaybackSourceConfigurePlayerWatch");LOAD(stageWatch,"RtPlaybackSourceStagePlayerWatch");
    LOAD(readWatch,"RtPlaybackSourceReadPlayerWatch");LOAD(activateWatch,"RtPlaybackSourceActivateInitialPlayerWatch");LOAD(retireWatch,"RtPlaybackSourceRetirePlayerWatch");
    LOAD(bindGate,"RtPlaybackSourceBindLogicalGate");LOAD(gateAction,"RtPlaybackSourceLogicalAction");
#undef LOAD
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackSourceVersion")()==4);
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackPlayerWatchVersion")()==2);
    static_assert(sizeof(RtPlaybackSourceFrame)==168 && sizeof(RtPlaybackSourceInfo)==136 && sizeof(RtPlaybackPlayerWatchInfoV2)==120);
    return failures==0;
}
void Setup() {
    owner=createOwner(Observe,&otherClass);CHECK(owner);
    CHECK(configure(owner,RtSourceRef1,0,RtSourcePlayerConstructor,reinterpret_cast<void*>(&DummyCtor),reinterpret_cast<void*>(&OriginalCtor))==1);
    CHECK(configure(owner,RtSourceVoid0,1,RtSourcePause,reinterpret_cast<void*>(&DummyPause),reinterpret_cast<void*>(&OriginalPlay))==1);
    for(unsigned i=0;i<6;++i) {
        CHECK(configure(owner,kinds[i],slots[i],roles[i],&targetMarkers[i],originals[i])==1);
        entries[i]=getEntry(kinds[i],slots[i]);CHECK(entries[i]);
    }
    auto health=Health();CHECK(health.ConfiguredSlots==8 && arm(owner,health.OwnerEpoch,1)==1);
    CHECK(configureWatch(owner,&playerClass,&audioClass)==1);
    void* wrong=nullptr;CHECK(stageWatch(owner,&playerA,&foreign,&wrong)==0 && !wrong);
    CHECK(stageWatch(owner,&playerA,&audioA,&initialWatch)==1 && initialWatch);
    CHECK(activateWatch(owner,initialWatch)==1);
    auto watch=Watch(initialWatch);CHECK(watch.Active==1 && watch.AudioSource==reinterpret_cast<std::uint64_t>(&audioA)
        && watch.AudioClass==reinterpret_cast<std::uint64_t>(&audioClass));
}
void ReleaseKnownRows() {
    for(const auto& frame:before) {
        RtPlaybackSourceFrame receipt{};CHECK(readReceipt(owner,frame.Sequence,frame.Token,&receipt)==1);
        CHECK(receipt.Sequence==frame.Sequence && (receipt.Flags&RtSourcePhysicalExitedFlag)!=0);
        CHECK(releaseReceipt(owner,frame.Sequence,frame.Token)==1);
        CHECK(readReceipt(owner,frame.Sequence,frame.Token,&receipt)==0);
    }
}
void CallChecked(unsigned i,std::uint64_t bits,void* method) {
    const auto health=Health();const auto b=before.size(),r=returned.size();const auto calls=audioA.calls;
    std::uint32_t code;CHECK(Invoke(i,&audioA,bits,method,&code)==1 && code==0);
    CHECK(audioA.calls==calls+1 && audioA.value==bits && audioA.method==method && audioA.role==roles[i]);
    CHECK(before.size()==b+1 && returned.size()==r+1 && before.back().ArgBits==bits && before.back().IncomingMethod==reinterpret_cast<std::uint64_t>(method));
    const auto current=Health();CHECK(current.Entered==health.Entered+1 && current.Exited==health.Exited+1 && current.InFlight==0
        && current.WriterSequence==health.WriterSequence && current.SourceSequence==health.SourceSequence
        && current.SchedulingSequence==health.SchedulingSequence+1 && current.Faults==0);
}
void Bits() {
    for(const auto value:{0ull,1ull,0x8000000000000000ull,0xFFFFFFFFFFFFFFFFull,0xFEDCBA9876543210ull})CallChecked(0,value,expectedMethod);
    for(const auto value:{0ull,0x8000000000000000ull,0x7FF8000000001234ull,0x3FF8000000000000ull})CallChecked(1,value,expectedMethod);
    CallChecked(2,0,nullptr);CallChecked(3,0,reinterpret_cast<void*>(std::uintptr_t{0x99}));
    for(unsigned i=4;i<6;++i)for(const auto value:{0ull,1ull,0x80ull,0xFFull})CallChecked(i,value,expectedMethod);
    ReleaseKnownRows();
}
struct WorkerArgs {Audio* self;unsigned loops;std::uint32_t code;unsigned firstIndex=0;};
DWORD WINAPI Worker(void* raw) {
    auto* args=static_cast<WorkerArgs*>(raw);
    for(unsigned i=0;i<args->loops;++i) {
        const auto index=(i+args->firstIndex)%6;
        CHECK(Invoke(index,args->self,TestBits(index),expectedMethod,&args->code)==1 && args->code==0);
    }
    return 0;
}
void Foreign() {
    const auto health=Health();WorkerArgs args{&foreign,320,0};auto thread=CreateThread(nullptr,0,Worker,&args,0,nullptr);
    CHECK(thread && WaitForSingleObject(thread,10000)==WAIT_OBJECT_0);CloseHandle(thread);
    CHECK(foreign.calls==320 && before.empty() && returned.empty() && abnormal.empty());
    const auto after=Health();CHECK(std::memcmp(&health,&after,sizeof(health))==0);
    CHECK(retireWatch(owner,initialWatch)==1);
}
void WorkerFault() {
    enteredEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);continueEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    WorkerArgs args{&audioA,1,0,selectedIndex};auto thread=CreateThread(nullptr,0,Worker,&args,0,nullptr);
    CHECK(thread && WaitForSingleObject(enteredEvent,10000)==WAIT_OBJECT_0);
    auto health=Health();CHECK(health.InFlight==1 && (health.Flags&RtSourceUnknown) && health.SchedulingSequence==2);
    CHECK(before.empty() && returned.empty() && retireWatch(owner,initialWatch)==0);
    SetEvent(continueEvent);CHECK(WaitForSingleObject(thread,10000)==WAIT_OBJECT_0);
    CloseHandle(thread);CloseHandle(enteredEvent);CloseHandle(continueEvent);enteredEvent=continueEvent=nullptr;
    health=Health();CHECK(audioA.calls==1 && health.Entered==health.Exited && health.InFlight==0 && (health.Flags&RtSourceUnknown));
    CHECK((lastOriginalHealth.Flags&RtSourceUnknown) && before.empty() && returned.empty() && retireWatch(owner,initialWatch)==0);
}
void Exceptional(const wchar_t* mode) {
    const auto initial=audioA.value;
    throwObserver=std::wcscmp(mode,L"audio-observer-seh")==0;
    throwOriginal=std::wcscmp(mode,L"audio-original-seh")==0;
    if(std::wcscmp(mode,L"audio-class")==0)audioA.klass=&otherClass;
    withdrawInOriginal=std::wcscmp(mode,L"audio-inflight")==0;
    reenter=std::wcscmp(mode,L"audio-reentry")==0;
    std::uint32_t code;const auto status=Invoke(selectedIndex,&audioA,TestBits(selectedIndex),expectedMethod,&code);
    CHECK(status==(throwOriginal?2:1) && code==(throwOriginal?0xE2420011u:0));
    const auto h=Health();CHECK(h.InFlight==0 && h.Entered==h.Exited && audioA.calls==(reenter?2u:1u));
    if(throwOriginal)CHECK(audioA.value==initial && abnormal.size()==1 && returned.empty());
    else CHECK(audioA.value==TestBits(selectedIndex) && returned.size()==(reenter?2u:1u));
    const bool unknown=throwObserver || audioA.klass!=&audioClass;
    CHECK(((h.Flags&RtSourceUnknown)!=0)==unknown);
    CHECK(((h.Flags&RtSourceWithdrawn)!=0)==withdrawInOriginal);
    CHECK(retireWatch(owner,initialWatch)==(unknown?0:1));
    ReleaseKnownRows();
}
void Transfer(const wchar_t* mode) {
    gate=bindGate(owner,Commit,&otherClass);CHECK(gate);
    CHECK(gateAction(gate,0,&otherClass)==0 && gateAction(gate,1,&otherClass)==0);
    void* next=nullptr;CHECK(stageWatch(owner,&playerB,&audioB,&next)==1 && next);
    auto staged=Watch(next);CHECK(staged.Phase==1 && !staged.Active);
    const bool pending=std::wcscmp(mode,L"audio-pending")==0;
    const bool opened=std::wcscmp(mode,L"audio-after-open")==0;
    std::uint32_t code;
    if(pending) {
        CHECK(Invoke(selectedIndex,&audioB,TestBits(selectedIndex),expectedMethod,&code)==1 && code==0);
        CHECK(gateAction(gate,4,&otherClass)==8 && gateAction(gate,4,&otherClass)==8);
        CHECK(Watch(next).Phase==1 && Watch(initialWatch).Phase==2 && before.size()==1 && (before.back().Flags&RtSourceDormant));return;
    }
    // A same-class foreign source must not stale the pending capture.
    CHECK(Invoke(0,&foreign,0xF123456789ABCDEFull,nullptr,&code)==1 && code==0);
    CHECK(gateAction(gate,4,&otherClass)==0 && Watch(next).Active==1 && Watch(initialWatch).Phase==3);
    CHECK(retireWatch(owner,initialWatch)==1);
    const auto h=Health();CHECK(Invoke(0,&audioA,0xFF,nullptr,&code)==1 && code==0);
    auto after=Health();CHECK(std::memcmp(&h,&after,sizeof(h))==0);
    if(opened)CHECK(Invoke(selectedIndex,&audioB,TestBits(selectedIndex),expectedMethod,&code)==1 && code==0);
    CHECK(gateAction(gate,3,&otherClass)==(opened?8u:0u));
    CHECK(Watch(next).Active==1);
}
void Capacity() {
    owner=createOwner(Observe,&otherClass);CHECK(owner);
    for(unsigned i=0;i<32;++i) {
        for(unsigned k=1;k<=7;++k)CHECK(getEntry(k,i)!=nullptr);
        CHECK(configure(owner,RtSourceVoid0,i,RtSourceAudioStop,&targetMarkers[i],reinterpret_cast<void*>(&OriginalStop))==1);
    }
    auto h=Health();CHECK(h.ConfiguredSlots==32 && configure(owner,RtSourceVoid0,32,RtSourceAudioStop,&targetMarkers[0],originals[0])==0);
    CHECK(getEntry(7,32)==nullptr && getEntry(8,0)==nullptr && getEntry(0,0)==nullptr && arm(owner,h.OwnerEpoch,1)==1);
    h=Health();CHECK(h.ConfiguredSlots==32 && h.Faults==0);
}
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=3)return 64;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);
    library=LoadLibraryW(argv[2]);CHECK(library);if(!library || !Load())return 65;
    wchar_t mode[80]{};wcsncpy_s(mode,argv[1],_TRUNCATE);
    const auto length=std::wcslen(mode);
    if(length>2 && mode[length-2]==L'-' && mode[length-1]>=L'0' && mode[length-1]<=L'5') {
        selectedIndex=static_cast<unsigned>(mode[length-1]-L'0');mode[length-2]=0;
    }
    if(std::wcscmp(mode,L"audio-capacity")==0)Capacity();
    else {
        Setup();
        if(std::wcscmp(mode,L"audio-bits")==0)Bits();
        else if(std::wcscmp(mode,L"audio-foreign")==0)Foreign();
        else if(std::wcscmp(mode,L"audio-worker")==0)WorkerFault();
        else if(std::wcscmp(mode,L"audio-pending")==0 || std::wcscmp(mode,L"audio-after-open")==0 || std::wcscmp(mode,L"audio-transfer")==0)Transfer(mode);
        else if(std::wcscmp(mode,L"audio-observer-seh")==0 || std::wcscmp(mode,L"audio-original-seh")==0 || std::wcscmp(mode,L"audio-class")==0
            || std::wcscmp(mode,L"audio-inflight")==0 || std::wcscmp(mode,L"audio-reentry")==0)Exceptional(mode);
        else CHECK(false);
        NoScope();
    }
    const auto h=Health();
    std::printf("{\"mode\":\"%ls\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"before\":%zu,\"returned\":%zu,\"abnormal\":%zu,\"entered\":%llu,\"exited\":%llu,\"scheduling\":%llu,\"unknown\":%s,\"sourceVersion\":4,\"playerWatchVersion\":2,\"watchBytes\":120,\"slotLimit\":32,\"directHelperOnly\":true,\"dobbyExecuted\":false,\"gameExecuted\":false}\n",
        argv[1],failures?"false":"true",checks.load(),failures.load(),before.size(),returned.size(),abnormal.size(),h.Entered,h.Exited,h.SchedulingSequence,(h.Flags&RtSourceUnknown)?"true":"false");
    return failures?1:0;
}
