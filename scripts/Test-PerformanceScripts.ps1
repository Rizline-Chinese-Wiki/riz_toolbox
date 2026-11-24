#requires -Version 7.2
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Measure-Foundation.ps1" -FunctionsOnly
$testRoot=Join-Path ([IO.Path]::GetTempPath()) ('RizToolbox.PerformanceScripts-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$passed=0
function Assert-Performance($Value,[string]$Message){if(-not $Value){throw $Message}}
function Assert-PerformanceThrows([scriptblock]$Action,[string]$Contains){
    try{& $Action | Out-Null}catch{if(-not $Contains -or $_.Exception.Message.Contains($Contains)){return};throw}
    throw "Expected rejection containing '$Contains'."
}
function New-TestStatus([long]$Revision,[DateTimeOffset]$Captured,[string]$Session='perf-fixture'){
    return @{revision=$Revision;diagnostics=@{SessionId='diagnostic-fixture'};performance=@{SessionId=$Session;CapturedAtUtc=$Captured.ToString('o');DroppedSeriesSamples=0;RetainedSamplesPerSeries=2048;MaximumSeries=256;ConsecutiveLimit=3;CallbackBudgetMilliseconds=$null;Callbacks=@(@{Owner='probe.a';Operation='FrameUpdate';TotalSamples=100+$Revision;WindowSamples=16;P50Milliseconds=1;P95Milliseconds=2;P99Milliseconds=3;MaximumMilliseconds=4;TotalAllocatedBytes=1000+$Revision;MaximumAllocatedBytes=100;OverBudget=0})};
        processResources=@{processId=123;managedBytes=1000;totalAllocatedBytes=2000+$Revision;workingSetBytes=3000;privateBytes=4000;handleCount=10;gen0Collections=1;gen1Collections=0;gen2Collections=0};plugins=@(@{Id='probe.a';State=2});pluginResources=@(@{PluginId='probe.a';Scopes=1;RetainedOwnedResources=3;CachedServiceViews=1})}
}
function Run-PerformanceTest([string]$Name,[scriptblock]$Action){& $Action;$script:passed++;Write-Host "PASS $Name"}
try{
    Run-PerformanceTest 'Nearest-rank process percentiles and OLS trend use raw points' {
        $stats=Get-FoundationSeriesMetrics (0..99) (1..100)
        Assert-Performance ($stats.p50 -eq 50 -and $stats.p95 -eq 95 -and $stats.p99 -eq 99 -and [Math]::Abs($stats.slopePerSecond-1) -lt 0.00001) 'Incorrect quantiles/trend.'
        Assert-PerformanceThrows {Get-FoundationSeriesMetrics @() @()} 'At least two'
        Assert-PerformanceThrows {Get-FoundationSeriesMetrics @(0,0) @(1,2)} 'strictly increase'
    }
    $now=[DateTimeOffset]::UtcNow;$processBegan=$now.AddMinutes(-1)
    $status=New-TestStatus 1 $now
    $first=Get-FoundationHostSample $status 123 $processBegan $now 10 $null @('probe.a')
    Run-PerformanceTest 'Only fresh revisions from fixed performance/diagnostic sessions are accepted' {
        Assert-Performance ($null -eq (Get-FoundationHostSample $status 123 $processBegan $now 10 $first @('probe.a'))) 'Duplicate was counted.'
        Assert-PerformanceThrows {Get-FoundationHostSample (New-TestStatus 2 $now.AddSeconds(1) 'different') 123 $processBegan $now.AddSeconds(1) 10 $first @('probe.a')} 'Mixed host sessions'
        Assert-PerformanceThrows {Get-FoundationHostSample (New-TestStatus 1 $now.AddSeconds(-20)) 123 $processBegan $now 10 $null @('probe.a')} 'Stale'
        Assert-PerformanceThrows {Get-FoundationHostSample $status 999 $processBegan $now 10 $null @('probe.a')} 'another process'
        Assert-PerformanceThrows {Get-FoundationHostSample $status 123 $processBegan $now 10 $null @('probe.b')} 'Enabled plugins'
    }
    $livePath=Join-Path $testRoot 'own-process.json'
    Run-PerformanceTest 'GameOnly collector samples the existing test shell without host status or launching a process' {
        & "$PSScriptRoot/Measure-Foundation.ps1" -Condition GameOnly -GameProcessId $PID -OutputPath $livePath -Scenario 'fixture-selftest' -Workload 'Existing PowerShell test process; not game evidence' -ResolutionWidth 1 -ResolutionHeight 1 -DurationSeconds .35 -WarmupSeconds 0 -IntervalMilliseconds 100 | Out-Null
        $actual=Read-FoundationJson $livePath
        Assert-Performance (@($actual.processSamples).Count -ge 2 -and @($actual.hostSamples).Count -eq 0) 'Collector did not produce valid process-only samples.'
    }
    $baseline=Read-FoundationJson $livePath;$baseline.evidenceKind='fixture';$baseline.measurementId=[Guid]::NewGuid().ToString('N');$baseline.condition='StressPlugin';$baseline.expectedPluginIds=@('probe.a')
    $baseline.elapsedSeconds=60;$baseline.requestedDurationSeconds=60;$baseline.startedAtUtc=$now.ToString('o');$baseline.endedAtUtc=$now.AddSeconds(60).ToString('o');$baseline.processIdentity.startedAtUtc=$processBegan.ToString('o')
    $baseline.processSamples=@(@{elapsedSeconds=0;cpuTotalMilliseconds=10;workingSetBytes=100;privateBytes=200;handleCount=10;threadCount=2},@{elapsedSeconds=60;cpuTotalMilliseconds=12;workingSetBytes=120;privateBytes=220;handleCount=11;threadCount=2})
    $second=Get-FoundationHostSample (New-TestStatus 2 $now.AddSeconds(60)) 123 $processBegan $now.AddSeconds(60) 10 $first @('probe.a')
    $first.callbacks[0].P99Milliseconds=30;$second.callbacks[0].P99Milliseconds=50;$first.callbacks[0].MaximumMilliseconds=60;$second.callbacks[0].MaximumMilliseconds=60
    $baseline.hostSamples=@($first,$second)
    $basePath=Join-Path $testRoot 'baseline.json';Write-FoundationJson $basePath $baseline
    $candidate=Read-FoundationJson $basePath;$candidate.measurementId=[Guid]::NewGuid().ToString('N');$candidate.processSamples[1].privateBytes=320
    $candidatePath=Join-Path $testRoot 'candidate.json';Write-FoundationJson $candidatePath $candidate
    Run-PerformanceTest 'Callback summaries retain the worst window p99 and never average windows' {
        $metrics=Get-FoundationMetrics $baseline
        Assert-Performance ($metrics['callback/probe.a/FrameUpdate/maxWindowP99Milliseconds'] -eq 50) 'Window percentiles were combined incorrectly.'
        $empty=Read-FoundationJson $basePath;$empty.hostSamples=@()
        Assert-PerformanceThrows {Get-FoundationMetrics $empty} 'Zero/one fresh host'
        $empty=Read-FoundationJson $basePath;$empty.hostSamples[1].performanceSessionId='mixed'
        Assert-PerformanceThrows {Get-FoundationMetrics $empty} 'Mixed/duplicate/backward'
    }
    Run-PerformanceTest 'Default comparison only observes and recomputes values from curves' {
        $candidate.metrics=@{'process/privateBytes/maximum'=1}
        $candidateTampered=Join-Path $testRoot 'candidate-summary.json';Write-FoundationJson $candidateTampered $candidate
        $result=& "$PSScriptRoot/Compare-FoundationPerformance.ps1" -BaselinePath $basePath -CandidatePath $candidateTampered | ConvertFrom-Json -AsHashtable
        $difference=@($result.differences | Where-Object {$_.metricId -eq 'process/privateBytes/maximum'})[0]
        Assert-Performance ($result.verdict -eq 'Observed' -and -not $result.releaseEligible -and $difference.candidate -eq 320) 'Comparison trusted the supplied summary or declared a release pass.'
    }
    $thresholdPath=Join-Path $testRoot 'thresholds.json';$threshold=@{schemaVersion=1;metricDefinitionVersion='foundation-observation/1';thresholdVersion='0.0.0';calibration=@{state='uncalibrated';notes='Self-test only';sources=@()};rules=@(@{baselineCondition='StressPlugin';candidateCondition='StressPlugin';metricId='process/privateBytes/maximum';maximumIncrease=100})};Write-FoundationJson $thresholdPath $threshold
    Run-PerformanceTest 'Uncalibrated examples and fixtures cannot pass a requested release gate' {
        $report=Join-Path $testRoot 'not-eligible.json'
        Assert-PerformanceThrows {& "$PSScriptRoot/Compare-FoundationPerformance.ps1" -BaselinePath $basePath -CandidatePath $candidatePath -ThresholdPath $thresholdPath -OutputPath $report -ReleaseGate} 'NotEligible'
        Assert-Performance ((Read-FoundationJson $report).verdict -eq 'NotEligible') 'Missing refusal artifact.'
    }
    Run-PerformanceTest 'Fixture sources cannot masquerade as calibrated live evidence' {
        $threshold.calibration=@{state='calibrated';notes='Synthetic test must be rejected';calibratedAtUtc=$now.ToString('o');minimumDurationSeconds=10;sources=@(1..4 | ForEach-Object {@{path='baseline.json';sha256=(Get-FileHash -LiteralPath $basePath -Algorithm SHA256).Hash.ToLowerInvariant()}})}
        $calibratedPath=Join-Path $testRoot 'calibrated.json';Write-FoundationJson $calibratedPath $threshold
        Assert-PerformanceThrows {& "$PSScriptRoot/Compare-FoundationPerformance.ps1" -BaselinePath $basePath -CandidatePath $candidatePath -ThresholdPath $calibratedPath} 'Fixture evidence cannot calibrate'
    }
    Run-PerformanceTest 'Mixed scenario evidence and missing threshold metrics are refused' {
        $altered=Read-FoundationJson $candidatePath;$altered.scenario='another-scene';$alteredPath=Join-Path $testRoot 'another-scene.json';Write-FoundationJson $alteredPath $altered
        Assert-PerformanceThrows {& "$PSScriptRoot/Compare-FoundationPerformance.ps1" -BaselinePath $basePath -CandidatePath $alteredPath} 'different metric definitions'
        $threshold.calibration.state='uncalibrated';$threshold.rules[0].metricId='missing';$missingPath=Join-Path $testRoot 'missing-rule.json';Write-FoundationJson $missingPath $threshold
        Assert-PerformanceThrows {& "$PSScriptRoot/Compare-FoundationPerformance.ps1" -BaselinePath $basePath -CandidatePath $candidatePath -ThresholdPath $missingPath} 'Threshold metric is absent'
    }
    Run-PerformanceTest 'Versioned example schema is valid and stale imported evidence is refused' {
        Assert-Performance (Get-Content -LiteralPath (Join-Path $PSScriptRoot '../docs/performance-thresholds.example.json') -Raw | Test-Json -SchemaFile (Join-Path $PSScriptRoot '../docs/performance-thresholds.schema.json')) 'Example does not match schema.'
        $stale=Read-FoundationJson $candidatePath;$stale.hostSamples[0].observedAtUtc=$now.AddMinutes(-1).ToString('o');$stalePath=Join-Path $testRoot 'stale-evidence.json';Write-FoundationJson $stalePath $stale
        Assert-PerformanceThrows {& "$PSScriptRoot/Compare-FoundationPerformance.ps1" -BaselinePath $basePath -CandidatePath $stalePath} 'Stale host evidence'
    }
    Run-PerformanceTest 'Empty framework observations support zero callbacks and zero owned resources' {
        $emptyFramework=Read-FoundationJson $basePath;$emptyFramework.condition='EmptyFramework';$emptyFramework.expectedPluginIds=@()
        foreach($sample in $emptyFramework.hostSamples){$sample.enabledPluginIds=@();$sample.callbacks=@();$sample.pluginResources=@()}
        $metrics=Get-FoundationMetrics $emptyFramework
        Assert-Performance ($metrics['plugins/scopes/maximum'] -eq 0 -and $metrics['plugins/retainedOwnedResources/delta'] -eq 0) 'Empty framework produced nonzero/missing counters.'
    }
    Write-Host "$passed/10 performance script checks passed. Fixtures do not certify real game performance or 20 scene cycles."
}finally{
    $resolved=[IO.Path]::GetFullPath($testRoot);$temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or -not [IO.Path]::GetFileName($resolved).StartsWith('RizToolbox.PerformanceScripts-')){throw 'Unexpected self-test cleanup target.'}
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
