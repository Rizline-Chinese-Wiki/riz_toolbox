#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[ValidateRange(1,60)][int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute paths required.'};[IO.Path]::GetFullPath($Path)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))}
$candidate=Absolute $CandidateDirectory;$output=Absolute $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent result directory.'}
$buildPath=Join-Path $candidate 'owned-candidate-build.json';$build=Get-Content -LiteralPath $buildPath -Raw|ConvertFrom-Json
if($build.passed -ne $true -or $build.kind -ne 'engine-transport-owned-candidate-build'){throw 'A successful exact owned candidate build is required.'}
$inputPaths=@(@($build.files|ForEach-Object{$_.path})+@($buildPath,$PSCommandPath)|Sort-Object -Unique)
$locks=[Collections.Generic.List[IDisposable]]::new();$child=[Diagnostics.Process]::new()
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-owned-cold-checks';passed=$false;started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false
    inputs=@();inputHashesUnchanged=$false;child=$null;failure=$null;gameExecuted=$false;installed=$false;productionAdmissionVerified=$false;fullFamilyAdoptionVerified=$false}
$null=New-Item -ItemType Directory -Path $output
try{
    foreach($path in $inputPaths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    foreach($row in $build.files){if((Hash $row.path) -ne $row.sha256){throw "Candidate changed: $($row.path)"}}
    $manifest.inputs=@($inputPaths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_}});WriteJson $manifest.inputs (Join-Path $output 'input-before.json')
    $child.StartInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $candidate 'RizTransportOwnedHost.exe'))
    $child.StartInfo.WorkingDirectory=$candidate;$child.StartInfo.UseShellExecute=$false;$child.StartInfo.CreateNoWindow=$true;$child.StartInfo.ErrorDialog=$false
    $child.StartInfo.RedirectStandardOutput=$true;$child.StartInfo.RedirectStandardError=$true
    $jsonPath=Join-Path $output 'child-result.json';$rawPath=Join-Path $output 'publication.raw'
    $child.StartInfo.ArgumentList.Add($jsonPath);$child.StartInfo.ArgumentList.Add($rawPath)
    if(-not $child.Start()){throw 'Owned cold process did not start.'};$manifest.started=$true;$manifest.pid=$child.Id
    $outTask=$child.StandardOutput.ReadToEndAsync();$errTask=$child.StandardError.ReadToEndAsync()
    if(-not $child.WaitForExit($TimeoutSeconds*1000)){$manifest.timedOut=$true;$child.Kill();$manifest.killed=$true;$child.WaitForExit()}
    $manifest.exitCode=$child.ExitCode
    [IO.File]::WriteAllText((Join-Path $output 'stdout.log'),$outTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output 'stderr.log'),$errTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    if(Test-Path -LiteralPath $jsonPath){$manifest.child=Get-Content -LiteralPath $jsonPath -Raw|ConvertFrom-Json}
    if($manifest.timedOut -or $manifest.exitCode -ne 0 -or -not $manifest.child){throw "Owned cold process rejected the candidate (exit $($manifest.exitCode))."}
    $value=$manifest.child
    if($value.pid -ne $manifest.pid -or $value.passed -ne $true -or $value.kind -ne 'engine-transport-cold-owned' -or
        $value.immutable -ne $true -or $value.lastErrorPreserved -ne $true -or $value.rawPersisted -ne $true -or
        $value.gameExecuted -ne $false -or $value.originalGameCodeExecuted -ne $false -or $value.productionAdmissionVerified -ne $false -or
        (Get-Item -LiteralPath $rawPath).Length -ne 91376){throw 'Actual owned cold receipt has the wrong scope or shape.'}
    foreach($row in $manifest.inputs){if((Hash $row.path) -ne $row.sha256){throw "Run input changed: $($row.path)"}}
    $manifest.inputHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};$child.Dispose();$manifest.artifacts=@(Get-ChildItem -LiteralPath $output -File|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}});WriteJson $manifest (Join-Path $output 'test-result.json')}
Write-Output "Actual owned V5 cold process passed: PID=$($manifest.pid); $output"
