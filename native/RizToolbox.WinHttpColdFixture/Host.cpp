#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "ColdApi.h"
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
using InfoFn=std::uint32_t(__cdecl*)(RizWinHttpInfoV1*);
volatile LONG64 sequence{};
struct Call { Row row{};std::uint32_t pair{},worker{};std::string label,route; };
struct Pair {std::uint32_t id{},kind{};std::uint64_t directSequence{},forwardSequence{};bool equal{};};
struct Memory {Address address{},allocationBase{},base{},regionSize{};DWORD state{},type{},protect{},error{};};
struct Module {Address base{},fileIndex{};DWORD volume{};std::wstring path;};
struct InfoSnapshot {std::string label,path,sha256;RizCold::PhaseProof proof{};std::array<Address,4> activeSlots{},initialSlots{},poisonSlots{},resolvedSlots{};};
struct Report {
    std::wstring outputPath;
    HMODULE forward{},system{},importer{},additionalReference{};
    Api forwarded{},direct{};
    Salt salt{};Module forwardModule{},systemModule{},importerModule{};
    std::vector<Call> calls;std::vector<Pair> pairs;std::vector<InfoSnapshot> snapshots;
    std::vector<std::string> failures;
    std::array<Memory,4> slots{};
    RizCold::Snapshot importerSnapshot{};
    std::string importerSnapshotPath,importerSnapshotSha;
    std::string callsSnapshotPath,callsSnapshotSha;
    std::uint32_t actualProcessId{},mainThreadId{},outsideResult{UINT32_MAX},sehCode{};
    std::uint32_t initialImporterPhaseMask{},initialImporterRowCount{};
    std::uint64_t initialImporterFailures{},checks{};
    bool finished{};std::string error;
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
void ReadSlots(Address table,std::array<Address,4>& output)
{if(!table)return;SIZE_T bytes{};if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(table),output.data(),sizeof(output),&bytes)||bytes!=sizeof(output))throw std::runtime_error("Cannot read actual fixed table slots");}
bool Ready(const RizWinHttpInfoV1& info,Address proxy)
{
    return info.version==1&&info.byteSize==sizeof(info)&&info.state==RizWinHttpReady&&info.failure==0
        &&info.phase==RizWinHttpPhaseComplete&&info.flags==63&&info.initializeAttempts==1&&info.publishCount==1
        &&info.resolvedCount==4&&!info.unknown&&info.selfModule==proxy&&info.systemModule&&info.systemModule!=proxy
        &&info.activeTable==info.resolvedTable&&info.activeTable!=info.initialTable&&info.activeTable!=info.poisonTable
        &&info.initialTable!=info.poisonTable;
}
void CheckProof(Report& r,const RizCold::PhaseProof& proof,std::uint32_t phase)
{
    Check(r,proof.phase==phase&&proof.threadId==r.mainThreadId,"actual-phase-and-main-thread");
    Check(r,proof.infoResult==0&&proof.lastErrorBefore==proof.lastErrorAfter&&Ready(proof.info,proof.proxyBase),"live-ready-before-observer-with-one-attempt");
    const auto& entry=proof.entryRecord;
    Check(r,entry.version==1&&entry.byteSize==sizeof(entry)&&entry.stage==RizWinHttpDoorstopReady&&entry.attempts==1
        &&entry.threadId==r.mainThreadId&&entry.initializeResult==0&&entry.infoResult==0&&entry.entryLastError==entry.initializeLastError
        &&entry.entryLastError==entry.exitLastError&&entry.readyChecksPassed==1&&entry.failure==0&&entry.selfModule==proof.proxyBase
        &&Ready(entry.info,proof.proxyBase),"actual-DllEntry-published-before-importer-initializer");
    Check(r,proof.getInfoAddress==proof.proxyBase+RizColdSymbols::GetInfoRva,"validated-private-MAP-address");
}
void Snapshot(Report& r,const char* label)
{
    InfoSnapshot snapshot{};snapshot.label=label;auto& proof=snapshot.proof;HMODULE actual{};
    if(!RizCold::MatchProxy(r.forwarded,actual)||actual!=r.forward)throw std::runtime_error("Live proxy no longer matches held production contract");
    proof.phase=ImporterOutside;proof.threadId=GetCurrentThreadId();proof.proxyBase=reinterpret_cast<Address>(actual);
    proof.getInfoAddress=proof.proxyBase+RizColdSymbols::GetInfoRva;
    proof.info.version=1;proof.info.byteSize=sizeof(proof.info);
    proof.lastErrorBefore=0x6c620000u+static_cast<std::uint32_t>(r.snapshots.size());SetLastError(proof.lastErrorBefore);
    proof.infoResult=reinterpret_cast<InfoFn>(proof.getInfoAddress)(&proof.info);proof.lastErrorAfter=GetLastError();
    SIZE_T copied{};
    if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(proof.proxyBase+RizColdSymbols::DoorstopRecordRva),
        &proof.entryRecord,sizeof(proof.entryRecord),&copied)||copied!=sizeof(proof.entryRecord))throw std::runtime_error("Cannot read completed actual DllEntry record");
    CheckProof(r,proof,ImporterOutside);
    const auto path=std::filesystem::path(r.outputPath).parent_path()/(std::filesystem::path(r.outputPath).stem().wstring()+L".info-"+std::filesystem::path(label).wstring()+L".bin");
    snapshot.path=Utf8(path.filename().wstring());snapshot.sha256=Hash(&proof,sizeof(proof));WriteBytes(path,&proof,sizeof(proof));
    ReadSlots(proof.info.activeTable,snapshot.activeSlots);ReadSlots(proof.info.initialTable,snapshot.initialSlots);
    ReadSlots(proof.info.poisonTable,snapshot.poisonSlots);ReadSlots(proof.info.resolvedTable,snapshot.resolvedSlots);
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
void ReadImporter(Report& r)
{
    RizCold::Snapshot before{};before.version=RizCold::Version;before.byteSize=sizeof(before);
    Check(r,RizColdImporter_GetSnapshot(&before)==0,"initial-importer-snapshot-return0");
    r.initialImporterPhaseMask=before.phaseMask;r.initialImporterRowCount=before.rowCount;r.initialImporterFailures=before.failures;
    Check(r,before.phaseMask==0xf0&&before.rowCount==8&&before.failures==0&&before.checks>0,"all-four-true-initializers-completed-before-main");
    r.outsideResult=RizColdImporter_RunOutsideLoader();Check(r,r.outsideResult==0,"importer-outside-four-API-return0");
    auto& snap=r.importerSnapshot;snap.version=RizCold::Version;snap.byteSize=sizeof(snap);
    Check(r,RizColdImporter_GetSnapshot(&snap)==0,"final-importer-snapshot-return0");
    Check(r,snap.version==RizCold::Version&&snap.byteSize==sizeof(snap)&&snap.phaseMask==0x8f0&&snap.rowCount==12
        &&snap.failures==0&&snap.checks>0,"four-initializer-phases-and-outside-phase-all-succeeded");
    const auto path=std::filesystem::path(r.outputPath).parent_path()/(std::filesystem::path(r.outputPath).stem().wstring()+L".importer.bin");
    r.importerSnapshotPath=Utf8(path.filename().wstring());r.importerSnapshotSha=Hash(&snap,sizeof(snap));WriteBytes(path,&snap,sizeof(snap));
    for(std::uint32_t i=0;i<snap.rowCount&&i<RizCold::RowCount;++i)Keep(r,snap.rows[i],"actual-static-import-initializer","importer");
    InterlockedExchange64(&sequence,static_cast<LONG64>(snap.rowCount));
    for(unsigned i=0;i<RizCold::PhaseCount;++i)CheckProof(r,snap.phases[i],i==4?ImporterOutside:ImporterTls+i);
    if(!r.failures.empty())throw std::runtime_error("Cold importer rejected selected production proxy contract");
    HMODULE actualImporter{};
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&RizColdImporter_GetSnapshot),&actualImporter)||!actualImporter)throw std::runtime_error("Cannot identify statically imported DLL");
    r.importer=actualImporter;r.importerModule=Identify(r.importer);
}
void Run(Report& r)
{
    r.salt=MakeSalt(&r);ReadImporter(r);
    const auto& ready=r.importerSnapshot.phases[0].info;r.forward=reinterpret_cast<HMODULE>(ready.selfModule);
    r.forwardModule=Identify(r.forward);r.forwarded=Functions(r.forward);HMODULE matched{};
    Check(r,RizCold::MatchProxy(r.forwarded,matched)&&matched==r.forward,"live-IAT-PE-export-file-hash-contract");
    if(!r.failures.empty())throw std::runtime_error("Production proxy identity contract failed");
    r.system=reinterpret_cast<HMODULE>(ready.systemModule);r.systemModule=Identify(r.system);r.direct=Functions(r.system);
    Check(r,r.systemModule.fileIndex==ready.fileIndex&&r.systemModule.volume==ready.fileVolumeSerial,"actual-system-file-identity-matches-retained-file");
    const Address directAddresses[]{reinterpret_cast<Address>(r.direct.open),reinterpret_cast<Address>(r.direct.proxy),reinterpret_cast<Address>(r.direct.ie),reinterpret_cast<Address>(r.direct.close)};
    for(unsigned k=0;k<4;++k){r.slots[k]=Query(ready.addresses[k]);Check(r,ready.addresses[k]==directAddresses[k]&&r.slots[k].allocationBase==ready.systemModule
        &&r.slots[k].state==MEM_COMMIT&&r.slots[k].type==MEM_IMAGE&&(r.slots[k].protect&0xffu)==PAGE_EXECUTE_READ,"slot-is-actual-by-name-system-RX-export");}
    Snapshot(r,"after-main");Round(r,HostDirect,HostForward,0x6b400000u);
    const auto a=CallOpen(r.direct,HostDirect,0x6b410000u,Next(),0x40000000u),b=CallOpen(r.forwarded,HostForward,0x6b410000u,Next(),0x40000000u);
    Compare(r,a,b,"nonzero-fifth-stack-argument-open-flags");
    if(a.rawReturn||b.rawReturn){const auto aClose=CallClose(r.direct,HostDirect,0x6b410001u,Next(),reinterpret_cast<HINTERNET>(a.rawReturn)),
        bClose=CallClose(r.forwarded,HostForward,0x6b410001u,Next(),reinterpret_cast<HINTERNET>(b.rawReturn));Compare(r,aClose,bClose,"cleanup-if-platform-accepts-nonzero-flags");}
    Workers(r);Snapshot(r,"after-workers");
    r.additionalReference=LoadLibraryExW(r.forwardModule.path.c_str(),nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
    Check(r,r.additionalReference==r.forward,"same-absolute-proxy-additional-reference");Snapshot(r,"after-additional-reference");
    const auto& last=r.snapshots.back().proof.info;
    Check(r,last.initializeAttempts==1&&last.publishCount==1&&last.systemModule==ready.systemModule&&last.heldSystemFile==ready.heldSystemFile
        &&last.resolvedTable==ready.resolvedTable&&std::memcmp(last.addresses,ready.addresses,sizeof(ready.addresses))==0,"additional-reference-does-not-repeat-DllEntry-or-resolver");
    r.finished=true;
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
void WriteEntry(std::ostream& out,const RizWinHttpDoorstopRecordV1& v)
{
    out<<'{';
#define NUMBER(field) out<<"\"" #field "\":"<<v.field<<','
    NUMBER(version);NUMBER(byteSize);NUMBER(stage);NUMBER(attempts);NUMBER(threadId);NUMBER(initializeResult);NUMBER(infoResult);
    NUMBER(entryLastError);NUMBER(initializeLastError);NUMBER(exitLastError);NUMBER(readyChecksPassed);NUMBER(failure);NUMBER(selfModule);
#undef NUMBER
    out<<"\"info\":";WriteInfo(out,v.info);out<<'}';
}
void WriteProof(std::ostream& out,const RizCold::PhaseProof& v)
{
    out<<"{\"phase\":"<<v.phase<<",\"threadId\":"<<v.threadId<<",\"infoResult\":"<<v.infoResult<<",\"lastErrorBefore\":"<<v.lastErrorBefore
        <<",\"lastErrorAfter\":"<<v.lastErrorAfter<<",\"reserved\":"<<v.reserved<<",\"proxyBase\":"<<v.proxyBase<<",\"getInfoAddress\":"<<v.getInfoAddress<<",\"info\":";
    WriteInfo(out,v.info);out<<",\"entryRecord\":";WriteEntry(out,v.entryRecord);out<<'}';
}
std::string Serialize(const Report& r)
{
    std::ostringstream out;out<<"{\"schemaVersion\":1,\"kind\":\"winhttp-cold-fixture\",\"actualProcessId\":"<<r.actualProcessId<<",\"mainThreadId\":"<<r.mainThreadId
        <<",\"passed\":"<<Bool(r.finished&&r.failures.empty()&&r.error.empty()&&!r.sehCode)<<",\"finished\":"<<Bool(r.finished)<<",\"checks\":"<<r.checks
        <<",\"error\":"<<Quote(r.error)<<",\"sehCode\":"<<r.sehCode<<",\"arguments\":{\"output\":"<<Quote(Utf8(r.outputPath))<<"},\"outsideResult\":"<<r.outsideResult
        <<",\"initialImporter\":{\"phaseMask\":"<<r.initialImporterPhaseMask<<",\"rowCount\":"<<r.initialImporterRowCount<<",\"failures\":"<<r.initialImporterFailures<<'}';
    out<<",\"forwardModule\":";WriteModule(out,r.forwardModule);out<<",\"systemModule\":";WriteModule(out,r.systemModule);out<<",\"importerModule\":";WriteModule(out,r.importerModule);
    out<<",\"additionalReference\":"<<reinterpret_cast<Address>(r.additionalReference);
    out<<",\"slots\":[";for(unsigned i=0;i<4;++i){if(i)out<<',';const auto& v=r.slots[i];out<<"{\"kind\":"<<i<<",\"address\":"<<v.address<<",\"allocationBase\":"<<v.allocationBase
        <<",\"base\":"<<v.base<<",\"regionSize\":"<<v.regionSize<<",\"state\":"<<v.state<<",\"type\":"<<v.type<<",\"protect\":"<<v.protect<<",\"error\":"<<v.error<<'}';}out<<']';
    out<<",\"infoSnapshots\":[";for(std::size_t i=0;i<r.snapshots.size();++i){if(i)out<<',';const auto& v=r.snapshots[i];out<<"{\"label\":"<<Quote(v.label)<<",\"path\":"<<Quote(v.path)<<",\"sha256\":"<<Quote(v.sha256)<<",\"proof\":";
        WriteProof(out,v.proof);out<<",\"activeSlots\":";WriteArray(out,v.activeSlots.data(),4);out<<",\"initialSlots\":";WriteArray(out,v.initialSlots.data(),4);
        out<<",\"poisonSlots\":";WriteArray(out,v.poisonSlots.data(),4);out<<",\"resolvedSlots\":";WriteArray(out,v.resolvedSlots.data(),4);out<<'}';}out<<']';
    const auto& snap=r.importerSnapshot;
    out<<",\"importerSnapshot\":{\"path\":"<<Quote(r.importerSnapshotPath)<<",\"sha256\":"<<Quote(r.importerSnapshotSha)<<",\"version\":"<<snap.version<<",\"byteSize\":"<<snap.byteSize
        <<",\"phaseMask\":"<<snap.phaseMask<<",\"rowCount\":"<<snap.rowCount<<",\"checks\":"<<snap.checks<<",\"failures\":"<<snap.failures<<",\"phases\":[";
    for(unsigned i=0;i<RizCold::PhaseCount;++i){if(i)out<<',';WriteProof(out,snap.phases[i]);}out<<"]}";
    out<<",\"callsSnapshot\":{\"path\":"<<Quote(r.callsSnapshotPath)<<",\"sha256\":"<<Quote(r.callsSnapshotSha)<<",\"rowCount\":"<<r.calls.size()<<",\"rowByteSize\":"<<sizeof(Row)<<"},\"calls\":[";
    for(std::size_t i=0;i<r.calls.size();++i){if(i)out<<',';WriteRow(out,r.calls[i]);}out<<"],\"pairs\":[";
    for(std::size_t i=0;i<r.pairs.size();++i){if(i)out<<',';const auto& v=r.pairs[i];out<<"{\"id\":"<<v.id<<",\"kind\":"<<v.kind<<",\"directSequence\":"<<v.directSequence<<",\"forwardSequence\":"<<v.forwardSequence<<",\"equal\":"<<Bool(v.equal)<<'}';}
    out<<"],\"failures\":[";for(std::size_t i=0;i<r.failures.size();++i){if(i)out<<',';out<<Quote(r.failures[i]);}
    out<<"],\"controlledInputs\":{\"agent\":"<<Quote(Utf8(Agent))<<",\"invalidUrl\":"<<Quote(Utf8(InvalidUrl))<<",\"openAccessType\":1,\"normalOpenFlags\":0,\"fifthArgumentProbeFlags\":1073741824,\"autoProxyFlags\":0,\"autoDetectFlags\":0,\"autoConfigUrl\":0,\"reservedPointer\":0,\"reserved\":0,\"autoLogon\":0},\"scope\":{\"httpRequestsIssued\":false,\"autoDetectOrPacEnabled\":false,\"settingsModified\":false,\"privateConfigStringsLogged\":false,\"hostInitializedResolver\":false,\"importerInitializedResolver\":false,\"trueStaticDependencyChain\":true,\"gameExecuted\":false,\"installed\":false}}\n";return out.str();
}
} // namespace
int wmain(int argc,wchar_t** argv)
{
    Report report{};report.actualProcessId=GetCurrentProcessId();report.mainThreadId=GetCurrentThreadId();
    try{
        for(int i=1;i<argc;++i){const std::wstring arg=argv[i];if(arg==L"--output"&&i+1<argc)report.outputPath=argv[++i];else return 2;}
        if(!std::filesystem::path(report.outputPath).is_absolute())return 2;
        report.sehCode=Guard(&report);if(report.sehCode)report.error="Native exception in exact owned cold fixture";
    }catch(const std::exception& e){report.error=e.what();}catch(...){report.error="Unexpected cold fixture exception";}
    try{if(report.outputPath.empty())return 2;std::vector<Row> rows;rows.reserve(report.calls.size());for(const auto& call:report.calls)rows.push_back(call.row);
        const auto path=std::filesystem::path(report.outputPath).parent_path()/(std::filesystem::path(report.outputPath).stem().wstring()+L".calls.bin");
        report.callsSnapshotPath=Utf8(path.filename().wstring());report.callsSnapshotSha=Hash(rows.data(),rows.size()*sizeof(Row));WriteBytes(path,rows.data(),rows.size()*sizeof(Row));
        const auto json=Serialize(report);WriteBytes(report.outputPath,json.data(),json.size());}catch(...){return 2;}
    return report.finished&&report.failures.empty()&&report.error.empty()&&!report.sehCode?0:1;
}
