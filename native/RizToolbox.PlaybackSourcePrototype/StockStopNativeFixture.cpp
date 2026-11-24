#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cwchar>
#include <vector>
#include "PlaybackSourceContext.h"

extern "C" {
void* StockStopEntry;
void* StockStopMethod;
void StockStopTarget();
void StockStopReplayCaller(void*);
void StockStopResumeCaller(void*);
extern const unsigned char StockStopReplayReturned, StockStopResumeReturned;
}

namespace {
unsigned checks{}, failures{}, originals{}, flagged{}, unclassified{}, abnormal{}, beforeRoots{};
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; std::fprintf(stderr,"line %d: %s\n",__LINE__,#x); } } while(0)
int playerClass, audioClass, otherClass, replayMethod, resumeMethod;
unsigned char targetMarkers[3];
struct Audio { void* klass; };
struct Player { void* klass; void* monitor; Audio* source; };
Audio audio{&audioClass}, foreign{&audioClass};
Player player{&playerClass,nullptr,&audio};
void* owner{};
void* watch{};
void* replayEntry{};
void* resumeEntry{};
void* ctorEntry{};
bool nestedEntered{};
HMODULE library{};
const wchar_t* mode{};
std::vector<RtPlaybackSourceFrame> befores;
decltype(&RtPlaybackSourceCreateOwner) createOwner;
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceGetEntry) getEntry;
decltype(&RtPlaybackSourceSetArmed) arm;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadReceipt) receipt;
decltype(&RtPlaybackSourceConfigurePlayerWatch) configureWatch;
decltype(&RtPlaybackSourceStagePlayerWatch) stageWatch;
decltype(&RtPlaybackSourceActivateInitialPlayerWatch) activateWatch;
decltype(&RtPlaybackSourceConfigureStockStop) configureStop;

template<class T> T Symbol(const char* name) {
    auto* value=GetProcAddress(library,name); CHECK(value); return reinterpret_cast<T>(value);
}
bool Is(const wchar_t* value) { return std::wcscmp(mode,value)==0; }
RtPlaybackSourceInfo Health() { RtPlaybackSourceInfo value{}; CHECK(readOwner(owner,&value)==1); return value; }
__declspec(noinline) void __cdecl StopOriginal(void*,void*) {
    ++originals;
    if(Is(L"stop-seh")) RaiseException(0xe2440011u,0,0,nullptr);
}
void IndirectStop(Audio* source) { reinterpret_cast<void(__cdecl*)(void*,void*)>(StockStopEntry)(source,nullptr); }
void Body(Player* self,bool resume) {
    if(Is(L"nested-root") && !nestedEntered) {
        nestedEntered=true;
        reinterpret_cast<void(__cdecl*)(void*,float,void*)>(resumeEntry)(self,.25f,&resumeMethod);
    }
    if(Is(L"late")) IndirectStop(self->source);
    if(Is(L"class")) self->source->klass=&otherClass;
    auto* source=Is(L"foreign")?&foreign:self->source;
    if(Is(L"wrong-caller")) IndirectStop(source);
    else if(resume) StockStopResumeCaller(source);
    else StockStopReplayCaller(source);
    if(Is(L"duplicate")) {
        if(resume) StockStopResumeCaller(source); else StockStopReplayCaller(source);
    }
}
__declspec(noinline) void __cdecl ReplayOriginal(void* self,float,void*) { Body(static_cast<Player*>(self),false); }
__declspec(noinline) void __cdecl ResumeOriginal(void* self,float,void*) { Body(static_cast<Player*>(self),true); }
__declspec(noinline) void __cdecl CtorOriginal(void* self,void* source,void*) { static_cast<Player*>(self)->source=static_cast<Audio*>(source); }
void Observe(void*,const RtPlaybackSourceFrame* frame) {
    CHECK(frame && frame->Version==4 && frame->Size==168);
    if(frame->Stage==RtSourceBefore) {
        befores.push_back(*frame);
        if(frame->Role==RtSourceAudioStop) {
            if(frame->Flags&RtSourceStockStopFlag) ++flagged; else ++unclassified;
            CHECK(frame->Depth==1 || frame->ParentSequence!=0);
        } else {
            ++beforeRoots;
            if(Is(L"callback")) StockStopReplayCaller(&audio);
        }
    } else if(frame->Stage==RtSourceAbnormal) ++abnormal;
}
int Invoke(void* entry,void* method,std::uint32_t* code) {
    *code=0;
    __try { reinterpret_cast<void(__cdecl*)(void*,float,void*)>(entry)(&player,.125f,method); return 1; }
    __except(EXCEPTION_EXECUTE_HANDLER) { *code=GetExceptionCode(); return 2; }
}
DWORD WINAPI Worker(void*) { std::uint32_t code{}; CHECK(Invoke(replayEntry,&replayMethod,&code)==1 && code==0); return 0; }
void Setup() {
#define LOAD(field,name) field=Symbol<decltype(field)>(name)
    LOAD(createOwner,"RtPlaybackSourceCreateOwner"); LOAD(configure,"RtPlaybackSourceConfigure");
    LOAD(getEntry,"RtPlaybackSourceGetEntry"); LOAD(arm,"RtPlaybackSourceSetArmed");
    LOAD(readOwner,"RtPlaybackSourceReadOwner"); LOAD(receipt,"RtPlaybackSourceReadReceipt");
    LOAD(configureWatch,"RtPlaybackSourceConfigurePlayerWatch"); LOAD(stageWatch,"RtPlaybackSourceStagePlayerWatch");
    LOAD(activateWatch,"RtPlaybackSourceActivateInitialPlayerWatch"); LOAD(configureStop,"RtPlaybackSourceConfigureStockStop");
#undef LOAD
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackStockStopVersion")()==1);
    owner=createOwner(Observe,&otherClass); CHECK(owner);
    CHECK(configure(owner,RtSourceRef1,0,RtSourcePlayerConstructor,&targetMarkers[0],reinterpret_cast<void*>(&CtorOriginal))==1);
    CHECK(configure(owner,RtSourceSingle1,1,RtSourceReplay,&targetMarkers[1],reinterpret_cast<void*>(&ReplayOriginal))==1);
    CHECK(configure(owner,RtSourceSingle1,2,RtSourceResume,&targetMarkers[2],reinterpret_cast<void*>(&ResumeOriginal))==1);
    CHECK(configure(owner,RtSourceVoid0,3,RtSourceAudioStop,reinterpret_cast<void*>(&StockStopTarget),reinterpret_cast<void*>(&StopOriginal))==1);
    ctorEntry=getEntry(RtSourceRef1,0); replayEntry=getEntry(RtSourceSingle1,1); resumeEntry=getEntry(RtSourceSingle1,2); StockStopEntry=getEntry(RtSourceVoid0,3);
    auto health=Health(); CHECK(replayEntry && resumeEntry && StockStopEntry && arm(owner,health.OwnerEpoch,1)==1);
    CHECK(configureWatch(owner,&playerClass,&audioClass)==1);
    auto* replayCaller=const_cast<unsigned char*>(&StockStopReplayReturned);
    auto* resumeCaller=const_cast<unsigned char*>(&StockStopResumeReturned);
    CHECK(configureStop(owner,nullptr,resumeCaller,&replayMethod,&resumeMethod)==0);
    CHECK(configureStop(owner,replayCaller,replayCaller,&replayMethod,&resumeMethod)==0);
    CHECK(configureStop(owner,replayCaller+1,resumeCaller,&replayMethod,&resumeMethod)==0);
    CHECK(configureStop(owner,replayCaller,resumeCaller,&replayMethod,&resumeMethod)==1);
    CHECK(configureStop(owner,replayCaller,resumeCaller,&replayMethod,&resumeMethod)==0);
    CHECK(stageWatch(owner,&player,&audio,&watch)==1 && watch && activateWatch(owner,watch)==1);
}
}

