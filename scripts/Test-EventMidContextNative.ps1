#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$HelperDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\event-mid-context-native' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperDll = Join-Path ([IO.Path]::GetFullPath($HelperDirectory)) 'RizToolbox.JudgeContext.dll'
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$sourceRoot = Join-Path $repoRoot 'native\RizToolbox.JudgeContext\fixture'
foreach ($path in @($sourceRoot, $nativeOutput, $helperDll, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($vcvarsPath, $helperDll)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Build the existing native helper first; required file is missing: $path" }
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$fixture = Join-Path $nativeOutput 'EventMidContext.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-event-mid-fixture.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\MidCaller.obj`" `"$sourceRoot\MidCaller.asm`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\MidFixture.obj`" /Fd`"$nativeOutput\MidFixture.compiler.pdb`" `"$sourceRoot\MidFixture.cpp`" `"$nativeOutput\MidCaller.obj`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\EventMidContext.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$fixture`" > `"$nativeOutput\disassembly.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native event mid fixture build failed: $LASTEXITCODE" }
& $fixture $helperDll 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'fixture-result.json')
if ($LASTEXITCODE -ne 0) { throw "Native event mid fixture failed: $LASTEXITCODE" }
$files = @($helperDll, $fixture) | ForEach-Object {
    [ordered]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion = 1; kind = 'native-event-mid-full-state-fixture'; fixtureOnlyPatch = $true; nativeParentVerified = $false; gameRuntimeVerified = $false; files = @($files) } |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "Native event mid fixture: $nativeOutput"
