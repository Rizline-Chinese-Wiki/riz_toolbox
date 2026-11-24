#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SkipEntryFixture,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$source=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.cpp'
$header=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'
$inlineHeader=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineInlineProbe.h'
$inlineAssembly=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineInlineProbeEntry.asm'
$schedulerSource=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineSchedulerContext.cpp'
$schedulerHeader=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineSchedulerContext.h'
$schedulerAssembly=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineSchedulerEntry.asm'
$waitSource=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineWaitContext.cpp'
$waitHeader=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineWaitContext.h'
$waitAssembly=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineWaitEntry.asm'
$midHeader=Join-Path $repoRoot 'native/RizToolbox.JudgeContext/MidContext.h'
$probe=Join-Path $repoRoot 'native/RizToolbox.EngineNodeKernel/KernelEntryFixture.cpp'
$output=[IO.Path]::GetFullPath($OutputDirectory)
foreach($path in @($source,$header,$inlineHeader,$inlineAssembly,$schedulerSource,$schedulerHeader,$schedulerAssembly,$waitSource,$waitHeader,$waitAssembly,$midHeader,$probe,$output,$VcVars)){
    if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported build path metacharacter.'}
}
foreach($path in @($source,$header,$inlineHeader,$inlineAssembly,$schedulerSource,$schedulerHeader,$schedulerAssembly,$waitSource,$waitHeader,$waitAssembly,$midHeader,$VcVars)){
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing kernel input: $path"}
}
if(-not $SkipEntryFixture -and -not(Test-Path -LiteralPath $probe -PathType Leaf)){throw 'Missing kernel entry fixture source.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$sourcePaths=@($source,$header,$inlineHeader,$inlineAssembly,$schedulerSource,$schedulerHeader,$schedulerAssembly,$waitSource,$waitHeader,$waitAssembly,$midHeader,$PSCommandPath)
if(-not $SkipEntryFixture){$sourcePaths+=$probe}
$inputs=@($sourcePaths|ForEach-Object{@{path=$_;sha256=(Hash $_)}})
New-Item -ItemType Directory -Path $output -Force | Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines((Join-Path $output 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),$utf8)
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\seh.def`" /out:`"$output\seh.lib`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi /DRIZ_JUDGE_CONTEXT_BUILD /DRIZ_ENGINE_INLINE_PROBE /DRIZ_ENGINE_WAIT_CONTEXT /Fo`"$output\EngineNodeContext.obj`" /Fd`"$output\EngineNodeContext.compiler.pdb`" `"$source`"",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /W3 /WX /Zi /Fo`"$output\EngineInlineProbeEntry.obj`" `"$inlineAssembly`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi /DRIZ_JUDGE_CONTEXT_BUILD /DRIZ_ENGINE_WAIT_CONTEXT /Fo`"$output\EngineSchedulerContext.obj`" /Fd`"$output\EngineSchedulerContext.compiler.pdb`" `"$schedulerSource`"",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /W3 /WX /Zi /Fo`"$output\EngineSchedulerEntry.obj`" `"$schedulerAssembly`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi /DRIZ_JUDGE_CONTEXT_BUILD /DRIZ_ENGINE_WAIT_CONTEXT /Fo`"$output\EngineWaitContext.obj`" /Fd`"$output\EngineWaitContext.compiler.pdb`" `"$waitSource`"",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /W3 /WX /Zi /Fo`"$output\EngineWaitEntry.obj`" `"$waitAssembly`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /NOENTRY /NODEFAULTLIB /MACHINE:X64 /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG `"$output\EngineNodeContext.obj`" `"$output\EngineInlineProbeEntry.obj`" `"$output\EngineSchedulerContext.obj`" `"$output\EngineSchedulerEntry.obj`" `"$output\EngineWaitContext.obj`" `"$output\EngineWaitEntry.obj`" `"$output\seh.lib`" kernel32.lib /OUT:`"$output\RizToolbox.EngineNodeKernel.dll`" /IMPLIB:`"$output\RizToolbox.EngineNodeKernel.lib`" /PDB:`"$output\RizToolbox.EngineNodeKernel.pdb`"",'if errorlevel 1 exit /b 1')
if(-not $SkipEntryFixture){
    $commands+=@("cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\KernelEntryFixture.obj`" /Fd`"$output\KernelEntryFixture.compiler.pdb`" `"$probe`" /link /OUT:`"$output\EngineNodeKernel.EntryFixture.exe`" /PDB:`"$output\KernelEntryFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1')
}
$names=@('RizToolbox.EngineNodeKernel.dll')
if(-not $SkipEntryFixture){$names+='EngineNodeKernel.EntryFixture.exe'}
foreach($name in $names){
    $commands+="dumpbin /nologo /headers /imports /exports /unwindinfo /relocations `"$output\$name`" > `"$output\$name.pe.txt`""
    $commands+='if errorlevel 1 exit /b 1'
}
$commands+="dumpbin /nologo /headers /symbols /directives `"$output\EngineNodeContext.obj`" > `"$output\EngineNodeContext.obj.txt`""
$commands+='if errorlevel 1 exit /b 1'
$commands+="dumpbin /nologo /headers /symbols /directives `"$output\EngineSchedulerContext.obj`" > `"$output\EngineSchedulerContext.obj.txt`""
$commands+='if errorlevel 1 exit /b 1'
$commands+="dumpbin /nologo /headers /symbols /directives `"$output\EngineWaitContext.obj`" > `"$output\EngineWaitContext.obj.txt`""
$commands+='if errorlevel 1 exit /b 1'
$command=Join-Path $output 'build-engine-node-kernel.cmd'
[IO.File]::WriteAllLines($command,$commands,$utf8)
& $env:ComSpec /d /c $command 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw "Engine node kernel build failed: $LASTEXITCODE"}
foreach($inputRow in $inputs){if((Hash $inputRow.path) -ne $inputRow.sha256){throw "Kernel source changed during build: $($inputRow.path)"}}
$files=@($names | ForEach-Object{$path=Join-Path $output $_;@{name=$_;path=$path;sha256=(Hash $path);role=$(if($_ -eq 'RizToolbox.EngineNodeKernel.dll'){'kernel'}else{'entry-fixture'})}})
[ordered]@{schemaVersion=1;kind='engine-node-kernel-build';files=$files;sources=$inputs;compilerEnvironment=$VcVars;
    entryFixtureBuilt=(-not $SkipEntryFixture);canonicalSourceReused=$true;fixtureExecuted=$false;gameExecuted=$false;installed=$false;
    productionFirstInstallVerified=$false;engineCancellationCompleted=$false;inlineSideAbi=1;legacyNodeTransportAbi=2;
    schedulerObservationAbi=1;schedulerTypedChannels=6;schedulerFrameSize=256;schedulerRowFrameSize=224;
    waitObservationAbi=1;waitTypedChannels=3;waitPhysicalKinds=13;waitPhaseSites=18;waitFrameSize=240;waitPhaseFrameSize=288;
    sourceHashesUnchanged=$true} | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $output 'kernel-manifest.json') -Encoding utf8
Write-Output "Engine node kernel: $output"
