#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$KernelDirectory='D:/riz-build-cache-next/engine-wait-kernel-r2-20261026',
    [string]$UnityPath='D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll',
    [string]$VcVars = '',
    [ValidateRange(1,60)][int]$TimeoutSeconds=60
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path){
    if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute paths required.'}
    $full=[IO.Path]::GetFullPath($Path)
    if($full -match '[\r\n"%&|<>^]'){throw 'Unsupported command path characters.'}
    $full
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))}
function FileRow([string]$Path){[ordered]@{path=$Path;sha256=Hash $Path;bytes=(Get-Item -LiteralPath $Path).Length}}
$output=Absolute $OutputDirectory;$kernelRoot=Absolute $KernelDirectory;$unity=Absolute $UnityPath;$vcvarsPath=Absolute $VcVars
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent output directory.'}
if(-not(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($output)) -PathType Container)){throw 'Output parent must exist.'}
$kernel=Join-Path $kernelRoot 'RizToolbox.EngineNodeKernel.dll';$importLib=Join-Path $kernelRoot 'RizToolbox.EngineNodeKernel.lib'
$kernelManifest=Join-Path $kernelRoot 'kernel-manifest.json'
$bootstrap=Join-Path $repo 'native/RizToolbox.EngineBootstrap'
$fixtureCpp=Join-Path $repo 'native/RizToolbox.EngineTransportFamiliesFixture/Families.cpp'
$sourcePaths=@($fixtureCpp,(Join-Path $bootstrap 'EngineTransportFamilies.cpp'),(Join-Path $bootstrap 'EngineTransportFamilies.h'),
    (Join-Path $bootstrap 'EngineTransportRelays.cpp'),(Join-Path $bootstrap 'EngineTransportRelays.h'),
    (Join-Path $bootstrap 'EngineTransportGameProfile.h'),(Join-Path $bootstrap 'EngineTransportPublication.h'),
    (Join-Path $bootstrap 'EngineOriginals.asm'),(Join-Path $bootstrap 'EngineTransportOriginals.asm'),(Join-Path $bootstrap 'EngineTransportOriginals.h'),
    (Join-Path $repo 'native/RizToolbox.JudgeContext/EngineNodeContext.h'),(Join-Path $repo 'native/RizToolbox.JudgeContext/EngineInlineProbe.h'),
    (Join-Path $repo 'native/RizToolbox.JudgeContext/EngineSchedulerContext.h'),(Join-Path $repo 'native/RizToolbox.JudgeContext/EngineWaitContext.h'),$PSCommandPath)
