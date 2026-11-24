#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$PublisherDirectory,
    [string]$VcVars = '',
    [switch]$EngineKernel
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$source=Join-Path $repoRoot 'native/RizToolbox.EarlyFixedFixture'
$fixed=Join-Path $repoRoot 'native/RizToolbox.FixedPublisher'
$output=[IO.Path]::GetFullPath($OutputDirectory)
$helperName=if($EngineKernel){'RizToolbox.EngineNodeKernel.dll'}else{'RizToolbox.JudgeContext.dll'}
$publisherName=if($EngineKernel){'RizKernelFixedPublisher.dll'}else{'RizFixedPublisher.dll'}
$prefix=if($EngineKernel){'RizKernelOwned'}else{'RizOwned'}
$profileDefine=if($EngineKernel){'/DRIZ_EARLY_ENGINE_KERNEL=1'}else{''}
$helper=(Resolve-Path -LiteralPath (Join-Path $HelperDirectory $helperName)).Path
$publisher=(Resolve-Path -LiteralPath (Join-Path $PublisherDirectory $publisherName)).Path
$transitiveSources=@()
if($EngineKernel){
    $kernelManifestPath=(Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'kernel-manifest.json')).Path
    $publisherManifestPath=(Resolve-Path -LiteralPath (Join-Path $PublisherDirectory 'fixture-build-manifest.json')).Path
    $kernelManifest=Get-Content -LiteralPath $kernelManifestPath -Raw|ConvertFrom-Json -AsHashtable
    $publisherManifest=Get-Content -LiteralPath $publisherManifestPath -Raw|ConvertFrom-Json -AsHashtable
    if($kernelManifest.kind -ne 'engine-node-kernel-build' -or $publisherManifest.profile -ne 'engine-kernel' -or $publisherManifest.publisherVersion -ne 2){throw 'Kernel/publisher completed-build identity mismatch.'}
    foreach($item in @(@{manifest=$kernelManifest;path=$helper;name=$helperName},@{manifest=$publisherManifest;path=$publisher;name=$publisherName})){
        $rows=@($item.manifest.files|Where-Object name -eq $item.name)
        if($rows.Count -ne 1 -or [IO.Path]::GetFullPath($rows[0].path) -ne [IO.Path]::GetFullPath($item.path) -or
            $rows[0].sha256 -ne (Get-FileHash -LiteralPath $item.path -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Kernel/publisher bytes differ from exact completed build.'}
    }
    $transitiveSources=@($kernelManifest.sources)+@($publisherManifest.sources)
    foreach($path in @($kernelManifestPath,$publisherManifestPath)){$transitiveSources+=@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
    foreach($row in $transitiveSources){
        if(-not [IO.Path]::IsPathFullyQualified($row.path) -or $row.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
            $row.sha256 -ne (Get-FileHash -LiteralPath $row.path -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Transitive native source differs from its completed build.'}
    }
}
foreach($path in @($source,$fixed,$output,$helper,$publisher,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported build path metacharacter.'}}
$sourceFiles=@('EarlyFixedShared.h','EarlyFixedProfile.h','Recorder.cpp','Proxy.cpp','ImporterApi.h','Importer.cpp','Host.cpp','EarlyFixedDebugger.cpp')
foreach($name in $sourceFiles){if(-not(Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)){throw "Missing early fixed input: $name"}}
New-Item -ItemType Directory -Path $output -Force|Out-Null
Copy-Item -LiteralPath $helper -Destination (Join-Path $output $helperName) -Force
Copy-Item -LiteralPath $publisher -Destination (Join-Path $output $publisherName) -Force
$utf8=[Text.UTF8Encoding]::new($false)
$helperExports=@('RizEngineNodeContext_GetAbiVersion','RizEngineNodeContext_GetLimit','RizEngineNodeContext_GetFrameSize',
    'RizEngineNodeContext_GetCompletionSize','RizEngineNodeContext_GetEntry','RizEngineNodeContext_Configure','RizEngineNodeContext_Attach','RizEngineNodeContext_GetInfo')
$publisherExports=@('RizFixedPublisher_Prepare','RizFixedPublisher_Bind','RizFixedPublisher_Commit','RizFixedPublisher_GetInfo','RizFixedPublisher_GetVersion','RizFixedPublisher_GetInfoSize')
[IO.File]::WriteAllLines((Join-Path $output 'helper.def'),@("LIBRARY $helperName",'EXPORTS')+$helperExports,$utf8)
[IO.File]::WriteAllLines((Join-Path $output 'publisher.def'),@("LIBRARY $publisherName",'EXPORTS')+$publisherExports,$utf8)
[IO.File]::WriteAllLines((Join-Path $output 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),$utf8)
$targets=@('LIBRARY RizFixedOwnedTargets.dll','EXPORTS','RizFixedOwnedMarker DATA')
foreach($kind in @('Start','Execute','Release','Detach')){foreach($suffix in @('','PrefixEnd','PrologEnd','BodyReturn','End')){$targets+="RizFixedOwned$kind$suffix"}}
[IO.File]::WriteAllLines((Join-Path $output 'targets.def'),$targets,$utf8)
$common="/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf $profileDefine"
$link='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$minimal='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO'
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
foreach($name in @('helper','publisher','seh')){
    $commands+="lib /nologo /machine:x64 /def:`"$output\$name.def`" /out:`"$output\$name.lib`""
    $commands+='if errorlevel 1 exit /b 1'
}
$commands+=@(
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa $profileDefine /Fo`"$output\Recorder.obj`" `"$source\Recorder.cpp`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /ENTRY:DllMain /NODEFAULTLIB /MACHINE:X64 `"$output\Recorder.obj`" `"$output\publisher.lib`" `"$output\seh.lib`" kernel32.lib /OUT:`"$output\${prefix}Recorder.dll`" /IMPLIB:`"$output\RizOwnedRecorder.lib`" $minimal",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa $profileDefine /Fo`"$output\Proxy.obj`" `"$source\Proxy.cpp`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /ENTRY:DllMain /NODEFAULTLIB /MACHINE:X64 `"$output\Proxy.obj`" `"$output\RizOwnedRecorder.lib`" `"$output\helper.lib`" `"$output\publisher.lib`" `"$output\seh.lib`" kernel32.lib /OUT:`"$output\${prefix}Proxy.dll`" /IMPLIB:`"$output\RizOwnedProxy.lib`" $minimal",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$output\FixedTargets.obj`" `"$fixed\FixedTargets.asm`"",'if errorlevel 1 exit /b 1',
    "cl $common /LD /Fo`"$output\Importer.obj`" /Fd`"$output\Importer.compiler.pdb`" `"$source\Importer.cpp`" /link /DEF:`"$output\targets.def`" `"$output\FixedTargets.obj`" `"$output\RizOwnedProxy.lib`" `"$output\RizOwnedRecorder.lib`" `"$output\helper.lib`" /OUT:`"$output\RizFixedOwnedTargets.dll`" /IMPLIB:`"$output\RizFixedOwnedTargets.lib`" /PDB:`"$output\Importer.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /Fo`"$output\Host.obj`" /Fd`"$output\Host.compiler.pdb`" `"$source\Host.cpp`" /link `"$output\RizFixedOwnedTargets.lib`" `"$output\RizOwnedProxy.lib`" `"$output\RizOwnedRecorder.lib`" `"$output\helper.lib`" /OUT:`"$output\${prefix}Host.exe`" /PDB:`"$output\Host.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /Fo`"$output\EarlyFixedDebugger.obj`" /Fd`"$output\EarlyFixedDebugger.compiler.pdb`" `"$source\EarlyFixedDebugger.cpp`" /link /OUT:`"$output\${prefix}Debugger.exe`" /PDB:`"$output\Debugger.pdb`" $link",'if errorlevel 1 exit /b 1'
)
$binaryNames=@("${prefix}Recorder.dll","${prefix}Proxy.dll",'RizFixedOwnedTargets.dll',"${prefix}Host.exe","${prefix}Debugger.exe",$helperName,$publisherName)
foreach($name in $binaryNames){$commands+="dumpbin /nologo /headers /imports /exports /unwindinfo `"$output\$name`" > `"$output\$name.pe.txt`"";$commands+='if errorlevel 1 exit /b 1'}
$command=Join-Path $output 'build-early-fixed.cmd';[IO.File]::WriteAllLines($command,$commands,$utf8)
& $env:ComSpec /d /c $command 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw "Owned early fixed build failed: $LASTEXITCODE"}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$files=@($binaryNames|ForEach-Object{$path=Join-Path $output $_;@{name=$_;path=$path;sha256=(Hash $path)}})
$inputs=@($sourceFiles|ForEach-Object{$path=Join-Path $source $_;@{path=$path;sha256=(Hash $path)}})
foreach($path in @((Join-Path $fixed 'FixedTargets.asm'),(Join-Path $fixed 'FixedPublisher.h'),(Join-Path $fixed 'FixedPublisherProfile.h'),(Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'),$PSCommandPath)){$inputs+=@{path=$path;sha256=(Hash $path)}}
$inputs+=@($transitiveSources)
[ordered]@{schemaVersion=2;kind='owned-early-fixed-build';profile=$(if($EngineKernel){'engine-kernel'}else{'legacy'});publisherVersion=$(if($EngineKernel){2}else{1});wireVersion=$(if($EngineKernel){3}else{2});files=$files;sources=$inputs;reusedPublisherInput=$publisher;reusedPublisherSha256=(Hash $publisher);reusedHelperInput=$helper;reusedHelperSha256=(Hash $helper);
    fixturesExecuted=$false;gameExecuted=$false;productionFirstInstallVerified=$false;installed=$false}|ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Owned early fixed fixture: $output"
