#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include "../RizToolbox.EngineBootstrap/EngineTransportPublication.h"
extern "C" __declspec(dllimport) int __cdecl UnityMain2();
namespace {
RizEngineTransportPublicationV5 first{},after{},finalSnapshot{};
bool Persist(const wchar_t* path,const void* value,std::size_t bytes){FILE* file{};
    if(_wfopen_s(&file,path,L"wb")||!file)return false;
    const bool wrote=std::fwrite(value,1,bytes,file)==bytes,flushed=std::fflush(file)==0,closed=std::fclose(file)==0;
    return wrote&&flushed&&closed;
}
template<class T> T Export(HMODULE module,const char* name){return reinterpret_cast<T>(GetProcAddress(module,name));}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=3||std::wcslen(argv[1])<4||std::wcslen(argv[2])<4)return 2;
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);
    const auto bootstrap=GetModuleHandleW(L"RizToolbox.EngineBootstrap.dll");
    if(!bootstrap||GetModuleHandleW(L"UnityPlayer.dll")||GetModuleHandleW(L"GameAssembly.dll"))return 3;
    using Value=std::uint32_t(__cdecl*)();using Read=std::uint32_t(__cdecl*)(RizEngineTransportPublicationV5*);
    const auto version=Export<Value>(bootstrap,"RizEngineTransport_GetVersion");
    const auto read=Export<Read>(bootstrap,"RizEngineTransport_GetInfoV5");
    const auto verify=Export<Value>(bootstrap,"RizEngineTransport_VerifyV5");
    if(!version||!read||!verify)return 4;
    first.version=after.version=finalSnapshot.version=5;
    first.byteSize=after.byteSize=finalSnapshot.byteSize=sizeof(first);
    constexpr DWORD marker=0x6244a973;SetLastError(marker);
    const auto v=version();const bool lastVersion=GetLastError()==marker;
    SetLastError(marker);const auto r=read(&first);const bool lastRead=GetLastError()==marker;
    SetLastError(marker);const auto checked=verify();const bool lastVerify=GetLastError()==marker;
    SetLastError(marker);const auto r2=read(&after);const bool lastAfter=GetLastError()==marker;
    const int unity=UnityMain2(); // Generated owned leaf stub only; no game code.
    SetLastError(marker);const auto r3=read(&finalSnapshot);const bool lastFinal=GetLastError()==marker;
    const bool identical=std::memcmp(&first,&after,sizeof(first))==0&&std::memcmp(&first,&finalSnapshot,sizeof(first))==0;
    const bool shape=v==5&&!r&&!checked&&!r2&&!r3&&first.version==5&&first.byteSize==sizeof(first)
        &&first.schemaMagic==RizEngineTransportPublicationLayout::Magic&&first.state==RizEngineTransportReadyV5
        &&first.profile==1&&first.mainThreadProof==RizEngineTransportMainThreadOwnedContractV5
        &&first.runtimeFunctionCount==67&&first.configuredFamilies==0xfff&&first.boundFamilies==0xfff
        &&first.committedWindows==((1ull<<36)-1)&&first.sourceWriteAttempts==36&&first.sourceWriteCompletions==36
        &&!first.protectionOutstanding&&first.resourceCount==1&&first.retainedResources==1;
    const bool lastErrors=lastVersion&&lastRead&&lastVerify&&lastAfter&&lastFinal;
    const bool raw=Persist(argv[2],&first,sizeof(first));
    char json[1800]{};const bool passed=shape&&identical&&lastErrors&&unity==0&&raw;
    const int bytes=std::snprintf(json,sizeof(json),"{\"schemaVersion\":1,\"kind\":\"engine-transport-cold-owned\",\"pid\":%lu,\"passed\":%s,\"version\":%u,\"readResult\":%u,\"verifyResult\":%u,\"afterResult\":%u,\"finalResult\":%u,\"publicationState\":%u,\"publicationFailure\":%u,\"immutable\":%s,\"lastErrorPreserved\":%s,\"ownedUnityResult\":%d,\"rawPersisted\":%s,\"runtimeFunctionCount\":%u,\"configuredFamilies\":%llu,\"committedWindows\":%llu,\"actualColdProcess\":true,\"gameExecuted\":false,\"originalGameCodeExecuted\":false,\"productionAdmissionVerified\":false,\"fullFamilyAdoptionVerified\":false}\n",
        GetCurrentProcessId(),passed?"true":"false",v,r,checked,r2,r3,first.state,first.failure,
        identical?"true":"false",lastErrors?"true":"false",unity,raw?"true":"false",first.runtimeFunctionCount,
        static_cast<unsigned long long>(first.configuredFamilies),static_cast<unsigned long long>(first.committedWindows));
    if(bytes<=0||static_cast<std::size_t>(bytes)>=sizeof(json)||!Persist(argv[1],json,static_cast<std::size_t>(bytes)))return 5;
    std::fwrite(json,1,static_cast<std::size_t>(bytes),stdout);return passed?0:1;
}
