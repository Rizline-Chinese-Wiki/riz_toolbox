#include "TransportOriginalFixture.h"
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>
#include <sstream>
#include <iomanip>
#include <intrin.h>

extern "C" {
uintptr_t RizEngineTransportContinuation[9]{};
TransportCallRecord* RtTransportActiveCall{};
std::uint32_t RtTransportTraceRequested{};
}
namespace {
constexpr std::uint64_t Magic=0x0031524f545a4952ULL;
constexpr DWORD InputError=0x12345678,OutputError=0x76543210,OwnedSeh=0xe0130042;
constexpr std::uint64_t Canary=0xd125e236f347a458ULL,R11Marker=0xa135b246c357d468ULL;
constexpr std::uint64_t Nv[]{0x1122334455667788ULL,0x2233445566778899ULL,0x33445566778899aaULL,0x445566778899aabbULL,
    0x5566778899aabbccULL,0x66778899aabbccddULL,0x778899aabbccddeeULL,0x8899aabbccddeeffULL};
constexpr unsigned MaximumCalls=128,MaximumUnwinds=1024;
TransportCallRecord calls[MaximumCalls]{};TransportUnwindRecord unwinds[MaximumUnwinds]{};
unsigned checks{},failures{},callCount{},unwindCount{},baselineCalls{},originalCalls{},sehCaught{},cppCaught{};
unsigned persistenceChecks{},traceSteps{},traceHits{};bool traceActive{},traceStopped{};DWORD traceThread{};
struct OwnedException{std::uint64_t token;unsigned kind,branch;};
struct Shape{
    const char* name;const unsigned char* baseline;const unsigned char* continuation;const unsigned char* baselineEnd;
    const unsigned char* original;const unsigned char* prefixEnd;const unsigned char* originalEnd;
    unsigned prefix;unsigned offsets[10];unsigned boundaries;
};
#define ADD_SHAPE(Name,Length,Count,...) {#Name,reinterpret_cast<const unsigned char*>(&RtTransportBaseline##Name), \
    &RtTransportBaseline##Name##PrefixEnd,&RtTransportBaseline##Name##End,reinterpret_cast<const unsigned char*>(&RizEngineTransportOriginal##Name), \
    RizEngineTransportOriginal##Name##PrefixEnd,RizEngineTransportOriginal##Name##End,Length,{__VA_ARGS__},Count}
const Shape shapes[]{
    ADD_SHAPE(Register,15,4,0,5,10,15),ADD_SHAPE(Remove,14,5,0,5,6,10,14),
    ADD_SHAPE(Consume,14,5,0,4,8,11,14),ADD_SHAPE(Pump,15,8,0,4,5,6,7,9,13,15),
    ADD_SHAPE(Drain,14,5,0,2,6,11,14),ADD_SHAPE(Callback,17,5,0,3,10,13,17),
    ADD_SHAPE(Yield,15,4,0,5,10,15),ADD_SHAPE(Dispatch,14,4,0,2,9,14),
    ADD_SHAPE(Batch,14,6,0,5,6,8,10,14)
};
#undef ADD_SHAPE
static_assert(sizeof(shapes)/sizeof(shapes[0])==9);
void Check(bool value,const char* reason){++checks;if(!value){++failures;std::fprintf(stderr,"OriginalFixture check failed: %s\n",reason);}}
void ReadNv(const CONTEXT& value,std::uint64_t* out){out[0]=value.Rbx;out[1]=value.Rbp;out[2]=value.Rsi;out[3]=value.Rdi;
    out[4]=value.R12;out[5]=value.R13;out[6]=value.R14;out[7]=value.R15;}
bool EqualNv(const std::uint64_t* value){return std::memcmp(value,Nv,sizeof(Nv))==0;}
bool UnwindActual(CONTEXT* copy,PRUNTIME_FUNCTION function,DWORD64 image){
    bool succeeded=false;
    __try{PVOID data{};DWORD64 establisher{};RtlVirtualUnwind(UNW_FLAG_NHANDLER,image,copy->Rip,function,copy,&data,&establisher,nullptr);succeeded=true;}
    __except(EXCEPTION_EXECUTE_HANDLER){succeeded=false;}
    return succeeded;
}
void RecordUnwind(const CONTEXT& actual,unsigned offset){
    if(unwindCount==MaximumUnwinds){Check(false,"Actual unwind receipt capacity");return;}
    auto& receipt=unwinds[unwindCount++];const auto& call=*RtTransportActiveCall;
    receipt.kind=call.kind;receipt.variant=call.variant;receipt.branch=call.branch;receipt.offset=offset;
    receipt.pc=actual.Rip;receipt.beforeRsp=actual.Rsp;receipt.expectedRip=reinterpret_cast<std::uint64_t>(&RtTransportInvokeReturn);
    receipt.expectedRsp=call.callerRspBefore;receipt.tailR11=actual.R11;receipt.rflags=actual.EFlags;receipt.actualContext=1;
    ReadNv(actual,receipt.beforeNv);CONTEXT copy=actual;DWORD64 image{};const auto function=RtlLookupFunctionEntry(actual.Rip,&image,nullptr);
    receipt.lookup=function?1u:0u;receipt.imageBase=image;
    bool okay=function!=nullptr;
    if(function){receipt.functionBegin=function->BeginAddress;receipt.functionEnd=function->EndAddress;receipt.unwindRva=function->UnwindData;
        receipt.prologSize=*(reinterpret_cast<const unsigned char*>(image+function->UnwindData)+1);
        okay=UnwindActual(&copy,function,image);}
    receipt.afterRip=copy.Rip;receipt.afterRsp=copy.Rsp;ReadNv(copy,receipt.afterNv);
    okay=okay&&copy.Rip==receipt.expectedRip&&copy.Rsp==receipt.expectedRsp&&EqualNv(receipt.afterNv);
    receipt.passed=okay?1u:0u;Check(okay,"Windows unwind of actual single-step CONTEXT");++traceHits;
}
LONG CALLBACK TraceHandler(EXCEPTION_POINTERS* exception){
    const DWORD incoming=GetLastError();
    if(!exception||exception->ExceptionRecord->ExceptionCode!=EXCEPTION_SINGLE_STEP||!traceActive
        ||GetCurrentThreadId()!=traceThread||!RtTransportActiveCall){SetLastError(incoming);return EXCEPTION_CONTINUE_SEARCH;}
    auto* actual=exception->ContextRecord;const auto& shape=shapes[RtTransportActiveCall->kind];
    const auto* pc=reinterpret_cast<const unsigned char*>(actual->Rip);++traceSteps;
    const auto* start=RtTransportActiveCall->variant?shape.original:shape.baseline;
    bool boundary=false;unsigned offset{};
    for(unsigned index=0;index<shape.boundaries;++index)if(pc==start+shape.offsets[index]){boundary=true;offset=shape.offsets[index];break;}
    if(RtTransportActiveCall->variant&&pc==shape.prefixEnd+7){boundary=true;offset=shape.prefix+7;}
    if(boundary)RecordUnwind(*actual,offset);
    if(pc==shape.continuation){traceStopped=true;traceActive=false;actual->EFlags&=~0x100u;}
    else if(traceSteps>64){Check(false,"Single-step did not reach actual owned continuation");traceActive=false;actual->EFlags&=~0x100u;}
    else actual->EFlags|=0x100u; // Windows clears TF when delivering each single-step exception.
    SetLastError(incoming);return EXCEPTION_CONTINUE_EXECUTION;
}
int SehFilter(EXCEPTION_POINTERS* error,TransportCallRecord* record){
    if(!error||error->ExceptionRecord->ExceptionCode!=OwnedSeh)return EXCEPTION_CONTINUE_SEARCH;
    const auto* native=error->ExceptionRecord;record->exceptionCode=native->ExceptionCode;
    const bool exact=native->NumberParameters==2&&native->ExceptionInformation[0]==record->kind
        &&native->ExceptionInformation[1]==record->branch;
    record->caught=exact?1u:0u;return EXCEPTION_EXECUTE_HANDLER;
}
void InvokeSeh(void* target,TransportCallRecord* record){
    __try{RtTransportInvoke(target,record->args,record);}
    __except(SehFilter(GetExceptionInformation(),record)){++sehCaught;}
}
void InvokeCpp(void* target,TransportCallRecord* record){
    try{RtTransportInvoke(target,record->args,record);}
    catch(const OwnedException& value){++cppCaught;record->caught=value.token==0xc135d246e357f468ULL&&value.kind==record->kind&&value.branch==record->branch?1u:0u;}
    catch(...){record->caught=0;Check(false,"Original C++ exception object changed");}
}
std::uint64_t ExpectedResult(const TransportCallRecord& record){
    if(record.kind==0)return record.args[1];
    if(record.kind==5)return record.branch?0x12340001ULL:1ULL;
    return (0xabcdef0100000000ULL|((static_cast<std::uint64_t>(record.kind)+1)<<16))
        ^record.tail[4]^record.tail[5]^record.tail[6]^record.tail[7];
}
void ValidateNormal(TransportCallRecord& record){
    const bool flagsKind=record.kind==2||record.kind==4||record.kind==5||record.kind==7;
    Check(record.tailCount==1,"Exactly one real continuation per actual call");
    Check(record.resultRax==ExpectedResult(record),"Actual full RAX result");
    Check(record.tail[4]==record.args[0]&&record.tail[5]==(record.kind==2?record.args[0]:record.args[1])
        &&record.tail[6]==record.args[2]&&record.tail[7]==record.args[3],"Four live arguments follow the exact fixed prefix");
    const auto prefixRax=record.kind==1||record.kind==2?0x3344556677889900ULL:record.kind==5?record.args[1]:0x9135792468abcdefULL;
    Check(record.tail[0]==prefixRax,"Actual RAX follows the independent fixed-prefix expectation");
    Check(record.callerRspBefore==record.callerRspAfter&&record.canaryBefore==Canary&&record.canaryAfter==Canary,"Actual caller stack and independent canary");
    Check(EqualNv(record.afterNv),"Actual nonvolatile register preservation");
    Check(record.lastErrorBefore==InputError&&record.lastErrorAfter==OutputError,"Actual LastError preservation relative to baseline original");
    Check(traceStopped&&traceHits==shapes[record.kind].boundaries+(record.variant?1u:0u),"Every actual instruction boundary including original MOV/JMP was observed");
    if(flagsKind)Check(((record.tail[3]&0x40)!=0)==(record.branch!=0),"Actual continuation consumes preserved prefix CMP ZF");
    if(record.kind==5)Check(record.tail[1]==record.callerRspBefore-8&&record.r11Write==R11Marker,"Callback live R11 points to its real entry stack");
    if(record.kind==0)Check(*reinterpret_cast<const std::uint64_t*>(record.args[1])==(record.args[0]^record.args[2]),"Register actual output-slot side effect");
}
void ComparePair(const TransportCallRecord& baseline,const TransportCallRecord& original){
    Check(baseline.branch==original.branch&&baseline.tailCount==original.tailCount&&baseline.resultRax==original.resultRax,"Split original and same-byte baseline behavior");
    Check(baseline.tail[0]==original.tail[0]&&std::memcmp(baseline.tail+4,original.tail+4,4*sizeof(std::uint64_t))==0,
        "Four live argument registers and RAX match the actual unsplit prefix");
    constexpr std::uint64_t ArithmeticFlags=0x8d5;
    Check((baseline.tail[3]&ArithmeticFlags)==(original.tail[3]&ArithmeticFlags),"Original MOV/JMP tail preserves arithmetic flags");
    Check(baseline.lastErrorAfter==original.lastErrorAfter&&baseline.canaryAfter==original.canaryAfter,"Baseline LastError and canary equivalence");
}
alignas(16) unsigned char inputObject[0x100]{};
alignas(16) unsigned char callbackNode[0x100]{};
std::uint64_t outputSlot{};
void One(unsigned kind,unsigned variant,unsigned branch,unsigned exceptionKind){
    if(callCount==MaximumCalls){Check(false,"Call receipt capacity");return;}
    auto& record=calls[callCount++];record.kind=kind;record.variant=variant;record.branch=branch;record.exceptionKind=exceptionKind;
    auto* object=reinterpret_cast<std::uint64_t*>(inputObject);std::memset(inputObject,0,sizeof(inputObject));std::memset(callbackNode,0,sizeof(callbackNode));
    object[0x40/8]=0x3344556677889900ULL;object[0x38/8]=branch?0:9;object[0x10/8]=branch?0:9;
    record.args[0]=reinterpret_cast<std::uint64_t>(inputObject);record.args[1]=reinterpret_cast<std::uint64_t>(&outputSlot);
    record.args[2]=branch?object[0x40/8]:object[0x40/8]+1;record.args[3]=0x7346a52b1908fedcULL;
    if(kind==5){record.args[1]=reinterpret_cast<std::uint64_t>(callbackNode);*reinterpret_cast<std::uint64_t*>(callbackNode+0x58)=branch?record.args[0]:record.args[0]+8;}
    outputSlot=~(record.args[0]^record.args[2]);
    // Prefix-generated flags are validated against this independently retained
    // expected input, even though the actual tail overwrites record.branch.
    const unsigned expectedBranch=branch;const unsigned beforeFailures=failures;
    RtTransportActiveCall=&record;RtTransportTraceRequested=exceptionKind?0u:1u;traceThread=GetCurrentThreadId();traceSteps=traceHits=0;
    traceActive=exceptionKind==0;traceStopped=false;SetLastError(InputError);record.lastErrorBefore=GetLastError();
    auto* target=const_cast<unsigned char*>(variant?shapes[kind].original:shapes[kind].baseline);
    if(variant)++originalCalls;else ++baselineCalls;
    if(exceptionKind==1)InvokeSeh(target,&record);
    else if(exceptionKind==2)InvokeCpp(target,&record);
    else RtTransportInvoke(target,record.args,&record);
    record.lastErrorAfter=GetLastError();
    if(exceptionKind==0)Check(!traceActive&&(__readeflags()&0x100u)==0,"Actual trap flag drained before returning to the caller");
    traceActive=false;RtTransportTraceRequested=0;
    Check(record.branch==expectedBranch,"Actual CMP branch matches independent owned inputs");
    if(exceptionKind){Check(record.tailCount==1&&record.caught==1,"Original SEH/C++ exception preserved through actual owned continuation");}
    else ValidateNormal(record);
    record.passed=failures==beforeFailures?1u:0u;RtTransportActiveCall=nullptr;
}
bool Absolute(const wchar_t* path){return path&&((std::wcslen(path)>3&&path[1]==L':'&&(path[2]==L'/'||path[2]==L'\\'))||(path[0]==L'\\'&&path[1]==L'\\'));}
}

