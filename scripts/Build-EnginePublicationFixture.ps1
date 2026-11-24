#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '',
    [string]$HostHeaders = '',
    [string]$Python = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }
if (-not $HostHeaders) { $HostHeaders = Get-RizHostHeadersDirectory }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
$fixture=Join-Path $repo 'native/RizToolbox.EngineBootstrapFixture'
$source=Join-Path $repo 'native/RizToolbox.EngineBootstrap'
$generator=Join-Path $repo 'docs/validation/foundation-engine-publication-20261025/generate-owned-profile.py'
$kernel=Join-Path $repo 'artifacts/package/BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
foreach($path in @($output,$fixture,$source,$generator,$kernel,$VcVars,$HostHeaders)){
    if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported compiler path metacharacter.'}
}
if(Test-Path -LiteralPath (Join-Path $output 'publication-build-manifest.json')){throw 'Preserve the completed publication build.'}
New-Item -ItemType Directory -Path $output -Force|Out-Null
$utf8=[Text.UTF8Encoding]::new($false)
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
if((Hash $kernel) -ne '923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662'){throw 'Existing Kernel bytes differ from verified independent core.'}
Copy-Item -LiteralPath $kernel -Destination (Join-Path $output 'RizToolbox.EngineNodeKernel.dll')
$bootstrapNames=@('RizEngineFirstInstall_GetVersion','RizEngineFirstInstall_GetInfoV4','RizEngineFirstInstall_VerifyV4','RizEngineFirstInstall_AbortAfterPublication')
$kernelNames=@('RizEngineNodeContext_GetAbiVersion','RizEngineNodeContext_GetLimit','RizEngineNodeContext_GetFrameSize','RizEngineNodeContext_GetCompletionSize',
    'RizEngineNodeContext_GetEntry','RizEngineNodeContext_Configure','RizEngineNodeContext_Attach','RizEngineNodeContext_GetInfo')
