#requires -Version 7.2
[CmdletBinding()]
param(
    [ValidateSet('GameOnly','EmptyFramework','TwoProbes','StressPlugin')][string]$Condition,
    [int]$GameProcessId,
    [string]$StatusPath,
    [string]$OutputPath,
    [string]$Scenario,
    [string]$Workload,
    [int]$ResolutionWidth,
    [int]$ResolutionHeight,
    [ValidateRange(0,1000)][int]$FrameRateLimit = 0,
    [ValidateRange(0,4)][int]$VSyncCount = 0,
    [string[]]$ExpectedPluginIds = @(),
    [ValidateRange(0.2,86400)][double]$DurationSeconds = 60,
    [ValidateRange(0,3600)][double]$WarmupSeconds = 15,
    [ValidateRange(100,60000)][int]$IntervalMilliseconds = 500,
    [ValidateRange(2,300)][int]$MaximumStatusAgeSeconds = 10,
    [switch]$FunctionsOnly
)
$ErrorActionPreference = 'Stop'

function Read-FoundationJson([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -lt 2 -or $file.Length -gt 67108864) { throw 'JSON evidence must be between 2 bytes and 64 MiB.' }
    return Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -AsHashtable
}
function Get-FoundationNumber($Value, [string]$Name, [switch]$Signed) {
    if ($null -eq $Value -or $Value -is [bool] -or $Value -is [string]) { throw "Missing or nonnumeric $Name." }
    $number = [double]$Value
    if (-not [double]::IsFinite($number) -or (-not $Signed -and $number -lt 0)) { throw "Invalid $Name." }
    return $number
}
function Get-FoundationHash([string]$Value) {
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))).ToLowerInvariant()
}
function Get-FoundationHostSample($Status, [int]$ExpectedProcessId, [DateTimeOffset]$ProcessStartedAt,
    [DateTimeOffset]$ObservedAt, [int]$MaximumAge, $Previous, [string[]]$ExpectedPlugins) {
    if (-not $Status.performance -or -not $Status.processResources -or -not $Status.diagnostics -or -not $Status.Contains('pluginResources')) { throw 'Host status lacks performance/processResources/diagnostics/pluginResources.' }
    $revision = Get-FoundationNumber $Status.revision 'status revision'
    if ($revision -lt 1 -or $revision -ne [Math]::Truncate($revision)) { throw 'Invalid status revision.' }
    $performanceSession = [string]$Status.performance.SessionId
    $diagnosticSession = [string]$Status.diagnostics.SessionId
    if ([string]::IsNullOrWhiteSpace($performanceSession) -or [string]::IsNullOrWhiteSpace($diagnosticSession)) { throw 'Host status session identity is missing.' }
    if ([int]$Status.processResources.processId -ne $ExpectedProcessId) { throw 'Host status belongs to another process.' }
    $captured = [DateTimeOffset]::Parse([string]$Status.performance.CapturedAtUtc)
    if ($captured -lt $ProcessStartedAt -or ($ObservedAt-$captured).TotalSeconds -gt $MaximumAge -or ($captured-$ObservedAt).TotalSeconds -gt 2) { throw 'Stale or future host status was refused.' }
    if ($Previous) {
        if ($Previous.performanceSessionId -cne $performanceSession -or $Previous.diagnosticSessionId -cne $diagnosticSession) { throw 'Mixed host sessions were refused.' }
        if ($revision -lt $Previous.revision) { throw 'Host status revision moved backwards.' }
        if ($revision -eq $Previous.revision) {
            if ($captured -ne [DateTimeOffset]$Previous.capturedAtUtc) { throw 'A duplicate revision changed its timestamp.' }
            return $null
        }
        if ($captured -le [DateTimeOffset]$Previous.capturedAtUtc) { throw 'Host capture timestamp did not advance.' }
    }
    $enabled = @($Status.plugins | Where-Object { $_.State -eq 2 -or $_.State -ceq 'Enabled' } | ForEach-Object { [string]$_.Id } | Sort-Object)
    $expected = @($ExpectedPlugins | Sort-Object -Unique)
    if (($enabled -join "`0") -cne ($expected -join "`0")) { throw 'Enabled plugins differ from the declared baseline condition.' }
    if (@($Status.plugins | Where-Object { $_.State -in @(1,3,4,5,'Enabling','Disabling','Faulted','Waiting') }).Count -gt 0) { throw 'Host plugins are transitioning, faulted or waiting.' }
    $resources = [ordered]@{}
    foreach ($name in @('managedBytes','totalAllocatedBytes','workingSetBytes','privateBytes','handleCount','gen0Collections','gen1Collections','gen2Collections')) {
        $resources[$name] = Get-FoundationNumber $Status.processResources[$name] $name
    }
    $plugins = @($Status.pluginResources | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_.PluginId)) { throw 'Missing plugin resource identity.' }
        [ordered]@{ pluginId = [string]$_.PluginId; scopes = Get-FoundationNumber $_.Scopes 'scopes'; retainedOwnedResources = Get-FoundationNumber $_.RetainedOwnedResources 'retainedOwnedResources'; cachedServiceViews = Get-FoundationNumber $_.CachedServiceViews 'cachedServiceViews' }
    })
    if(@($plugins.pluginId | Sort-Object -Unique).Count -ne $plugins.Count){throw 'Duplicate plugin resource identities.'}
    $monitor=[ordered]@{retainedSamplesPerSeries=Get-FoundationNumber $Status.performance.RetainedSamplesPerSeries 'retainedSamplesPerSeries';maximumSeries=Get-FoundationNumber $Status.performance.MaximumSeries 'maximumSeries';consecutiveLimit=Get-FoundationNumber $Status.performance.ConsecutiveLimit 'consecutiveLimit';callbackBudgetMilliseconds=$null}
    if($null -ne $Status.performance.CallbackBudgetMilliseconds){$monitor.callbackBudgetMilliseconds=Get-FoundationNumber $Status.performance.CallbackBudgetMilliseconds 'callbackBudgetMilliseconds'}
    if($monitor.retainedSamplesPerSeries -lt 16 -or $monitor.maximumSeries -lt 1 -or $monitor.consecutiveLimit -lt 1){throw 'Invalid callback monitor configuration.'}
    $callbacks = @($Status.performance.Callbacks | ForEach-Object {
        if ([string]::IsNullOrWhiteSpace($_.Owner) -or [string]::IsNullOrWhiteSpace($_.Operation)) { throw 'Missing callback series identity.' }
        $item = [ordered]@{ owner = [string]$_.Owner; operation = [string]$_.Operation }
        foreach ($name in @('TotalSamples','WindowSamples','P50Milliseconds','P95Milliseconds','P99Milliseconds','MaximumMilliseconds','TotalAllocatedBytes','MaximumAllocatedBytes','OverBudget')) { $item[$name] = Get-FoundationNumber $_[$name] $name }
        if ($item.WindowSamples -lt 1 -or $item.WindowSamples -gt $monitor.retainedSamplesPerSeries -or $item.TotalSamples -lt $item.WindowSamples -or $item.P50Milliseconds -gt $item.P95Milliseconds -or $item.P95Milliseconds -gt $item.P99Milliseconds) { throw 'Invalid callback window.' }
        $item
    })
    $callbackKeys=@($callbacks | ForEach-Object {[Uri]::EscapeDataString($_.owner)+'/'+[Uri]::EscapeDataString($_.operation)} | Sort-Object -Unique)
    if($callbackKeys.Count -ne $callbacks.Count){throw 'Duplicate callback series identities.'}
    return [ordered]@{ revision = [long]$revision; performanceSessionId = $performanceSession; diagnosticSessionId = $diagnosticSession;
        capturedAtUtc = $captured.ToString('o'); observedAtUtc = $ObservedAt.ToString('o'); processResources = $resources;
        enabledPluginIds = $enabled; pluginResources = $plugins; monitorConfiguration = $monitor; callbacks = $callbacks; droppedSeriesSamples = Get-FoundationNumber $Status.performance.DroppedSeriesSamples 'droppedSeriesSamples' }
}
function Get-FoundationSeriesMetrics([double[]]$Times, [double[]]$Values) {
    if ($Values.Count -lt 2 -or $Times.Count -ne $Values.Count) { throw 'At least two paired samples are required.' }
    $sorted = @($Values | Sort-Object); $meanX = ($Times | Measure-Object -Average).Average; $meanY = ($Values | Measure-Object -Average).Average
    $numerator = 0.0; $denominator = 0.0; $monotonic = $true; $increased = $false
    for ($i=0; $i -lt $Values.Count; $i++) {
        [void](Get-FoundationNumber $Values[$i] 'series value' -Signed); [void](Get-FoundationNumber $Times[$i] 'sample time')
        if ($i -gt 0) { if ($Times[$i] -le $Times[$i-1]) { throw 'Sample times must strictly increase.' }; if ($Values[$i] -lt $Values[$i-1]) { $monotonic=$false }; if ($Values[$i] -gt $Values[$i-1]) { $increased=$true } }
        $numerator += ($Times[$i]-$meanX)*($Values[$i]-$meanY); $denominator += [Math]::Pow($Times[$i]-$meanX,2)
    }
    return [ordered]@{ p50 = $sorted[[Math]::Max(0,[Math]::Ceiling($sorted.Count*.5)-1)]; p95 = $sorted[[Math]::Max(0,[Math]::Ceiling($sorted.Count*.95)-1)]; p99 = $sorted[[Math]::Max(0,[Math]::Ceiling($sorted.Count*.99)-1)];
        maximum = $sorted[-1]; delta = $Values[-1]-$Values[0]; slopePerSecond = $numerator/$denominator; monotonicGrowth = [int]($monotonic -and $increased) }
}
function Get-FoundationMetrics($Measurement) {
    if ($Measurement.schemaVersion -ne 1 -or $Measurement.metricDefinitionVersion -cne 'foundation-observation/1') { throw 'Unsupported measurement schema/metric definitions.' }
    $process = @($Measurement.processSamples); $hostSamples = @($Measurement.hostSamples)
    if ($process.Count -lt 2) { throw 'Zero/one process sample cannot establish a measurement.' }
    $metrics = [ordered]@{}
    foreach ($name in @('workingSetBytes','privateBytes','handleCount','threadCount')) {
        $stats = Get-FoundationSeriesMetrics @($process | ForEach-Object { Get-FoundationNumber $_.elapsedSeconds 'elapsedSeconds' }) @($process | ForEach-Object { Get-FoundationNumber $_[$name] $name })
        foreach ($key in $stats.Keys) { $metrics["process/$name/$key"]=$stats[$key] }
    }
    $cpuStart = Get-FoundationNumber $process[0].cpuTotalMilliseconds 'cpuTotalMilliseconds'; $cpuEnd = Get-FoundationNumber $process[-1].cpuTotalMilliseconds 'cpuTotalMilliseconds'
    if ($cpuEnd -lt $cpuStart) { throw 'CPU counter moved backwards.' }
    $metrics['process/cpuMilliseconds/delta'] = $cpuEnd-$cpuStart
    if ($Measurement.condition -eq 'GameOnly') { if ($hostSamples.Count -ne 0) { throw 'GameOnly evidence must not contain host status.' }; return $metrics }
    if ($hostSamples.Count -lt 2) { throw 'Zero/one fresh host status sample cannot establish a host measurement.' }
    $previous = $null
    foreach ($sample in $hostSamples) {
        if((Get-FoundationNumber $sample.revision 'host revision') -lt 1 -or -not $sample.monitorConfiguration){throw 'Host revision/monitor configuration is missing.'}
        if ($previous -and ($sample.performanceSessionId -cne $previous.performanceSessionId -or $sample.diagnosticSessionId -cne $previous.diagnosticSessionId -or $sample.revision -le $previous.revision -or [DateTimeOffset]$sample.capturedAtUtc -le [DateTimeOffset]$previous.capturedAtUtc)) { throw 'Mixed/duplicate/backward host evidence was refused.' }
        if($previous -and ($sample.monitorConfiguration | ConvertTo-Json -Compress) -cne ($previous.monitorConfiguration | ConvertTo-Json -Compress)){throw 'Callback monitor configuration changed within the measurement.'}
        $previous=$sample
    }
    $times = @($hostSamples | ForEach-Object { ([DateTimeOffset]$_.capturedAtUtc-[DateTimeOffset]$hostSamples[0].capturedAtUtc).TotalSeconds })
    foreach ($name in @('managedBytes','workingSetBytes','privateBytes','handleCount')) {
        $stats=Get-FoundationSeriesMetrics $times @($hostSamples | ForEach-Object { Get-FoundationNumber $_.processResources[$name] $name })
        foreach ($key in $stats.Keys) { $metrics["host/$name/$key"]=$stats[$key] }
    }
    foreach ($name in @('totalAllocatedBytes','gen0Collections','gen1Collections','gen2Collections')) {
        $values=@($hostSamples | ForEach-Object { Get-FoundationNumber $_.processResources[$name] $name })
        for($i=1;$i -lt $values.Count;$i++){ if($values[$i] -lt $values[$i-1]){throw "Host counter $name moved backwards."} }
        $metrics["host/$name/delta"]=$values[-1]-$values[0]
    }
    foreach ($name in @('scopes','retainedOwnedResources','cachedServiceViews')) {
        $values=@($hostSamples | ForEach-Object { @($_.pluginResources | ForEach-Object { Get-FoundationNumber $_[$name] $name } | Measure-Object -Sum).Sum })
        $stats=Get-FoundationSeriesMetrics $times $values
        foreach($key in $stats.Keys){$metrics["plugins/$name/$key"]=$stats[$key]}
    }
    $pluginIds=@($hostSamples | ForEach-Object {$_.pluginResources | ForEach-Object {$_.pluginId}} | Sort-Object -Unique)
    foreach($pluginId in $pluginIds){foreach($name in @('scopes','retainedOwnedResources','cachedServiceViews')){
        $values=@($hostSamples | ForEach-Object {$found=@($_.pluginResources | Where-Object {$_.pluginId -ceq $pluginId});if($found.Count -eq 1){Get-FoundationNumber $found[0][$name] $name}})
        if($values.Count -eq $hostSamples.Count){$stats=Get-FoundationSeriesMetrics $times $values;foreach($key in $stats.Keys){$metrics['plugin/'+[Uri]::EscapeDataString($pluginId)+"/$name/$key"]=$stats[$key]}}
    }}
    $groups=@{}
    foreach($sample in $hostSamples){foreach($callback in $sample.callbacks){
        if($callback.P50Milliseconds -gt $callback.P95Milliseconds -or $callback.P95Milliseconds -gt $callback.P99Milliseconds -or $callback.P99Milliseconds -gt $callback.MaximumMilliseconds -or $callback.WindowSamples -gt $sample.monitorConfiguration.retainedSamplesPerSeries){throw 'Invalid callback window in measurement.'}
        $key=[Uri]::EscapeDataString($callback.owner)+'/'+[Uri]::EscapeDataString($callback.operation);if(-not $groups.ContainsKey($key)){$groups[$key]=[Collections.Generic.List[object]]::new()};$groups[$key].Add($callback)
    }}
    foreach($key in $groups.Keys){
        $series=$groups[$key]
        foreach($percent in @(50,95,99)){$metrics["callback/$key/maxWindowP${percent}Milliseconds"]=($series | ForEach-Object {Get-FoundationNumber $_["P${percent}Milliseconds"] 'callback percentile'} | Measure-Object -Maximum).Maximum}
        foreach($name in @('TotalSamples','TotalAllocatedBytes','OverBudget')){
            for($i=1;$i -lt $series.Count;$i++){if($series[$i][$name] -lt $series[$i-1][$name]){throw 'Callback lifetime counter moved backwards.'}}
            $metrics["callback/$key/$name`Delta"]=$series[-1][$name]-$series[0][$name]
        }
    }
    $metrics['host/droppedSeriesSamples/maximum']=($hostSamples | ForEach-Object {$_.droppedSeriesSamples} | Measure-Object -Maximum).Maximum
    return $metrics
}
function Write-FoundationJson([string]$Path,$Object) {
    $json=$Object | ConvertTo-Json -Depth 32
    if([Text.Encoding]::UTF8.GetByteCount($json) -gt 67108864){throw 'Measurement exceeds the 64 MiB output budget; shorten sampling or increase interval.'}
    $stream=[IO.FileStream]::new([IO.Path]::GetFullPath($Path),[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes($json);$stream.Write($bytes)}finally{$stream.Dispose()}
}
if($FunctionsOnly){return}
if(-not $Condition -or $GameProcessId -lt 1 -or -not $OutputPath -or -not $Scenario -or -not $Workload -or $ResolutionWidth -lt 1 -or $ResolutionHeight -lt 1){throw 'Condition, GameProcessId, OutputPath, Scenario, Workload and actual resolution are required.'}
if(($Condition -in @('GameOnly','EmptyFramework') -and $ExpectedPluginIds.Count -ne 0) -or ($Condition -eq 'TwoProbes' -and @($ExpectedPluginIds | Sort-Object -Unique).Count -ne 2) -or ($Condition -eq 'StressPlugin' -and $ExpectedPluginIds.Count -lt 1)){throw 'ExpectedPluginIds do not match the baseline condition.'}
if($Condition -ne 'GameOnly' -and -not $StatusPath){throw 'Host baseline conditions require StatusPath.'}
if(Test-Path -LiteralPath $OutputPath){throw 'OutputPath must be a new file.'}
$process=Get-Process -Id $GameProcessId
$processStart=$process.StartTime.ToUniversalTime();$processName=$process.ProcessName
$machine=[ordered]@{computerName=[Environment]::MachineName;os=[Runtime.InteropServices.RuntimeInformation]::OSDescription;architecture=[Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString();logicalProcessors=[Environment]::ProcessorCount;cpu='unavailable';memoryBytes=$null;gpu='unavailable'}
if($IsWindows){try{$machine.cpu=(Get-CimInstance Win32_Processor | Select-Object -First 1).Name;$machine.memoryBytes=(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory;$machine.gpu=@(Get-CimInstance Win32_VideoController | ForEach-Object {$_.Name}) -join ' / '}catch{$machine.hardwareUnavailableReason=$_.Exception.Message}}
$machineFingerprint=Get-FoundationHash ($machine | ConvertTo-Json -Compress)
if($WarmupSeconds -gt 0){Start-Sleep -Milliseconds ([int]($WarmupSeconds*1000))}
$started=[DateTimeOffset]::UtcNow;$clock=[Diagnostics.Stopwatch]::StartNew();$processSamples=[Collections.Generic.List[object]]::new();$hostSamples=[Collections.Generic.List[object]]::new();$last=$null
try{do{
    $process.Refresh();if($process.HasExited -or $process.StartTime.ToUniversalTime() -ne $processStart){throw 'The selected process exited or its identity changed.'}
    $now=[DateTimeOffset]::UtcNow
    $processSamples.Add([ordered]@{observedAtUtc=$now.ToString('o');elapsedSeconds=$clock.Elapsed.TotalSeconds;cpuTotalMilliseconds=$process.TotalProcessorTime.TotalMilliseconds;workingSetBytes=$process.WorkingSet64;privateBytes=$process.PrivateMemorySize64;handleCount=$process.HandleCount;threadCount=$process.Threads.Count})
    if($Condition -ne 'GameOnly'){
        $sample=Get-FoundationHostSample (Read-FoundationJson $StatusPath) $GameProcessId ([DateTimeOffset]$processStart) $now $MaximumStatusAgeSeconds $last $ExpectedPluginIds
        if($sample){$hostSamples.Add($sample);$last=$sample}
    }
    if($processSamples.Count -gt 200000 -or $hostSamples.Count -gt 20000){throw 'Sample count budget exceeded.'}
    $remaining=$DurationSeconds-$clock.Elapsed.TotalSeconds;if($remaining -gt 0){Start-Sleep -Milliseconds ([int][Math]::Min($IntervalMilliseconds,[Math]::Ceiling($remaining*1000)))}
}while($clock.Elapsed.TotalSeconds -lt $DurationSeconds)}finally{$process.Dispose()}
$measurement=[ordered]@{schemaVersion=1;metricDefinitionVersion='foundation-observation/1';evidenceKind='live-observation';measurementId=[Guid]::NewGuid().ToString('N');condition=$Condition;maximumStatusAgeSeconds=$MaximumStatusAgeSeconds;
    scenario=$Scenario;workload=$Workload;startedAtUtc=$started.ToString('o');endedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');elapsedSeconds=$clock.Elapsed.TotalSeconds;requestedDurationSeconds=$DurationSeconds;warmupSeconds=$WarmupSeconds;intervalMilliseconds=$IntervalMilliseconds;
    machine=$machine;machineFingerprint=$machineFingerprint;settings=[ordered]@{source='operator-reported';resolutionWidth=$ResolutionWidth;resolutionHeight=$ResolutionHeight;frameRateLimit=$FrameRateLimit;vSyncCount=$VSyncCount};
    processIdentity=[ordered]@{processId=$GameProcessId;name=$processName;startedAtUtc=([DateTimeOffset]$processStart).ToString('o')};expectedPluginIds=@($ExpectedPluginIds | Sort-Object -Unique);
    processSamples=$processSamples.ToArray();hostSamples=$hostSamples.ToArray();limitations=@('Sampled process/host observations; no whole-frame or GPU timings.','Callback percentiles are overlapping bounded windows, never pooled or averaged.','Workload/scene cycles and frame-rate settings require operator evidence.')}
$measurement.metrics=Get-FoundationMetrics $measurement
Write-FoundationJson $OutputPath $measurement
[ordered]@{outputPath=[IO.Path]::GetFullPath($OutputPath);measurementId=$measurement.measurementId;condition=$Condition;processSamples=$processSamples.Count;freshHostSamples=$hostSamples.Count;releaseVerdict='NotEvaluated'} | ConvertTo-Json
