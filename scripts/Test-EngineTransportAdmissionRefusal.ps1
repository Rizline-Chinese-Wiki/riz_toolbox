#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ProductionNativeBuildDirectory,
    [Parameter(Mandatory)][string]$InteriorClosurePath,
    [Parameter(Mandatory)][string]$ColdTracePath,
    [string]$Python = '',
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute path required.'};[IO.Path]::GetFullPath($Path)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$output=Absolute $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent output directory.'}
$prodDir=Absolute $ProductionNativeBuildDirectory
$icPath=Absolute $InteriorClosurePath
$ctPath=Absolute $ColdTracePath
$vcvarsPath=Absolute $VcVars
$null=New-Item -ItemType Directory -Path $output

$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-admission-refusal-tests';passed=$false;cases=@()}
try {
    # Case 1 & 4: generate-engine-transport-profile.py rejects bad Stage A hashes & unknown interior entry & wrong cold-trace order
    $generator=Join-Path $repo 'scripts/generate-engine-transport-profile.py'
    $target='D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll'
    $exe='D:/SteamLibrary/steamapps/common/Rizline/Rizline.exe'
    $proxy=Join-Path $prodDir 'winhttp.dll'
    $kernel=Join-Path $prodDir 'RizToolbox.EngineNodeKernel.dll'

    $badIc=Join-Path $output 'bad-interior-closure.json'
    $icObj=Get-Content -LiteralPath $icPath -Raw | ConvertFrom-Json
    $icObj.externalInteriorBranches=@(@{function='NodeStart';srcRva='0x1234';targetRva='0x792250'})
    $icObj.interiorEntryClosureVerified=$false
    [IO.File]::WriteAllText($badIc,($icObj|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    $genOut1=Join-Path $output 'gen-bad-ic'
    & $Python -B $generator --target $target --executable $exe --doorstop $proxy --kernel $kernel --profile production --interior-closure $badIc --cold-trace $ctPath --output-header (Join-Path $genOut1 'EngineTransportProfile.Generated.h') --output-json (Join-Path $genOut1 'native-profile.json') 2>&1 | Out-Null
    if($LASTEXITCODE -eq 0){throw 'Generator accepted tampered interior closure.'}
    $manifest.cases += @{name='unknown-interior-entry-refused';passed=$true}

    $badCt=Join-Path $output 'bad-cold-trace.json'
    $ctObj=Get-Content -LiteralPath $ctPath -Raw | ConvertFrom-Json
    $ctObj.preUnityMain2PageFaultCount=1
    [IO.File]::WriteAllText($badCt,($ctObj|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
    $genOut2=Join-Path $output 'gen-bad-ct'
    & $Python -B $generator --target $target --executable $exe --doorstop $proxy --kernel $kernel --profile production --interior-closure $icPath --cold-trace $badCt --output-header (Join-Path $genOut2 'EngineTransportProfile.Generated.h') --output-json (Join-Path $genOut2 'native-profile.json') 2>&1 | Out-Null
    if($LASTEXITCODE -eq 0){throw 'Generator accepted dirty pre-UnityMain2 cold trace.'}
    $manifest.cases += @{name='dirty-cold-trace-refused';passed=$true}

    # Unverified default generation keeps ProductionFirstInstallAuthorized=false
    $unverifiedDir=Join-Path $output 'gen-unverified'
    & $Python -B $generator --target $target --executable $exe --doorstop $proxy --kernel $kernel --profile production --output-header (Join-Path $unverifiedDir 'EngineTransportProfile.Generated.h') --output-json (Join-Path $unverifiedDir 'native-profile.json') 2>&1 | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Default unverified profile generation failed.'}
    $unvHdr=Get-Content -LiteralPath (Join-Path $unverifiedDir 'EngineTransportProfile.Generated.h') -Raw
    if(-not $unvHdr.Contains('ProductionFirstInstallAuthorized=false;')){throw 'Unverified generation opened ProductionFirstInstallAuthorized.'}
    $manifest.cases += @{name='unverified-default-stays-closed';passed=$true}

    # Case 2 & 5: Dynamic LoadLibrary of admitted Bootstrap DLL returns DisabledNoSourceWriteV5 (0 writes),
    # and calling Families/SourceWrites with forged FixedGame (profile=2) outside Bootstrap cold attach fails AdmissionUnproven.
    $nativeDir=Join-Path $repo 'native/RizToolbox.EngineBootstrap'
    $dynSrc=Join-Path $output 'RefusalHost.cpp'
    $dynCode=@"
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <cstdio>
#include <cstdint>
#include <cstring>
#include "$($nativeDir.Replace('\','/'))/EngineTransportPublication.h"
#include "$($nativeDir.Replace('\','/'))/EngineTransportFamilies.h"
#include "$($nativeDir.Replace('\','/'))/EngineTransportSourceWrites.h"
#include "$($nativeDir.Replace('\','/'))/EngineTransportRelays.h"

static RizEngineTransportPublicationV5 g_pub{};
static RizEngineTransportFamilies::Request g_fReq{};
static RizEngineTransportFamilies::Receipt g_fRec{};
static RizEngineTransportSourceWrites::Request g_wReq{};
static RizEngineTransportSourceWrites::Receipt g_wRec{};
static RizEngineTransportRelays::Plan g_plan{};

int main(int argc, char** argv) {
    if (argc < 2) return 10;
    const char* mode = argv[1];
    if (std::strcmp(mode, "dynamic-bootstrap") == 0) {
        HMODULE mod = LoadLibraryA(argv[2]);
        if (!mod) return 11;
        auto getInfo = reinterpret_cast<std::uint32_t(__cdecl*)(RizEngineTransportPublicationV5*)>(GetProcAddress(mod, "RizEngineTransport_GetInfoV5"));
        if (!getInfo) return 12;
        g_pub.version = 5; g_pub.byteSize = sizeof(g_pub);
        if (getInfo(&g_pub) != 0) return 13;
        printf("state=%u failure=%u startupStatic=%u writes=%u\n", g_pub.state, g_pub.failure, g_pub.startupStatic, g_pub.sourceWriteAttempts);
        if (g_pub.state != RizEngineTransportDisabledNoSourceWriteV5) return 14;
        if (g_pub.failure != RizEngineTransportDynamicLoadV5) return 15;
        if (g_pub.startupStatic != 0 || g_pub.sourceWriteAttempts != 0) return 16;
        return 0;
    }
    if (std::strcmp(mode, "forged-fixed-game") == 0) {
        g_fReq.version = RizEngineTransportFamilies::Version; g_fReq.byteSize = sizeof(g_fReq);
        g_fReq.profile = RizEngineTransportFamilies::FixedGame; g_fReq.slot = 28; g_fReq.mainOsTid = GetCurrentThreadId();
        g_fReq.kernelModule = 0x10000; g_fReq.targetModule = 0x20000; g_fReq.originalModule = 0x30000;
        g_fReq.relayAllocationBase = 0x40000; g_fReq.relayAllocationBytes = 0x10000;
        for (unsigned i = 0; i < 13; ++i) { g_fReq.targets[i] = 0x20000 + i * 16; g_fReq.originals[i] = 0x30000 + i * 16; }
        const auto fres = RizEngineTransportFamilies::PrepareAndConfigure(&g_fReq, &g_fRec);
        g_wReq.version = RizEngineTransportSourceWrites::Version; g_wReq.byteSize = sizeof(g_wReq);
        g_wReq.profile = 2; g_wReq.prepared = &g_plan;
        const auto wres = RizEngineTransportSourceWrites::Commit(&g_wReq, &g_wRec);
        printf("fres=%u wres=%u\n", static_cast<unsigned>(fres), static_cast<unsigned>(wres));
        if (fres != RizEngineTransportFamilies::AdmissionUnproven) return 21;
        if (wres != RizEngineTransportSourceWrites::AdmissionUnproven) return 22;
        return 0;
    }
    return 99;
}
"@
    [IO.File]::WriteAllText($dynSrc,$dynCode,[Text.UTF8Encoding]::new($false))

    # Also Case 7: Dummy Bootstrap publishing Ready with mainThread != producerThread so Doorstop calls AbortAfterPublication + Fatal
    $dummyBootSrc=Join-Path $output 'FakeReadyBootstrap.cpp'
    $dummyBootCode=@"
#define WIN32_LEAN_AND_MEAN
#define RIZ_ENGINE_TRANSPORT_PUBLICATION_BUILD
#include <Windows.h>
#include "$($nativeDir.Replace('\','/'))/EngineTransportPublication.h"
static RizEngineTransportPublicationV5 g_fake{};
static HANDLE g_self = nullptr;
BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = h;
        g_fake.version = 5; g_fake.byteSize = sizeof(g_fake);
        g_fake.schemaMagic = RizEngineTransportPublicationLayout::Magic;
        g_fake.state = RizEngineTransportReadyV5; g_fake.failure = 0;
        g_fake.epoch = 1; g_fake.snapshotSequence = 1; g_fake.claimed = 1;
        g_fake.processId = GetCurrentProcessId();
        g_fake.ownerThread = g_fake.producerThread = GetCurrentThreadId();
        g_fake.mainThread = g_fake.producerThread + 4; // forged mismatched mainThread!
        g_fake.mainThreadProof = RizEngineTransportMainThreadFixedProductionContractV5;
        g_fake.attachReason = DLL_PROCESS_ATTACH; g_fake.startupStatic = 1;
        g_fake.profile = 2; g_fake.moduleCount = 5; g_fake.typedCount = 13; g_fake.siteCount = 23;
        g_fake.windowCount = 36; g_fake.familyCount = 12; g_fake.eventCapacity = 1024;
        g_fake.eventCount = 1; g_fake.events[0].sequence = 1; g_fake.events[0].stage = RizEngineTransportPublishedV5; g_fake.events[0].state = RizEngineTransportReadyV5;
        g_fake.configuredFamilies = RizEngineTransportPublicationLayout::AllFamilies;
        g_fake.boundFamilies = RizEngineTransportPublicationLayout::AllFamilies;
        g_fake.retainedFamilies = RizEngineTransportPublicationLayout::AllFamilies;
        g_fake.preparedWindows = RizEngineTransportPublicationLayout::AllWindows;
        g_fake.writeAttemptedWindows = RizEngineTransportPublicationLayout::AllWindows;
        g_fake.committedWindows = RizEngineTransportPublicationLayout::AllWindows;
        g_fake.sourceWriteAttempts = 36; g_fake.sourceWriteCompletions = 36;
        g_fake.runtimeFunctionCount = 67; g_fake.resourceCount = 1; g_fake.retainedResources = 1;
    }
    return TRUE;
}
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_GetVersion() { return 5; }
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_GetInfoV5(RizEngineTransportPublicationV5* out) {
    if (!out || out->version != 5 || out->byteSize != sizeof(*out)) return 1;
    *out = g_fake; return 0;
}
RIZ_ENGINE_TRANSPORT_PUBLICATION_API std::uint32_t __cdecl RizEngineTransport_VerifyV5() { return 0; }
RIZ_ENGINE_TRANSPORT_PUBLICATION_API void __cdecl RizEngineTransport_AbortAfterPublication() {
    char buf[512]{};
    if (GetEnvironmentVariableA("RIZ_ABORT_MARKER", buf, sizeof(buf)) > 0) {
        HANDLE f = CreateFileA(buf, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
        if (f != INVALID_HANDLE_VALUE) { DWORD w = 0; WriteFile(f, "ABORT_CALLED\n", 13, &w, nullptr); CloseHandle(f); }
    }
}
"@
    [IO.File]::WriteAllText($dummyBootSrc,$dummyBootCode,[Text.UTF8Encoding]::new($false))
    $doorstopHostSrc=Join-Path $output 'DoorstopAbortHost.cpp'
    [IO.File]::WriteAllText($doorstopHostSrc,@'
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
extern "C" __declspec(dllimport) void* __stdcall WinHttpOpen(const wchar_t*, DWORD, const wchar_t*, const wchar_t*, DWORD);
int main() { (void)WinHttpOpen(nullptr, 0, nullptr, nullptr, 0); return 0; }
'@,[Text.UTF8Encoding]::new($false))

    $doorstopDir=Join-Path $output 'doorstop-abort-case'
    $null=New-Item -ItemType Directory -Path $doorstopDir
    Copy-Item -LiteralPath (Join-Path $prodDir 'winhttp.dll') -Destination (Join-Path $doorstopDir 'winhttp.dll')
    Copy-Item -LiteralPath (Join-Path $prodDir 'RizToolbox.EngineNodeKernel.dll') -Destination (Join-Path $output 'RizToolbox.EngineNodeKernel.dll')

    # Note: RefusalHost links Families.obj + SourceWrites.obj + Relays.obj + FirstInstall.obj (production candidate!) + kernel.lib + seh.lib
    # so we genuinely prove forged FixedGame is rejected even with RIZ_ENGINE_TRANSPORT_PRODUCTION=1 linked!
    $buildCmd=Join-Path $output 'build-refusal.cmd'
    $winhttpLib=Join-Path $output 'winhttp_imp.lib'
    $winhttpDef=Join-Path $output 'winhttp_imp.def'
    [IO.File]::WriteAllLines($winhttpDef,@('LIBRARY winhttp.dll','EXPORTS','WinHttpOpen'),[Text.UTF8Encoding]::new($false))
    $cmds=@(
        '@echo off','setlocal DisableDelayedExpansion',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /EHa /MD /I`"$prodDir`" `"$dynSrc`" `"$prodDir\EngineTransportFirstInstall.obj`" `"$prodDir\EngineTransportRelays.obj`" `"$prodDir\EngineTransportFamilies.obj`" `"$prodDir\EngineTransportSourceWrites.obj`" `"$prodDir\EngineOriginals.obj`" `"$prodDir\EngineTransportOriginals.obj`" `"$prodDir\kernel.lib`" `"$prodDir\seh.lib`" /Fe:`"$output\RefusalHost.exe`" /link /INCREMENTAL:NO",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /LD /MD `"$dummyBootSrc`" /Fe:`"$doorstopDir\RizToolbox.EngineBootstrap.dll`" /link /INCREMENTAL:NO",'if errorlevel 1 exit /b 1',
        "lib /nologo /machine:x64 /def:`"$winhttpDef`" /out:`"$winhttpLib`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /MD `"$doorstopHostSrc`" `"$winhttpLib`" /Fe:`"$doorstopDir\DoorstopAbortHost.exe`" /link /INCREMENTAL:NO",'if errorlevel 1 exit /b 1',
        'exit /b 0'
    )
    [IO.File]::WriteAllLines($buildCmd,$cmds,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $buildCmd 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    if($LASTEXITCODE -ne 0){throw "Refusal native fixture build failed: $LASTEXITCODE"}

    $bootDll=Join-Path $prodDir 'RizToolbox.EngineBootstrap.dll'
    & (Join-Path $output 'RefusalHost.exe') dynamic-bootstrap $bootDll | Tee-Object -FilePath (Join-Path $output 'dynamic-bootstrap.log')
    if($LASTEXITCODE -ne 0){throw "Dynamic LoadLibrary refusal check failed: $LASTEXITCODE"}
    $manifest.cases += @{name='dynamic-load-library-refused';passed=$true}

    & (Join-Path $output 'RefusalHost.exe') forged-fixed-game | Tee-Object -FilePath (Join-Path $output 'forged-fixed-game.log')
    if($LASTEXITCODE -ne 0){throw "Forged FixedGame refusal check failed: $LASTEXITCODE"}
    $manifest.cases += @{name='forged-fixed-game-without-bootstrap-attach-refused';passed=$true}

    $abortMarker=Join-Path $doorstopDir 'abort-called.txt'
    $oldMarker=[Environment]::GetEnvironmentVariable('RIZ_ABORT_MARKER','Process')
    [Environment]::SetEnvironmentVariable('RIZ_ABORT_MARKER',$abortMarker,'Process')
    try {
        $p=Start-Process -FilePath (Join-Path $doorstopDir 'DoorstopAbortHost.exe') -WorkingDirectory $doorstopDir -Wait -PassThru -NoNewWindow
        if($p.ExitCode -eq 0){throw 'Doorstop did not abort/Fatal on tampered Ready mainThread mismatch.'}
        if(-not (Test-Path -LiteralPath $abortMarker -PathType Leaf)){throw 'Doorstop did not call RizEngineTransport_AbortAfterPublication before Fatal.'}
        $manifest.cases += @{name='doorstop-post-publication-abort-and-fatal';passed=$true;exitCode=$p.ExitCode}
    } finally {
        [Environment]::SetEnvironmentVariable('RIZ_ABORT_MARKER',$oldMarker,'Process')
    }

    $manifest.passed=$true
} finally {
    [IO.File]::WriteAllText((Join-Path $output 'refusal-result.json'),($manifest|ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
}
Write-Output "All native production admission refusal counter-tests passed: $output"
