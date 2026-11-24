# RizToolbox Doorstop changes — 2026-10-24

Based on UnityDoorstop commit `33dab9a6733862eb81869ff08431d9478b28784b`, LGPL-2.1; retain the included LICENSE and upstream copyright notices. The additional `src/windows/riz_preinit.c/.h` and the Windows seam in `src/bootstrap.c` synchronously call the independent RizToolbox native producer before the original IL2CPP init. The actual upstream Windows wrapper then calls the shared production `src/windows/riz_init_call.c/.h` function. A known no-write rejection runs the original init and skips the mod host. An uncertain publication fails fast before original init. A ready producer runs original init once and then the existing CoreCLR bootstrap only when original init returns nonzero, including a negative int. The original int result, exact domain pointer and native SEH/C++ propagation are preserved. A process-lifetime atomic guard rejects reentry and repeated calls before a second original invocation. No handler catches exceptions from original init or CoreCLR bootstrap. This component does not implement the separate C++ producer or claim game acceptance.

Use MSVC 2019 toolset 14.29.30133, Windows SDK 10.0.19041.0 and xmake 2.6.1 on Windows x64. The pinned source/tool archive hashes and URLs are in `RIZ-UPSTREAM.json`. From the extracted corresponding source directory, with the xmake tool on PATH:

```powershell
xmake f -p windows -a x64 -m release --vs=2019 --vs_toolset=14.29.30133 --vs_sdkver=10.0.19041.0 --include_logging=y '--cflags=/std:c11 /Zc:preprocessor-'
xmake -vD doorstop
```

The output is `build/windows/x64/release/doorstop.dll`; the installer names it `winhttp.dll`. Traditional preprocessing selects the supported upstream logger branch on this compiler, while C11 handles designated initializers. Generated upstream proxy/SDK warnings remain in the build log. This is an actual build recipe, not a claim of byte-for-byte reproducibility or successful clean-environment validation.

All four RizToolbox C/header files are included in this corresponding-source archive. `xmake.lua` already compiles `src/windows/*.c`; no generated or external copy of the init-call logic is needed. In the RizToolbox repository, `scripts/Test-DoorstopInitNative.ps1` compiles this exact `riz_init_call.c` source as C11 and links an independent native fixture. Its controlled readiness cases exercise return/order/exception and fail-fast contracts; they do not execute the actual producer, CoreCLR or game and do not establish successful game cold startup.

## Optional fixed system WinHTTP candidate — 2026-10-25

`Build-Doorstop.ps1 -FixedSystemWinHttp` selects a separately built candidate. Its actual `DLL_PROCESS_ATTACH` calls `RizWinHttpDoorstopAttach` before `init_crt`, logger, paths and the preserved legacy proxy loader. The wrapper invokes the canonical fixed System32 resolver and checks its complete Ready record; failure returns FALSE before the remaining Doorstop startup. This is execution inside the real loader callback, not the earlier standalone resolver's outside-loader profile. Self-owned cold startup results are recorded separately from game acceptance and general loader-safety claims.

The candidate includes `riz_winhttp.h/.c`, `riz_winhttp_entry.asm` and `riz_winhttp_doorstop.h/.c`. The embedded compile definition suppresses the standalone diagnostic exports; the DEF retains exactly the existing 82 names and their actual old ordinals. Those ordinals are assigned by ordinal export-name order, not the grouped proxylist row number. Four WinHTTP names directly export canonical typed tail stubs. The other 78 retain the upstream generated wrappers and original proxy source selection; 17 Version names absent from system WinHTTP are not thereby made callable or covered by four-API validation.

The modified `xmake.lua` adds the MASM source, `RIZ_WINHTTP_EMBEDDED_DOORSTOP=1`, the one-name native SEH import library and an internal linker MAP. `RizWinHttpForward_GetInfo`, `RizWinHttpDoorstopRecord` and `RizWinHttpDoorstopAttach` remain internal public link symbols retained by `/INCLUDE`, not DLL exports. A self-owned caller verifies exact held image identity before using its generated MAP RVAs; this is not a public diagnostic API.

For the candidate's extracted corresponding source, first open an x64 MSVC 2019 build environment and create the import library from the included DEF, then use the same xmake commands above:

```powershell
New-Item -ItemType Directory -Path build/riz-import -Force
lib /nologo /machine:x64 /def:src/windows/riz_winhttp_seh.def /out:build/riz-import/riz-seh.lib
xmake f -p windows -a x64 -m release --vs=2019 --vs_toolset=14.29.30133 --vs_sdkver=10.0.19041.0 --include_logging=y '--cflags=/std:c11 /Zc:preprocessor-'
xmake -vD doorstop
```

The old init wrapper and JudgeContext producer remain distinct from this four-API forwarding candidate. No production Engine V4 receipt, new Kernel adoption, game first-publication authority, GC ownership or cancellation completion is granted by this change.
