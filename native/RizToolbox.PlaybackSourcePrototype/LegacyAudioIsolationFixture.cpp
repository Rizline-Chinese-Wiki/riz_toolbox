// Owned-memory regression for the five older AudioSource leaf entries.
// This calls actual helper entries and originals, without Dobby or game code.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cstdlib>
#include "PlaybackSourceContext.h"

namespace {
LONG checks{}, failures{};
void Check(bool value, const char* text, int line) {
    InterlockedIncrement(&checks);
    if (!value) { InterlockedIncrement(&failures); std::printf("FAIL %d %s\n", line, text); }
}
#define CHECK(value) Check((value), #value, __LINE__)
struct Audio { void* Class; std::uint64_t Calls{}, Bits{}; void* Method{}; };
struct Player { void* Class; void* Monitor; Audio* Source; };
int playerClass{}, audioClass{}, otherClass{};
Audio current{&audioClass}, foreign{&audioClass};
Player player{&playerClass,nullptr,&current};
HMODULE library{};
void* owner{};
void* receipt{};
decltype(&RtPlaybackSourceCreateOwner) createOwner;
decltype(&RtPlaybackSourceConfigure) configure;
decltype(&RtPlaybackSourceGetEntry) getEntry;
decltype(&RtPlaybackSourceSetArmed) arm;
decltype(&RtPlaybackSourceReadOwner) readOwner;
decltype(&RtPlaybackSourceReadCurrent) readCurrent;
decltype(&RtPlaybackSourceReadReceipt) readReceipt;
decltype(&RtPlaybackSourceReleaseReceipt) releaseReceipt;
decltype(&RtPlaybackSourceConfigurePlayerWatch) configureWatch;
decltype(&RtPlaybackSourceStagePlayerWatch) stageWatch;
decltype(&RtPlaybackSourceActivateInitialPlayerWatch) activateWatch;
constexpr std::uint32_t kinds[]={RtSourceSingle1,RtSourceDouble1,RtSourceRef1,RtSourceVoid0,RtSourceSingle1};
constexpr std::uint32_t roles[]={RtSourceSetPitch,RtSourcePlayScheduled,RtSourceSetClip,RtSourcePauseLeaf,RtSourceSetTime};
void* entries[5]{};
unsigned char targetMarkers[6]{};
unsigned indexValue{};
std::uint64_t observed{}, normalReturns{};
RtPlaybackSourceFrame lastBefore{};
RtPlaybackSourceInfo storeHealth{};
bool expectWorker{}, throwOriginal{};
std::uint64_t Bits(unsigned index) {
    return index==0 ? 0x7FC01234ull : index==1 ? 0x7FF8000000001234ull : index==2 ? 0xABCDEF0001234567ull : index==3 ? 0ull : 0x80000000ull;
}
template<class T> T Symbol(const char* name) {
    auto* value=GetProcAddress(library,name); CHECK(value!=nullptr); return reinterpret_cast<T>(value);
}
RtPlaybackSourceInfo Health() {
    RtPlaybackSourceInfo value{}; CHECK(readOwner(owner,&value)==1); return value;
}
void Store(void* self,std::uint64_t bits,void* method) {
    auto* source=static_cast<Audio*>(self);
    storeHealth=Health();
    if (expectWorker) CHECK(storeHealth.InFlight==1 && (storeHealth.Flags & RtSourceUnknown)!=0);
    if (throwOriginal) RaiseException(0xE2430001,0,0,nullptr);
    ++source->Calls;source->Bits=bits;source->Method=method;
}
__declspec(noinline) void __cdecl OriginalPitch(void* self,float value,void* method) {
    std::uint32_t bits;std::memcpy(&bits,&value,sizeof(bits));Store(self,bits,method);
}
__declspec(noinline) void __cdecl OriginalSchedule(void* self,double value,void* method) {
    std::uint64_t bits;std::memcpy(&bits,&value,sizeof(bits));Store(self,bits,method);
}
__declspec(noinline) void __cdecl OriginalClip(void* self,void* value,void* method) {
    Store(self,reinterpret_cast<std::uint64_t>(value),method);
}
__declspec(noinline) void __cdecl OriginalPause(void* self,void* method) { Store(self,0,method); }
__declspec(noinline) void __cdecl OriginalTime(void* self,float value,void* method) {
    std::uint32_t bits;std::memcpy(&bits,&value,sizeof(bits));Store(self,bits,method);
}
__declspec(noinline) void __cdecl OriginalCtor(void* self,void* source,void*) {
    static_cast<Player*>(self)->Source=static_cast<Audio*>(source);
}
void* originals[]={reinterpret_cast<void*>(&OriginalPitch),reinterpret_cast<void*>(&OriginalSchedule),
    reinterpret_cast<void*>(&OriginalClip),reinterpret_cast<void*>(&OriginalPause),reinterpret_cast<void*>(&OriginalTime)};
void Observe(void*,const RtPlaybackSourceFrame* frame) {
    CHECK(frame && frame->Version==4 && frame->Size==sizeof(*frame));
    if (!frame) return;
    CHECK(frame->Role==roles[indexValue] && frame->Kind==kinds[indexValue]);
    if (frame->Stage==RtSourceBefore) { ++observed;lastBefore=*frame; }
    else if (frame->Stage==RtSourceOriginalReturned) ++normalReturns;
}
int Call(unsigned index,Audio* self,void* method,std::uint32_t* code) {
    *code=0;
    __try {
        const auto bits=Bits(index);
        if (kinds[index]==RtSourceSingle1) {
            const auto raw=static_cast<std::uint32_t>(bits);float value;std::memcpy(&value,&raw,sizeof(value));
            reinterpret_cast<void(__cdecl*)(void*,float,void*)>(entries[index])(self,value,method);
        } else if (kinds[index]==RtSourceDouble1) {
            double value;std::memcpy(&value,&bits,sizeof(value));
            reinterpret_cast<void(__cdecl*)(void*,double,void*)>(entries[index])(self,value,method);
        } else if (kinds[index]==RtSourceRef1)
            reinterpret_cast<void(__cdecl*)(void*,void*,void*)>(entries[index])(self,reinterpret_cast<void*>(bits),method);
        else reinterpret_cast<void(__cdecl*)(void*,void*)>(entries[index])(self,method);
        return 1;
    } __except(EXCEPTION_EXECUTE_HANDLER) { *code=GetExceptionCode();return 2; }
}
void* Method(unsigned iteration) {
    return iteration%3==0 ? reinterpret_cast<void*>(0x12345678ull) : iteration%3==1 ? nullptr : reinterpret_cast<void*>(0x23456789ull);
}
DWORD WINAPI Worker(void*) {
    std::uint32_t code{};
    CHECK(Call(indexValue,&current,Method(0),&code)==1 && code==0);
    return 0;
}
DWORD WINAPI ForeignWorker(void*) {
    for (unsigned iteration=0;iteration<320;++iteration)
        for (unsigned index=0;index<5;++index) {
            indexValue=index;std::uint32_t code{};
            CHECK(Call(index,&foreign,Method(iteration),&code)==1 && code==0);
        }
    return 0;
}
bool SameAuthority(const RtPlaybackSourceInfo& a,const RtPlaybackSourceInfo& b) {
    return a.Entered==b.Entered && a.Exited==b.Exited && a.InFlight==b.InFlight && a.WriterSequence==b.WriterSequence
        && a.SourceSequence==b.SourceSequence && a.SchedulingSequence==b.SchedulingSequence && a.Flags==b.Flags && a.Faults==b.Faults;
}
void Load() {
#define LOAD(field,name) field=Symbol<decltype(field)>(name)
    LOAD(createOwner,"RtPlaybackSourceCreateOwner");LOAD(configure,"RtPlaybackSourceConfigure");
    LOAD(getEntry,"RtPlaybackSourceGetEntry");LOAD(arm,"RtPlaybackSourceSetArmed");LOAD(readOwner,"RtPlaybackSourceReadOwner");
    LOAD(readCurrent,"RtPlaybackSourceReadCurrent");LOAD(readReceipt,"RtPlaybackSourceReadReceipt");LOAD(releaseReceipt,"RtPlaybackSourceReleaseReceipt");
    LOAD(configureWatch,"RtPlaybackSourceConfigurePlayerWatch");LOAD(stageWatch,"RtPlaybackSourceStagePlayerWatch");
    LOAD(activateWatch,"RtPlaybackSourceActivateInitialPlayerWatch");
#undef LOAD
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackSourceVersion")()==4);
    CHECK(Symbol<std::uint32_t(__cdecl*)()>("RtPlaybackPlayerWatchVersion")()==2);
    owner=createOwner(Observe,nullptr);CHECK(owner!=nullptr);
    CHECK(configure(owner,RtSourceRef1,0,RtSourcePlayerConstructor,&targetMarkers[0],reinterpret_cast<void*>(&OriginalCtor))==1);
    for (unsigned index=0;index<5;++index) {
        CHECK(configure(owner,kinds[index],index+1,roles[index],&targetMarkers[index+1],originals[index])==1);
        entries[index]=getEntry(kinds[index],index+1);CHECK(entries[index]!=nullptr);
    }
    const auto health=Health();CHECK(arm(owner,health.OwnerEpoch,1)==1);
    CHECK(configureWatch(owner,&playerClass,&audioClass)==1);
}
}
int main(int argc,char** argv) {
    if (argc<3 || argc>4) return 2;
    library=LoadLibraryA(argv[1]);if (!library) return 3;
    const char* mode=argv[2];if (argc==4) indexValue=static_cast<unsigned>(std::strtoul(argv[3],nullptr,10));
    if (indexValue>=5) return 4;
    Load();
    const bool early=std::strcmp(mode,"early-foreign")==0;
    const bool pending=std::strcmp(mode,"pending")==0;
    if (!early) {
        CHECK(stageWatch(owner,&player,&current,&receipt)==1 && receipt!=nullptr);
        if (!pending) CHECK(activateWatch(owner,receipt)==1);
    }
    const auto initial=Health();std::uint32_t code{};
    if (std::strcmp(mode,"foreign")==0 || early) {
        for (unsigned iteration=0;iteration<3;++iteration) for (unsigned index=0;index<5;++index) {
            indexValue=index;CHECK(Call(index,&foreign,Method(iteration),&code)==1 && code==0);
            CHECK(foreign.Bits==Bits(index) && foreign.Method==Method(iteration));
        }
        const auto thread=CreateThread(nullptr,0,ForeignWorker,nullptr,0,nullptr);CHECK(thread!=nullptr);
        CHECK(WaitForSingleObject(thread,10000)==WAIT_OBJECT_0);CloseHandle(thread);
        CHECK(foreign.Calls==1615 && observed==0 && normalReturns==0 && SameAuthority(initial,Health()));
    } else if (std::strcmp(mode,"current")==0 || pending || std::strcmp(mode,"worker")==0 || std::strcmp(mode,"class")==0 || std::strcmp(mode,"seh")==0) {
        expectWorker=std::strcmp(mode,"worker")==0;throwOriginal=std::strcmp(mode,"seh")==0;
        if (std::strcmp(mode,"class")==0) current.Class=&otherClass;
        if (expectWorker) {
            const auto thread=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);CHECK(thread!=nullptr);
            CHECK(WaitForSingleObject(thread,10000)==WAIT_OBJECT_0);CloseHandle(thread);
        } else {
            const auto status=Call(indexValue,&current,Method(0),&code);
            CHECK(throwOriginal ? status==2 && code==0xE2430001 : status==1 && code==0);
        }
        const auto final=Health();
        CHECK(final.Entered==initial.Entered+1 && final.Exited==initial.Exited+1 && final.InFlight==0);
        CHECK(final.WriterSequence==initial.WriterSequence+(indexValue==0?1:0));
        CHECK(final.SourceSequence==initial.SourceSequence+(indexValue==2?1:0));
        CHECK(final.SchedulingSequence==initial.SchedulingSequence+(indexValue==0||indexValue==2?0:1));
        CHECK(throwOriginal ? current.Calls==0 : current.Calls==1 && current.Bits==Bits(indexValue) && current.Method==Method(0));
        if (expectWorker) CHECK((final.Flags & RtSourceUnknown)!=0 && observed==0 && normalReturns==0);
        else {
            CHECK(observed==1 && lastBefore.Self==reinterpret_cast<std::uint64_t>(&current) && lastBefore.ArgBits==Bits(indexValue));
            RtPlaybackSourceFrame physical{};CHECK(readReceipt(owner,lastBefore.Sequence,lastBefore.Token,&physical)==1);
            CHECK((physical.Flags & RtSourcePhysicalExitedFlag)!=0);
            CHECK(releaseReceipt(owner,lastBefore.Sequence,lastBefore.Token)==1);
        }
        if (pending) CHECK(activateWatch(owner,receipt)==0);
    } else return 5;
    std::printf("{\"kind\":\"owned-legacy-audio-isolation\",\"mode\":\"%s\",\"index\":%u,\"passed\":%s,\"checks\":%ld,\"failures\":%ld,\"gameExecuted\":false}\n",
        mode,indexValue,failures==0?"true":"false",checks,failures);
    return failures==0?0:1;
}
