#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [Parameter(Mandatory)][string]$OutputDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/OperationDobbyFixture.cpp'
$header = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/OperationContext.h'
foreach ($path in @($source, $header, $output, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $header, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required Dobby operation fixture input is missing: $path" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$fixture = Join-Path $output 'OperationDobby.Fixture.exe'
$cmdPath = Join-Path $output 'build-operation-dobby-fixture.cmd'
$commands = @(
    '@echo off', 'chcp 65001 >nul', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\OperationDobbyFixture.obj`" /Fd`"$output\OperationDobbyFixture.compiler.pdb`" `"$source`" /link bcrypt.lib /OUT:`"$fixture`" /PDB:`"$output\OperationDobbyFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$output\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$fixture`" > `"$output\disassembly.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native Dobby operation fixture build failed: $LASTEXITCODE" }
[ordered]@{
    schemaVersion=1; kind='native-operation-dobby-fixture-build'; fixture=$fixture
    fixtureSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash.ToLowerInvariant()
    sourceSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
    headerSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $header).Hash.ToLowerInvariant()
    fixtureExecuted=$false; gameCodeExecuted=$false; installed=$false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Native Dobby operation fixture: $fixture"
