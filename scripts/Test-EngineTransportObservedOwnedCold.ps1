#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$CandidateDirectory,[Parameter(Mandatory)][string]$OutputDirectory,[ValidateRange(1,60)][int]$TimeoutSeconds=60)
$ErrorActionPreference='Stop'
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute paths required.'};[IO.Path]::GetFullPath($Path)}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))}
$candidate=Absolute $CandidateDirectory;$output=Absolute $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent result directory.'}
$buildPath=Join-Path $candidate 'observed-owned-candidate-build.json';$build=Get-Content -LiteralPath $buildPath -Raw|ConvertFrom-Json
if($build.passed -ne $true -or $build.kind -ne 'engine-transport-observed-owned-candidate-build'){throw 'A successful exact owned candidate build is required.'}
$inputPaths=@(@($build.files|ForEach-Object{$_.path})+@($buildPath,$PSCommandPath)|Sort-Object -Unique)
$locks=[Collections.Generic.List[IDisposable]]::new();$child=[Diagnostics.Process]::new()
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-observed-owned-cold-checks';passed=$false;started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false
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
    $initialPath=Join-Path $output 'observations-initial.raw';$finalPath=Join-Path $output 'observations-final.raw'
    $child.StartInfo.ArgumentList.Add($jsonPath);$child.StartInfo.ArgumentList.Add($rawPath);$child.StartInfo.ArgumentList.Add($initialPath);$child.StartInfo.ArgumentList.Add($finalPath)
    if(-not $child.Start()){throw 'Owned cold process did not start.'};$manifest.started=$true;$manifest.pid=$child.Id
    $outTask=$child.StandardOutput.ReadToEndAsync();$errTask=$child.StandardError.ReadToEndAsync()
    if(-not $child.WaitForExit($TimeoutSeconds*1000)){$manifest.timedOut=$true;$child.Kill();$manifest.killed=$true;$child.WaitForExit()}
    $manifest.exitCode=$child.ExitCode
    [IO.File]::WriteAllText((Join-Path $output 'stdout.log'),$outTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output 'stderr.log'),$errTask.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
    if(Test-Path -LiteralPath $jsonPath){$manifest.child=Get-Content -LiteralPath $jsonPath -Raw|ConvertFrom-Json}
    if($manifest.timedOut -or $manifest.exitCode -ne 0 -or -not $manifest.child){throw "Owned cold process rejected the candidate (exit $($manifest.exitCode))."}
    $value=$manifest.child
    if($value.pid -ne $manifest.pid -or $value.passed -ne $true -or $value.kind -ne 'engine-transport-observed-owned-cold' -or $value.schemaVersion -ne 1 -or
        $value.checks -le 0 -or $value.failures -ne 0 -or $value.initialRecords -ne 3 -or $value.finalRecords -ne 4 -or $value.windowsPerRecord -ne 36 -or
        $value.actualLoaderTlsEntryOrder -ne $true -or $value.actualSourcePayloadMatches -ne $true -or $value.staticLeafCodeVerified -ne $true -or
        $value.storagePolicyVersion -ne 2 -or $value.initialStorageSectionRwBacked -ne $true -or $value.finalStorageSectionRwBacked -ne $true -or
        $value.initialStorageStrictAccepted -ne $true -or $value.finalStorageStrictAccepted -ne $true -or
        (Compare-Object @(4,8) @($value.allowedStorageProtections) -SyncWindow 0) -or
        $value.immutablePublication -ne $true -or $value.rawPersisted -ne $true -or $value.ownedUnityResult -ne 0 -or
        $value.gameExecuted -ne $false -or $value.originalGameCodeExecuted -ne $false -or $value.productionAdmissionVerified -ne $false -or
        $value.fullFamilyAdoptionVerified -ne $false -or (Get-Item -LiteralPath $rawPath).Length -ne 91376){throw 'Actual observed-owned cold receipt has the wrong scope or shape.'}
    $publication=[IO.File]::ReadAllBytes($rawPath);$initial=[IO.File]::ReadAllBytes($initialPath);$final=[IO.File]::ReadAllBytes($finalPath)
    function U32($data,[int]$at){[BitConverter]::ToUInt32($data,$at)}
    function U64($data,[int]$at){[BitConverter]::ToUInt64($data,$at)}
    function Zero($data,[int]$at,[int]$bytes){for($z=$at;$z -lt $at+$bytes;++$z){if($data[$z] -ne 0){return $false}};return $true}
    if((U32 $publication 0) -ne 5 -or (U32 $publication 4) -ne 91376 -or (U32 $publication 8) -ne 4 -or
        (U32 $publication 44) -ne $manifest.pid -or (U32 $publication 56) -ne 1 -or (U32 $publication 68) -ne $value.threadId){throw 'Native raw publication identity differs from actual child.'}
    $targetBase=U64 $publication 216
    foreach($pair in @(@{data=$initial;count=3},@{data=$final;count=4})){
        $data=$pair.data;$count=$pair.count
        if($data.Length -ne 9792 -or [Text.Encoding]::ASCII.GetString($data,0,13) -ne 'RIZOBSERVEDV1' -or
            -not(Zero $data 13 3) -or (U32 $data 16) -ne 1 -or (U32 $data 20) -ne 64 -or (U32 $data 24) -ne 9792 -or
            (U32 $data 28) -ne 8 -or (U32 $data 32) -ne 1216 -or (U32 $data 36) -ne 36 -or
            (U64 $data 40) -ne $count -or (U64 $data 48) -ne 0 -or (U64 $data 56) -ne $count){throw 'Actual observation header/count/overflow differs.'}
        for($record=0;$record -lt $count;++$record){
            $at=64+$record*1216;$reason=if($record -lt 3){1UL}else{0xffffffffUL}
            if((U64 $data $at) -ne $record+1 -or (U64 $data ($at+8)) -ne $record+1 -or (U32 $data ($at+16)) -ne $record -or
                (U32 $data ($at+20)) -ne $value.threadId -or (U32 $data ($at+24)) -ne $reason -or
                (U32 $data ($at+28)) -ne 36 -or (U64 $data ($at+32)) -ne $targetBase -or (U64 $data ($at+40)) -eq 0 -or -not(Zero $data ($at+52) 12)){throw 'Actual earliest record identity/order/TID/returnPC differs.'}
            $payloadBytes=0
            for($window=0;$window -lt 36;++$window){
                $rawAt=8352+$window*448;$size=U32 $publication ($rawAt+44);$source=U64 $publication ($rawAt+80);$rva=U32 $publication ($rawAt+40)
                if($size -le 0 -or $size -gt 32 -or $source -ne $targetBase+$rva){throw 'Raw source-window geometry differs.'}
                $payload=$at+64+$window*32;$payloadBytes+=$size
                for($byte=0;$byte -lt $size;++$byte){if($data[$payload+$byte] -ne $publication[$rawAt+312+$byte] -or $data[$payload+$byte] -ne $publication[$rawAt+280+$byte]){throw 'Earliest actual source bytes differ from native committed/readback patch payload.'}}
                if(-not(Zero $data ($payload+$size) (32-$size))){throw 'Observed payload padding is not zero.'}
            }
            if((U32 $data ($at+48)) -ne $payloadBytes){throw 'Recorded full source byte count differs.'}
        }
        if(-not(Zero $data (64+$count*1216) ((8-$count)*1216))){throw 'Unused observation record capacity is not zero.'}
    }
    for($byte=64;$byte -lt 64+3*1216;++$byte){if($initial[$byte] -ne $final[$byte]){throw 'First three loader records changed after the explicit owned export call.'}}
    foreach($row in $manifest.inputs){if((Hash $row.path) -ne $row.sha256){throw "Run input changed: $($row.path)"}}
    $manifest.inputHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};$child.Dispose();$manifest.artifacts=@(Get-ChildItem -LiteralPath $output -File|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}});WriteJson $manifest (Join-Path $output 'test-result.json')}
Write-Output "Actual observed-owned TLS/entry/patch-window process passed: PID=$($manifest.pid); $output"
