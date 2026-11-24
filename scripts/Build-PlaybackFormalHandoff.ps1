#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$Project='tests/RizToolbox.PlaybackFormalHandoff.Tests/RizToolbox.PlaybackFormalHandoff.Tests.csproj')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop'
$source=Split-Path -Parent $PSScriptRoot
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(-not $out.StartsWith('D:\riz-build-cache-next\',[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $out)){throw 'Use a fresh D-cache build directory.'}
New-Item -ItemType Directory -Path $out|Out-Null
New-Item -ItemType Directory -Path (Join-Path $out 'temporary')|Out-Null
$env:DOTNET_CLI_HOME=Join-Path $out 'dotnet-home'
$env:NUGET_PACKAGES=Join-Path $out 'packages'
$env:TEMP=Join-Path $out 'temporary';$env:TMP=$env:TEMP
$env:DOTNET_NOLOGO='1';$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
function Fact([string]$Path){$p=Get-Item -LiteralPath $Path;[ordered]@{path=$p.FullName;sha256=(Get-FileHash -LiteralPath $p.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$p.Length}}
$sources=@(Get-ChildItem -LiteralPath $source -Recurse -File|Where-Object{$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
$arguments=@('build',(Join-Path $source $Project),'-c','Release','--artifacts-path',(Join-Path $out 'artifacts'),'-o',(Join-Path $out 'managed'),'-p:DebugType=portable','-p:UseSharedCompilation=false','--nologo')
$result=[ordered]@{kind='fresh-current-source-managed-build';passed=$false;actualExitCode=$null;project=$Project;arguments=$arguments;sources=$sources;outputs=@();recipe=Fact $PSCommandPath}
try{
    & (Get-RizDotnetPath) @arguments 1>(Join-Path $out 'build.stdout.log') 2>(Join-Path $out 'build.stderr.log')
    $result.actualExitCode=$LASTEXITCODE;[IO.File]::WriteAllText((Join-Path $out 'build.exit.txt'),[string]$LASTEXITCODE)
    if($LASTEXITCODE -ne 0){throw 'Actual managed build failed; retained logs identify the error.'}
    foreach($row in $sources){if((Fact $row.path).sha256 -cne $row.sha256){throw 'A managed build source changed.'}}
    $result.passed=$true
}finally{
    $result.outputs=@(Get-ChildItem -LiteralPath $out -Recurse -File|Sort-Object FullName|ForEach-Object{Fact $_.FullName})
    [IO.File]::WriteAllText((Join-Path $out 'build-result.json'),($result|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
}
Write-Output "Current source managed build actualExit=$($result.actualExitCode)."
