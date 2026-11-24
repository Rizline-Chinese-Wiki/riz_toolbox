#pragma once
#include <Windows.h>
#include <cstdint>
#ifdef RIZ_FIXED_TARGETS_BUILD
#define RIZ_FIXED_TARGET_API extern "C" __declspec(dllexport)
#else
#define RIZ_FIXED_TARGET_API extern "C" __declspec(dllimport)
#endif
inline constexpr DWORD RizFixedSehCode = 0xe0465853u;
inline constexpr std::uint64_t RizFixedPayload = 0xfedcba9876543210ull;
struct RizFixedOriginalException { std::uint64_t payload; std::uint32_t kind; };
struct RizFixedCase {
    std::uint32_t throwKind, throwMode, nested, reserved;
    unsigned char node[0x88], owner[16], enumerator[16], name[16];
    void* outputNode;
    unsigned char outputByte;
    std::uint32_t calls[4], releaseCount, releaseZeroFlag, detachZero, diagnosticsFailed;
    std::uint32_t returned, rawReturn, exceptionCode, exceptionFlags, exceptionParameters;
    std::uint64_t exceptionPayload, exceptionKind;
};
RIZ_FIXED_TARGET_API RizFixedCase* __cdecl RizFixedOwnedSetCase(RizFixedCase* value);
RIZ_FIXED_TARGET_API std::uint8_t __cdecl RizFixedOwnedStart(void*, void*, void*, void**);
RIZ_FIXED_TARGET_API std::uint32_t __cdecl RizFixedOwnedExecute(void*, std::uint8_t*);
RIZ_FIXED_TARGET_API std::uint8_t __cdecl RizFixedOwnedRelease(void*);
RIZ_FIXED_TARGET_API void __cdecl RizFixedOwnedDetach(void*);