foreach($path in $sourcePaths+@($kernel,$importLib,$kernelManifest,$unity,$vcvarsPath)){
    $null=Absolute $path;if(-not(Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing input: $path"}
}
$null=New-Item -ItemType Directory -Path $output
$result=[ordered]@{schemaVersion=1;kind='engine-transport-families-owned-checks';passed=$false;startedUtc=[DateTime]::UtcNow.ToString('o')
    buildExitCode=$null;buildSucceeded=$false;checks=0;childChecks=0;caseCount=0;cases=@();failure=$null
    sources=@();sourceAfter=@();inputs=@();inputArchive=$null;artifacts=@();sourceHashesUnchanged=$false;inputHashesUnchanged=$false
    actualKernelConfigureVerified=$false;actualKernelConfigureCalls=0;actualSingleNodeAttachCalls=0
    privateMappingOnly=$true;mappedImageExecuted=$false;relayCodeExecuted=$false;originalsExecuted=$false
    sourcePatched=$false;sourceWrites=0;gameExecuted=$false;installerExecuted=$false;productionAdmissionVerified=$false;fullFamilyAdoptionVerified=$false}
$resultPath=Join-Path $output 'test-result.json';$locks=[Collections.Generic.List[IDisposable]]::new()
function Require([bool]$Condition,[string]$Reason){$script:result.checks++;if(-not $Condition){throw $Reason}}
function SegmentHash([byte[]]$Bytes,[int]$Offset,[int]$Count){
    $sha=[Security.Cryptography.SHA256]::Create()
    try{[Convert]::ToHexString($sha.ComputeHash($Bytes,$Offset,$Count)).ToLowerInvariant()}finally{$sha.Dispose()}
}
try{
    foreach($path in $sourcePaths+@($kernel,$importLib,$kernelManifest,$unity)){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    $result.sources=@($sourcePaths|ForEach-Object{FileRow $_})
    $result.inputs=@(@($kernel,$importLib,$kernelManifest,$unity)|ForEach-Object{FileRow $_})
    Require ((Hash $kernel) -ceq '1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a') 'Use the exact prebuilt real Kernel.'
    Require ((Hash $importLib) -ceq 'eb1a8bde36b6e7ff2039e5fc5e69d428ced8ae5279046b1a1a0aa82d1eadce1c') 'Use its exact ordinary import library.'
    Require ((Hash $unity) -ceq '43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1') 'Fixed disk input differs.'
    $manifest=Get-Content -LiteralPath $kernelManifest -Raw|ConvertFrom-Json
    $kernelRows=@($manifest.files|Where-Object name -ceq 'RizToolbox.EngineNodeKernel.dll')
    Require ($manifest.kind -ceq 'engine-node-kernel-build' -and $kernelRows.Count -eq 1 -and $kernelRows[0].sha256 -ceq (Hash $kernel)) 'Kernel build manifest differs.'
    $copiedKernel=Join-Path $output 'RizToolbox.EngineNodeKernel.dll';$copiedLib=Join-Path $output 'RizToolbox.EngineNodeKernel.lib'
    Copy-Item -LiteralPath $kernel -Destination $copiedKernel;Copy-Item -LiteralPath $importLib -Destination $copiedLib
    Require ((Hash $copiedKernel) -ceq (Hash $kernel) -and (Hash $copiedLib) -ceq (Hash $importLib)) 'Copied ordinary dependency differs.'
    $locks.Add([IO.File]::Open($copiedKernel,'Open','Read','Read'));$locks.Add([IO.File]::Open($copiedLib,'Open','Read','Read'))
    WriteJson $result.sources (Join-Path $output 'source-before.json');WriteJson $result.inputs (Join-Path $output 'inputs.json')
    $archive=Join-Path $output 'families-fixture-sources.zip';$zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
    try{foreach($row in $result.sources){$name=[IO.Path]::GetRelativePath($repo,$row.path).Replace('\','/')
        Require (-not $name.StartsWith('../') -and -not [IO.Path]::IsPathRooted($name)) 'Source archive escapes repository.'
        $entry=$zip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal);$to=$entry.Open();$from=[IO.File]::OpenRead($row.path)
        try{$from.CopyTo($to)}finally{$from.Dispose();$to.Dispose()}
    }}finally{$zip.Dispose()}
    $result.inputArchive=[ordered]@{path=$archive;sha256=Hash $archive;entries=$result.sources.Count;gameIncluded=$false}
    $exe=Join-Path $output 'EngineTransportFamilies.Fixture.exe';$commands=[Collections.Generic.List[string]]::new()
    foreach($line in @('@echo off','setlocal DisableDelayedExpansion','chcp 65001 >nul',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1')){$commands.Add($line)}
    foreach($unit in @(@{name='FamiliesFixture';path=$fixtureCpp},@{name='EngineTransportFamilies';path=(Join-Path $bootstrap 'EngineTransportFamilies.cpp')},
        @{name='EngineTransportRelays';path=(Join-Path $bootstrap 'EngineTransportRelays.cpp')})){
        $commands.Add("cl /nologo /std:c++17 /c /W4 /WX /O2 /EHa /MD /Zi /guard:cf /Fo`"$output\$($unit.name).obj`" /Fd`"$output\$($unit.name).compiler.pdb`" `"$($unit.path)`"")
        $commands.Add('if errorlevel 1 exit /b 1')
    }
    foreach($unit in @(@{name='EngineOriginals';path=(Join-Path $bootstrap 'EngineOriginals.asm')},@{name='EngineTransportOriginals';path=(Join-Path $bootstrap 'EngineTransportOriginals.asm')})){
        $commands.Add("ml64 /nologo /c /Zi /Fo`"$output\$($unit.name).obj`" /Fl`"$output\$($unit.name).lst`" `"$($unit.path)`"")
        $commands.Add('if errorlevel 1 exit /b 1')
    }
    $commands.Add("link /nologo /OUT:`"$exe`" /PDB:`"$output\EngineTransportFamilies.Fixture.pdb`" /MAP:`"$output\EngineTransportFamilies.Fixture.map`" /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF `"$output\FamiliesFixture.obj`" `"$output\EngineTransportFamilies.obj`" `"$output\EngineTransportRelays.obj`" `"$output\EngineOriginals.obj`" `"$output\EngineTransportOriginals.obj`" `"$copiedLib`" bcrypt.lib kernel32.lib ntdll.lib")
    $commands.Add('if errorlevel 1 exit /b 1')
    $commands.Add("dumpbin /nologo /headers /imports /unwindinfo `"$exe`" > `"$output\fixture.pe.txt`"")
    $commands.Add('if errorlevel 1 exit /b 1');$commands.Add('exit /b 0')
    $command=Join-Path $output 'build-families-fixture.cmd';[IO.File]::WriteAllLines($command,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $command 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log')
    $result.buildExitCode=$LASTEXITCODE;Require ($LASTEXITCODE -eq 0) 'Strict owned Families fixture build failed.';$result.buildSucceeded=$true
    $locks.Add([IO.File]::Open($exe,'Open','Read','Read'));$result.executable=FileRow $exe
    foreach($mode in @('normal','bad-site','production-gate','receipt-tamper','repeat-attempt','node-attach')){
        $caseDirectory=Join-Path $output $mode;$null=New-Item -ItemType Directory -Path $caseDirectory
        $casePath=Join-Path $caseDirectory 'case.json';$childPath=Join-Path $caseDirectory 'child-result.json'
        $stdoutPath=Join-Path $caseDirectory 'stdout.log';$stderrPath=Join-Path $caseDirectory 'stderr.log'
        $case=[ordered]@{name=$mode;passed=$false;started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false
            arguments=@('--case',$mode,$unity,$caseDirectory);startedUtc=[DateTime]::UtcNow.ToString('o');child=$null;issues=@();files=@()}
        $process=[Diagnostics.Process]::new();$outFile=$null;$errFile=$null;$outTask=$null;$errTask=$null;$timer=[Diagnostics.Stopwatch]::StartNew()
        try{
            $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$exe;$start.WorkingDirectory=$output
            $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
            foreach($arg in $case.arguments){$start.ArgumentList.Add($arg)};$process.StartInfo=$start
            $outFile=[IO.File]::Open($stdoutPath,'CreateNew','Write','ReadWrite');$errFile=[IO.File]::Open($stderrPath,'CreateNew','Write','ReadWrite')
            Require ($process.Start()) 'Owned Families child failed to start.';$case.started=$true;$case.pid=$process.Id
            $outTask=$process.StandardOutput.BaseStream.CopyToAsync($outFile);$errTask=$process.StandardError.BaseStream.CopyToAsync($errFile)
            WriteJson $case $casePath
            if(-not $process.WaitForExit($TimeoutSeconds*1000)){$case.timedOut=$true;$process.Kill();$case.killed=$true
                Require ($process.WaitForExit(10000)) 'Created child did not terminate after timeout.'}
            $case.exitCode=$process.ExitCode;$null=$outTask.GetAwaiter().GetResult();$null=$errTask.GetAwaiter().GetResult()
            $outFile.Dispose();$outFile=$null;$errFile.Dispose();$errFile=$null
            Require (-not $case.timedOut -and $case.exitCode -eq 0) "Actual Families child failed: $mode"
            Require (Test-Path -LiteralPath $childPath -PathType Leaf) 'Actual child JSON missing.'
            $text=[IO.File]::ReadAllText($childPath);$child=$text|ConvertFrom-Json
            Require ([IO.File]::ReadAllText($stdoutPath).Trim() -ceq $text.Trim()) 'Actual stdout and persisted child differ.'
            Require ($child.kind -ceq 'engine-transport-families-owned-child' -and $child.schemaVersion -eq 1 -and $child.mode -ceq $mode -and $child.pid -eq $case.pid -and $child.passed -eq $true -and $child.failures -eq 0 -and $child.checks -gt 0 -and $child.fatal -ceq '') 'Actual child contract failed.'
            Require ($child.fixedDiskSha256 -ceq (Hash $unity) -and $child.requestBytes -eq 640 -and $child.receiptBytes -eq 9792 -and $child.planBytes -eq 13184) 'Raw/schema/fixed-input mismatch.'
            foreach($proof in @('selfOwnedOnly','mappedSourceUnchanged','nearBytesUnchanged','retainedUntilProcessExit','runtimeTableRegistered')){
                Require ($child.$proof -eq $true) "Missing actual proof $mode/$proof"}
            foreach($proof in @('gameExecuted','gameLoaded','mappedImageExecuted','originalsExecuted','relayCodeExecuted','sourcePatched','fullFamilyAdoptionVerified',
                'runtimeTableDeleted','ownedMappingsFreed','productionAdmissionVerified','installerExecuted')){Require ($child.$proof -eq $false) "Boundary changed: $mode/$proof"}
            Require ($child.sourceWrites -eq 0 -and $child.observerCalls -eq 0 -and $child.actualAttachCalls -eq $(if($mode -ceq 'node-attach'){1}else{0})) 'Unexpected source/entry/observer execution or Attach.'
            foreach($raw in @(@{name='request.bin';bytes=640;sha=$child.requestSha256},@{name='receipt.bin';bytes=9792;sha=$child.receiptSha256},@{name='plan.bin';bytes=13184;sha=$child.planSha256})){
                $p=Join-Path $caseDirectory $raw.name;Require ((Test-Path -LiteralPath $p -PathType Leaf) -and (Get-Item -LiteralPath $p).Length -eq $raw.bytes -and (Hash $p) -ceq $raw.sha) 'Actual raw bytes/hash differ from child.'}
            $receiptBytes=[IO.File]::ReadAllBytes((Join-Path $caseDirectory 'receipt.bin'))
            $successful=$mode -cnotin @('bad-site','production-gate')
            Require ($child.actualConfigured -eq $successful) 'Wrong configured mode boundary.'
            if($successful){
                Require ($child.result -eq 0 -and $child.state -eq 3 -and $child.configureAttempts -eq 12 -and $child.configureSucceeded -eq 12 -and $child.apiCount -eq 35 -and $child.attemptedFamilies -eq 4095 -and $child.configuredFamilies -eq 4095 -and $child.retainedFamilies -eq 4095 -and $child.verifiedFamilies -eq 4095) 'Not all real native claims completed.'
                foreach($proof in @('configurationShaVerified','actualInformationVerified','verifyReceiptUnchanged')){Require ($child.$proof -eq $true) "Missing configuration proof $proof"}
                Require (@($child.families).Count -eq 12) 'Missing actual family rows.'
                $lengths=@(24,24,24,24,88,64,64,64,64,64,64,432)
                $requestBytes=[IO.File]::ReadAllBytes((Join-Path $caseDirectory 'request.bin'))
                $actualMainTid=[BitConverter]::ToUInt32($requestBytes,12)
                foreach($i in 0..11){$row=$child.families[$i]
                    Require ($row.index -eq $i -and $row.epoch -gt 0 -and $row.slot -eq 28 -and $row.configBytes -eq $lengths[$i] -and $row.configSha256 -ceq (SegmentHash $receiptBytes (2448+432*$i) $lengths[$i])) 'Actual config row/hash does not match raw bytes.'}
                Require ($actualMainTid -gt 0 -and @($child.families[0..3]|Where-Object mainOsTid -ne 0).Count -eq 0 -and @($child.families[4..11]|Where-Object mainOsTid -ne $actualMainTid).Count -eq 0) 'Wrong actual main OS TID or invented Node main-TID field.'
                Require ([BitConverter]::ToUInt64($receiptBytes,48) -eq 4095 -and [BitConverter]::ToUInt64($receiptBytes,56) -eq 4095 -and [BitConverter]::ToUInt64($receiptBytes,64) -eq 4095 -and [BitConverter]::ToUInt64($receiptBytes,72) -eq 4095) 'Actual raw claim masks differ.'
                Require ([BitConverter]::ToUInt64($receiptBytes,2448+432*4+16) -eq $child.families[0].epoch) 'Inline raw Configure borrowed no actual Start epoch.'
            }else{
                Require ($child.configureAttempts -eq 0 -and $child.configureSucceeded -eq 0 -and $child.attemptedFamilies -eq 0 -and $child.configuredFamilies -eq 0 -and $child.retainedFamilies -eq 0 -and $child.negativeRejected -eq $true -and $child.stickyRejected -eq $true) 'Negative crossed Configure boundary.'
                Require ($child.result -eq $(if($mode -ceq 'bad-site'){10}else{16})) 'Wrong negative rejection reason.'
            }
            if($mode -ceq 'receipt-tamper'){Require ($child.negativeRejected -eq $true -and $child.stickyRejected -eq $true) 'Receipt drift not sticky.'
                $terminal=[IO.File]::ReadAllBytes((Join-Path $caseDirectory 'after-attempt.bin'))
                Require ($terminal.Length -eq 9792 -and [BitConverter]::ToUInt32($terminal,8) -eq 4 -and [BitConverter]::ToUInt32($terminal,12) -eq 14 -and [BitConverter]::ToUInt64($terminal,72) -eq 4095) 'Sticky failed owner lost original actual claims.'}
            if($mode -ceq 'repeat-attempt'){Require ($child.repeatReceiptPreserved -eq $true -and (Hash (Join-Path $caseDirectory 'after-attempt.bin')) -ceq $child.receiptSha256) 'Repeat replaced original receipt.'}
            $case.child=$child;$case.passed=$true;$result.childChecks+=$child.checks;$result.actualKernelConfigureCalls+=$child.configureSucceeded
            $result.actualSingleNodeAttachCalls+=$child.actualAttachCalls
        }catch{$case.issues+=$_.Exception.Message;throw}
        finally{
            if($case.started){try{if(-not $process.HasExited){$process.Kill();$case.killed=$true;$null=$process.WaitForExit(10000)}}catch{$case.issues+=$_.Exception.Message}}
            foreach($pair in @(@{task=$outTask;file=$outFile},@{task=$errTask;file=$errFile})){
                try{if($null -ne $pair.task){$null=$pair.task.GetAwaiter().GetResult()}}catch{$case.issues+=$_.Exception.Message;$case.passed=$false}
                if($null -ne $pair.file){$pair.file.Dispose()}}
            $timer.Stop();$case.elapsedMilliseconds=$timer.ElapsedMilliseconds;$case.completedUtc=[DateTime]::UtcNow.ToString('o');$process.Dispose()
            $case.files=@(Get-ChildItem -LiteralPath $caseDirectory -File|Where-Object Name -cne 'case.json'|ForEach-Object{FileRow $_.FullName})
            WriteJson $case $casePath;$result.cases+=[ordered]@{name=$mode;passed=$case.passed;pid=$case.pid;exitCode=$case.exitCode;timedOut=$case.timedOut;path=$casePath;sha256=Hash $casePath}
            $result.caseCount=$result.cases.Count;WriteJson $result $resultPath
        }
    }
    Require ($result.caseCount -eq 6 -and @($result.cases|Where-Object passed -ne $true).Count -eq 0) 'Actual six-child matrix incomplete.'
    Require ($result.actualKernelConfigureCalls -eq 48 -and $result.actualSingleNodeAttachCalls -eq 1) 'Unexpected real configure/Attach total.'
    $result.sourceAfter=@($sourcePaths|ForEach-Object{FileRow $_})
    $result.sourceHashesUnchanged=$true
    for($i=0;$i -lt $result.sources.Count;++$i){if($result.sources[$i].sha256 -cne $result.sourceAfter[$i].sha256){$result.sourceHashesUnchanged=$false}}
    $result.inputHashesUnchanged=$true;foreach($row in $result.inputs){if((Hash $row.path) -cne $row.sha256){$result.inputHashesUnchanged=$false}}
    Require ($result.sourceHashesUnchanged -and $result.inputHashesUnchanged) 'Frozen source/dependency bytes changed.'
    Require ((Hash $copiedKernel) -ceq (Hash $kernel) -and (Hash $exe) -ceq $result.executable.sha256) 'Actual executed native image changed.'
    $result.actualKernelConfigureVerified=$true;$result.passed=$true
}catch{$result.failure=$_.Exception.Message;throw}
finally{
    foreach($held in $locks){$held.Dispose()}
    $result.completedUtc=[DateTime]::UtcNow.ToString('o')
    $result.artifacts=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -cne 'test-result.json'|ForEach-Object{FileRow $_.FullName})
    WriteJson $result $resultPath
}
if(-not $result.passed){throw "Owned Families verification failed; evidence retained at $resultPath"}
Write-Output "Families owned checks: 6 child processes; 48 actual Configure calls; parent=$($result.checks), child=$($result.childChecks); $resultPath"