int wmain(int argc,wchar_t** argv) {
    if(argc!=3)return 64;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);
    mode=argv[1];library=LoadLibraryW(argv[2]);CHECK(library); if(!library)return 65;
    Setup();
    if(Is(L"stale-source") || Is(L"source-aba")) {
        auto ctor=reinterpret_cast<void(__cdecl*)(void*,void*,void*)>(ctorEntry);
        if(Is(L"source-aba")) ctor(&player,&foreign,nullptr);
        ctor(&player,&audio,nullptr);
    }
    if(Is(L"wrong-mi")) StockStopMethod=&otherClass;
    if(Is(L"worker")) {auto t=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);CHECK(t && WaitForSingleObject(t,10000)==WAIT_OBJECT_0);if(t)CloseHandle(t);}
    else if(Is(L"external")) IndirectStop(&audio);
    else {
        std::uint32_t code{};
        const bool resume=Is(L"resume");
        void* method=Is(L"null-parent-mi")?nullptr:Is(L"wrong-parent-mi")?&otherClass:resume?&resumeMethod:&replayMethod;
        const auto status=Invoke(resume?resumeEntry:replayEntry,method,&code);
        CHECK(status==(Is(L"stop-seh")?2:1) && code==(Is(L"stop-seh")?0xe2440011u:0));
    }
    auto health=Health();
    CHECK(health.InFlight==0 && health.Entered==health.Exited);
    const bool positive=Is(L"replay") || Is(L"resume") || Is(L"null-parent-mi") || Is(L"stop-seh");
    CHECK(flagged==((positive || Is(L"duplicate"))?1u:0u));
    CHECK(originals==((Is(L"duplicate") || Is(L"callback") || Is(L"late") || Is(L"nested-root"))?2u:1u));
    CHECK(unclassified==(Is(L"foreign") || Is(L"worker") || positive?0u:Is(L"callback") || Is(L"late") || Is(L"nested-root")?2u:1u));
    CHECK(((health.Flags&RtSourceUnknown)!=0)==(Is(L"worker") || Is(L"class")));
    if(positive) CHECK(health.SchedulingSequence==3 && health.Faults==0);
    if(Is(L"stop-seh"))CHECK(abnormal==2);
    for(const auto& before:befores) {
        RtPlaybackSourceFrame completed{};CHECK(receipt(owner,before.Sequence,before.Token,&completed)==1);
        CHECK((completed.Flags&RtSourcePhysicalExitedFlag)!=0);
        CHECK((completed.Flags&RtSourceStockStopFlag)==(before.Flags&RtSourceStockStopFlag));
    }
    std::printf("{\"mode\":\"%ls\",\"passed\":%s,\"checks\":%u,\"failures\":%u,\"originals\":%u,\"flagged\":%u,\"unclassified\":%u,\"entered\":%llu,\"exited\":%llu,\"stockStopVersion\":1,\"gameExecuted\":false}\n",
        mode,failures?"false":"true",checks,failures,originals,flagged,unclassified,health.Entered,health.Exited);
    return failures?1:0;
}