[IO.File]::WriteAllLines((Join-Path $output 'bootstrap.def'),@('LIBRARY RizToolbox.EngineBootstrap.dll','EXPORTS')+$bootstrapNames,$utf8)
[IO.File]::WriteAllLines((Join-Path $output 'kernel.def'),@('LIBRARY RizToolbox.EngineNodeKernel.dll','EXPORTS')+$kernelNames,$utf8)
[IO.File]::WriteAllLines((Join-Path $output 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),$utf8)
$prelude=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
$common='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
$link='/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands=$prelude+@(
    "lib /nologo /machine:x64 /def:`"$output\bootstrap.def`" /out:`"$output\RizToolbox.EngineBootstrap.lib`"",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\kernel.def`" /out:`"$output\RizToolbox.EngineNodeKernel.lib`"",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\seh.def`" /out:`"$output\seh.lib`"",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$output\Targets.asm.obj`" `"$fixture\Targets.asm`"",'if errorlevel 1 exit /b 1',
    "cl $common /c /DRIZ_ENGINE_OWNED_TARGETS_BUILD /Fo`"$output\Targets.obj`" /Fd`"$output\Targets.compiler.pdb`" `"$fixture\Targets.cpp`"",'if errorlevel 1 exit /b 1',
    "cl $common /c /DRIZ_ENGINE_OWNED_TARGETS_BUILD /Fo`"$output\Importer.obj`" /Fd`"$output\Importer.compiler.pdb`" `"$fixture\Importer.cpp`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL `"$output\Targets.obj`" `"$output\Importer.obj`" `"$output\Targets.asm.obj`" `"$output\RizToolbox.EngineBootstrap.lib`" /OUT:`"$output\RizEngineOwnedTargets.dll`" /IMPLIB:`"$output\RizEngineOwnedTargets.lib`" /PDB:`"$output\Targets.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /c /I`"$HostHeaders`" /Fo`"$output\ManagedHost.obj`" /Fd`"$output\ManagedHost.compiler.pdb`" `"$fixture\ManagedHost.cpp`"",'if errorlevel 1 exit /b 1',
    "cl $common /Fo`"$output\Host.obj`" /Fd`"$output\Host.compiler.pdb`" `"$fixture\Host.cpp`" /link `"$output\ManagedHost.obj`" `"$output\RizEngineOwnedTargets.lib`" /OUT:`"$output\EnginePublication.Host.exe`" /PDB:`"$output\Host.pdb`" $link",'if errorlevel 1 exit /b 1')
[IO.File]::WriteAllLines((Join-Path $output 'build-owned-callers.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build-owned-callers.cmd') 2>&1|Tee-Object -FilePath (Join-Path $output 'build-owned-callers.log')
if($LASTEXITCODE -ne 0){throw 'Owned publication callers did not compile.'}
& $Python $generator --target (Join-Path $output 'RizEngineOwnedTargets.dll') --executable (Join-Path $output 'EnginePublication.Host.exe') --kernel (Join-Path $output 'RizToolbox.EngineNodeKernel.dll') --header (Join-Path $output 'EngineBootstrapOwnedProfile.h') --output (Join-Path $output 'owned-profile.json')
if($LASTEXITCODE -ne 0){throw 'Exact compiled owned profile generation failed.'}
$profiles=@(@{name='owned';defines='/DRIZ_ENGINE_BOOTSTRAP_OWNED=1';stage=0;index=0})
foreach($plan in @(@(1,0),@(8,0))){$profiles+=@{name="fault-$($plan[0])-$($plan[1])";defines='/DRIZ_ENGINE_BOOTSTRAP_OWNED=1';stage=$plan[0];index=$plan[1]}}
foreach($stage in @(2,3,4,5,6,7)){foreach($index in 0..3){$profiles+=@{name="fault-$stage-$index";defines='/DRIZ_ENGINE_BOOTSTRAP_OWNED=1';stage=$stage;index=$index}}}
$profiles+=@{name='production-candidate';defines='';stage=0;index=0}
$commands=$prelude
$files=[Collections.Generic.List[object]]::new()
foreach($profile in $profiles){
    $directory=Join-Path $output $profile.name;New-Item -ItemType Directory -Path $directory -Force|Out-Null
    $defines=$profile.defines
    if($profile.stage){$defines+=" /DRIZ_ENGINE_BOOTSTRAP_FAULT_STAGE=$($profile.stage) /DRIZ_ENGINE_BOOTSTRAP_FAULT_INDEX=$($profile.index)"}
    $commands+=@(
        "ml64 /nologo /c /Zi /Fo`"$directory\EngineOriginals.obj`" `"$source\EngineOriginals.asm`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi $defines /I`"$output`" /Fo`"$directory\EngineFirstInstall.obj`" /Fd`"$directory\EngineFirstInstall.compiler.pdb`" `"$source\EngineFirstInstall.cpp`"",'if errorlevel 1 exit /b 1',
        "link /nologo /DLL /NODEFAULTLIB /MACHINE:X64 /ENTRY:RizEngineBootstrapEntry /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG /MAP:`"$directory\bootstrap.map`" `"$directory\EngineFirstInstall.obj`" `"$directory\EngineOriginals.obj`" `"$output\RizToolbox.EngineNodeKernel.lib`" `"$output\seh.lib`" kernel32.lib /OUT:`"$directory\RizToolbox.EngineBootstrap.dll`" /IMPLIB:`"$directory\RizToolbox.EngineBootstrap.lib`" /PDB:`"$directory\Bootstrap.pdb`"",'if errorlevel 1 exit /b 1')
    $files.Add(@{profile=$profile.name;stage=$profile.stage;index=$profile.index;path=(Join-Path $directory 'RizToolbox.EngineBootstrap.dll')})
}
[IO.File]::WriteAllLines((Join-Path $output 'build-bootstrap.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build-bootstrap.cmd') 2>&1|Tee-Object -FilePath (Join-Path $output 'build-bootstrap.log')
if($LASTEXITCODE -ne 0){throw 'Native Bootstrap profiles did not compile.'}
foreach($file in $files){$file.sha256=Hash $file.path}
foreach($name in @('RizToolbox.EngineNodeKernel.dll','RizEngineOwnedTargets.dll','EnginePublication.Host.exe')){$files.Add(@{profile='shared';path=(Join-Path $output $name);sha256=(Hash (Join-Path $output $name))})}
$paths=@('native/RizToolbox.EngineBootstrap/EnginePublication.h','native/RizToolbox.EngineBootstrap/EngineGameProfile.h',
    'native/RizToolbox.EngineBootstrap/EngineBootstrapProfile.h','native/RizToolbox.EngineBootstrap/EngineFirstInstall.cpp','native/RizToolbox.EngineBootstrap/EngineOriginals.asm',
    'native/RizToolbox.EngineBootstrapFixture/FixtureApi.h','native/RizToolbox.EngineBootstrapFixture/Targets.cpp','native/RizToolbox.EngineBootstrapFixture/Targets.asm',
    'native/RizToolbox.EngineBootstrapFixture/Importer.cpp','native/RizToolbox.EngineBootstrapFixture/Host.cpp','native/RizToolbox.EngineBootstrapFixture/ManagedHost.cpp',
    'scripts/Build-EnginePublicationFixture.ps1','docs/validation/foundation-engine-publication-20261025/generate-owned-profile.py')
[ordered]@{kind='engine-publication-build';schemaVersion=1;files=@($files);sources=@($paths|ForEach-Object{@{path=(Join-Path $repo $_);sha256=(Hash (Join-Path $repo $_))}});
    generatedProfile=@{path=(Join-Path $output 'owned-profile.json');sha256=(Hash (Join-Path $output 'owned-profile.json'))};
    generatedHeader=@{path=(Join-Path $output 'EngineBootstrapOwnedProfile.h');sha256=(Hash (Join-Path $output 'EngineBootstrapOwnedProfile.h'))};
    fixtureExecuted=$false;gameExecuted=$false;installed=$false;productionAdmissionProved=$false}|ConvertTo-Json -Depth 9|
    Set-Content -LiteralPath (Join-Path $output 'publication-build-manifest.json') -Encoding utf8
Write-Output "Native publication profiles built: $output"
