#pragma once
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>
#include <winhttp.h>
#include <cstddef>
#include <cstdint>

namespace RizWinFixture {
constexpr std::uint32_t Version = 1, ImporterRows = 32;
constexpr wchar_t Agent[] = L"RizToolbox.OwnedWinHttpFixture";
constexpr wchar_t InvalidUrl[] = L"::riz-owned-invalid-url::";
enum Kind : std::uint32_t { Open, ProxyForUrl, IeConfig, Close };
enum Phase : std::uint32_t { Uninitialized = 1, HostDirect = 2, HostForward = 3,
    ImporterTls = 4, ImporterC = 5, ImporterCpp = 6, ImporterDll = 7,
    WorkerDirect = 8, WorkerForward = 9, Poisoned = 10, ImporterOutside = 11 };
using OpenFn = HINTERNET(WINAPI*)(LPCWSTR, DWORD, LPCWSTR, LPCWSTR, DWORD);
using ProxyFn = BOOL(WINAPI*)(HINTERNET, LPCWSTR, WINHTTP_AUTOPROXY_OPTIONS*, WINHTTP_PROXY_INFO*);
using IeFn = BOOL(WINAPI*)(WINHTTP_CURRENT_USER_IE_PROXY_CONFIG*);
using CloseFn = BOOL(WINAPI*)(HINTERNET);
struct Api { OpenFn open; ProxyFn proxy; IeFn ie; CloseFn close; };
struct Salt { std::uint64_t words[4]; };
struct PrivateString {
    std::uint64_t pointer;
    std::uint32_t present, length, complete, reserved;
    unsigned char sha256[32];
};
struct Row {
    std::uint32_t version, byteSize, phase, kind, threadId, seed;
    std::uint64_t sequence, apiAddress, args[5], rawReturn;
    std::uint32_t lastErrorBefore, lastErrorAfter, openAccessType, openFlags;
    std::uint32_t autoFlags, autoDetectFlags, autoReserved, autoLogon;
    std::uint64_t autoConfigUrl, autoReservedPointer;
    std::uint32_t outputAccessType, outputAutoDetect, freesAttempted, freesFailed;
    PrivateString strings[3];
};
struct ImporterSnapshot {
    std::uint32_t version, byteSize, phaseMask, rowCount;
    std::uint64_t checks, failures;
    Row rows[ImporterRows];
};
static_assert(sizeof(BOOL) == 4 && sizeof(void*) == 8);
static_assert(sizeof(PrivateString) == 56);
static_assert(sizeof(Row) == 320);
static_assert(offsetof(Row, args) == 40 && offsetof(Row, strings) == 152);
static_assert(sizeof(ImporterSnapshot) == 10272);

// Bounded, allocation-free SHA-256. User proxy/PAC strings are never serialized;
// an unlogged per-process salt is included only to compare this process's calls.
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
inline Salt MakeSalt(const void* identity)
{
    LARGE_INTEGER counter{}; QueryPerformanceCounter(&counter);
    FILETIME time{}; GetSystemTimeAsFileTime(&time);
    return {{static_cast<std::uint64_t>(counter.QuadPart),
        (static_cast<std::uint64_t>(time.dwHighDateTime)<<32)|time.dwLowDateTime,
        reinterpret_cast<std::uint64_t>(identity),static_cast<std::uint64_t>(GetCurrentProcessId())}};
}
inline PrivateString CaptureString(LPWSTR value,const Salt& salt)
{
    PrivateString out{};out.pointer=reinterpret_cast<std::uint64_t>(value);out.present=value?1u:0u;out.complete=1;
    if(!value)return out;
    for(;out.length<32768;++out.length)if(value[out.length]==0)break;
    if(out.length==32768)out.complete=0;
    Sha256 hash;hash.Feed(reinterpret_cast<const unsigned char*>(&salt),sizeof(salt));
    hash.Feed(reinterpret_cast<const unsigned char*>(value),static_cast<std::size_t>(out.length)*sizeof(wchar_t));
    hash.Finish(out.sha256);return out;
}
inline void FreeString(Row& row,LPWSTR value)
{ if(value){++row.freesAttempted;if(GlobalFree(value)!=nullptr)++row.freesFailed;} }
inline Row Base(std::uint32_t phase,std::uint32_t kind,std::uint32_t seed,std::uint64_t sequence)
{Row row{};row.version=Version;row.byteSize=sizeof(row);row.phase=phase;row.kind=kind;row.threadId=GetCurrentThreadId();row.seed=seed;row.sequence=sequence;return row;}
inline Row CallOpen(const Api& api,std::uint32_t phase,std::uint32_t seed,std::uint64_t sequence,DWORD flags=0)
{
    auto row=Base(phase,Open,seed,sequence);row.apiAddress=reinterpret_cast<std::uint64_t>(api.open);
    row.args[0]=reinterpret_cast<std::uint64_t>(Agent);row.args[1]=WINHTTP_ACCESS_TYPE_NO_PROXY;row.args[4]=flags;
    row.openAccessType=WINHTTP_ACCESS_TYPE_NO_PROXY;row.openFlags=flags;row.lastErrorBefore=seed;
    SetLastError(seed);const auto value=api.open(Agent,WINHTTP_ACCESS_TYPE_NO_PROXY,nullptr,nullptr,flags);const auto error=GetLastError();
    row.rawReturn=reinterpret_cast<std::uint64_t>(value);row.lastErrorAfter=error;return row;
}
inline Row CallClose(const Api& api,std::uint32_t phase,std::uint32_t seed,std::uint64_t sequence,HINTERNET handle)
{
    auto row=Base(phase,Close,seed,sequence);row.apiAddress=reinterpret_cast<std::uint64_t>(api.close);row.args[0]=reinterpret_cast<std::uint64_t>(handle);row.lastErrorBefore=seed;
    SetLastError(seed);const BOOL value=api.close(handle);const auto error=GetLastError();row.rawReturn=static_cast<std::uint32_t>(value);row.lastErrorAfter=error;return row;
}
inline Row CallIe(const Api& api,std::uint32_t phase,std::uint32_t seed,std::uint64_t sequence,const Salt& salt)
{
    WINHTTP_CURRENT_USER_IE_PROXY_CONFIG config{};auto row=Base(phase,IeConfig,seed,sequence);row.apiAddress=reinterpret_cast<std::uint64_t>(api.ie);row.args[0]=reinterpret_cast<std::uint64_t>(&config);row.lastErrorBefore=seed;
    SetLastError(seed);const BOOL value=api.ie(&config);const auto error=GetLastError();row.rawReturn=static_cast<std::uint32_t>(value);row.lastErrorAfter=error;
    row.outputAutoDetect=static_cast<std::uint32_t>(config.fAutoDetect);row.strings[0]=CaptureString(config.lpszAutoConfigUrl,salt);row.strings[1]=CaptureString(config.lpszProxy,salt);row.strings[2]=CaptureString(config.lpszProxyBypass,salt);
    FreeString(row,config.lpszAutoConfigUrl);FreeString(row,config.lpszProxy);FreeString(row,config.lpszProxyBypass);return row;
}
inline Row CallProxy(const Api& api,std::uint32_t phase,std::uint32_t seed,std::uint64_t sequence,HINTERNET handle,const Salt& salt)
{
    WINHTTP_AUTOPROXY_OPTIONS options{};WINHTTP_PROXY_INFO info{};auto row=Base(phase,ProxyForUrl,seed,sequence);row.apiAddress=reinterpret_cast<std::uint64_t>(api.proxy);
    row.args[0]=reinterpret_cast<std::uint64_t>(handle);row.args[1]=reinterpret_cast<std::uint64_t>(InvalidUrl);row.args[2]=reinterpret_cast<std::uint64_t>(&options);row.args[3]=reinterpret_cast<std::uint64_t>(&info);row.lastErrorBefore=seed;
    // Flags/autodetect/reserved/url all stay zero: no WPAD, PAC URL, credentials
    // or HTTP request is supplied. The deliberately invalid URL is local text.
    SetLastError(seed);const BOOL value=api.proxy(handle,InvalidUrl,&options,&info);const auto error=GetLastError();row.rawReturn=static_cast<std::uint32_t>(value);row.lastErrorAfter=error;
    row.outputAccessType=info.dwAccessType;row.strings[0]=CaptureString(info.lpszProxy,salt);row.strings[1]=CaptureString(info.lpszProxyBypass,salt);
    FreeString(row,info.lpszProxy);FreeString(row,info.lpszProxyBypass);return row;
}
} // namespace RizWinFixture

#ifdef RIZ_WINHTTP_IMPORTER_BUILD
extern "C" __declspec(dllexport) std::uint32_t __cdecl RizWinHttpImporter_GetSnapshot(RizWinFixture::ImporterSnapshot* out);
extern "C" __declspec(dllexport) std::uint32_t __cdecl RizWinHttpImporter_RunOutsideLoader();
#else
extern "C" std::uint32_t __cdecl RizWinHttpImporter_GetSnapshot(RizWinFixture::ImporterSnapshot* out);
extern "C" std::uint32_t __cdecl RizWinHttpImporter_RunOutsideLoader();
#endif