extern "C" std::uint64_t RtTransportOwnedBody(TransportCallRecord* record){
    // Called by each real ASM continuation after its machine state was copied.
    if(record!=RtTransportActiveCall){Check(false,"Unexpected actual owned body record");return 0;}
    if(record->kind==5)record->r11Write=*reinterpret_cast<const std::uint64_t*>(record->tail[1]+0x20);
    if(record->exceptionKind==1){ULONG_PTR values[]{record->kind,record->branch};SetLastError(OutputError);RaiseException(OwnedSeh,0,2,values);Check(false,"Owned SEH unexpectedly resumed");}
    if(record->exceptionKind==2){SetLastError(OutputError);throw OwnedException{0xc135d246e357f468ULL,record->kind,record->branch};}
    if(record->kind==0)*reinterpret_cast<std::uint64_t*>(record->args[1])=record->args[0]^record->args[2];
    const auto result=ExpectedResult(*record);SetLastError(OutputError);return result;
}

namespace {
// Literal instructions from the pinned image, independently compared with both
// actual linked bodies. No game image is loaded by this owned fixture.
constexpr unsigned char Pinned[9][17]{
    {0x48,0x89,0x5c,0x24,0x08,0x48,0x89,0x74,0x24,0x10,0x48,0x89,0x7c,0x24,0x18},
    {0x48,0x89,0x5c,0x24,0x08,0x57,0x48,0x83,0xec,0x20,0x48,0x8b,0x41,0x40},
    {0x48,0x83,0xec,0x28,0x48,0x8b,0x41,0x40,0x48,0x8b,0xd1,0x49,0x3b,0xc0},
    {0x89,0x54,0x24,0x10,0x53,0x55,0x57,0x41,0x55,0x48,0x83,0xec,0x78,0x8b,0xea},
    {0x40,0x55,0x48,0x83,0xec,0x30,0x48,0x83,0x79,0x38,0x00,0x48,0x8b,0xe9},
    {0x4c,0x8b,0xdc,0x48,0x81,0xec,0x98,0x00,0x00,0x00,0x48,0x8b,0xc2,0x48,0x39,0x4a,0x58},
    {0x48,0x89,0x5c,0x24,0x08,0x48,0x89,0x74,0x24,0x18,0x48,0x89,0x7c,0x24,0x20},
    {0x40,0x57,0x48,0x81,0xec,0x50,0x01,0x00,0x00,0x48,0x83,0x79,0x10,0x00},
    {0x48,0x89,0x5c,0x24,0x18,0x56,0x41,0x56,0x41,0x57,0x48,0x83,0xec,0x40}
};
void Prepare(){
    Check(GetModuleHandleW(L"UnityPlayer.dll")==nullptr&&GetModuleHandleW(L"GameAssembly.dll")==nullptr,"Owned process does not load the game");
    for(unsigned kind=0;kind<9;++kind){
        const auto& shape=shapes[kind];
        Check(static_cast<std::size_t>(shape.prefixEnd-shape.original)==shape.prefix,"Exact original prefix length");
        Check(static_cast<std::size_t>(shape.continuation-shape.baseline)==shape.prefix,"Exact baseline prefix length");
        Check(shape.originalEnd-shape.prefixEnd==10,"Fixed original MOV/JMP tail length");
        Check(std::memcmp(shape.original,Pinned[kind],shape.prefix)==0&&std::memcmp(shape.baseline,Pinned[kind],shape.prefix)==0,
            "Actual original and baseline bytes both match pinned prefix");
        Check(RizEngineTransportContinuation[kind]==0,"Continuation storage is initially unpublished");
        RizEngineTransportContinuation[kind]=reinterpret_cast<uintptr_t>(shape.continuation);
        DWORD64 image{};const auto function=RtlLookupFunctionEntry(reinterpret_cast<DWORD64>(shape.original),&image,nullptr);
        Check(function&&image+function->BeginAddress==reinterpret_cast<DWORD64>(shape.original)
            &&image+function->EndAddress==reinterpret_cast<DWORD64>(shape.originalEnd),"Actual original RF exactly covers the whole fixed body");
    }
}
void PersistRaw(const wchar_t* path,const TransportRawHeader& header){
    const auto byteSize=static_cast<std::size_t>(header.rawBytes);std::string bytes(byteSize,'\0');
    std::memcpy(bytes.data(),&header,sizeof(header));
    std::memcpy(bytes.data()+sizeof(header),calls,callCount*sizeof(TransportCallRecord));
    std::memcpy(bytes.data()+sizeof(header)+callCount*sizeof(TransportCallRecord),unwinds,unwindCount*sizeof(TransportUnwindRecord));
    FILE* file{};const auto opened=_wfopen_s(&file,path,L"wb");
    const bool wrote=opened==0&&file&&std::fwrite(bytes.data(),1,byteSize,file)==byteSize;
    ++persistenceChecks;Check(wrote,"Exact raw fwrite");
    const bool flushed=file&&std::fflush(file)==0;++persistenceChecks;Check(flushed,"Raw fflush");
    const bool closed=file&&std::fclose(file)==0;++persistenceChecks;Check(closed,"Raw fclose");
}
std::string Json(const TransportRawHeader& header){
    const char* passed=failures==0?"true":"false";std::ostringstream json;
    json<<"{\"schemaVersion\":1,\"kind\":\"engine-transport-original-fixture\",\"passed\":"<<passed
        <<",\"checks\":"<<checks<<",\"failures\":"<<failures<<",\"rawChecks\":"<<header.checks
        <<",\"rawFailures\":"<<header.failures<<",\"persistenceChecks\":"<<persistenceChecks
        <<",\"rawByteSize\":"<<header.rawBytes<<",\"callCount\":"<<callCount<<",\"unwindCount\":"<<unwindCount
        <<",\"actualCalls\":"<<callCount<<",\"baselineCalls\":"<<baselineCalls<<",\"originalCalls\":"<<originalCalls
        <<",\"sehCaught\":"<<sehCaught<<",\"cppCaught\":"<<cppCaught<<",\"fixedOriginals\":9"
        <<",\"callbackR11Verified\":"<<passed<<",\"fullEaxVerified\":"<<passed<<",\"flagsBranchesVerified\":"<<passed
        <<",\"lastErrorVerified\":"<<passed<<",\"nvStackCanaryVerified\":"<<passed<<",\"actualUnwindVerified\":"<<passed
        <<",\"selfOwnedOnly\":true,\"gameExecuted\":false,\"productionAdmissionVerified\":false,\"sourcePatched\":false"
        <<",\"actualSingleStepContexts\":true,\"exceptionsAtEveryPrefixPcVerified\":false"
        <<",\"scope\":\"Nine fixed prefixes and owned representative tails; SEH/C++ are raised in actual owned tails. No full game body or production startup is exercised.\""
        <<",\"calls\":[";
    for(unsigned index=0;index<callCount;++index){const auto& record=calls[index];if(index)json<<',';
        json<<"{\"kind\":"<<record.kind<<",\"name\":\""<<shapes[record.kind].name<<"\",\"variant\":"<<record.variant
            <<",\"branch\":"<<record.branch<<",\"exceptionKind\":"<<record.exceptionKind<<",\"caught\":"<<record.caught
            <<",\"passed\":"<<(record.passed?"true":"false")<<",\"resultRax\":"<<record.resultRax
            <<",\"tailRax\":"<<record.tail[0]<<",\"tailR11\":"<<record.tail[1]<<",\"tailRsp\":"<<record.tail[2]
            <<",\"tailRflags\":"<<record.tail[3]<<",\"callerRspBefore\":"<<record.callerRspBefore
            <<",\"callerRspAfter\":"<<record.callerRspAfter<<",\"tailCount\":"<<record.tailCount<<'}';}
    json<<"],\"unwinds\":[";
    for(unsigned index=0;index<unwindCount;++index){const auto& record=unwinds[index];if(index)json<<',';
        json<<"{\"kind\":"<<record.kind<<",\"variant\":"<<record.variant<<",\"branch\":"<<record.branch
            <<",\"offset\":"<<record.offset<<",\"pc\":"<<record.pc<<",\"imageBase\":"<<record.imageBase
            <<",\"lookup\":"<<(record.lookup?"true":"false")<<",\"passed\":"<<(record.passed?"true":"false")
            <<",\"actualContext\":true,\"expectedRip\":"<<record.expectedRip<<",\"expectedRsp\":"<<record.expectedRsp
            <<",\"afterRip\":"<<record.afterRip<<",\"afterRsp\":"<<record.afterRsp<<'}';}
    json<<"]}\n";return json.str();
}
bool PersistJson(const wchar_t* path,const std::string& value){
    FILE* file{};if(_wfopen_s(&file,path,L"wb")!=0||!file)return false;
    const bool wrote=std::fwrite(value.data(),1,value.size(),file)==value.size();
    const bool flushed=std::fflush(file)==0;const bool closed=std::fclose(file)==0;
    return wrote&&flushed&&closed;
}
}

