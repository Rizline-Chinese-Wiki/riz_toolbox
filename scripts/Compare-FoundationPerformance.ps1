#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BaselinePath,
    [Parameter(Mandatory)][string]$CandidatePath,
    [string]$ThresholdPath,
    [string]$OutputPath,
    [switch]$ReleaseGate
)
$ErrorActionPreference='Stop'
. "$PSScriptRoot/Measure-Foundation.ps1" -FunctionsOnly -OutputPath $OutputPath

function Read-FoundationMeasurement([string]$Path) {
    $m=Read-FoundationJson $Path
    if($m.condition -notin @('GameOnly','EmptyFramework','TwoProbes','StressPlugin') -or $m.evidenceKind -notin @('live-observation','fixture') -or [string]$m.measurementId -notmatch '^[a-f0-9]{32}$' -or -not $m.scenario -or -not $m.workload){throw 'Invalid measurement identity/condition.'}
    if($m.machineFingerprint -cne (Get-FoundationHash ($m.machine | ConvertTo-Json -Compress))){throw 'Machine metadata fingerprint does not match.'}
    if((Get-FoundationNumber $m.elapsedSeconds 'elapsedSeconds') -le 0 -or [DateTimeOffset]$m.endedAtUtc -le [DateTimeOffset]$m.startedAtUtc){throw 'Invalid measurement duration.'}
    if([Math]::Abs(([DateTimeOffset]$m.endedAtUtc-[DateTimeOffset]$m.startedAtUtc).TotalSeconds-$m.elapsedSeconds) -gt 2){throw 'Wall-clock duration differs from recorded monotonic duration.'}
    if((Get-FoundationNumber $m.settings.resolutionWidth 'resolutionWidth') -lt 1 -or (Get-FoundationNumber $m.settings.resolutionHeight 'resolutionHeight') -lt 1){throw 'Missing actual resolution.'}
    foreach($sample in $m.processSamples){if($sample.elapsedSeconds -gt $m.elapsedSeconds+1){throw 'Process sample lies outside measurement duration.'}}
    if(@($m.processSamples).Count -gt 0 -and $m.processSamples[-1].elapsedSeconds -lt $m.elapsedSeconds-([double]$m.intervalMilliseconds/1000)*1.5-.25){throw 'Process curve does not span the recorded duration.'}
    $expectedCount=@($m.expectedPluginIds | Sort-Object -Unique).Count
    if(($m.condition -in @('GameOnly','EmptyFramework') -and $expectedCount -ne 0) -or ($m.condition -eq 'TwoProbes' -and $expectedCount -ne 2) -or ($m.condition -eq 'StressPlugin' -and $expectedCount -lt 1)){throw 'Plugin identities do not match the baseline condition.'}
    $statusAge=Get-FoundationNumber $m.maximumStatusAgeSeconds 'maximumStatusAgeSeconds'
    if($statusAge -lt 2 -or $statusAge -gt 300){throw 'Invalid status age budget.'}
    foreach($sample in $m.hostSamples){
        if(-not $sample.performanceSessionId -or -not $sample.diagnosticSessionId){throw 'Host session identity is missing.'}
        if([DateTimeOffset]$sample.capturedAtUtc -lt [DateTimeOffset]$m.processIdentity.startedAtUtc -or [DateTimeOffset]$sample.capturedAtUtc -gt [DateTimeOffset]$m.endedAtUtc){throw 'Host sample lies outside process/measurement lifetime.'}
        $age=([DateTimeOffset]$sample.observedAtUtc-[DateTimeOffset]$sample.capturedAtUtc).TotalSeconds
        if([DateTimeOffset]$sample.observedAtUtc -lt ([DateTimeOffset]$m.startedAtUtc).AddSeconds(-1) -or [DateTimeOffset]$sample.observedAtUtc -gt ([DateTimeOffset]$m.endedAtUtc).AddSeconds(1) -or $age -gt $statusAge -or $age -lt -2){throw 'Stale host evidence lies outside its observation window.'}
        if((@($sample.enabledPluginIds | Sort-Object) -join "`0") -cne (@($m.expectedPluginIds | Sort-Object) -join "`0")){throw 'Host sample plugin set differs from its condition metadata.'}
    }
    $m.metrics=Get-FoundationMetrics $m # Recompute from curves, never trust serialized metric summaries.
    return $m
}
function Assert-FoundationComparable($Left,$Right) {
    if($Left.metricDefinitionVersion -cne $Right.metricDefinitionVersion -or $Left.machineFingerprint -cne $Right.machineFingerprint -or $Left.scenario -cne $Right.scenario -or $Left.workload -cne $Right.workload -or $Left.intervalMilliseconds -ne $Right.intervalMilliseconds -or $Left.requestedDurationSeconds -ne $Right.requestedDurationSeconds -or $Left.warmupSeconds -ne $Right.warmupSeconds -or ($Left.settings | ConvertTo-Json -Compress) -cne ($Right.settings | ConvertTo-Json -Compress)){throw 'Measurements have different metric definitions, machine, scenario/workload, sampling schedule or display/FPS settings.'}
    if($Left.condition -ceq $Right.condition -and (@($Left.expectedPluginIds | Sort-Object) -join "`0") -cne (@($Right.expectedPluginIds | Sort-Object) -join "`0")){throw 'The same baseline condition uses different plugin identities.'}
    if(@($Left.hostSamples).Count -gt 0 -and @($Right.hostSamples).Count -gt 0 -and ($Left.hostSamples[0].monitorConfiguration | ConvertTo-Json -Compress) -cne ($Right.hostSamples[0].monitorConfiguration | ConvertTo-Json -Compress)){throw 'Callback window/budget configurations differ.'}
}
$baseline=Read-FoundationMeasurement $BaselinePath;$candidate=Read-FoundationMeasurement $CandidatePath
if($baseline.measurementId -ceq $candidate.measurementId){throw 'Baseline and candidate must be distinct measurements.'}
Assert-FoundationComparable $baseline $candidate
$differences=@(foreach($key in $candidate.metrics.Keys){if($baseline.metrics.Contains($key)){
    $before=[double]$baseline.metrics[$key];$after=[double]$candidate.metrics[$key]
    [ordered]@{metricId=$key;baseline=$before;candidate=$after;increase=$after-$before;increasePercent=$(if($before -gt 0){($after-$before)*100/$before}else{$null})}
}})
$checks=[Collections.Generic.List[object]]::new();$eligibility=[Collections.Generic.List[string]]::new();$thresholdVersion=$null;$calibrationSources=@()
if(-not $ThresholdPath){$eligibility.Add('No calibrated threshold file was supplied.')}
else{
    $threshold=Read-FoundationJson $ThresholdPath
    $schemaPath=Join-Path $PSScriptRoot '../docs/performance-thresholds.schema.json'
    if(-not (Get-Content -LiteralPath $ThresholdPath -Raw | Test-Json -SchemaFile $schemaPath -ErrorAction Stop)){throw 'Threshold file does not conform to the versioned schema.'}
    if($threshold.schemaVersion -ne 1 -or $threshold.metricDefinitionVersion -cne 'foundation-observation/1' -or [string]$threshold.thresholdVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$'){throw 'Unsupported threshold schema, metric definitions or threshold version.'}
    $thresholdVersion=$threshold.thresholdVersion
    if($threshold.calibration.state -notin @('uncalibrated','calibrated')){throw 'Unknown calibration state.'}
    if($threshold.calibration.state -ne 'calibrated'){$eligibility.Add('Thresholds are an uncalibrated example; no release pass is possible.')}
    else{
        if(-not $threshold.calibration.notes -or -not $threshold.calibration.calibratedAtUtc){throw 'Calibrated thresholds require dated calibration notes.'}
        [void][DateTimeOffset]::Parse($threshold.calibration.calibratedAtUtc)
        $minimumDuration=Get-FoundationNumber $threshold.calibration.minimumDurationSeconds 'minimumDurationSeconds'
        if($minimumDuration -lt 10){throw 'Calibration minimumDurationSeconds must be at least 10 seconds.'}
        $conditions=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $sourceIdentities=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $sources=[Collections.Generic.List[object]]::new()
        foreach($source in $threshold.calibration.sources){
            if(-not $source.path -or [string]$source.sha256 -notmatch '^[a-f0-9]{64}$'){throw 'Calibration source path/hash is missing.'}
            $sourcePath=[IO.Path]::GetFullPath([IO.Path]::Combine([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ThresholdPath)),$source.path))
            if((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $source.sha256){throw 'Calibration source hash mismatch.'}
            $evidence=Read-FoundationMeasurement $sourcePath;Assert-FoundationComparable $baseline $evidence
            if($evidence.evidenceKind -cne 'live-observation'){throw 'Fixture evidence cannot calibrate release thresholds.'}
            if(-not $sourceIdentities.Add($evidence.measurementId)){throw 'Duplicate calibration measurement identity.'}
            if($evidence.elapsedSeconds -lt $minimumDuration){throw 'Calibration source duration is too short.'}
            [void]$conditions.Add($evidence.condition);$sources.Add([ordered]@{measurementId=$evidence.measurementId;condition=$evidence.condition;path=$sourcePath;sha256=$source.sha256})
        }
        foreach($conditionName in @('GameOnly','EmptyFramework','TwoProbes','StressPlugin')){if(-not $conditions.Contains($conditionName)){throw "Calibration lacks the $conditionName baseline."}}
        if($baseline.elapsedSeconds -lt $minimumDuration -or $candidate.elapsedSeconds -lt $minimumDuration){$eligibility.Add('Compared measurements are shorter than the calibrated minimum duration.')}
        $calibrationSources=$sources.ToArray()
    }
    $rules=@($threshold.rules | Where-Object {$_.baselineCondition -ceq $baseline.condition -and $_.candidateCondition -ceq $candidate.condition})
    if($rules.Count -eq 0){$eligibility.Add('No threshold rules cover this condition pair.')}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($rule in $rules){
        if(-not $rule.metricId -or -not $seen.Add($rule.metricId)){throw 'Threshold rules require distinct metric identities per condition pair.'}
        if(-not $baseline.metrics.Contains($rule.metricId) -or -not $candidate.metrics.Contains($rule.metricId)){throw "Threshold metric is absent: $($rule.metricId)"}
        $before=[double]$baseline.metrics[$rule.metricId];$after=[double]$candidate.metrics[$rule.metricId];$violations=[Collections.Generic.List[string]]::new();$bounds=0
        foreach($bound in @('maximumAbsoluteValue','maximumIncrease','maximumIncreasePercent')){
            if($rule.Contains($bound) -and $null -ne $rule[$bound]){
                $limit=Get-FoundationNumber $rule[$bound] $bound -Signed;$bounds++
                if($bound -eq 'maximumAbsoluteValue' -and $after -gt $limit){$violations.Add($bound)}
                if($bound -eq 'maximumIncrease' -and $after-$before -gt $limit){$violations.Add($bound)}
                if($bound -eq 'maximumIncreasePercent'){if($before -le 0){throw 'Percentage thresholds require a positive baseline; use an absolute bound for zero/negative baselines.'};if(($after-$before)*100/$before -gt $limit){$violations.Add($bound)}}
            }
        }
        if($bounds -eq 0){throw 'Each threshold rule requires at least one numeric bound.'}
        $checks.Add([ordered]@{metricId=$rule.metricId;baseline=$before;candidate=$after;passed=$violations.Count -eq 0;violatedBounds=$violations.ToArray()})
    }
}
if($baseline.evidenceKind -cne 'live-observation' -or $candidate.evidenceKind -cne 'live-observation'){$eligibility.Add('Fixture comparisons cannot pass a release gate.')}
$failed=@($checks | Where-Object {-not $_.passed}).Count
$eligible=$eligibility.Count -eq 0 -and $checks.Count -gt 0
$verdict=if($ReleaseGate){if(-not $eligible){'NotEligible'}elseif($failed -gt 0){'Fail'}else{'Pass'}}else{'Observed'}
$report=[ordered]@{schemaVersion=1;metricDefinitionVersion='foundation-observation/1';thresholdVersion=$thresholdVersion;comparedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');baselineMeasurementId=$baseline.measurementId;candidateMeasurementId=$candidate.measurementId;
    baselineCondition=$baseline.condition;candidateCondition=$candidate.condition;releaseGateRequested=[bool]$ReleaseGate;releaseEligible=$eligible;verdict=$verdict;ineligibleReasons=$eligibility.ToArray();calibrationSources=$calibrationSources;
    thresholdChecks=$checks.ToArray();differences=$differences;limitations=@('No whole-frame/GPU percentiles are available.','Callback maxWindowP99 is a worst observed window statistic, not a pooled p99.','Resource slopes describe sample time; they do not certify 20 real lifecycle/scene cycles.')}
if($OutputPath){Write-FoundationJson $OutputPath $report}
$report | ConvertTo-Json -Depth 16
if($ReleaseGate -and ($verdict -ne 'Pass')){throw "Release performance gate $verdict; inspect the JSON report."}
