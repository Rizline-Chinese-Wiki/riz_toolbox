#include "Api.h"
#include <filesystem>
#include <fstream>
#include <sstream>
#include <stdexcept>
#include <string>

namespace {
std::string Quote(const std::string& text)
{std::string out="\"";constexpr char hex[]="0123456789abcdef";for(unsigned char c:text){if(c=='"'||c=='\\'){out+='\\';out+=static_cast<char>(c);}else if(c<32){out+="\\u00";out+=hex[c>>4];out+=hex[c&15];}else out+=static_cast<char>(c);}return out+'"';}
std::string Utf8(const std::wstring& text)
{if(text.empty())return {};const auto n=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),nullptr,0,nullptr,nullptr);if(n<=0)throw std::runtime_error("UTF8 conversion");std::string out(static_cast<std::size_t>(n),'\0');if(WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text.data(),static_cast<int>(text.size()),out.data(),n,nullptr,nullptr)!=n)throw std::runtime_error("UTF8 conversion");return out;}
std::string Hash(const void* data,std::size_t count)
{RizWinFixture::Sha256 hash;hash.Feed(static_cast<const unsigned char*>(data),count);unsigned char bytes[32];hash.Finish(bytes);constexpr char hex[]="0123456789abcdef";std::string out;for(auto b:bytes){out+=hex[b>>4];out+=hex[b&15];}return out;}
void Save(const std::filesystem::path& path,const void* data,std::size_t count)
{if(std::filesystem::exists(path))throw std::runtime_error("Existing evidence refused");std::ofstream out(path,std::ios::binary);if(!out)throw std::runtime_error("Cannot create evidence");out.write(static_cast<const char*>(data),static_cast<std::streamsize>(count));out.close();if(!out)throw std::runtime_error("Cannot persist evidence");}
struct Report {
    RizProductionCold::Snapshot snapshot{};
    std::uint32_t initialMask{},initialCalls{},initialResult{UINT32_MAX},runResult{UINT32_MAX},snapshotResult{UINT32_MAX};
};
void Run(Report& report)
{
    report.snapshot.version=RizProductionCold::Version;report.snapshot.byteSize=sizeof(report.snapshot);
    report.initialResult=RizProductionCold_GetSnapshot(&report.snapshot);report.initialMask=report.snapshot.phaseMask;report.initialCalls=report.snapshot.callCount;
    report.runResult=RizProductionCold_Run();report.snapshot.version=RizProductionCold::Version;report.snapshot.byteSize=sizeof(report.snapshot);
    report.snapshotResult=RizProductionCold_GetSnapshot(&report.snapshot);
}
DWORD Guard(Report* report)
{__try{Run(*report);return 0;}__except(GetExceptionCode()==0xe06d7363u?EXCEPTION_CONTINUE_SEARCH:EXCEPTION_EXECUTE_HANDLER){return GetExceptionCode();}}
}
int wmain(int argc,wchar_t** argv)
{
    if(argc!=3||std::wstring(argv[1])!=L"--output")return 2;
    const auto output=std::filesystem::path(argv[2]);if(!output.is_absolute())return 2;
    Report report{};DWORD exception{};std::string error;
    try{exception=Guard(&report);}catch(const std::exception& e){error=e.what();}catch(...){error="Unexpected owned fixture exception";}
    const bool passed=!exception&&error.empty()&&!report.initialResult&&report.initialMask==6&&report.initialCalls==2
        &&!report.runResult&&!report.snapshotResult&&report.snapshot.phaseMask==14&&report.snapshot.phaseCount==3
        &&report.snapshot.callCount==4&&!report.snapshot.failures;
    try{
        const auto raw=output.parent_path()/(output.stem().wstring()+L".snapshot.bin");Save(raw,&report.snapshot,sizeof(report.snapshot));
        std::ostringstream out;out<<"{\"schemaVersion\":2,\"kind\":\"owned-engine-production-cold-case\",\"actualProcessId\":"<<GetCurrentProcessId()
            <<",\"mainThreadId\":"<<GetCurrentThreadId()<<",\"passed\":"<<(passed?"true":"false")<<",\"error\":"<<Quote(error)<<",\"sehCode\":"<<exception
            <<",\"initialResult\":"<<report.initialResult<<",\"initialPhaseMask\":"<<report.initialMask<<",\"initialCalls\":"<<report.initialCalls
            <<",\"runResult\":"<<report.runResult<<",\"snapshotResult\":"<<report.snapshotResult<<",\"raw\":{\"path\":"<<Quote(Utf8(raw.filename().wstring()))
            <<",\"sha256\":"<<Quote(Hash(&report.snapshot,sizeof(report.snapshot)))<<",\"bytes\":"<<sizeof(report.snapshot)<<"},\"scope\":{\"httpRequestsIssued\":false,\"hostInitializedBootstrap\":false,\"importerCalledBootstrapAbi\":false,\"gameExecuted\":false,\"installed\":false}}\n";
        const auto text=out.str();Save(output,text.data(),text.size());
    }catch(...){return 2;}
    return passed?0:1;
}
