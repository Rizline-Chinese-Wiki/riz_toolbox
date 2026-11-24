#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "WinHttpFixtureApi.h"
#include "../RizToolbox.Doorstop/riz_winhttp.h"
#include <array>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

namespace {
using namespace RizWinFixture;
using Address=std::uint64_t;
using InitFn=std::uint32_t(__cdecl*)();
using VersionFn=std::uint32_t(__cdecl*)();
using InfoFn=std::uint32_t(__cdecl*)(RizWinHttpInfoV1*);
using FaultFn=std::uint32_t(__cdecl*)(std::uint32_t,std::uint32_t);
using SnapshotFn=std::uint32_t(__cdecl*)(ImporterSnapshot*);
volatile LONG64 sequence{};
struct Call { Row row{};std::uint32_t pair{},worker{};std::string label,route; };
struct Pair {std::uint32_t id{},kind{};std::uint64_t directSequence{},forwardSequence{};bool equal{};};
struct Memory {Address address{},allocationBase{},base{},regionSize{};DWORD state{},type{},protect{},error{};};
struct Module {Address base{},fileIndex{};DWORD volume{};std::wstring path;};
struct InfoSnapshot {std::string label,path,sha256;std::uint32_t result{},before{},after{};RizWinHttpInfoV1 info{};std::array<Address,4> activeSlots{},initialSlots{},poisonSlots{},resolvedSlots{};};
struct Report {
    std::wstring forwardPath,outputPath,importerPath;
    HMODULE forward{},system{},importer{};
    InitFn initialize{};InfoFn getInfo{};FaultFn setFault{};Api forwarded{},direct{};
    Salt salt{};Module forwardModule{},systemModule{},importerModule{};
    std::vector<Call> calls;std::vector<Pair> pairs;std::vector<InfoSnapshot> snapshots;
    std::vector<std::string> failures;
    std::array<Memory,4> slots{};
    ImporterSnapshot importerSnapshot{};
    std::string importerSnapshotPath,importerSnapshotSha;
    std::string callsSnapshotPath,callsSnapshotSha;
    std::uint32_t actualProcessId{},mainThreadId{};
    std::uint64_t checks{};std::uint32_t fault{},faultIndex{},initResult{},repeatResult{},initErrorBefore{},initErrorAfter{},faultSelection{},faultRepeatSelection{},sehCode{};
    Address systemBeforeInit{};
    bool fixtureProfile{},finished{};
    std::string error;
};
void Check(Report& r,bool okay,const char* name){++r.checks;if(!okay)r.failures.emplace_back(name);}
std::string Utf8(const std::wstring& s)
{if(s.empty())return{};const auto n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),static_cast<int>(s.size()),nullptr,0,nullptr,nullptr);if(n<=0)throw std::runtime_error("UTF8 conversion");std::string out(static_cast<std::size_t>(n),'\0');if(WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),static_cast<int>(s.size()),out.data(),n,nullptr,nullptr)!=n)throw std::runtime_error("UTF8 conversion");return out;}
std::string Quote(const std::string& s)
{std::string out="\"";constexpr char hex[]="0123456789abcdef";for(unsigned char c:s){if(c=='"'||c=='\\'){out+='\\';out+=static_cast<char>(c);}else if(c<32){out+="\\u00";out+=hex[c>>4];out+=hex[c&15];}else out+=static_cast<char>(c);}return out+'"';}
const char* Bool(bool b){return b?"true":"false";}
std::string Hex(const unsigned char* p,std::size_t size)
{constexpr char chars[]="0123456789abcdef";std::string s;s.reserve(size*2);for(std::size_t i=0;i<size;++i){s+=chars[p[i]>>4];s+=chars[p[i]&15];}return s;}
std::string Hash(const void* p,std::size_t size)
{Sha256 hash;hash.Feed(static_cast<const unsigned char*>(p),size);unsigned char bytes[32];hash.Finish(bytes);return Hex(bytes,sizeof(bytes));}
void WriteBytes(const std::filesystem::path& path,const void* p,std::size_t size)
{std::ofstream out(path,std::ios::binary|std::ios::trunc);if(!out)throw std::runtime_error("Cannot create raw evidence file");out.write(static_cast<const char*>(p),static_cast<std::streamsize>(size));out.close();if(!out)throw std::runtime_error("Cannot persist raw evidence file");}
template<class Fn> Fn Find(HMODULE module,const char* name)
{const auto raw=GetProcAddress(module,name);if(!raw)throw std::runtime_error(name);Fn fn{};static_assert(sizeof(fn)==sizeof(raw));std::memcpy(&fn,&raw,sizeof(fn));return fn;}
Api Functions(HMODULE module)
{return {Find<OpenFn>(module,"WinHttpOpen"),Find<ProxyFn>(module,"WinHttpGetProxyForUrl"),Find<IeFn>(module,"WinHttpGetIEProxyConfigForCurrentUser"),Find<CloseFn>(module,"WinHttpCloseHandle")};}
Memory Query(Address address)
{Memory out{};out.address=address;MEMORY_BASIC_INFORMATION m{};if(VirtualQuery(reinterpret_cast<void*>(address),&m,sizeof(m))!=sizeof(m)){out.error=GetLastError();return out;}out.allocationBase=reinterpret_cast<Address>(m.AllocationBase);out.base=reinterpret_cast<Address>(m.BaseAddress);out.regionSize=m.RegionSize;out.state=m.State;out.type=m.Type;out.protect=m.Protect;return out;}
Module Identify(HMODULE module)
{
    Module out{};out.base=reinterpret_cast<Address>(module);if(!module)return out;
    std::array<wchar_t,32768> path{};const auto n=GetModuleFileNameW(module,path.data(),static_cast<DWORD>(path.size()));
    if(!n||n>=path.size())throw std::runtime_error("Cannot get actual module path");out.path.assign(path.data(),n);
    const auto file=CreateFileW(out.path.c_str(),FILE_READ_ATTRIBUTES,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)throw std::runtime_error("Cannot open actual module file");
    BY_HANDLE_FILE_INFORMATION info{};const auto okay=GetFileInformationByHandle(file,&info);CloseHandle(file);
    if(!okay)throw std::runtime_error("Cannot identify actual module file");out.volume=info.dwVolumeSerialNumber;out.fileIndex=(static_cast<Address>(info.nFileIndexHigh)<<32)|info.nFileIndexLow;return out;
}
std::uint64_t Next(){return static_cast<std::uint64_t>(InterlockedIncrement64(&sequence));}
void Keep(Report& r,const Row& row,const char* label,const char* route,std::uint32_t pair=0,std::uint32_t worker=0)
{r.calls.push_back({row,pair,worker,label,route});Check(r,row.freesFailed==0,"system-output-free-succeeded");for(const auto& s:row.strings)Check(r,!s.present||s.complete==1,"bounded-private-output-captured");}
bool EqualPrivate(const PrivateString& a,const PrivateString& b)
{return a.present==b.present&&a.length==b.length&&a.complete==b.complete&&std::memcmp(a.sha256,b.sha256,32)==0;}
bool Equivalent(const Row& direct,const Row& forwarded)
{
    if(direct.kind!=forwarded.kind||direct.lastErrorBefore!=forwarded.lastErrorBefore||direct.lastErrorAfter!=forwarded.lastErrorAfter)return false;
    if(direct.kind==Open){if((direct.rawReturn!=0)!=(forwarded.rawReturn!=0))return false;}
    else if(direct.rawReturn!=forwarded.rawReturn)return false;
    if(direct.outputAccessType!=forwarded.outputAccessType||direct.outputAutoDetect!=forwarded.outputAutoDetect||direct.freesAttempted!=forwarded.freesAttempted||direct.freesFailed!=forwarded.freesFailed)return false;
    for(unsigned i=0;i<3;++i)if(!EqualPrivate(direct.strings[i],forwarded.strings[i]))return false;
    return true;
}
void Compare(Report& r,const Row& direct,const Row& forwarded,const char* label,std::uint32_t worker=0)
{
    const auto id=static_cast<std::uint32_t>(r.pairs.size())+1;const auto equal=Equivalent(direct,forwarded);
    Keep(r,direct,label,"direct",id,worker);Keep(r,forwarded,label,"forward",id,worker);
    r.pairs.push_back({id,direct.kind,direct.sequence,forwarded.sequence,equal});Check(r,equal,"direct-and-forward-return-error-private-output-equivalent");
}
void Refusals(Report& r,std::uint32_t phase,const char* label)
{
    const auto seed=0x735a0000u+phase;
    const Row rows[]{CallOpen(r.forwarded,phase,seed,Next()),CallProxy(r.forwarded,phase,seed+1,Next(),nullptr,r.salt),
        CallIe(r.forwarded,phase,seed+2,Next(),r.salt),CallClose(r.forwarded,phase,seed+3,Next(),nullptr)};
    for(const auto& row:rows){Keep(r,row,label,"forward");Check(r,row.rawReturn==0&&row.lastErrorAfter==ERROR_DLL_INIT_FAILED,"unavailable-four-typed-null-false-error1114");}
}
void ReadSlots(Address table,std::array<Address,4>& output)
{if(!table)return;SIZE_T bytes{};if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(table),output.data(),sizeof(output),&bytes)||bytes!=sizeof(output))throw std::runtime_error("Cannot read actual fixed table slots");}
void Snapshot(Report& r,const char* label)
{
    InfoSnapshot snapshot{};snapshot.label=label;snapshot.info.version=1;snapshot.info.byteSize=sizeof(snapshot.info);
    snapshot.before=0x61e20000u+static_cast<std::uint32_t>(r.snapshots.size());SetLastError(snapshot.before);
    snapshot.result=r.getInfo(&snapshot.info);snapshot.after=GetLastError();Check(r,snapshot.result==0,"actual-GetInfo-snapshot-return0");Check(r,snapshot.after==snapshot.before,"GetInfo-preserves-LastError");
    const auto path=std::filesystem::path(r.outputPath).parent_path()/(std::filesystem::path(r.outputPath).stem().wstring()+L".info-"+std::filesystem::path(label).wstring()+L".bin");
    snapshot.path=Utf8(path.filename().wstring());snapshot.sha256=Hash(&snapshot.info,sizeof(snapshot.info));WriteBytes(path,&snapshot.info,sizeof(snapshot.info));
    ReadSlots(snapshot.info.activeTable,snapshot.activeSlots);ReadSlots(snapshot.info.initialTable,snapshot.initialSlots);ReadSlots(snapshot.info.poisonTable,snapshot.poisonSlots);ReadSlots(snapshot.info.resolvedTable,snapshot.resolvedSlots);
    r.snapshots.push_back(snapshot);
}
void Round(Report& r,std::uint32_t directPhase,std::uint32_t forwardPhase,std::uint32_t seed,std::uint32_t worker=0)
{
    const auto directOpen=CallOpen(r.direct,directPhase,seed,Next());const auto forwardOpen=CallOpen(r.forwarded,forwardPhase,seed,Next());Compare(r,directOpen,forwardOpen,"no-proxy-session-open",worker);
    Check(r,directOpen.rawReturn!=0&&forwardOpen.rawReturn!=0,"both-real-no-proxy-sessions-open");
    const auto directHandle=reinterpret_cast<HINTERNET>(directOpen.rawReturn),forwardHandle=reinterpret_cast<HINTERNET>(forwardOpen.rawReturn);
    const auto ieDirect=CallIe(r.direct,directPhase,seed+1,Next(),r.salt),ieForward=CallIe(r.forwarded,forwardPhase,seed+1,Next(),r.salt);
    Compare(r,ieDirect,ieForward,"read-only-ie-config",worker);
    const auto proxyDirect=CallProxy(r.direct,directPhase,seed+2,Next(),directHandle,r.salt),proxyForward=CallProxy(r.forwarded,forwardPhase,seed+2,Next(),forwardHandle,r.salt);
    Compare(r,proxyDirect,proxyForward,"invalid-local-url-zero-autoproxy-options",worker);Check(r,proxyDirect.rawReturn==0&&proxyForward.rawReturn==0,"invalid-proxy-options-refused-without-pac-wpad");
    const auto closedDirect=CallClose(r.direct,directPhase,seed+3,Next(),directHandle),closedForward=CallClose(r.forwarded,forwardPhase,seed+3,Next(),forwardHandle);
    Compare(r,closedDirect,closedForward,"close-associated-real-session",worker);Check(r,closedDirect.rawReturn!=0&&closedForward.rawReturn!=0,"both-own-session-handles-close");
    const auto nullDirect=CallClose(r.direct,directPhase,seed+4,Next(),nullptr),nullForward=CallClose(r.forwarded,forwardPhase,seed+4,Next(),nullptr);
    Compare(r,nullDirect,nullForward,"close-null-invalid-handle",worker);
}
struct WorkerPlan {Report* parent{};HANDLE start{};std::uint32_t number{};Report results;DWORD error{};};
DWORD WINAPI Worker(void* parameter)
{
    auto& plan=*static_cast<WorkerPlan*>(parameter);
    try{if(WaitForSingleObject(plan.start,15000)!=WAIT_OBJECT_0)throw std::runtime_error("worker start timeout");plan.results.direct=plan.parent->direct;plan.results.forwarded=plan.parent->forwarded;plan.results.salt=plan.parent->salt;
        for(unsigned i=0;i<4;++i)Round(plan.results,WorkerDirect,WorkerForward,0x7b000000u+plan.number*0x10000u+i*0x100u,plan.number);
    }catch(...){plan.error=1;}return plan.error;
}
void Workers(Report& r)
{
    const auto start=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!start)throw std::runtime_error("Cannot create worker start event");
    std::array<WorkerPlan,3> plans{};std::array<HANDLE,3> handles{};
    for(std::size_t i=0;i<plans.size();++i){plans[i].parent=&r;plans[i].start=start;plans[i].number=static_cast<std::uint32_t>(i)+1;handles[i]=CreateThread(nullptr,0,Worker,&plans[i],0,nullptr);if(!handles[i])throw std::runtime_error("Cannot start exact owned worker");}
    if(!SetEvent(start))throw std::runtime_error("Cannot release owned workers");
    if(WaitForMultipleObjects(static_cast<DWORD>(handles.size()),handles.data(),TRUE,30000)!=WAIT_OBJECT_0)throw std::runtime_error("Owned worker finite timeout");
    for(std::size_t i=0;i<plans.size();++i){DWORD exit{};Check(r,GetExitCodeThread(handles[i],&exit)&&exit==0&&!plans[i].error,"owned-worker-returned0");CloseHandle(handles[i]);auto& source=plans[i].results;r.checks+=source.checks;r.failures.insert(r.failures.end(),source.failures.begin(),source.failures.end());
        const auto offset=static_cast<std::uint32_t>(r.pairs.size());for(auto call:source.calls){if(call.pair)call.pair+=offset;r.calls.push_back(call);}for(auto pair:source.pairs){pair.id+=offset;r.pairs.push_back(pair);}}
    CloseHandle(start);
}
void Import(Report& r)
{
    r.importer=LoadLibraryExW(r.importerPath.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    if(!r.importer)throw std::runtime_error("Cannot load exact owned importer after resolver Ready");r.importerModule=Identify(r.importer);
    Check(r,Find<InitFn>(r.importer,"RizWinHttpImporter_RunOutsideLoader")()==0,"importer-four-API-explicit-outside-loader-return0");
    const auto get=Find<SnapshotFn>(r.importer,"RizWinHttpImporter_GetSnapshot");r.importerSnapshot.version=Version;r.importerSnapshot.byteSize=sizeof(r.importerSnapshot);
    Check(r,get(&r.importerSnapshot)==0,"importer-actual-snapshot-return0");const auto& s=r.importerSnapshot;
    Check(r,s.phaseMask==0x8f0&&s.rowCount==12&&s.failures==0&&s.checks>0,"actual-four-initializer-two-refusals-plus-outside-four-API");
    const auto path=std::filesystem::path(r.outputPath).parent_path()/(std::filesystem::path(r.outputPath).stem().wstring()+L".importer.bin");r.importerSnapshotPath=Utf8(path.filename().wstring());r.importerSnapshotSha=Hash(&s,sizeof(s));WriteBytes(path,&s,sizeof(s));
    for(std::uint32_t i=0;i<s.rowCount&&i<ImporterRows;++i)Keep(r,s.rows[i],"actual-static-import-initializer","importer");
}
void Run(Report& r)
{
    r.salt=MakeSalt(&r);r.systemBeforeInit=reinterpret_cast<Address>(GetModuleHandleW(L"winhttp.dll"));
    r.forward=LoadLibraryExW(r.forwardPath.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);if(!r.forward)throw std::runtime_error("Cannot load exact owned forward DLL");r.forwardModule=Identify(r.forward);
    r.initialize=Find<InitFn>(r.forward,"RizWinHttpForward_Initialize");r.getInfo=Find<InfoFn>(r.forward,"RizWinHttpForward_GetInfo");Check(r,Find<VersionFn>(r.forward,"RizWinHttpForward_GetVersion")()==1,"resolver-version1");r.forwarded=Functions(r.forward);
    const auto setter=GetProcAddress(r.forward,"RizWinHttpForward_SetFault");Check(r,(setter!=nullptr)==r.fixtureProfile,"production-setter-absent-fixture-setter-present");
    if(setter){static_assert(sizeof(r.setFault)==sizeof(setter));std::memcpy(&r.setFault,&setter,sizeof(setter));r.faultSelection=r.setFault(r.fault,r.faultIndex);r.faultRepeatSelection=r.setFault(RizWinHttpFaultNone,0);Check(r,r.faultSelection==0&&r.faultRepeatSelection==2,"fixture-selector-once-no-reset");}
    Snapshot(r,"before");Check(r,r.snapshots.back().info.state==RizWinHttpUninitialized,"fresh-initial-state");Refusals(r,Uninitialized,"before-resolver");
    r.initErrorBefore=0x6d320001u;SetLastError(r.initErrorBefore);r.initResult=r.initialize();r.initErrorAfter=GetLastError();Snapshot(r,"initialized");const auto ready=r.snapshots.back().info;
    if(ready.systemModule){r.system=reinterpret_cast<HMODULE>(ready.systemModule);r.systemModule=Identify(r.system);}
    if(r.fault){Check(r,r.initResult!=0&&ready.publishCount==0&&ready.state!=RizWinHttpReady,"injected-failure-never-publishes-ready");Refusals(r,Poisoned,"failed-resolver");r.finished=true;return;}
    Check(r,r.initResult==0&&r.initErrorAfter==r.initErrorBefore,"successful-initialize-preserves-caller-error");
    Check(r,ready.state==RizWinHttpReady&&!ready.unknown&&ready.publishCount==1&&ready.resolvedCount==4&&ready.activeTable==ready.resolvedTable,"four-slots-atomically-ready");
    if(!r.system)throw std::runtime_error("Ready omitted actual system module");r.direct=Functions(r.system);
    Check(r,r.systemModule.fileIndex==ready.fileIndex&&r.systemModule.volume==ready.fileVolumeSerial,"actual-system-file-identity-matches-held-resolver-file");
    const Address directAddresses[]{reinterpret_cast<Address>(r.direct.open),reinterpret_cast<Address>(r.direct.proxy),reinterpret_cast<Address>(r.direct.ie),reinterpret_cast<Address>(r.direct.close)};
    for(unsigned k=0;k<4;++k){r.slots[k]=Query(ready.addresses[k]);Check(r,ready.addresses[k]==directAddresses[k]&&r.slots[k].allocationBase==ready.systemModule&&r.slots[k].state==MEM_COMMIT&&r.slots[k].type==MEM_IMAGE&&(r.slots[k].protect&0xffu)==PAGE_EXECUTE_READ,"slot-is-actual-by-name-system-RX-export");}
    Round(r,HostDirect,HostForward,0x6b400000u);
    const auto a=CallOpen(r.direct,HostDirect,0x6b410000u,Next(),0x40000000u),b=CallOpen(r.forwarded,HostForward,0x6b410000u,Next(),0x40000000u);Compare(r,a,b,"nonzero-fifth-stack-argument-open-flags");
    if(a.rawReturn||b.rawReturn){const auto aClose=CallClose(r.direct,HostDirect,0x6b410001u,Next(),reinterpret_cast<HINTERNET>(a.rawReturn)),bClose=CallClose(r.forwarded,HostForward,0x6b410001u,Next(),reinterpret_cast<HINTERNET>(b.rawReturn));Compare(r,aClose,bClose,"cleanup-if-platform-accepts-nonzero-flags");}
    Import(r);Workers(r);Snapshot(r,"after-workers");
    SetLastError(0x6d320002u);r.repeatResult=r.initialize();Snapshot(r,"repeated");const auto& poison=r.snapshots.back().info;
    Check(r,r.repeatResult!=0&&poison.state==RizWinHttpUnknown&&poison.unknown&&poison.activeTable==poison.poisonTable&&poison.publishCount==1,"repeat-initialize-permanent-poison");
    Check(r,poison.systemModule==ready.systemModule&&poison.heldSystemFile==ready.heldSystemFile&&poison.resolvedTable==ready.resolvedTable&&std::memcmp(poison.addresses,ready.addresses,sizeof(ready.addresses))==0,"repeat-retains-owned-module-file-and-resolved-table");
    Refusals(r,Poisoned,"after-repeat-poison");r.finished=true;
}
DWORD Guard(Report* report){__try{Run(*report);return 0;}__except(GetExceptionCode()==0xe06d7363u?EXCEPTION_CONTINUE_SEARCH:EXCEPTION_EXECUTE_HANDLER){return GetExceptionCode();}}
void WriteModule(std::ostream& out,const Module& m)
{out<<"{\"base\":"<<m.base<<",\"path\":"<<Quote(Utf8(m.path))<<",\"fileVolumeSerial\":"<<m.volume<<",\"fileIndex\":"<<m.fileIndex<<'}';}
void WriteArray(std::ostream& out,const Address* p,std::size_t n)
{out<<'[';for(std::size_t i=0;i<n;++i){if(i)out<<',';out<<p[i];}out<<']';}
void WriteInfo(std::ostream& out,const RizWinHttpInfoV1& v)
{
    out<<'{';
#define NUMBER(field) out<<"\"" #field "\":"<<v.field<<','
    NUMBER(version);NUMBER(byteSize);NUMBER(state);NUMBER(failure);NUMBER(phase);NUMBER(flags);NUMBER(initializeAttempts);NUMBER(publishCount);
    NUMBER(systemModule);NUMBER(heldSystemFile);NUMBER(selfModule);NUMBER(activeTable);NUMBER(initialTable);NUMBER(poisonTable);NUMBER(resolvedTable);NUMBER(fileIndex);NUMBER(fileBytes);NUMBER(fileVolumeSerial);
    NUMBER(systemPathChars);NUMBER(loadedPathChars);NUMBER(peMachine);NUMBER(peImageSize);NUMBER(peTimeDateStamp);NUMBER(peExportRva);NUMBER(peExportBytes);NUMBER(loadFlags);NUMBER(resolvedCount);NUMBER(lastWin32Error);NUMBER(unknown);NUMBER(faultStage);NUMBER(faultIndex);
#undef NUMBER
    out<<"\"addresses\":";WriteArray(out,v.addresses,4);out<<",\"protections\":[";for(unsigned i=0;i<4;++i){if(i)out<<',';out<<v.protections[i];}
    out<<"],\"systemPath\":"<<Quote(Utf8(std::wstring(v.systemPath,v.systemPathChars<260?v.systemPathChars:259)))<<",\"loadedPath\":"<<Quote(Utf8(std::wstring(v.loadedPath,v.loadedPathChars<260?v.loadedPathChars:259)))<<'}';
}
void WriteRow(std::ostream& out,const Call& call)
{
    const auto& v=call.row;out<<"{\"label\":"<<Quote(call.label)<<",\"route\":"<<Quote(call.route)<<",\"pairId\":"<<call.pair<<",\"worker\":"<<call.worker<<',';
#define NUMBER(field) out<<"\"" #field "\":"<<v.field<<','
    NUMBER(version);NUMBER(byteSize);NUMBER(phase);NUMBER(kind);NUMBER(threadId);NUMBER(seed);NUMBER(sequence);NUMBER(apiAddress);NUMBER(rawReturn);NUMBER(lastErrorBefore);NUMBER(lastErrorAfter);NUMBER(openAccessType);NUMBER(openFlags);NUMBER(autoFlags);NUMBER(autoDetectFlags);NUMBER(autoReserved);NUMBER(autoLogon);NUMBER(autoConfigUrl);NUMBER(autoReservedPointer);NUMBER(outputAccessType);NUMBER(outputAutoDetect);NUMBER(freesAttempted);NUMBER(freesFailed);
#undef NUMBER
    out<<"\"args\":";WriteArray(out,v.args,5);out<<",\"strings\":[";for(unsigned i=0;i<3;++i){if(i)out<<',';const auto& text=v.strings[i];out<<"{\"pointer\":"<<text.pointer<<",\"present\":"<<text.present<<",\"length\":"<<text.length<<",\"complete\":"<<text.complete<<",\"saltedSha256\":"<<Quote(Hex(text.sha256,32))<<'}';}out<<"]}";
}
std::string Serialize(const Report& r)
{
    std::ostringstream out;out<<"{\"schemaVersion\":1,\"kind\":\"winhttp-forward-fixture\",\"actualProcessId\":"<<r.actualProcessId<<",\"mainThreadId\":"<<r.mainThreadId<<",\"passed\":"<<Bool(r.finished&&r.failures.empty()&&r.error.empty()&&!r.sehCode)<<",\"finished\":"<<Bool(r.finished)<<",\"checks\":"<<r.checks<<",\"error\":"<<Quote(r.error)<<",\"sehCode\":"<<r.sehCode
        <<",\"arguments\":{\"forward\":"<<Quote(Utf8(r.forwardPath))<<",\"importer\":"<<Quote(Utf8(r.importerPath))<<",\"output\":"<<Quote(Utf8(r.outputPath))<<",\"fixtureProfile\":"<<Bool(r.fixtureProfile)<<",\"faultStage\":"<<r.fault<<",\"faultIndex\":"<<r.faultIndex<<"},\"initialize\":{\"result\":"<<r.initResult<<",\"lastErrorBefore\":"<<r.initErrorBefore<<",\"lastErrorAfter\":"<<r.initErrorAfter<<",\"repeatResult\":"<<r.repeatResult<<",\"faultSelection\":"<<r.faultSelection<<",\"faultRepeatSelection\":"<<r.faultRepeatSelection<<"},\"systemBeforeInit\":"<<r.systemBeforeInit;
    out<<",\"forwardModule\":";WriteModule(out,r.forwardModule);out<<",\"systemModule\":";WriteModule(out,r.systemModule);out<<",\"importerModule\":";WriteModule(out,r.importerModule);
    out<<",\"slots\":[";for(unsigned i=0;i<4;++i){if(i)out<<',';const auto& v=r.slots[i];out<<"{\"kind\":"<<i<<",\"address\":"<<v.address<<",\"allocationBase\":"<<v.allocationBase<<",\"base\":"<<v.base<<",\"regionSize\":"<<v.regionSize<<",\"state\":"<<v.state<<",\"type\":"<<v.type<<",\"protect\":"<<v.protect<<",\"error\":"<<v.error<<'}';}out<<']';
    out<<",\"infoSnapshots\":[";for(std::size_t i=0;i<r.snapshots.size();++i){if(i)out<<',';const auto& v=r.snapshots[i];out<<"{\"label\":"<<Quote(v.label)<<",\"path\":"<<Quote(v.path)<<",\"sha256\":"<<Quote(v.sha256)<<",\"result\":"<<v.result<<",\"lastErrorBefore\":"<<v.before<<",\"lastErrorAfter\":"<<v.after<<",\"info\":";WriteInfo(out,v.info);out<<",\"activeSlots\":";WriteArray(out,v.activeSlots.data(),4);out<<",\"initialSlots\":";WriteArray(out,v.initialSlots.data(),4);out<<",\"poisonSlots\":";WriteArray(out,v.poisonSlots.data(),4);out<<",\"resolvedSlots\":";WriteArray(out,v.resolvedSlots.data(),4);out<<'}';}out<<']';
    out<<",\"importerSnapshot\":{\"path\":"<<Quote(r.importerSnapshotPath)<<",\"sha256\":"<<Quote(r.importerSnapshotSha)<<",\"version\":"<<r.importerSnapshot.version<<",\"byteSize\":"<<r.importerSnapshot.byteSize<<",\"phaseMask\":"<<r.importerSnapshot.phaseMask<<",\"rowCount\":"<<r.importerSnapshot.rowCount<<",\"checks\":"<<r.importerSnapshot.checks<<",\"failures\":"<<r.importerSnapshot.failures<<"},\"callsSnapshot\":{\"path\":"<<Quote(r.callsSnapshotPath)<<",\"sha256\":"<<Quote(r.callsSnapshotSha)<<",\"rowCount\":"<<r.calls.size()<<",\"rowByteSize\":"<<sizeof(Row)<<"},\"calls\":[";
    for(std::size_t i=0;i<r.calls.size();++i){if(i)out<<',';WriteRow(out,r.calls[i]);}out<<"],\"pairs\":[";
    for(std::size_t i=0;i<r.pairs.size();++i){if(i)out<<',';const auto& v=r.pairs[i];out<<"{\"id\":"<<v.id<<",\"kind\":"<<v.kind<<",\"directSequence\":"<<v.directSequence<<",\"forwardSequence\":"<<v.forwardSequence<<",\"equal\":"<<Bool(v.equal)<<'}';}
    out<<"],\"failures\":[";for(std::size_t i=0;i<r.failures.size();++i){if(i)out<<',';out<<Quote(r.failures[i]);}
    out<<"],\"controlledInputs\":{\"agent\":"<<Quote(Utf8(Agent))<<",\"invalidUrl\":"<<Quote(Utf8(InvalidUrl))<<",\"openAccessType\":1,\"normalOpenFlags\":0,\"fifthArgumentProbeFlags\":1073741824,\"autoProxyFlags\":0,\"autoDetectFlags\":0,\"autoConfigUrl\":0,\"reservedPointer\":0,\"reserved\":0,\"autoLogon\":0},\"scope\":{\"httpRequestsIssued\":false,\"autoDetectOrPacEnabled\":false,\"systemModuleLoadedByResolverOnly\":true,\"settingsModified\":false,\"privateConfigStringsLogged\":false,\"loaderLockResolverSafetyVerified\":false,\"gameExecuted\":false,\"installed\":false}}\n";return out.str();
}
} // namespace
int wmain(int argc,wchar_t** argv)
{
    Report report{};
    report.actualProcessId=GetCurrentProcessId();report.mainThreadId=GetCurrentThreadId();
    try{
        for(int i=1;i<argc;++i){const std::wstring arg=argv[i];if(arg==L"--forward"&&i+1<argc)report.forwardPath=argv[++i];else if(arg==L"--output"&&i+1<argc)report.outputPath=argv[++i];else if(arg==L"--importer"&&i+1<argc)report.importerPath=argv[++i];else if(arg==L"--fixture")report.fixtureProfile=true;else if(arg==L"--fault"&&i+1<argc)report.fault=static_cast<std::uint32_t>(std::stoul(argv[++i]));else if(arg==L"--fault-index"&&i+1<argc)report.faultIndex=static_cast<std::uint32_t>(std::stoul(argv[++i]));else return 2;}
        if(!std::filesystem::path(report.forwardPath).is_absolute()||!std::filesystem::path(report.outputPath).is_absolute()||report.fault>10||report.faultIndex>3||(!report.fixtureProfile&&report.fault)||(!report.fault&&!std::filesystem::path(report.importerPath).is_absolute()))return 2;
        report.sehCode=Guard(&report);if(report.sehCode)report.error="Native exception in exact owned fixture";
    }catch(const std::exception& e){report.error=e.what();}catch(...){report.error="Unexpected fixture exception";}
    try{if(report.outputPath.empty())return 2;std::vector<Row> rows;rows.reserve(report.calls.size());for(const auto& call:report.calls)rows.push_back(call.row);
        const auto path=std::filesystem::path(report.outputPath).parent_path()/(std::filesystem::path(report.outputPath).stem().wstring()+L".calls.bin");report.callsSnapshotPath=Utf8(path.filename().wstring());report.callsSnapshotSha=Hash(rows.data(),rows.size()*sizeof(Row));WriteBytes(path,rows.data(),rows.size()*sizeof(Row));
        const auto json=Serialize(report);WriteBytes(report.outputPath,json.data(),json.size());}catch(...){return 2;}
    return report.finished&&report.failures.empty()&&report.error.empty()&&!report.sehCode?0:1;
}
