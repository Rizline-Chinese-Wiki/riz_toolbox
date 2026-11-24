#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh fixture output directory.'}
$source=Join-Path $repo 'native/RizToolbox.PlaybackFixture'
foreach($path in @($source,$output,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported build path metacharacter.'}}
New-Item -ItemType Directory -Path $output | Out-Null
$commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$output\OwnedPlayback.asm.obj`" `"$source\OwnedPlayback.asm`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\OwnedPlayback.cpp.obj`" /Fd`"$output\OwnedPlayback.compiler.pdb`" `"$source\OwnedPlayback.cpp`" `"$output\OwnedPlayback.asm.obj`" /link /OUT:`"$output\OwnedPlayback.dll`" /PDB:`"$output\OwnedPlayback.pdb`" /IMPLIB:`"$output\OwnedPlayback.lib`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$output\OwnedPlayback.dll`" > `"$output\disassembly.txt`"",'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$output\OwnedPlayback.dll`" > `"$output\unwindinfo.txt`"",'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /imports `"$output\OwnedPlayback.dll`" > `"$output\imports.txt`"",'if errorlevel 1 exit /b 1')
[IO.File]::WriteAllLines((Join-Path $output 'build.cmd'),$commands,[Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c (Join-Path $output 'build.cmd') 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if($LASTEXITCODE -ne 0){throw "Owned playback fixture build failed: $LASTEXITCODE"}
[ordered]@{kind='owned-playback-broker-fixture'; actualExitCode=$LASTEXITCODE; gameCodeExecuted=$false
    files=@(Get-ChildItem -LiteralPath $output -File | ForEach-Object {[ordered]@{name=$_.Name;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
    sources=@(Get-ChildItem -LiteralPath $source -File | ForEach-Object {[ordered]@{path=$_.FullName;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
