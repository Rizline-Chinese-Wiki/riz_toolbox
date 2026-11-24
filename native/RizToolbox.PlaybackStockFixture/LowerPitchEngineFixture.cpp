#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <cstring>
#include "PlaybackStockFixture.h"
extern "C" void __cdecl StockLowerSetPitch(StockSource*,float);
using Boundary=void(__cdecl*)(StockSource*,std::uint32_t,std::uint32_t);
namespace {
volatile LONG64 calls{},stored{};
std::uint64_t lastSource{},lastBits{},lastThread{};
std::uint32_t flags{};
Boundary boundary{};
}
extern "C" {
extern __declspec(dllexport) const char StockLowerPitchProfileMarker[]="owned-native-engine-pitch-icall-v1";
extern __declspec(dllexport) const char StockLowerPitchRegisteredName[]="UnityEngine.AudioSource::SetPitch";
extern __declspec(dllexport) void(__cdecl* const StockLowerPitchFunctions[])(StockSource*,float)={StockLowerSetPitch};
extern __declspec(dllexport) const char* const StockLowerPitchNames[]={StockLowerPitchRegisteredName};
extern __declspec(dllexport) const std::uint32_t StockLowerPitchRegistrationCount=1;
__declspec(dllexport) void __cdecl StockLowerFixtureConfigure(std::uint32_t value,Boundary callback){flags=value;boundary=callback;}
__declspec(dllexport) std::uint64_t __cdecl StockLowerFixtureRead(std::uint32_t field){
    switch(field){case 0:return static_cast<std::uint64_t>(InterlockedCompareExchange64(&calls,0,0));
        case 1:return static_cast<std::uint64_t>(InterlockedCompareExchange64(&stored,0,0));
        case 2:return lastSource;case 3:return lastBits;case 4:return lastThread;default:return UINT64_MAX;}
}
std::uint32_t __cdecl StockLowerPitchBefore(StockSource* self,float value){
    std::uint32_t bits{};std::memcpy(&bits,&value,4);
    InterlockedIncrement64(&calls);lastSource=reinterpret_cast<std::uint64_t>(self);lastBits=bits;lastThread=GetCurrentThreadId();
    if(boundary)boundary(self,bits,1);
    if(!self||!self->CachedPtr||(flags&1u))RaiseException(0xe2450001u,0,0,nullptr);
    ++self->PitchCalls;self->LastPitchBits=bits;
    // A legal physical icall return can have zero engine child. Neither
    // this branch nor the engine copies its value into wrapper->Pitch.
    return (bits&0x7f800000u)!=0x7f800000u ? 1u:0u;
}
void __cdecl StockLowerPitchAfter(StockSource* self,float value){
    std::uint32_t bits{};std::memcpy(&bits,&value,4);
    if((bits&0x7f800000u)!=0x7f800000u)InterlockedIncrement64(&stored);
    if(boundary)boundary(self,bits,2);
    if(self->Flags&StockPitchNestedEqual){self->Flags&=~StockPitchNestedEqual;StockSetPitch(self,value,nullptr);}
    if(flags&4u){flags&=~4u;StockLowerSetPitch(self,value);}
    if(flags&8u){flags&=~8u;StockSetPitch(self,value,nullptr);}
    if((flags&2u)||(self->Flags&StockPitchThrowAfterStore))RaiseException(0xe2450002u,0,0,nullptr);
}
}
