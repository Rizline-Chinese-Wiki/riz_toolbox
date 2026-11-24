#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OwnedProfileDirectory,
    [Parameter(Mandatory)][string]$LinkDirectory,[Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$native=Join-Path $repo 'native/RizToolbox.EngineBootstrap'
foreach($value in @($OwnedProfileDirectory,$LinkDirectory,$OutputDirectory,$native,$VcVars)){
    if(-not [IO.Path]::IsPathFullyQualified($value) -or $value -match '[\r\n"%&|<>^]'){throw 'Absolute compiler paths without shell metacharacters are required.'}
}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh compatibility directory.'}
if($output.StartsWith('D:\',[StringComparison]::OrdinalIgnoreCase)){throw 'Do not build into the game drive.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$paths=@($PSCommandPath,(Join-Path $OwnedProfileDirectory 'EngineBootstrapOwnedProfile.h'),
    (Join-Path $LinkDirectory 'EngineOriginals.obj'),(Join-Path $LinkDirectory 'kernel.lib'),(Join-Path $LinkDirectory 'seh.lib'))
$paths+=@(Get-ChildItem -LiteralPath $native -File | ForEach-Object{$_.FullName})
$inputs=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=(Hash $_)}})
New-Item -ItemType Directory -Path $output|Out-Null
$inputs|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'inputs-before.json') -Encoding utf8
$commands=@('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1')
foreach($profile in @('owned','default')){
    $directory=Join-Path $output $profile;New-Item -ItemType Directory -Path $directory|Out-Null
    $defines=if($profile -eq 'owned'){'/DRIZ_ENGINE_BOOTSTRAP_OWNED=1'}else{''}
    $commands+=@(
        "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi $defines /I`"$OwnedProfileDirectory`" /Fo`"$directory/EngineFirstInstall.obj`" /Fd`"$directory/compiler.pdb`" `"$native/EngineFirstInstall.cpp`"",'if errorlevel 1 exit /b 1',
        "link /nologo /DLL /NODEFAULTLIB /MACHINE:X64 /ENTRY:RizEngineBootstrapEntry /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG /MAP:`"$directory/bootstrap.map`" `"$directory/EngineFirstInstall.obj`" `"$LinkDirectory/EngineOriginals.obj`" `"$LinkDirectory/kernel.lib`" `"$LinkDirectory/seh.lib`" kernel32.lib /OUT:`"$directory/RizToolbox.EngineBootstrap.dll`" /IMPLIB:`"$directory/bootstrap.lib`" /PDB:`"$directory/bootstrap.pdb`"",'if errorlevel 1 exit /b 1')
}
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines((Join-Path $output 'build.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build.cmd') 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw 'Owned/default compatibility compilation failed.'}
$files=@()
foreach($name in @('EnginePublication.Host.exe','RizEngineOwnedTargets.dll')){
    $source=Join-Path $OwnedProfileDirectory $name;$target=Join-Path $output $name
    Copy-Item -LiteralPath $source -Destination $target
    if((Hash $source) -ne (Hash $target)){throw 'Shared owned binary copy differs.'}
    $files+=@{path=$target;sha256=(Hash $target);profile='shared'}
}
$kernel=Join-Path $LinkDirectory 'RizToolbox.EngineNodeKernel.dll'
if((Hash $kernel) -ne '923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662'){throw 'Fixed Kernel changed.'}
Copy-Item -LiteralPath $kernel -Destination $output
$files+=@{path=(Join-Path $output 'RizToolbox.EngineNodeKernel.dll');sha256=(Hash $kernel);profile='shared'}
foreach($profile in @('owned','default')){
    $path=Join-Path $output "$profile/RizToolbox.EngineBootstrap.dll"
    $files+=@{path=$path;sha256=(Hash $path);profile=$profile}
}
foreach($row in $inputs){if((Hash $row.path) -ne $row.sha256){throw 'Compatibility build input changed.'}}
[ordered]@{kind='engine-publication-build';schemaVersion=1;compatibilityOnly=$true;files=$files;sources=$inputs;
    gameExecuted=$false;installed=$false;testProcessesStarted=0;productionAdmissionProved=$false}|
    ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $output 'publication-build-manifest.json') -Encoding utf8
Write-Output "Owned and default compatibility DLLs compiled: $output"
