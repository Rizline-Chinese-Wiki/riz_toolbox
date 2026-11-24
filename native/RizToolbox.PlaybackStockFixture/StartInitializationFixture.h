#pragma once
#include "PlaybackStockFixture.h"
struct StockStartEnumerator {
    void* Class; void* Monitor;
    std::int32_t State; std::uint32_t StatePadding;
    void* Current; StockProgress* Progress;
    std::uint32_t Flags, ReturnByte, Calls, ThrowBoundary;
    void* Clip; StockPlayer* Candidate;
    void* WaitUntilReceiver; void* WaitUntilReference; void* WaitUntilMethod;
};
// All boundaries run in the actual native parent original. They confer no receipt.
using StockStartBoundary = void(__cdecl*)(StockStartEnumerator*, std::uint32_t);
extern "C" std::uint8_t __cdecl StockStartMoveNext(StockStartEnumerator*, void*);
