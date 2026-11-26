#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [string]$InputDirectory,
    [switch]$FixedSystemWinHttp, [switch]$EngineBootstrap, [switch]$EngineTransport, [switch]$Owned)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference = 'Stop'
if($EngineBootstrap -and $EngineTransport){throw '-EngineBootstrap (V4) and -EngineTransport (V5) are mutually exclusive.'}
if(($EngineBootstrap -or $EngineTransport) -and -not $FixedSystemWinHttp){throw '-EngineBootstrap and -EngineTransport require the explicit -FixedSystemWinHttp candidate.'}
if($Owned -and -not $EngineTransport){throw '-Owned requires -EngineTransport.'}
$repoRoot = Split-Path -Parent $PSScriptRoot
$patchRoot = Join-Path $repoRoot 'native/RizToolbox.Doorstop'
$lock = Get-Content -LiteralPath (Join-Path $patchRoot 'upstream.json') -Raw | ConvertFrom-Json
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $InputDirectory) { $InputDirectory = Join-Path $repoRoot '.tools/doorstop-preinit-inputs' }
$inputs = [IO.Path]::GetFullPath($InputDirectory)
New-Item -ItemType Directory -Path $output,$inputs -Force | Out-Null
foreach ($item in @(@('doorstop.zip',$lock.archiveUrl,$lock.archiveSha256),@('xmake.zip',$lock.xmakeUrl,$lock.xmakeSha256))) {
    $archive = Join-Path $inputs $item[0]
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) { Invoke-WebRequest -Uri $item[1] -OutFile $archive }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item[2]) { throw "Pinned input hash mismatch: $archive" }
}
# Fresh extraction prevents untracked C files or a modified cached xmake.lua
# from becoming build inputs. Prior attempts remain recoverable; never delete.
$sourceDirName = if ($EngineTransport -or $output.Length -gt 75) { 's' } else { 'source-' + [Guid]::NewGuid().ToString('N') }
$sourceRun = Join-Path $output $sourceDirName
New-Item -ItemType Directory -Path $sourceRun | Out-Null
Expand-Archive -LiteralPath (Join-Path $inputs 'doorstop.zip') -DestinationPath $sourceRun
$extractedSource = Join-Path $sourceRun "UnityDoorstop-$($lock.commit)"
if ($EngineTransport -or ($extractedSource.Length + 100 -ge 245)) {
    $shortSource = Join-Path $sourceRun 'u'
    Move-Item -LiteralPath $extractedSource -Destination $shortSource
    $source = $shortSource
} else {
    $source = $extractedSource
}
$toolsRoot = Join-Path $sourceRun 'tools'
if (-not (Test-Path -LiteralPath (Join-Path $toolsRoot 'xmake/xmake.exe'))) {
    Expand-Archive -LiteralPath (Join-Path $inputs 'xmake.zip') -DestinationPath $toolsRoot
}
$bootstrap = Join-Path $source 'src/bootstrap.c'
$body = [IO.File]::ReadAllText($bootstrap)
$include = '#include "windows/riz_preinit.h"'
if (-not $body.Contains($include)) {
    $needle = '#include "bootstrap.h"'
    if (-not $body.Contains($needle)) { throw 'Pinned Doorstop include seam missing.' }
    $body = $body.Replace($needle, "/* Modified by RizToolbox, 2026-10-24: synchronous native pre-init producer; original init and post-init CLR preserved. */`n$needle`n#if _WIN32`n$include`n#endif")
    $needle = "    const int orig_result = il2cpp.init(domain_name);`n    il2cpp_doorstop_bootstrap();`n    return orig_result;"
    $replacement = @'
#if _WIN32
    const int riz_ready = RizBeforeIl2CppInit((const void*)il2cpp.init);
    return RizInvokeIl2CppInit(riz_ready, domain_name, il2cpp.init, il2cpp_doorstop_bootstrap);
#else
    const int orig_result = il2cpp.init(domain_name);
    il2cpp_doorstop_bootstrap();
    return orig_result;
#endif
'@
    # Fixed source uses LF. Refuse a different wrapper rather than patching an
    # arbitrary nearby init call or silently allowing the old late installer.
    $body = $body.Replace("`r`n", "`n")
    if (-not $body.Contains($needle)) { throw 'Pinned Doorstop init seam missing.' }
    $body = $body.Replace($needle, $replacement.TrimEnd())
    [IO.File]::WriteAllText($bootstrap, $body, [Text.UTF8Encoding]::new($false))
}
Copy-Item -LiteralPath (Join-Path $patchRoot 'riz_preinit.c'),(Join-Path $patchRoot 'riz_preinit.h'),(Join-Path $patchRoot 'riz_init_call.c'),(Join-Path $patchRoot 'riz_init_call.h') -Destination (Join-Path $source 'src/windows') -Force
Copy-Item -LiteralPath (Join-Path $patchRoot 'upstream.json') -Destination (Join-Path $source 'RIZ-UPSTREAM.json') -Force
Copy-Item -LiteralPath (Join-Path $patchRoot 'RIZLINE-MODIFICATIONS.md') -Destination $source -Force
& (Join-Path $patchRoot 'Apply-ProcessEnvironment.ps1') -SourceDirectory $source
if ($FixedSystemWinHttp) {
    # This is an explicit candidate only. The old default extraction and proxy
    # generation above remain intact; never retrofit a prior package in place.
    function Replace-ExactlyOnce([string]$Body,[string]$Needle,[string]$Replacement,[string]$Seam) {
        $n = $Needle.Replace("`r`n","`n")
        $r = $Replacement.Replace("`r`n","`n")
        if ([string]::IsNullOrEmpty($n) -or $Body.Split(@($n),[StringSplitOptions]::None).Count -ne 2) {
            throw "Pinned fixed WinHTTP seam is not unique: $Seam"
        }
        $Body.Replace($n,$r)
    }
    $fixedFiles=@('riz_winhttp.h','riz_winhttp.c','riz_winhttp_entry.asm','riz_winhttp_doorstop.h','riz_winhttp_doorstop.c')
    foreach($name in $fixedFiles){Copy-Item -LiteralPath (Join-Path $patchRoot $name) -Destination (Join-Path $source 'src/windows') -Force}
    $entryPath=Join-Path $source 'src/windows/entrypoint.c'
    $entry=[IO.File]::ReadAllText($entryPath).Replace("`r`n","`n")
    $entry=Replace-ExactlyOnce $entry '#include "entrypoint.h"' "#include `"entrypoint.h`"`n#include `"riz_winhttp_doorstop.h`"" 'entry include'
    $entry=Replace-ExactlyOnce $entry '    init_crt();' "    /* Real PROCESS_ATTACH stage, before Doorstop heap/logger/paths. */`n    if (!RizWinHttpDoorstopAttach(hInstDll)) return FALSE;`n    init_crt();" 'PROCESS_ATTACH before init_crt'
    [IO.File]::WriteAllText($entryPath,$entry,[Text.UTF8Encoding]::new($false))

    # Preserve every original name and ordinal. Four names directly select the
    # typed ASM exports; no duplicate exp_* or legacy slots are generated for
    # them. The other 78 retain the upstream lookup and tail-forward generator.
    $proxyList=Join-Path $source 'src/windows/proxy/proxylist.txt'
    $names=@([IO.File]::ReadAllLines($proxyList)|ForEach-Object{$_.Trim()})
    if($names.Count -ne 82 -or @($names|Sort-Object -Unique).Count -ne 82 -or @($names|Where-Object{$_ -notmatch '^[A-Za-z_][A-Za-z_0-9]*$'}).Count){throw 'Pinned 82-name proxy list differs.'}
    # The old linker assigned unnamed ordinals by export-name order, not by
    # proxylist group order (Version's Ver* names precede Private1 in the list).
    # Explicit ordinals must preserve the actual old export-name ordering.
    $ordinalNames=[string[]]$names.Clone()
    [Array]::Sort($ordinalNames,[StringComparer]::Ordinal)
    $fixedOrdinals=@{WinHttpCloseHandle=23;WinHttpGetIEProxyConfigForCurrentUser=44;WinHttpGetProxyForUrl=45;WinHttpOpen=53}
    foreach($name in $fixedOrdinals.Keys){if($ordinalNames[$fixedOrdinals[$name]-1] -cne $name){throw "Pinned proxy ordinal differs: $name"}}
    $proxygenPath=Join-Path $source 'src/windows/build_tools/proxygen.lua'
    $proxygen=[IO.File]::ReadAllText($proxygenPath).Replace("`r`n","`n")
    $needle=@'
            proxy_defs = proxy_defs ..
                             format("static FARPROC __%s__;\r\n", func)
'@
    $replacement=@'
            local riz_fixed = func == "WinHttpOpen" or func == "WinHttpGetProxyForUrl" or
                func == "WinHttpGetIEProxyConfigForCurrentUser" or func == "WinHttpCloseHandle"
            if not riz_fixed then
            proxy_defs = proxy_defs ..
                             format("static FARPROC __%s__;\r\n", func)
'@
    $proxygen=Replace-ExactlyOnce $proxygen $needle $replacement 'fixed four generator skip begin'
    $needle=@'
            exports_funcs =
                exports_funcs .. format("%s = exp_%s", func, func) .. "\r\n"
'@
    $replacement=@'
            end
            if riz_fixed then
                exports_funcs = exports_funcs .. format("%s @%d\r\n", func, riz_ordinals[func])
            else
                exports_funcs = exports_funcs .. format("%s = exp_%s @%d\r\n", func, func, riz_ordinals[func])
            end
'@
    $proxygen=Replace-ExactlyOnce $proxygen $needle $replacement 'fixed 82 explicit ordinal exports'
    $ordinalTable='local riz_ordinals = {'+"`n"+(@(for($i=0;$i -lt $ordinalNames.Count;$i++){'    ["'+$ordinalNames[$i]+'"] = '+($i+1)+','}) -join "`n")+"`n}`n"
    $proxygen=$ordinalTable+$proxygen
    [IO.File]::WriteAllText($proxygenPath,$proxygen,[Text.UTF8Encoding]::new($false))

    # The pinned xmake distribution maps .asm to ml64.exe on the selected x64
    # MSVC toolchain. Keep the real DllEntry; this is not a /NOENTRY DLL.
    $xmakePath=Join-Path $source 'xmake.lua'
    $xmakeBody=[IO.File]::ReadAllText($xmakePath).Replace("`r`n","`n")
    $needle='        add_files("src/windows/*.c")'
    $replacement=@'
        add_files("src/windows/*.c")
        add_files("src/windows/riz_winhttp_entry.asm")
        add_defines("RIZ_WINHTTP_EMBEDDED_DOORSTOP=1")
        add_linkdirs("build/riz-import")
        add_links("riz-seh")
        add_shflags("/OPT:NOICF", "/MAP:build/windows/x64/release/doorstop.map",
            "/INCLUDE:RizWinHttpForward_GetInfo", "/INCLUDE:RizWinHttpDoorstopRecord",
            "/INCLUDE:RizWinHttpDoorstopAttach", {force=true})
'@
    $xmakeBody=Replace-ExactlyOnce $xmakeBody $needle $replacement 'embedded Windows source/ASM/map'
    [IO.File]::WriteAllText($xmakePath,$xmakeBody,[Text.UTF8Encoding]::new($false))

    # SDK ntdll.lib does not consistently expose the native SEH handler. Build
    # its exact one-name importlib, and include its DEF in corresponding source.
    $vcvars = Get-RizVcVarsPath
    foreach($path in @($output,$source,$vcvars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported fixed WinHTTP compiler path metacharacter.'}}
    if(-not(Test-Path -LiteralPath $vcvars -PathType Leaf)){throw 'Pinned MSVC 2019 environment script missing.'}
    $sehDirectory=Join-Path $source 'build/riz-import'
    New-Item -ItemType Directory -Path $sehDirectory -Force|Out-Null
    $sehDef=Join-Path $source 'src/windows/riz_winhttp_seh.def'
    [IO.File]::WriteAllLines($sehDef,@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),[Text.UTF8Encoding]::new($false))
    $sehCommand=Join-Path $output 'build-winhttp-seh.cmd'
    [IO.File]::WriteAllLines($sehCommand,@('@echo off',"call `"$vcvars`" >nul",'if errorlevel 1 exit /b 1',
        "lib /nologo /machine:x64 /def:`"$sehDef`" /out:`"$sehDirectory/riz-seh.lib`"",'if errorlevel 1 exit /b 1'),[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $sehCommand 2>&1|Tee-Object -FilePath (Join-Path $output 'seh-import.log')
    if($LASTEXITCODE -ne 0){throw 'Fixed WinHTTP SEH importlib build failed.'}
}
if($EngineBootstrap -or $EngineTransport){
    # A separate candidate: ordinary imports initialize Bootstrap/Kernel before
    # our actual DllEntry. Never add a DllMain LoadLibrary or delayed dependency.
    $v5HeadersDir = Join-Path $source 'src/RizToolbox.EngineBootstrap'
    if($EngineTransport){
        $engineFiles=@('riz_engine_bootstrap.h','riz_engine_transport.h','riz_engine_transport.cpp','riz_engine_bootstrap.def')
        foreach($name in $engineFiles[0..2]){Copy-Item -LiteralPath (Join-Path $patchRoot $name) -Destination (Join-Path $source 'src/windows') -Force}
        New-Item -ItemType Directory -Path $v5HeadersDir -Force | Out-Null
        foreach($headerName in @('EngineTransportPublication.h','EngineTransportGameProfile.h','EngineTransportAdmissionFacts.h','EngineTransportAdmissionSha256.h')){
            Copy-Item -LiteralPath (Join-Path $repoRoot ('native/RizToolbox.EngineBootstrap/'+$headerName)) -Destination $v5HeadersDir -Force
        }
        $bootstrapNames=@('RizEngineTransport_GetVersion','RizEngineTransport_GetInfoV5',
            'RizEngineTransport_VerifyV5','RizEngineTransport_AbortAfterPublication')
    }else{
        $engineFiles=@('riz_engine_bootstrap.h','riz_engine_bootstrap.c','riz_engine_bootstrap.def')
        foreach($name in $engineFiles[0..1]){Copy-Item -LiteralPath (Join-Path $patchRoot $name) -Destination (Join-Path $source 'src/windows') -Force}
        $bootstrapNames=@('RizEngineFirstInstall_GetVersion','RizEngineFirstInstall_GetInfoV4',
            'RizEngineFirstInstall_VerifyV4','RizEngineFirstInstall_AbortAfterPublication')
    }
    $engineDef=Join-Path $source 'src/windows/riz_engine_bootstrap.def'
    [IO.File]::WriteAllLines($engineDef,@('LIBRARY RizToolbox.EngineBootstrap.dll','EXPORTS')+$bootstrapNames,[Text.UTF8Encoding]::new($false))
    $engineCommand=Join-Path $output 'build-engine-bootstrap-import.cmd'
    [IO.File]::WriteAllLines($engineCommand,@('@echo off',"call `"$vcvars`" >nul",'if errorlevel 1 exit /b 1',
        "lib /nologo /machine:x64 /def:`"$engineDef`" /out:`"$sehDirectory/riz-engine-bootstrap.lib`"",'if errorlevel 1 exit /b 1'),[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $engineCommand 2>&1|Tee-Object -FilePath (Join-Path $output 'engine-bootstrap-import.log')
    if($LASTEXITCODE -ne 0){throw 'The four-name Bootstrap importlib did not build.'}

    $entry=[IO.File]::ReadAllText($entryPath).Replace("`r`n","`n")
    $entry=Replace-ExactlyOnce $entry '#include "riz_winhttp_doorstop.h"' "#include `"riz_winhttp_doorstop.h`"`n#include `"riz_engine_bootstrap.h`"" 'engine candidate entry include'
    $entry=Replace-ExactlyOnce $entry "BOOL WINAPI DllEntry(HINSTANCE hInstDll, DWORD reasonForDllLoad,`n                     LPVOID reserved) {" "static BOOL WINAPI RizDoorstopEntryBody(HINSTANCE hInstDll, DWORD reasonForDllLoad,`n                     LPVOID reserved) {" 'actual DllEntry body isolation'
    $entry=Replace-ExactlyOnce $entry '#define HOOK_SYS(mod, from, to) ok &= iat_hook(mod, "kernel32.dll", &from, &to)' '#define HOOK_SYS(mod, from, to) ok &= (RizEngineDoorstopBeforeIat() && iat_hook(mod, "kernel32.dll", &from, &to))' 'candidate pre-IAT permanent responsibility'
    $needle=@'
    if (!ok) {
        LOG("Failed to install IAT hook!");
        free_logger();
'@
    $replacement=@'
    if (!ok) {
        LOG("Failed to install IAT hook!");
        (void)RizEngineDoorstopOnFailure(RizEngineDoorstopIatFailed, GetLastError());
        free_logger();
'@
    $entry=Replace-ExactlyOnce $entry $needle $replacement 'candidate IAT failure responsibility'
    $entry+=@'

/* Candidate's real PE entry. All original attach returns and native unwinds
 * share responsibility with the already initialized ordinary dependency. */
BOOL WINAPI DllEntry(HINSTANCE hInstDll, DWORD reasonForDllLoad, LPVOID reserved) {
    BOOL result = FALSE;
    if (reasonForDllLoad == DLL_PROCESS_DETACH)
        RizEngineDoorstopOnDetach(reserved);
    __try {
        if (reasonForDllLoad == DLL_PROCESS_ATTACH &&
            !RizEngineDoorstopBeforeAttach(hInstDll, reasonForDllLoad, reserved)) {
            result = FALSE;
        } else {
            result = RizDoorstopEntryBody(hInstDll, reasonForDllLoad, reserved);
            if (reasonForDllLoad == DLL_PROCESS_ATTACH)
                result = RizEngineDoorstopAfterAttach(result, GetLastError());
            else if (!result)
                result = RizEngineDoorstopOnFailure(RizEngineDoorstopBodyFailed, GetLastError());
        }
    } __finally {
        if (AbnormalTermination())
            (void)RizEngineDoorstopOnFailure(RizEngineDoorstopNativeException, GetLastError());
    }
    /* No non-local exit from __try: custom no-CRT entry has no _local_unwind. */
    return result;
}
'@
    [IO.File]::WriteAllText($entryPath,$entry,[Text.UTF8Encoding]::new($false))
    $proxyPath=Join-Path $source 'src/windows/proxy/proxy.h'
    $proxy=[IO.File]::ReadAllText($proxyPath).Replace("`r`n","`n")
    $proxy=Replace-ExactlyOnce $proxy '#include "../../crt.h"' "#include `"../../crt.h`"`n#include `"../riz_engine_bootstrap.h`"" 'candidate proxy responsibility include'
    $needle='    ASSERT_F(handle != NULL, "Unable to load original %s.dll!", module_name);'
    $replacement=@'
    if (handle == NULL) {
        (void)RizEngineDoorstopOnFailure(RizEngineDoorstopProxyUnavailable, GetLastError());
        /* Known no-write refusal can return; a missing original proxy cannot. */
        ExitProcess(ERROR_DLL_INIT_FAILED);
    }
    ASSERT_F(handle != NULL, "Unable to load original %s.dll!", module_name);
'@
    $proxy=Replace-ExactlyOnce $proxy $needle $replacement 'candidate original proxy load failure'
    [IO.File]::WriteAllText($proxyPath,$proxy,[Text.UTF8Encoding]::new($false))

    $xmakeBody=[IO.File]::ReadAllText($xmakePath).Replace("`r`n","`n")
    $needle='        add_links("riz-seh")'
    if($EngineTransport){
        $ownedLine = if($Owned){"`n        add_defines(`"RIZ_ENGINE_TRANSPORT_DOORSTOP_OWNED=1`")"}else{""}
        $replacement=@'
        add_links("riz-seh", "riz-engine-bootstrap")
        add_defines("RIZ_ENGINE_BOOTSTRAP_DOORSTOP=1")
        add_files("src/windows/riz_engine_transport.cpp")
        add_cxxflags("/std:c++17", "/W4", "/WX", "/O2", "/GS-", "/GR-", "/Zl", "/EHa", {force=true})
        add_shflags("/INCLUDE:RizEngineTransportDoorstopRecord", "/INCLUDE:RizEngineDoorstopBeforeAttach",
            "/INCLUDE:RizEngineDoorstopBeforeIat",
            "/INCLUDE:RizEngineDoorstopAfterAttach", "/INCLUDE:RizEngineDoorstopOnFailure",
            "/INCLUDE:RizEngineDoorstopOnDetach", {force=true})
'@.TrimEnd() + $ownedLine
        $entry = $entry.Replace('RizEngineDoorstopRecord','RizEngineTransportDoorstopRecord')
        [IO.File]::WriteAllText($entryPath,$entry,[Text.UTF8Encoding]::new($false))
        $proxy = $proxy.Replace('RizEngineDoorstopRecord','RizEngineTransportDoorstopRecord')
        [IO.File]::WriteAllText($proxyPath,$proxy,[Text.UTF8Encoding]::new($false))
    }else{
        $replacement=@'
        add_links("riz-seh", "riz-engine-bootstrap")
        add_defines("RIZ_ENGINE_BOOTSTRAP_DOORSTOP=1")
        add_shflags("/INCLUDE:RizEngineDoorstopRecord", "/INCLUDE:RizEngineDoorstopBeforeAttach",
            "/INCLUDE:RizEngineDoorstopBeforeIat",
            "/INCLUDE:RizEngineDoorstopAfterAttach", "/INCLUDE:RizEngineDoorstopOnFailure",
            "/INCLUDE:RizEngineDoorstopOnDetach", {force=true})
'@.TrimEnd()
    }
    $xmakeBody=Replace-ExactlyOnce $xmakeBody $needle $replacement 'ordinary Bootstrap candidate import'
    [IO.File]::WriteAllText($xmakePath,$xmakeBody,[Text.UTF8Encoding]::new($false))
}
$xmake = Join-Path $toolsRoot 'xmake/xmake.exe'
Push-Location $source
try {
    # MSVC 19.29 supports C11 syntax but its conforming C preprocessor does not
    # implement the upstream logger's __VA_OPT__. Use its supported traditional
    # branch while retaining C11 designated initializers and verbose diagnostics.
    # The v142 compiler and Windows SDK are also available under VS 2022 on CI.
    $vsYear = if ($env:RIZ_DOORSTOP_VS_YEAR) { $env:RIZ_DOORSTOP_VS_YEAR } else { '2019' }
    if ($vsYear -notin '2019','2022') { throw "Unsupported Visual Studio year for Doorstop: $vsYear" }
    & $xmake f -p windows -a x64 -m release "--vs=$vsYear" --vs_toolset=14.29.30133 --vs_sdkver=10.0.19041.0 --include_logging=y '--cflags=/std:c11 /Zc:preprocessor-' 2>&1 | Tee-Object -FilePath (Join-Path $output 'configure.log')
    if ($LASTEXITCODE -ne 0) { throw 'Doorstop configuration failed.' }
    & $xmake -vD doorstop 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Doorstop build failed.' }
} finally { Pop-Location }
$dll = Join-Path $source 'build/windows/x64/release/doorstop.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Doorstop build returned without its DLL.' }
Copy-Item -LiteralPath $dll -Destination (Join-Path $output 'winhttp.dll') -Force
if($FixedSystemWinHttp){
    $map=Join-Path $source 'build/windows/x64/release/doorstop.map'
    if(-not(Test-Path -LiteralPath $map -PathType Leaf)){throw 'Candidate build omitted its internal public-symbol map.'}
    Copy-Item -LiteralPath $map -Destination (Join-Path $output 'doorstop.map') -Force
    $mapText=[IO.File]::ReadAllText($map)
    $mapSymbols=@('RizWinHttpForward_GetInfo','RizWinHttpDoorstopRecord','RizWinHttpDoorstopAttach')
    $recordSym = if($EngineTransport){'RizEngineTransportDoorstopRecord'}else{'RizEngineDoorstopRecord'}
    if($EngineBootstrap -or $EngineTransport){$mapSymbols+=@('DllEntry',$recordSym,'RizEngineDoorstopBeforeAttach','RizEngineDoorstopBeforeIat',
        'RizEngineDoorstopAfterAttach','RizEngineDoorstopOnFailure','RizEngineDoorstopOnDetach')}
    foreach($symbol in $mapSymbols){
        if([regex]::Matches($mapText,'(?m)^\s+[0-9A-Fa-f]+:[0-9A-Fa-f]+\s+'+[regex]::Escape($symbol)+'\s+').Count -ne 1){throw "Candidate MAP public symbol missing or ambiguous: $symbol"}
    }
}
Copy-Item -LiteralPath (Join-Path $source 'LICENSE') -Destination (Join-Path $output 'UnityDoorstop.LGPL-2.1.txt') -Force
# Corresponding source is supplied with the modified LGPL component. Exclude
# generated object files and the independent xmake executable tool distribution.
$sourceArchive = Join-Path $output 'doorstop-corresponding-source.zip'
$sourceItems = @('src','assets','LICENSE','README.md','xmake.lua','info.lua','RIZ-UPSTREAM.json','RIZLINE-MODIFICATIONS.md') | ForEach-Object { Join-Path $source $_ }
Compress-Archive -LiteralPath $sourceItems -DestinationPath $sourceArchive -Force
$outputFiles=@('winhttp.dll','doorstop-corresponding-source.zip','UnityDoorstop.LGPL-2.1.txt','configure.log','build.log')
if($FixedSystemWinHttp){$outputFiles+=@('doorstop.map','seh-import.log','build-winhttp-seh.cmd')}
if($EngineBootstrap -or $EngineTransport){$outputFiles+=@('engine-bootstrap-import.log','build-engine-bootstrap-import.cmd')}
$files = $outputFiles | ForEach-Object {
    [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath (Join-Path $output $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$profile=if($FixedSystemWinHttp){'fixed-system-winhttp-candidate'}else{'legacy-default'}
if($EngineBootstrap){$profile='fixed-system-winhttp-engine-bootstrap-candidate'}
if($EngineTransport){$profile=if($Owned){'fixed-system-winhttp-engine-transport-owned-candidate'}else{'fixed-system-winhttp-engine-transport-candidate'}}
$manifest=[ordered]@{ schemaVersion=1; upstreamCommit=$lock.commit; upstreamArchiveSha256=$lock.archiveSha256; xmakeSha256=$lock.xmakeSha256; modifiedAt='2026-10-24'; sourceDirectory=$source; files=@($files); installed=$false; gameRuntimeVerified=$false; profile=$profile }
if($FixedSystemWinHttp){
    $manifest.modifiedAt='2026-10-25';$manifest.fixedSystemWinHttp=$true
    $manifest.embeddedDefine='RIZ_WINHTTP_EMBEDDED_DOORSTOP=1';$manifest.internalMap='doorstop.map'
    $manifest.publicExportCount=82;$manifest.canonicalFixedOrdinals=$fixedOrdinals
    $manifest.loaderOutsideInitialization=$false;$manifest.actualDllEntryPathImplemented=$true
    $manifest.productionLoaderStageVerified=$false;$manifest.engineV4Issued=$false
    $manifest.remainingLegacyExports=78;$manifest.legacyVersionNamesCallableVerified=$false
    $manifest.canonicalFiles=@($fixedFiles|ForEach-Object{[ordered]@{path="src/windows/$_";sha256=(Get-FileHash -LiteralPath (Join-Path $source "src/windows/$_") -Algorithm SHA256).Hash.ToLowerInvariant()}})
}
if($EngineBootstrap){
    $manifest.engineBootstrap=$true;$manifest.engineBootstrapOrdinaryImportRequired=$true
    $manifest.engineBootstrapAbiNames=$bootstrapNames;$manifest.engineBootstrapPublicationVersion=4
    $manifest.engineBootstrapPublicationBytes=4096;$manifest.engineBootstrapProfile=2
    $manifest.engineBootstrapReadyAdmissionMask=65280;$manifest.engineBootstrapPrivateRecord='RizEngineDoorstopRecord'
    $manifest.engineBootstrapPrivateRecordBytes=12448;$manifest.engineBootstrapDisabledRequiresZeroResponsibility=$true
    $manifest.engineBootstrapProductionAdmissionVerified=$false
    $manifest.engineBootstrapFiles=@($engineFiles|ForEach-Object{[ordered]@{path="src/windows/$_";sha256=(Get-FileHash -LiteralPath (Join-Path $source "src/windows/$_") -Algorithm SHA256).Hash.ToLowerInvariant()}})
}
if($EngineTransport){
    $manifest.engineBootstrap=$true;$manifest.engineTransport=$true;$manifest.engineBootstrapOrdinaryImportRequired=$true
    $manifest.engineBootstrapAbiNames=$bootstrapNames;$manifest.engineBootstrapPublicationVersion=5
    $manifest.engineBootstrapPublicationBytes=91376;$manifest.engineBootstrapProfile=$(if($Owned){1}else{2})
    $manifest.engineBootstrapReadyAdmissionMask=$null;$manifest.engineBootstrapPrivateRecord='RizEngineTransportDoorstopRecord'
    $manifest.engineBootstrapPrivateRecordBytes=274288;$manifest.engineBootstrapDisabledRequiresZeroResponsibility=$true
    $manifest.engineBootstrapProductionAdmissionVerified=$(-not $Owned)
    $manifest.engineBootstrapFiles=@($engineFiles|ForEach-Object{[ordered]@{path="src/windows/$_";sha256=(Get-FileHash -LiteralPath (Join-Path $source "src/windows/$_") -Algorithm SHA256).Hash.ToLowerInvariant()}})
}
$manifest |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'doorstop-manifest.json') -Encoding utf8
Write-Output "Doorstop native pre-init: $output"
