#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [string]$VcVars = '',
    [switch]$EngineKernel,
    [switch]$PublisherOnly
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$source=Join-Path $repoRoot 'native/RizToolbox.FixedPublisher'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if($EngineKernel -and -not $PublisherOnly){throw 'The Kernel profile requires -PublisherOnly; the standalone legacy host has its own record identity.'}
$helperName=if($EngineKernel){'RizToolbox.EngineNodeKernel.dll'}else{'RizToolbox.JudgeContext.dll'}
$publisherName=if($EngineKernel){'RizKernelFixedPublisher.dll'}else{'RizFixedPublisher.dll'}
$profileDefine=if($EngineKernel){'/DRIZ_EARLY_ENGINE_KERNEL=1'}else{''}
$helper=(Resolve-Path -LiteralPath (Join-Path $HelperDirectory $helperName)).Path
foreach($path in @($source,$output,$helper,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported build path metacharacter.'}}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath $helper -Destination (Join-Path $output $helperName) -Force
$exports=@('RizEngineNodeContext_GetAbiVersion','RizEngineNodeContext_GetLimit','RizEngineNodeContext_GetFrameSize',
    'RizEngineNodeContext_GetCompletionSize','RizEngineNodeContext_GetEntry','RizEngineNodeContext_Configure',
    'RizEngineNodeContext_Attach','RizEngineNodeContext_GetInfo')
[IO.File]::WriteAllLines((Join-Path $output 'helper.def'),@("LIBRARY $helperName",'EXPORTS')+$exports,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $output 'native-seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),[Text.UTF8Encoding]::new($false))
$publisherExports=@("LIBRARY $publisherName",'EXPORTS')
$targetExports=@('LIBRARY RizFixedOwnedTargets.dll','EXPORTS')
foreach($kind in @('Start','Execute','Release','Detach')){
    foreach($suffix in @('','PrefixEnd','End')){$publisherExports+="RizFixedOriginal$kind$suffix"}
    foreach($suffix in @('','PrefixEnd','PrologEnd','BodyReturn','End')){$targetExports+="RizFixedOwned$kind$suffix"}
}
[IO.File]::WriteAllLines((Join-Path $output 'publisher.def'),$publisherExports,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $output 'targets.def'),$targetExports,[Text.UTF8Encoding]::new($false))
$common='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
$link='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\helper.def`" /out:`"$output\FixedHelper.lib`"",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\native-seh.def`" /out:`"$output\FixedNativeSeh.lib`"",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$output\FixedOriginals.obj`" `"$source\FixedOriginals.asm`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa $profileDefine /Fo`"$output\FixedPublisher.obj`" `"$source\FixedPublisher.cpp`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /NOENTRY /NODEFAULTLIB /MACHINE:X64 /DEF:`"$output\publisher.def`" `"$output\FixedPublisher.obj`" `"$output\FixedOriginals.obj`" kernel32.lib `"$output\FixedNativeSeh.lib`" /OUT:`"$output\$publisherName`" /IMPLIB:`"$output\RizFixedPublisher.lib`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO",'if errorlevel 1 exit /b 1')
if(-not $PublisherOnly){$commands+=@(
    "ml64 /nologo /c /Zi /Fo`"$output\FixedTargets.obj`" `"$source\FixedTargets.asm`"",'if errorlevel 1 exit /b 1',
    "cl $common /LD /Fo`"$output\OwnedBodies.obj`" /Fd`"$output\OwnedBodies.compiler.pdb`" `"$source\OwnedBodies.cpp`" /link /DEF:`"$output\targets.def`" `"$output\FixedTargets.obj`" /OUT:`"$output\RizFixedOwnedTargets.dll`" /IMPLIB:`"$output\RizFixedOwnedTargets.lib`" /PDB:`"$output\FixedTargets.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /Fo`"$output\FixedPublisherFixture.obj`" /Fd`"$output\FixedPublisherFixture.compiler.pdb`" `"$source\FixedPublisherFixture.cpp`" /link `"$output\RizFixedPublisher.lib`" `"$output\RizFixedOwnedTargets.lib`" `"$output\FixedHelper.lib`" bcrypt.lib /OUT:`"$output\FixedPublisher.Fixture.exe`" /PDB:`"$output\FixedPublisherFixture.pdb`" $link",'if errorlevel 1 exit /b 1')}
$binaryNames=@($publisherName,$helperName)
if(-not $PublisherOnly){$binaryNames+=@('RizFixedOwnedTargets.dll','FixedPublisher.Fixture.exe')}
foreach($name in $binaryNames){
    $commands+="dumpbin /nologo /headers /imports /exports /unwindinfo `"$output\$name`" > `"$output\$name.pe.txt`""
    $commands+='if errorlevel 1 exit /b 1'
}
$commandFile=Join-Path $output 'build-fixed-publisher.cmd'
[IO.File]::WriteAllLines($commandFile,$commands,[Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $commandFile 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw "Fixed publisher fixture build failed: $LASTEXITCODE"}
$files=$binaryNames|ForEach-Object{
    $path=Join-Path $output $_;@{name=$_;path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
$sources=@(Get-ChildItem -LiteralPath $source -File|ForEach-Object{@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}})
foreach($path in @($PSCommandPath,(Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'))){$sources+=@{path=$path;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()}}
[ordered]@{schemaVersion=1;kind='fixed-publisher-fixture-build';profile=$(if($EngineKernel){'engine-kernel'}else{'legacy'});publisherVersion=$(if($EngineKernel){2}else{1});publisherOnly=[bool]$PublisherOnly;files=@($files);sources=$sources;fixtureExecuted=$false;
    gameExecuted=$false;productionFirstInstallVerified=$false;installed=$false}|ConvertTo-Json -Depth 6|
    Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Fixed publisher fixture: $output"
