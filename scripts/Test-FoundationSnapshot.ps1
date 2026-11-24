#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SourceSnapshotManifest,[Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$Suites=@(),[string]$PlaybackOwnedFixturePath)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
$allSuites=@('Core','Adapter','Hosting','Game','Runtime','Sdk','Scope','Diagnostics','PackageLifecycle','Module','Mutation','Data','Storage','Services',
    'GameResources','GameAppearance','GameCamera','Hooks','Gameplay','GameplaySourceGeneration','Settings','Visual','Performance','AudioContent','UiInput','RunModification','Judgements','SingleEvent')
if(@($Suites|Sort-Object -Unique).Count -ne $Suites.Count -or @($Suites|Where-Object{$_ -notin $allSuites}).Count){throw 'Requested suites must be unique known managed regression suites.'}
$selectedSuites=if($Suites.Count){@($allSuites|Where-Object{$_ -in $Suites})}else{$allSuites}
foreach($path in @($SourceSnapshotManifest,$OutputDirectory)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute paths required.'}}
$output=[IO.Path]::GetFullPath($OutputDirectory);if(Test-Path -LiteralPath $output){throw 'Preserve earlier regression results; use a fresh directory.'}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$proofPath=[IO.Path]::GetFullPath($SourceSnapshotManifest);$proof=Get-Content -LiteralPath $proofPath -Raw|ConvertFrom-Json
if($proof.passed -ne $true -or $proof.kind -ne 'active-retirement-snapshot-checks'){throw 'An actual successful source snapshot regression is required.'}
$snapshotRoot=Join-Path (Split-Path -Parent $proofPath) 'source-snapshot';$source=Join-Path $output 'source'
$nativeRoot='C:/Users/71957/riz-build-cache/engine-production-audio-checks-r2-20261026'
$native=@('RizToolbox.InitializationAbiFixture.dll','RizToolbox.JudgeContext.dll','dobby.dll'|ForEach-Object{Join-Path $nativeRoot $_})
$profile='C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs'
$environmentNames=@('RIZ_INITIALIZATION_ABI_FIXTURE','RIZ_INITIALIZATION_CONTEXT_HELPER','RIZ_INITIALIZATION_ABI_DOBBY')
if('Gameplay' -in $selectedSuites){
    if(-not $PlaybackOwnedFixturePath -or -not [IO.Path]::IsPathFullyQualified($PlaybackOwnedFixturePath) -or
        -not (Test-Path -LiteralPath $PlaybackOwnedFixturePath -PathType Leaf)){throw 'Gameplay checks require the actual isolated native owned-call fixture path.'}
    $native+=,[IO.Path]::GetFullPath($PlaybackOwnedFixturePath)
    $environmentNames+=,'RIZ_PLAYBACK_OWNED_FIXTURE'
}
$oldEnvironment=@{};$oldLocation=Get-Location;$locks=[Collections.Generic.List[IDisposable]]::new()
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{schemaVersion=1;kind='foundation-managed-snapshot-checks';passed=$false;sourceProof=[ordered]@{path=$proofPath;sha256=Hash $proofPath};inputs=@();compiledSources=@();projects=@();failure=$null
    requestedSuites=$selectedSuites;fullManagedMatrix=($selectedSuites.Count -eq $allSuites.Count)
    distributionSuiteRun=$false;gameExecuted=$false;installed=$false;productionAdmissionVerified=$false}
try{
    Set-Location -LiteralPath $repo
    $extra=@(& rg --files --hidden --no-ignore templates examples -g '!**/obj/**' -g '!**/bin/**')
    if($LASTEXITCODE){throw 'Current template and example inventory failed.'}
    $extra+= 'RizToolbox.sln'
    $extras=@($extra|ForEach-Object{Join-Path $repo $_})+@($PSCommandPath,$profile,$proofPath)+$native
    foreach($path in @($proof.snapshotFiles.path)+$extras|Sort-Object -Unique){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    foreach($row in $proof.snapshotFiles){if((Hash $row.path) -ne $row.sha256){throw "Frozen source changed: $($row.path)"}
        $relative=[IO.Path]::GetRelativePath($snapshotRoot,$row.path);$destination=Join-Path $source $relative
        $null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force;Copy-Item -LiteralPath $row.path -Destination $destination
        if((Hash $destination) -ne $row.sha256){throw 'Exact compiled source snapshot copy failed.'}
    }
    $result.inputs=@($extras|Sort-Object -Unique|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
    foreach($path in @($extra|ForEach-Object{Join-Path $repo $_})){
        $destination=Join-Path $source ([IO.Path]::GetRelativePath($repo,$path));$null=New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force
        if(Test-Path -LiteralPath $destination){if((Hash $destination) -ne (Hash $path)){throw 'Additional fixture would replace a different frozen source.'}}
        else{Copy-Item -LiteralPath $path -Destination $destination}
    }
    $result.compiledSources=@(Get-ChildItem -LiteralPath $source -File -Recurse|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}})
    WriteJson $result.compiledSources (Join-Path $output 'source-before.json')
    foreach($name in $environmentNames){$oldEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
    for($i=0;$i -lt $environmentNames.Count;++$i){[Environment]::SetEnvironmentVariable($environmentNames[$i],$native[$i],'Process')}
    Set-Location -LiteralPath $source
    $projects=$selectedSuites
    foreach($name in $projects){
        $row=[ordered]@{name=$name;buildExitCode=$null;testExitCode=$null;passed=$false;checks=$null;summary=$null}
        $buildLog=Join-Path $output ($name+'-build.log');$testLog=Join-Path $output ($name+'-tests.log')
        try{
            $project=Join-Path $source ('tests/RizToolbox.'+$name+'.Tests/RizToolbox.'+$name+'.Tests.csproj')
            & (Get-RizDotnetPath) build $project -c Release -m:1 -p:RestoreIgnoreFailedSources=true -p:UseSharedCompilation=false "-p:RizEngineProductionProfileSource=$profile" --nologo -v:minimal 2>&1|Out-File -LiteralPath $buildLog
            $row.buildExitCode=$LASTEXITCODE;if($row.buildExitCode -ne 0){Get-Content -LiteralPath $buildLog -Tail 7;throw "Actual $name build failed."}
            $assemblyName=if($name -eq 'GameplaySourceGeneration'){'Gameplay'}else{$name}
            $assembly=Join-Path (Split-Path -Parent $project) ('bin/Release/net8.0/RizToolbox.'+$assemblyName+'.Tests.dll')
            & (Get-RizDotnetPath) $assembly 2>&1|Out-File -LiteralPath $testLog
            $row.testExitCode=$LASTEXITCODE;$text=[IO.File]::ReadAllText($testLog);$lines=@($text -split '\r?\n'|Where-Object{$_})
            $row.summary=if($lines.Count){$lines[-1]}else{''};Write-Output "$name`: $($row.summary)"
            if($row.testExitCode -ne 0){Get-Content -LiteralPath $testLog -Tail 7;throw "Actual $name regression failed."}
            if($name -eq 'GameplaySourceGeneration'){
                $actual=$row.summary|ConvertFrom-Json
                if($actual.kind -ne 'gameplay-source-generation-checks' -or $actual.passed -ne $true -or $actual.checks -le 0){throw 'No actual passing source-generation JSON summary.'}
                $row.checks=[int]$actual.checks;$row.passed=$true;continue
            }
            $match=[regex]::Match($text,'(?m)(\d+)/(\d+)[^\r\n]*passed\.?\s*$')
            if(-not $match.Success -or $match.Groups[1].Value -ne $match.Groups[2].Value -or [int]$match.Groups[1].Value -le 0){throw "No exact actual passing $name summary found."}
            $row.checks=[int]$match.Groups[1].Value;$row.passed=$true
        }finally{$result.projects+=,$row;WriteJson $result (Join-Path $output 'progress.json')}
    }
    foreach($row in @($result.inputs)+@($result.compiledSources)){if((Hash $row.path) -ne $row.sha256){throw "An actual compiled input changed: $($row.path)"}}
    $result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{Set-Location -LiteralPath $oldLocation;foreach($name in $oldEnvironment.Keys){[Environment]::SetEnvironmentVariable($name,$oldEnvironment[$name],'Process')};foreach($lock in $locks){$lock.Dispose()};WriteJson $result (Join-Path $output 'test-result.json')}
Write-Output "$($selectedSuites.Count) selected managed suites passed from the declared source snapshot; full matrix=$($result.fullManagedMatrix). SDK template distribution remains a separate actual proof."
