#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
using Boundary=void(__cdecl*)(void*,std::uint32_t,std::uint32_t);
extern "C" {std::uint64_t StockEngineLastReturn{};}
namespace{
volatile LONG64 calls{},stored{};
std::uint64_t lastSelf{},lastBits{},lastThread{};
std::uint32_t flags{};Boundary boundary{};
}
extern "C"{
extern __declspec(dllexport) const char StockNativeEnginePitchMarker[]="owned-native-engine-pitch-v1";
__declspec(dllexport) void __cdecl StockEngineFixtureConfigure(std::uint32_t value,Boundary callback){flags=value;boundary=callback;}
__declspec(dllexport) std::uint64_t __cdecl StockEngineFixtureRead(std::uint32_t field){
    switch(field){case 0:return static_cast<std::uint64_t>(InterlockedCompareExchange64(&calls,0,0));
        case 1:return static_cast<std::uint64_t>(InterlockedCompareExchange64(&stored,0,0));
        case 2:return lastSelf;case 3:return lastBits;case 4:return lastThread;case 5:return StockEngineLastReturn;default:return UINT64_MAX;}
}
std::uint32_t __cdecl StockEngineWitness(void* self,std::uint32_t at,std::uint32_t bits,std::uint64_t){
    if(at==1){InterlockedIncrement64(&calls);lastSelf=reinterpret_cast<std::uint64_t>(self);lastBits=bits;lastThread=GetCurrentThreadId();}
    else if(at==2){
        std::uint32_t actual{};std::memcpy(&actual,static_cast<const unsigned char*>(self)+0x1f4,4);
        if(actual!=bits)RaiseException(0xe2460003u,0,0,nullptr);
        InterlockedIncrement64(&stored);
    }
    if(boundary)boundary(self,bits,at);
    if(!self||(at==1&&(flags&1u)))RaiseException(0xe2460001u,0,0,nullptr);
    if(at==2&&(flags&2u))RaiseException(0xe2460002u,0,0,nullptr);
    return (bits&0x7f800000u)!=0x7f800000u ? 1u:0u;
}
}
