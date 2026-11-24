#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))}
foreach($path in @($CandidateDirectory,$OutputDirectory)){if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute paths required.'}}
$candidate=[IO.Path]::GetFullPath($CandidateDirectory);$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent result directory.'}
$buildPath=Join-Path $candidate 'owned-candidate-build.json';$build=Get-Content -LiteralPath $buildPath -Raw|ConvertFrom-Json
if($build.passed -ne $true -or $build.verifyBenchmark -ne $true -or $build.managedHost -ne $false){throw 'A successful exact owned verification benchmark candidate is required.'}
$null=New-Item -ItemType Directory -Path $output;$locks=[Collections.Generic.List[IDisposable]]::new();$child=[Diagnostics.Process]::new()
$result=[ordered]@{schemaVersion=1;kind='engine-transport-owned-verify-benchmark-checks';passed=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false
    inputs=@();inputHashesUnchanged=$false;child=$null;failure=$null;gameExecuted=$false;installed=$false;productionAdmissionVerified=$false}
try{
    $paths=@(@($build.files|ForEach-Object{$_.path})+@($buildPath,$PSCommandPath)|Sort-Object -Unique)
    foreach($path in $paths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    foreach($row in $build.files){if((Hash $row.path) -ne $row.sha256){throw "Benchmark candidate changed: $($row.path)"}}
    $result.inputs=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_}})
    $jsonPath=Join-Path $output 'child-result.json';$rawPath=Join-Path $output 'publication.raw';$finalRaw=$rawPath+'.final'
    $child.StartInfo=[Diagnostics.ProcessStartInfo]::new((Join-Path $candidate 'RizTransportOwnedHost.exe'))
    $child.StartInfo.WorkingDirectory=$candidate;$child.StartInfo.UseShellExecute=$false;$child.StartInfo.CreateNoWindow=$true;$child.StartInfo.ErrorDialog=$false
    $child.StartInfo.RedirectStandardOutput=$true;$child.StartInfo.RedirectStandardError=$true
    $child.StartInfo.ArgumentList.Add($jsonPath);$child.StartInfo.ArgumentList.Add($rawPath)
    if(-not $child.Start()){throw 'Owned benchmark did not start.'};$result.pid=$child.Id
    $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync();$elapsed=[Diagnostics.Stopwatch]::StartNew()
    while(-not $child.WaitForExit(5000)){Write-Output "Owned full-Verify benchmark running: PID=$($result.pid), elapsed=$([int]$elapsed.Elapsed.TotalSeconds)s"
        if($elapsed.Elapsed.TotalSeconds -ge 60){$result.timedOut=$true;$child.Kill();$result.killed=$true;$child.WaitForExit();break}}
    $result.exitCode=$child.ExitCode
    [IO.File]::WriteAllText((Join-Path $output 'stdout.log'),$stdout.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output 'stderr.log'),$stderr.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    if(Test-Path -LiteralPath $jsonPath){$result.child=Get-Content -LiteralPath $jsonPath -Raw|ConvertFrom-Json}
    $value=$result.child
    if($result.timedOut -or $result.exitCode -ne 0 -or -not $value -or $value.passed -ne $true -or $value.pid -ne $result.pid -or
        $value.kind -ne 'engine-transport-verify-benchmark' -or $value.samplesCompleted -ne 5 -or @($value.samples).Count -ne 5 -or
        $value.verifyChecksUnchanged -ne $true -or $value.clrHosted -ne $false -or $value.gameExecuted -ne $false -or
        $value.originalGameCodeExecuted -ne $false -or $value.productionAdmissionVerified -ne $false -or $value.immutable -ne $true -or
        $value.lastErrorPreserved -ne $true -or $value.allVerified -ne $true -or (Get-Item -LiteralPath $rawPath).Length -ne 91376 -or
        (Get-Item -LiteralPath $finalRaw).Length -ne 91376 -or (Hash $rawPath) -ne (Hash $finalRaw)){throw 'Actual full verification timing or native invariants failed.'}
    foreach($row in $result.inputs){if((Hash $row.path) -ne $row.sha256){throw "Benchmark input changed: $($row.path)"}}
    $result.inputHashesUnchanged=$true;$result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};$child.Dispose();$result.artifacts=@(Get-ChildItem -LiteralPath $output -File|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}});WriteJson $result (Join-Path $output 'test-result.json')}
Write-Output "Owned full native Verify: mean=$($result.child.verifyMeanMicroseconds)us, min=$($result.child.verifyMinimumMicroseconds)us, max=$($result.child.verifyMaximumMicroseconds)us. All five actual checks passed."
