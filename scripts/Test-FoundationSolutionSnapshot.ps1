#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$FoundationDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
foreach($path in @($FoundationDirectory,$OutputDirectory)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute paths required.'}}
$foundation=[IO.Path]::GetFullPath($FoundationDirectory);$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve earlier checks; use a fresh output directory.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))}
$proofPath=Join-Path $foundation 'test-result.json';$proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
if($proof.passed -ne $true -or $proof.kind -ne 'foundation-managed-snapshot-checks' -or $proof.fullManagedMatrix -ne $true -or
    @($proof.projects).Count -ne 28 -or @($proof.projects|Where-Object name -ceq 'GameCamera').Count -ne 1){throw 'A successful current 28-suite frozen foundation is required.'}
$source=Join-Path $foundation 'source';$solution=Join-Path $source 'RizToolbox.sln'
if(@($proof.compiledSources|Where-Object path -eq $solution).Count -ne 1){throw 'The solution must be part of the declared frozen inputs.'}
$profile='C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs'
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{schemaVersion=1;kind='foundation-solution-snapshot-build';passed=$false;buildExitCode=$null;warnings=$null;errors=$null
    foundationProof=[ordered]@{path=$proofPath;sha256=Hash $proofPath};solution=[ordered]@{path=$solution;sha256=Hash $solution}
    script=[ordered]@{path=$PSCommandPath;sha256=Hash $PSCommandPath};profile=[ordered]@{path=$profile;sha256=Hash $profile}
    log=$null;sourceHashesUnchanged=$false;failure=$null;gameExecuted=$false;installed=$false;productionAdmissionVerified=$false}
$locks=[Collections.Generic.List[IDisposable]]::new()
try{
    foreach($path in @($proof.compiledSources.path)+@($proofPath,$profile,$PSCommandPath)|Sort-Object -Unique){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    foreach($row in $proof.compiledSources){if((Hash $row.path) -ne $row.sha256){throw "Frozen source changed: $($row.path)"}}
    $log=Join-Path $output 'solution-build.log'
    & (Get-RizDotnetPath) build $solution -c Release -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false `
        "-p:RizEngineProductionProfileSource=$profile" --nologo -v:minimal 2>&1|Out-File -LiteralPath $log
    $result.buildExitCode=$LASTEXITCODE
    $body=[IO.File]::ReadAllText($log)
    $warnings=[regex]::Matches($body,'(?m)^\s*(\d+)\s+(?:个警告|Warning\(s\))\s*$')
    $errors=[regex]::Matches($body,'(?m)^\s*(\d+)\s+(?:个错误|Error\(s\))\s*$')
    if($warnings.Count){$result.warnings=[int]$warnings[-1].Groups[1].Value}
    if($errors.Count){$result.errors=[int]$errors[-1].Groups[1].Value}
    $result.log=[ordered]@{path=$log;sha256=Hash $log}
    if($result.buildExitCode -ne 0 -or $result.errors -ne 0 -or $null -eq $result.warnings){Get-Content -LiteralPath $log -Tail 12;throw 'The actual frozen solution build did not pass with a complete summary.'}
    foreach($row in $proof.compiledSources){if((Hash $row.path) -ne $row.sha256){throw "Frozen source changed: $($row.path)"}}
    $result.sourceHashesUnchanged=$true;$result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};WriteJson $result (Join-Path $output 'test-result.json')}
Write-Output "Frozen current solution passed: $($result.errors) errors, $($result.warnings) warnings."
