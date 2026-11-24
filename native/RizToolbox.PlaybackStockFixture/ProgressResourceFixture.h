#pragma once
#include "PlaybackStockFixture.h"

// This owned profile models the resource reference separately from AudioClip.
// Start dispatches this reference's actual getter through GetResource at +0x10.
struct StockResourceReference;
using StockResourceGetter = void*(__cdecl*)(StockResourceReference*, void*);
struct StockResourceReference {
    void* Class; void* Monitor;
    StockResourceGetter GetResource;
    void* Clip;
    volatile long Gets;
    std::uint32_t Flags;
    void* LastMethod; void* LastSelf; void* LastClip;
};
using StockResourceBoundary = void(__cdecl*)(StockResourceReference*, void*, std::uint32_t);
using StockProgressResourceBoundary = void(__cdecl*)(void*, void*, void*, std::uint32_t);

RIZ_STOCK_API void __cdecl StockProgressClip(void*, void*, void*);
RIZ_STOCK_API void __cdecl StockProgressClipTail(void*, void*, void*);
RIZ_STOCK_API void* __cdecl StockMethodProgressClip();
RIZ_STOCK_API void* __cdecl StockMethodResourceGet();
RIZ_STOCK_API void* __cdecl StockResourceGet(StockResourceReference*, void*);
RIZ_STOCK_API StockResourceReference* __cdecl StockFixtureCreateResourceReference(void*);
RIZ_STOCK_API void* __cdecl StockFixtureCreateAudioClip(std::uint64_t);
RIZ_STOCK_API StockProgress* __cdecl StockFixtureCreateProgressResourceReceiver(std::uint32_t, StockSource*, void*);
RIZ_STOCK_API void __cdecl StockFixtureSetProgressResource(StockProgress*, void*);
RIZ_STOCK_API void __cdecl StockFixtureConfigureResource(StockResourceReference*, std::uint32_t, StockResourceBoundary);
RIZ_STOCK_API std::uint64_t __cdecl StockFixtureResourceRead(StockResourceReference*, std::uint32_t);
RIZ_STOCK_API void __cdecl StockFixtureConfigureProgressResource(std::uint32_t, StockProgressResourceBoundary);
RIZ_STOCK_API std::uint64_t __cdecl StockFixtureProgressResourceRead(std::uint32_t);