int wmain(int argc,wchar_t** argv){
    if(argc!=4||std::wcscmp(argv[1],L"--all")!=0||!Absolute(argv[2])||!Absolute(argv[3])||_wcsicmp(argv[2],argv[3])==0){
        std::fputs("Usage: EngineTransportOriginal.Fixture.exe --all <absolute-json-path> <absolute-raw-path>\n",stderr);return 2;}
    SetErrorMode(SEM_FAILCRITICALERRORS|SEM_NOGPFAULTERRORBOX|SEM_NOOPENFILEERRORBOX);Prepare();
    auto* handler=AddVectoredExceptionHandler(1,TraceHandler);Check(handler!=nullptr,"Actual single-step handler installed");
    if(handler&&failures==0){
        for(unsigned kind=0;kind<9;++kind)for(unsigned branch=0;branch<2;++branch){
            const unsigned baselineIndex=callCount;One(kind,0,branch,0);const unsigned originalIndex=callCount;One(kind,1,branch,0);
            const unsigned beforeFailures=failures;ComparePair(calls[baselineIndex],calls[originalIndex]);
            if(failures!=beforeFailures)calls[originalIndex].passed=0;
            for(unsigned variant=0;variant<2;++variant){One(kind,variant,branch,1);One(kind,variant,branch,2);}
        }
    }
    if(handler)Check(RemoveVectoredExceptionHandler(handler)!=0,"Actual single-step handler removed");
    Check(!traceActive&&RtTransportActiveCall==nullptr&&RtTransportTraceRequested==0,"Actual trace and owned call scopes drained");
    Check(callCount==108&&baselineCalls==54&&originalCalls==54&&sehCaught==36&&cppCaught==36,"Exact actual baseline/original and exception counts");
    Check(unwindCount==202,"Exact complete per-PC unwind count");
    for(unsigned kind=0;kind<9;++kind)Check(RizEngineTransportContinuation[kind]==reinterpret_cast<uintptr_t>(shapes[kind].continuation),"Continuation storage stayed unchanged after one-time publication");
    TransportRawHeader header{};header.magic=Magic;header.version=1;header.headerBytes=static_cast<std::uint32_t>(sizeof(header));
    header.callRecordBytes=static_cast<std::uint32_t>(sizeof(TransportCallRecord));header.unwindRecordBytes=static_cast<std::uint32_t>(sizeof(TransportUnwindRecord));
    header.checks=checks;header.failures=failures;header.callCount=callCount;header.unwindCount=unwindCount;
    header.baselineCalls=baselineCalls;header.originalCalls=originalCalls;header.sehCaught=sehCaught;header.cppCaught=cppCaught;
    header.actualContext=1;header.selfOwnedOnly=1;header.rawBytes=sizeof(header)+static_cast<std::uint64_t>(callCount+unwindCount)*256;
    PersistRaw(argv[3],header);const auto json=Json(header);
    if(!PersistJson(argv[2],json)){std::fputs("Failed to persist actual fixture JSON.\n",stderr);return 1;}
    if(std::fwrite(json.data(),1,json.size(),stdout)!=json.size()||std::fflush(stdout)!=0)return 1;
    return failures?1:0;
}
