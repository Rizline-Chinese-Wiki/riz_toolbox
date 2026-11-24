#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include "FixtureApi.h"
#include <array>
#include <filesystem>
#include <fstream>
#include <memory>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

// The root-owned helper has no static CLR dependency. It is invoked only after
// the true static native chain and normal workload have completed.
std::uint32_t RizEngineOwned_RunManaged(const wchar_t*,const wchar_t*,const wchar_t*,const wchar_t*);

namespace {
using namespace RizEngineOwned;
struct Sha256 {
    std::uint32_t h[8]{0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19};
    unsigned char buffer[64]{};
    std::uint64_t bytes{};
    unsigned used{};
    static std::uint32_t R(std::uint32_t x, unsigned n) { return (x >> n) | (x << (32 - n)); }
    void Block() {
        constexpr std::uint32_t k[64]{0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,
            0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,
            0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,
            0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,
            0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,
            0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,
            0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,
            0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2};
        std::uint32_t w[64]{};
        for(unsigned i=0;i<16;++i)w[i]=(std::uint32_t(buffer[4*i])<<24)|(std::uint32_t(buffer[4*i+1])<<16)|(std::uint32_t(buffer[4*i+2])<<8)|buffer[4*i+3];
        for(unsigned i=16;i<64;++i)w[i]=w[i-16]+(R(w[i-15],7)^R(w[i-15],18)^(w[i-15]>>3))+w[i-7]+(R(w[i-2],17)^R(w[i-2],19)^(w[i-2]>>10));
        auto a=h[0],b=h[1],c=h[2],d=h[3],e=h[4],f=h[5],g=h[6],v=h[7];
        for(unsigned i=0;i<64;++i){const auto t1=v+(R(e,6)^R(e,11)^R(e,25))+((e&f)^(~e&g))+k[i]+w[i];const auto t2=(R(a,2)^R(a,13)^R(a,22))+((a&b)^(a&c)^(b&c));v=g;g=f;f=e;e=d+t1;d=c;c=b;b=a;a=t1+t2;}
        h[0]+=a;h[1]+=b;h[2]+=c;h[3]+=d;h[4]+=e;h[5]+=f;h[6]+=g;h[7]+=v;
    }
    void Feed(const unsigned char* p,std::size_t size){bytes+=size;for(std::size_t i=0;i<size;++i){buffer[used++]=p[i];if(used==64){Block();used=0;}}}
    void Finish(unsigned char* out){const auto bits=bytes*8;const unsigned char one=0x80,zero=0;Feed(&one,1);while(used!=56)Feed(&zero,1);unsigned char suffix[8];for(unsigned i=0;i<8;++i)suffix[i]=static_cast<unsigned char>(bits>>(56-8*i));Feed(suffix,8);for(unsigned i=0;i<8;++i)for(unsigned j=0;j<4;++j)out[4*i+j]=static_cast<unsigned char>(h[i]>>(24-8*j));}
};
struct Artifact {std::string path,sha256;};
struct Report {
    std::wstring output,runtimeConfig,managedAssembly,hostfxr,managedArgs;
    bool exceptions{},managedRequested{},finished{};
    std::uint32_t processId{},mainThreadId{},sehCode{},managedResult{UINT32_MAX};
    std::uint64_t checks{};std::vector<std::string> failures;std::string error;
    std::unique_ptr<Snapshot> initial,final;
    Artifact initialFile,finalFile,rowsFile;
};
void Check(Report& r,bool okay,const char* name){++r.checks;if(!okay)r.failures.emplace_back(name);}
std::string Utf8(const std::wstring& value)
{
    if(value.empty())return{};
    const auto size=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value.data(),static_cast<int>(value.size()),nullptr,0,nullptr,nullptr);
    if(size<=0)throw std::runtime_error("UTF8 conversion failed");std::string result(static_cast<std::size_t>(size),'\0');
    if(WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,value.data(),static_cast<int>(value.size()),result.data(),size,nullptr,nullptr)!=size)throw std::runtime_error("UTF8 conversion failed");
    return result;
}
std::string Quote(const std::string& value)
{std::string result="\"";constexpr char hex[]="0123456789abcdef";for(unsigned char c:value){if(c=='"'||c=='\\'){result+='\\';result+=static_cast<char>(c);}else if(c<32){result+="\\u00";result+=hex[c>>4];result+=hex[c&15];}else result+=static_cast<char>(c);}return result+'"';}
const char* Bool(bool value){return value?"true":"false";}
std::string Hex(const unsigned char* bytes,std::size_t count)
{std::string result;constexpr char hex[]="0123456789abcdef";result.reserve(count*2);for(std::size_t i=0;i<count;++i){result+=hex[bytes[i]>>4];result+=hex[bytes[i]&15];}return result;}
std::string Hash(const void* bytes,std::size_t count)
{Sha256 hash;hash.Feed(static_cast<const unsigned char*>(bytes),count);unsigned char digest[32]{};hash.Finish(digest);return Hex(digest,sizeof(digest));}
void Save(const std::filesystem::path& path,const void* bytes,std::size_t count)
{std::ofstream file(path,std::ios::binary|std::ios::trunc);if(!file)throw std::runtime_error("Cannot create actual fixture evidence");file.write(static_cast<const char*>(bytes),static_cast<std::streamsize>(count));file.close();if(!file)throw std::runtime_error("Cannot persist actual fixture evidence");}
Artifact SaveArtifact(const Report& r,const wchar_t* suffix,const void* bytes,std::size_t count)
{
    const auto path=std::filesystem::path(r.output).parent_path()/(std::filesystem::path(r.output).stem().wstring()+suffix);
    Save(path,bytes,count);return {Utf8(path.filename().wstring()),Hash(bytes,count)};
}
void ReadSnapshot(Report& r,Snapshot& output)
{output.version=Version;output.byteSize=sizeof(output);Check(r,RizEngineOwned_GetSnapshot(&output)==0,"actual-fixed-owned-snapshot-return0");Check(r,output.version==Version&&output.byteSize==sizeof(output)&&output.rowCount<=Capacity,"actual-snapshot-version-size-bounds");}
void One(Report& r,std::uint32_t kind,std::uint32_t mode)
{Row row{};row.version=Version;row.byteSize=sizeof(row);Check(r,RizEngineOwned_Run(kind,mode,&row)==0,"actual-fixed-target-invocation-return0");Check(r,row.kind==kind&&row.mode==mode&&row.phase==Outside&&row.threadId==GetCurrentThreadId(),"actual-owned-call-identity");}
struct WorkerPlan {Report* report{};HANDLE start{};std::uint32_t number{};DWORD threadId{};bool failed{};};
DWORD WINAPI Worker(void* parameter)
{
    auto& plan=*static_cast<WorkerPlan*>(parameter);plan.threadId=GetCurrentThreadId();
    if(WaitForSingleObject(plan.start,15000)!=WAIT_OBJECT_0){plan.failed=true;return 1;}
    for(std::uint32_t kind=0;kind<4;++kind){Row row{};row.version=Version;row.byteSize=sizeof(row);
        if(RizEngineOwned_Run(kind,0,&row)!=0||row.kind!=kind||row.mode!=0||row.threadId!=plan.threadId)plan.failed=true;}
    return plan.failed?1u:0u;
}
void Workers(Report& r)
{
    const auto start=CreateEventW(nullptr,TRUE,FALSE,nullptr);if(!start)throw std::runtime_error("Cannot create exact owned worker gate");
    std::array<WorkerPlan,3> plans{};std::array<HANDLE,3> handles{};std::size_t created{};
    for(std::size_t i=0;i<plans.size();++i){plans[i].report=&r;plans[i].start=start;plans[i].number=static_cast<std::uint32_t>(i)+1;
        handles[i]=CreateThread(nullptr,0,Worker,&plans[i],0,nullptr);if(!handles[i])break;++created;}
    const bool released=SetEvent(start)!=FALSE;
    for(std::size_t i=0;i<created;++i){
        // A child with an active thread cannot safely unwind its stack-owned
        // plans. Exit this exact owned process on a finite timeout instead.
        if(WaitForSingleObject(handles[i],30000)!=WAIT_OBJECT_0)ExitProcess(3);
        DWORD code{};Check(r,GetExitCodeThread(handles[i],&code)&&code==0&&!plans[i].failed,"owned-worker-completed-normal-targets");CloseHandle(handles[i]);
        Check(r,plans[i].threadId!=r.mainThreadId,"owned-worker-has-real-separate-thread");
        for(std::size_t j=0;j<i;++j)Check(r,plans[i].threadId!=plans[j].threadId,"owned-workers-have-three-distinct-threads");
    }
    CloseHandle(start);Check(r,released&&created==plans.size(),"all-three-owned-workers-started-and-released");
}
void Run(Report& r)
{
    r.initial=std::make_unique<Snapshot>();r.final=std::make_unique<Snapshot>();ReadSnapshot(r,*r.initial);
    Check(r,r.initial->phaseMask==0xf0&&r.initial->rowCount==16&&r.initial->checks>0&&r.initial->failures==0,"true-TLS-C-CPP-DllMain-completed-before-main");
    for(std::uint32_t i=0;i<16&&i<r.initial->rowCount;++i){const auto& row=r.initial->rows[i];
        Check(r,row.phase==Tls+i/4&&row.kind==i%4&&row.mode==0&&row.sequence==i+1&&row.threadId==r.mainThreadId,"true-initializer-call-order-and-main-thread");}
    r.initialFile=SaveArtifact(r,L".initial.bin",r.initial.get(),sizeof(Snapshot));
    if(!r.failures.empty())throw std::runtime_error("Actual static first-install initialization failed");
    for(std::uint32_t kind=0;kind<4;++kind)One(r,kind,0);Workers(r);
    if(r.managedRequested){r.managedResult=RizEngineOwned_RunManaged(r.runtimeConfig.c_str(),r.managedAssembly.c_str(),r.hostfxr.c_str(),r.managedArgs.c_str());Check(r,r.managedResult==0,"same-process-managed-adoption-workload-return0");}
    if(r.exceptions)for(std::uint32_t mode=1;mode<=2;++mode)for(std::uint32_t kind=0;kind<4;++kind)One(r,kind,mode);
    ReadSnapshot(r,*r.final);Check(r,r.final->phaseMask==0x8f0&&r.final->rowCount>=32&&r.final->failures==0,"final-actual-owned-evidence-has-no-native-fixture-failures");
    if(!r.managedRequested)Check(r,r.final->rowCount==(r.exceptions?40u:32u),"exact-native-only-call-count");
    r.finished=true;
}
DWORD Guard(Report* report)
{__try{Run(*report);return 0;}__except(GetExceptionCode()==0xe06d7363u?EXCEPTION_CONTINUE_SEARCH:EXCEPTION_EXECUTE_HANDLER){return GetExceptionCode();}}
template<class T>void Array(std::ostream& out,const T* values,std::size_t count)
{out<<'[';for(std::size_t i=0;i<count;++i){if(i)out<<',';out<<values[i];}out<<']';}
void Publication(std::ostream& out,const RizEnginePublicationV4& value)
{
    out<<'{';
#define NUMBER(field) out<<"\"" #field "\":"<<value.field<<','
    NUMBER(version);NUMBER(byteSize);NUMBER(state);NUMBER(failure);NUMBER(epoch);NUMBER(ownerThread);NUMBER(processId);NUMBER(attachReason);NUMBER(startupStatic);NUMBER(claimed);NUMBER(profile);
    NUMBER(configuredMask);NUMBER(boundMask);NUMBER(committedMask);NUMBER(sourceWriteAttempts);NUMBER(sourceWriteCompletions);NUMBER(protectionOutstanding);NUMBER(eventCount);NUMBER(overflow);
    NUMBER(reservedPointer);NUMBER(bootstrapModule);NUMBER(kernelModule);NUMBER(targetModule);NUMBER(executableModule);NUMBER(admissionFlags);NUMBER(verifyFailures);
#undef NUMBER
    out<<"\"modules\":[";
    for(unsigned i=0;i<4;++i){if(i)out<<',';const auto& m=value.modules[i];out<<'{';
#define NUMBER(field) out<<"\"" #field "\":"<<m.field<<','
        NUMBER(kind);NUMBER(machine);NUMBER(imageSize);NUMBER(timeDateStamp);NUMBER(module);NUMBER(fileBytes);NUMBER(fileIndex);NUMBER(volumeSerial);NUMBER(flags);NUMBER(heldFile);NUMBER(entryRva);NUMBER(exceptionRva);
#undef NUMBER
        out<<"\"sha256\":"<<Quote(Hex(m.sha256,32))<<'}';}out<<"],\"rows\":[";
    for(unsigned i=0;i<4;++i){if(i)out<<',';const auto& v=value.rows[i];out<<'{';
#define NUMBER(field) out<<"\"" #field "\":"<<v.field<<','
        NUMBER(version);NUMBER(byteSize);NUMBER(kind);NUMBER(prefixBytes);NUMBER(module);NUMBER(target);NUMBER(original);NUMBER(continuation);NUMBER(entry);NUMBER(epoch);NUMBER(rva);NUMBER(originalProtection);NUMBER(observedProtection);NUMBER(flags);
        NUMBER(protectAttempts);NUMBER(protectSucceeded);NUMBER(sourceWriteAttempts);NUMBER(sourceBytesWritten);NUMBER(readbackAttempts);NUMBER(readbackVerified);NUMBER(restoreAttempts);NUMBER(restoreSucceeded);NUMBER(flushAttempts);NUMBER(flushSucceeded);NUMBER(protectionOutstanding);NUMBER(lastWin32Error);NUMBER(helperEpoch);NUMBER(helperSlot);NUMBER(reserved);NUMBER(runtimeFunctionCount);
#undef NUMBER
        out<<"\"originalBytes\":"<<Quote(Hex(v.originalBytes,32))<<",\"patchBytes\":"<<Quote(Hex(v.patchBytes,32))<<",\"observedBytes\":"<<Quote(Hex(v.observedBytes,32))
            <<",\"moduleSha256\":"<<Quote(Hex(v.moduleSha256,32))<<",\"bodySha256\":"<<Quote(Hex(v.bodySha256,32))<<",\"reservedTail\":";Array(out,v.reservedTail,3);out<<'}';}out<<"],\"events\":[";
    for(unsigned i=0;i<64;++i){if(i)out<<',';const auto& e=value.events[i];out<<"{\"sequence\":"<<e.sequence<<",\"stage\":"<<e.stage<<",\"kind\":"<<e.kind<<",\"state\":"<<e.state<<",\"failure\":"<<e.failure<<",\"win32Error\":"<<e.win32Error<<",\"reserved\":"<<e.reserved<<'}';}
    out<<"],\"reserved\":"<<Quote(Hex(value.reserved,sizeof(value.reserved)))<<'}';
}
void WriteRow(std::ostream& out,const Row& value)
{
    out<<'{';
#define NUMBER(field) out<<"\"" #field "\":"<<value.field<<','
    NUMBER(version);NUMBER(byteSize);NUMBER(phase);NUMBER(kind);NUMBER(mode);NUMBER(threadId);NUMBER(returned);NUMBER(rawReturn);NUMBER(caught);NUMBER(exceptionCode);NUMBER(exceptionFlags);NUMBER(exceptionParameters);
    NUMBER(verifyBefore);NUMBER(verifyAfter);NUMBER(infoBeforeResult);NUMBER(infoAfterResult);NUMBER(sequence);NUMBER(target);NUMBER(releaseCount);NUMBER(releaseZeroFlag);NUMBER(detachZero);NUMBER(diagnosticsFailed);NUMBER(exceptionPayload);NUMBER(exceptionKind);
    NUMBER(infoBeforeErrorBefore);NUMBER(infoBeforeErrorAfter);NUMBER(infoAfterErrorBefore);NUMBER(infoAfterErrorAfter);NUMBER(outputByte);NUMBER(outputNodeMatches);NUMBER(nodeRefBefore);NUMBER(nodeRefAfter);
#undef NUMBER
    out<<"\"args\":";Array(out,value.args,4);out<<",\"calls\":";Array(out,value.calls,4);out<<",\"bodyBefore\":";Array(out,value.bodyBefore,4);out<<",\"bodyAfter\":";Array(out,value.bodyAfter,4);
    out<<",\"before\":";Publication(out,value.before);out<<",\"after\":";Publication(out,value.after);out<<'}';
}
void ArtifactJson(std::ostream& out,const Artifact& file)
{out<<"\"path\":"<<Quote(file.path)<<",\"sha256\":"<<Quote(file.sha256);}
void SnapshotJson(std::ostream& out,const Snapshot* value,const Artifact& file)
{
    out<<'{';ArtifactJson(out,file);
    if(value){out<<",\"version\":"<<value->version<<",\"byteSize\":"<<value->byteSize<<",\"phaseMask\":"<<value->phaseMask<<",\"rowCount\":"<<value->rowCount
        <<",\"checks\":"<<value->checks<<",\"failures\":"<<value->failures<<",\"bodyCounters\":";Array(out,value->bodyCounters,4);}out<<'}';
}
std::string Serialize(const Report& r)
{
    std::ostringstream out;out<<"{\"kind\":\"engine-bootstrap-owned-host\",\"schemaVersion\":1,\"actualProcessId\":"<<r.processId<<",\"mainThreadId\":"<<r.mainThreadId
        <<",\"passed\":"<<Bool(r.finished&&r.failures.empty()&&r.error.empty()&&!r.sehCode)<<",\"finished\":"<<Bool(r.finished)<<",\"checks\":"<<r.checks<<",\"sehCode\":"<<r.sehCode<<",\"error\":"<<Quote(r.error)
        <<",\"arguments\":{\"output\":"<<Quote(Utf8(r.output))<<",\"exceptions\":"<<Bool(r.exceptions)<<",\"runtimeConfig\":"<<Quote(Utf8(r.runtimeConfig))<<",\"managedAssembly\":"<<Quote(Utf8(r.managedAssembly))
        <<",\"hostfxr\":"<<Quote(Utf8(r.hostfxr))<<",\"managedArgs\":"<<Quote(Utf8(r.managedArgs))<<"},\"managedRequested\":"<<Bool(r.managedRequested)<<",\"managedResult\":"<<r.managedResult;
    out<<",\"initialSnapshot\":";SnapshotJson(out,r.initial.get(),r.initialFile);out<<",\"finalSnapshot\":";SnapshotJson(out,r.final.get(),r.finalFile);
    out<<",\"rowsSnapshot\":{";ArtifactJson(out,r.rowsFile);out<<",\"rowByteSize\":"<<sizeof(Row)<<",\"rowCount\":"<<(r.final?r.final->rowCount:0)<<"},\"rows\":[";
    if(r.final)for(std::uint32_t i=0;i<r.final->rowCount&&i<Capacity;++i){if(i)out<<',';WriteRow(out,r.final->rows[i]);}out<<"],\"failures\":[";
    for(std::size_t i=0;i<r.failures.size();++i){if(i)out<<',';out<<Quote(r.failures[i]);}
    out<<"],\"scope\":{\"trueStaticDependencyChain\":true,\"hostConfiguredOrPublishedTargets\":false,\"arbitraryTargetAccepted\":false,\"nativeExceptionWorkloadSelected\":"<<Bool(r.exceptions)
        <<",\"sameProcessManagedSelected\":"<<Bool(r.managedRequested)<<",\"gameExecuted\":false,\"installed\":false,\"wholeLoaderSafetyVerified\":false}}\n";return out.str();
}
}
int wmain(int argc,wchar_t** argv)
{
    Report report{};report.processId=GetCurrentProcessId();report.mainThreadId=GetCurrentThreadId();
    try{
        for(int i=1;i<argc;++i){const std::wstring arg=argv[i];
            if(arg==L"--output"&&i+1<argc)report.output=argv[++i];else if(arg==L"--exceptions")report.exceptions=true;
            else if(arg==L"--runtime-config"&&i+1<argc)report.runtimeConfig=argv[++i];else if(arg==L"--managed-assembly"&&i+1<argc)report.managedAssembly=argv[++i];
            else if(arg==L"--hostfxr"&&i+1<argc)report.hostfxr=argv[++i];else if(arg==L"--managed-args"&&i+1<argc)report.managedArgs=argv[++i];else return 2;}
        if(!std::filesystem::path(report.output).is_absolute())return 2;
        const std::array<std::wstring,4> managed{report.runtimeConfig,report.managedAssembly,report.hostfxr,report.managedArgs};unsigned supplied{};
        for(const auto& path:managed)if(!path.empty()){if(!std::filesystem::path(path).is_absolute())return 2;++supplied;}
        if(supplied!=0&&supplied!=4)return 2;report.managedRequested=supplied==4;if(report.managedRequested&&report.exceptions)return 2;
        report.sehCode=Guard(&report);if(report.sehCode)report.error="Native exception at exact owned host boundary";
    }catch(const std::exception& error){report.error=error.what();}catch(...){report.error="Unexpected owned host exception";}
    try{
        if(report.output.empty())return 2;
        if(report.final){report.finalFile=SaveArtifact(report,L".snapshot.bin",report.final.get(),sizeof(Snapshot));
            const auto count=report.final->rowCount<=Capacity?report.final->rowCount:Capacity;report.rowsFile=SaveArtifact(report,L".rows.bin",report.final->rows,static_cast<std::size_t>(count)*sizeof(Row));}
        const auto json=Serialize(report);Save(report.output,json.data(),json.size());
    }catch(...){return 2;}
    return report.finished&&report.failures.empty()&&report.error.empty()&&!report.sehCode?0:1;
}
