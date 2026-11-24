#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$FoundationDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
foreach($path in @($FoundationDirectory,$OutputDirectory)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute directories required.'}}
$foundation=[IO.Path]::GetFullPath($FoundationDirectory);$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh result directory.'}
$source=Join-Path $foundation 'source';$proofPath=Join-Path $foundation 'test-result.json'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
if($proof.passed -ne $true -or $proof.kind -ne 'foundation-managed-snapshot-checks' -or $proof.fullManagedMatrix -ne $true -or
    @($proof.projects).Count -ne 28 -or @($proof.projects|Where-Object name -ceq 'SingleEvent').Count -ne 1 -or
    @($proof.projects|Where-Object name -ceq 'GameAppearance').Count -ne 1 -or
    @($proof.projects|Where-Object name -ceq 'GameCamera').Count -ne 1){throw 'A successful actual current 28-suite foundation snapshot is required.'}
$null=New-Item -ItemType Directory -Path $output
$manifest=[ordered]@{schemaVersion=1;kind='sdk-distribution-snapshot-checks';passed=$false;runnerExitCode=$null;checks=0
    foundationProof=[ordered]@{path=$proofPath;sha256=Hash $proofPath};additionalSources=@();result=$null;failure=$null;gameExecuted=$false;installed=$false}
$locks=[Collections.Generic.List[IDisposable]]::new()
try{
    $relative=@('scripts/Pack-Sdk.ps1','scripts/Test-SdkDistribution.ps1','packaging/sdk/README.md')
    foreach($name in $relative){$path=Join-Path $repo $name;$locks.Add([IO.File]::Open($path,'Open','Read','Read'))
        $row=[ordered]@{path=$path;sha256=Hash $path};$manifest.additionalSources+=,$row
        $destination=Join-Path $source $name;$null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        if(Test-Path -LiteralPath $destination){if((Hash $destination) -ne $row.sha256){throw 'The existing snapshot helper differs from the declared extra source.'}}
        else{Copy-Item -LiteralPath $path -Destination $destination}
    }
    foreach($row in $proof.compiledSources){if((Hash $row.path) -ne $row.sha256){throw 'A frozen foundation input changed before SDK distribution verification.'}}
    & 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File (Join-Path $source 'scripts/Test-SdkDistribution.ps1') 2>&1|
        Tee-Object -FilePath (Join-Path $output 'sdk-distribution.log')
    $manifest.runnerExitCode=$LASTEXITCODE
    $results=@(Get-ChildItem -LiteralPath (Join-Path $source 'artifacts') -Directory -Filter 'sdk-distribution-*'|ForEach-Object{Join-Path $_.FullName 'result.json'}|Where-Object{Test-Path -LiteralPath $_})
    if($manifest.runnerExitCode -ne 0 -or $results.Count -ne 1){throw 'Actual SDK package/template distribution failed or returned ambiguous evidence.'}
    $manifest.result=[ordered]@{path=$results[0];sha256=Hash $results[0]}
    $result=Get-Content -LiteralPath $results[0] -Raw|ConvertFrom-Json
    if($result.checksPassed -ne 14 -or $result.runtimeGameVerification -ne $false){throw 'The actual SDK distribution evidence has the wrong scope.'}
    foreach($row in @($proof.compiledSources)+@($manifest.additionalSources)){if((Hash $row.path) -ne $row.sha256){throw 'A declared SDK distribution input changed.'}}
    $manifest.checks=14;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};WriteJson $manifest (Join-Path $output 'test-result.json')}
Write-Output "Actual isolated SDK package, two generated plugins and formal host loading passed all 14 checks."
