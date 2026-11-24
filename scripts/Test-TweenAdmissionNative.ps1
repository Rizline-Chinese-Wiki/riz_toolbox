#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$HelperDirectory,[Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Ordinary([string]$Path,[switch]$Directory,[switch]$MayNotExist){
    if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Native fixture paths must be explicit absolute paths.'}
    $resolved=[IO.Path]::GetFullPath($Path)
    for($cursor=$resolved;$cursor;$cursor=Split-Path -Parent $cursor){
        if(Test-Path -LiteralPath $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Native fixture paths cannot cross links.'}}
        elseif($cursor -ne $resolved -or -not $MayNotExist){throw 'An ordinary native fixture path is missing.'}
    }
    if(-not $MayNotExist){if($Directory -and -not(Test-Path -LiteralPath $resolved -PathType Container)){throw 'A directory is required.'}
        if(-not $Directory -and -not(Test-Path -LiteralPath $resolved -PathType Leaf)){throw 'A file is required.'}}
    return $resolved
}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$root=Ordinary $HelperDirectory -Directory;$output=Ordinary $OutputDirectory -MayNotExist;$vcvars=Ordinary $VcVars
if(Test-Path -LiteralPath $output){throw 'Preserve earlier gate evidence; use a fresh output directory.'}
foreach($inputRoot in @($root,$repo)){
    if($output -ieq $inputRoot -or $inputRoot.StartsWith($output+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){
        throw 'Result directory cannot replace or contain build/source inputs.'
    }
}
$helper=Ordinary (Join-Path $root 'RizToolbox.JudgeContext.dll')
$buildPath=Ordinary (Join-Path $root 'native-manifest.json');$build=Get-Content -LiteralPath $buildPath -Raw|ConvertFrom-Json
if($build.kind -cne 'judge-context-operation-native-build' -or $build.passed -ne $true -or $build.buildExitCode -ne 0 -or
    $build.inputHashesUnchanged -ne $true -or $build.publicationVersion -ne 7 -or $build.operationCount -ne 26 -or $build.expectedWrites -ne 64){throw 'An actual successful current helper build is required.'}
$helperRow=@($build.files|Where-Object{[IO.Path]::GetFileName($_.path) -ceq 'RizToolbox.JudgeContext.dll'})
if($helperRow.Count -ne 1 -or (Hash $helper) -cne $helperRow[0].sha256){throw 'Actual helper output differs from its source build.'}
$relative=@('native/RizToolbox.JudgeContext/TweenRetirementContext.h','native/RizToolbox.JudgeContext/TweenRetirementContext.cpp','native/RizToolbox.JudgeContext/TweenAdmission.h','native/RizToolbox.JudgeContext/TweenAdmission.cpp',
    'native/RizToolbox.JudgeContext/OperationContext.h','native/RizToolbox.JudgeContext/OperationContext.cpp',
    'native/RizToolbox.JudgeContext/FirstInstall.h',
    'native/RizToolbox.JudgeContext/fixture/TweenAdmissionFixture.cpp','scripts/Test-TweenAdmissionNative.ps1')
$null=New-Item -ItemType Directory -Path $output
$leases=[Collections.Generic.List[IDisposable]]::new();$sourceRows=@();$compileExit=-1;$testExit=-1;$modes=[Collections.Generic.List[object]]::new()
$fixture=Join-Path $output 'TweenAdmission.Fixture.exe';$started=[DateTime]::UtcNow.ToString('o');$passed=$false;$failure=$null;$sourceArchive=$null
try{
    foreach($name in $relative){
        $path=Ordinary (Join-Path $repo $name);$leases.Add([IO.File]::Open($path,'Open','Read','Read'))
        $row=@{path=$path;relativePath=$name;sha256=Hash $path;bytes=(Get-Item -LiteralPath $path).Length};$sourceRows+=$row
        if($name.StartsWith('native/',[StringComparison]::Ordinal)){
            $declared=@($build.sources|Where-Object relativePath -ceq $name)
            if($declared.Count -ne 1 -or $declared[0].sha256 -cne $row.sha256){throw 'The native fixture does not compile the actual helper build source.'}
        }
    }
    $archive=Join-Path $output 'fixture-sources.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{
        foreach($row in $sourceRows){
            $entry=$zip.CreateEntry($row.relativePath,[IO.Compression.CompressionLevel]::Optimal)
            $inputFile=[IO.File]::OpenRead($row.path);$entryStream=$entry.Open()
            try{$inputFile.CopyTo($entryStream)}finally{$inputFile.Dispose();$entryStream.Dispose()}
        }
    }finally{$zip.Dispose()}
    $sourceArchive=@{path=$archive;sha256=Hash $archive;bytes=(Get-Item -LiteralPath $archive).Length}
    $leases.Add([IO.File]::Open($helper,'Open','Read','Read'));$leases.Add([IO.File]::Open($buildPath,'Open','Read','Read'))
    foreach($path in @($output,$repo,$vcvars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported cmd build path.'}}
    $sourceRoot=Join-Path $repo 'native/RizToolbox.JudgeContext';$cmd=Join-Path $output 'build-tween-admission-fixture.cmd'
    $commands=@('@echo off',"call `"$vcvars`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_TWEEN_ADMISSION_FIXTURE /DRIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY /Fo`"$output\\`" /Fd`"$output\TweenAdmission.compiler.pdb`" `"$sourceRoot\TweenAdmission.cpp`" `"$sourceRoot\OperationContext.cpp`" `"$sourceRoot\TweenRetirementContext.cpp`" `"$sourceRoot\fixture\TweenAdmissionFixture.cpp`" /link /OUT:`"$fixture`" /PDB:`"$output\TweenAdmission.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1')
    [IO.File]::WriteAllLines($cmd,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $cmd 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log');$compileExit=$LASTEXITCODE
    if($compileExit -ne 0){throw "Dedicated actual native gate fixture compilation failed: $compileExit"}
    foreach($mode in @('normal','unknown-read','abnormal','capacity')){
        $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.ArgumentList.Add($mode)
        $child=[Diagnostics.Process]::new();$child.StartInfo=$start
        if(-not $child.Start()){throw 'Could not start the owned native gate fixture.'}
        $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
        if(-not $child.WaitForExit(60000)){$child.Kill();$null=$child.WaitForExit(10000);throw 'The exact owned native gate fixture exceeded its bounded timeout.'}
        $testExit=$child.ExitCode;$out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$child.Dispose()
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stdout.log')),$out);[IO.File]::WriteAllText((Join-Path $output ($mode+'.stderr.log')),$err)
        $lines=@($out -split '\r?\n'|Where-Object{$_.StartsWith('{')})
        if($testExit -ne 0 -or $lines.Count -ne 1){throw "Native Tween gate $mode failed exit$testExit : $err $out"}
        $value=$lines[0]|ConvertFrom-Json -AsHashtable
        if($value.kind -cne 'typed-tween-admission-native-fixture-mode' -or $value.mode -cne $mode -or $value.passed -ne $true -or
            $value.checks -le 0 -or $value.failures -ne 0 -or $value.actualNativeGateExecuted -ne $true -or
            $value.actualOperationDispatchExecuted -ne $true -or $value.controlledReadyAndMetadata -ne $true -or
            $value.productionMetadataVerified -ne $false -or $value.productionHelperExecuted -ne $false -or
            $value.gameExecuted -ne $false -or $value.specificIssuanceOnly -ne $true){throw 'Native gate execution result scope/contract differs.'}
        if($mode -eq 'normal' -and ($value.skippedCalls -ne 5 -or $value.beforeCalls -ne $value.afterCalls)){throw 'Actual skipped dispatch counts/pairing differ.'}
        if($mode -eq 'normal'){
            foreach($fact in @('workerClosingWithoutEarlySeal','allFivePhysicalLanesBlockUnregister','deadUnownedSkipped',
                'aliveUnownedForwarded','nullArgumentOriginalException','skippedFlagsWithoutReturned','exactOriginalCounters','retiredSlotNewEpoch')){
                if($value[$fact] -ne $true){throw 'A required actual native gate behavior is unproved.'}
            }
        }
        $modes.Add($value);$lines[0]|Set-Content -LiteralPath (Join-Path $output ($mode+'-result.json')) -Encoding utf8
        Write-Output "PASS native Tween gate $mode ($($value.checks) checks); controlled metadata only."
    }
    foreach($row in $sourceRows){if((Hash $row.path) -cne $row.sha256){throw 'A compiled native fixture input changed.'}}
    $passed=$true
} catch {$failure=$_.Exception.Message;throw}
finally{
    $ended=[DateTime]::UtcNow.ToString('o')
    $summary=[ordered]@{schemaVersion=1;kind='typed-tween-admission-native-fixture';passed=$passed;checks=($modes|Measure-Object checks -Sum).Sum;
        failures=@($modes|Where-Object passed -ne $true).Count;independentProcesses=$modes.Count;modes=@($modes);
        actualNativeGateExecuted=$true;actualOperationDispatchExecuted=$true;controlledReadyAndMetadata=$true;
        productionMetadataVerified=$false;productionHelperExecuted=$false;gameExecuted=$false;specificIssuanceOnly=$true;
        nativeBuildManifestSha256=Hash $buildPath;helperSha256=Hash $helper;failure=$failure}
    $summary|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
    $manifest=[ordered]@{schemaVersion=1;kind='typed-tween-admission-native-fixture-build';passed=$passed;startedUtc=$started;endedUtc=$ended;
        compileExitCode=$compileExit;testExitCode=$testExit;inputHashesUnchanged=$passed;sourceFiles=$sourceRows;sourceArchive=$sourceArchive;
        fixture=if(Test-Path -LiteralPath $fixture){@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}}else{$null};
        result=@{path=(Join-Path $output 'fixture-result.json');sha256=Hash (Join-Path $output 'fixture-result.json')};
        nativeBuildManifest=@{path=$buildPath;sha256=Hash $buildPath};helper=@{path=$helper;sha256=Hash $helper};
        controlledConfigurePreconditions='RIZ_TWEEN_ADMISSION_FIXTURE only in this EXE replaces Ready and official metadata checks; no production bypass export.';
        sameProductionGateAndDispatchSources=$true;productionHelperExecuted=$false;gameExecuted=$false;failure=$failure}
    $manifest|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $output 'fixture-manifest.json') -Encoding utf8
    foreach($lease in $leases){$lease.Dispose()}
}
