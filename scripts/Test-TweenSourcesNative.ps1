#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($OutputDirectory)){throw 'An absolute fresh output path is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve earlier native source evidence; use a fresh output.'}
if(-not(Test-Path -LiteralPath (Split-Path -Parent $output) -PathType Container)){throw 'The output parent must already exist.'}
foreach($path in @($repo,$output,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported native command path.'}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))}
$null=New-Item -ItemType Directory -Path $output
$relative=@('native/RizToolbox.JudgeContext/TweenRetirementContext.h','native/RizToolbox.JudgeContext/TweenRetirementContext.cpp','native/RizToolbox.JudgeContext/TweenSourceContext.cpp','native/RizToolbox.JudgeContext/TweenSourceContext.h',
    'native/RizToolbox.JudgeContext/TweenSourceProfile.h','native/RizToolbox.JudgeContext/TweenSourceEntry.asm',
    'native/RizToolbox.JudgeContext/MidContext.h','native/RizToolbox.JudgeContext/TweenAdmission.h',
    'native/RizToolbox.JudgeContext/TweenSourceContext.References.h',
    'native/RizToolbox.JudgeContext/TweenSourceReferences.h','native/RizToolbox.JudgeContext/TweenSourceReferences.cpp',
    'native/RizToolbox.JudgeContext/fixture/TweenSourceFixture.cpp','native/RizToolbox.JudgeContext/fixture/TweenSourceFixtureCaller.asm',
    'scripts/Test-TweenSourcesNative.ps1')
$locks=[Collections.Generic.List[IDisposable]]::new();$rows=@();$modes=[Collections.Generic.List[object]]::new()
$manifest=[ordered]@{schemaVersion=1;kind='owned-native-tween-source-fixture-build';passed=$false;compileExitCode=$null;
    sourceHashesUnchanged=$false;sourceFiles=@();sourceArchive=$null;fixture=$null;modes=@();result=$null;failure=$null;
    productionHelperExecuted=$false;gameExecuted=$false;officialMetadataVerified=$false}
try{
    foreach($name in $relative){
        $path=[IO.Path]::GetFullPath((Join-Path $repo $name))
        for($cursor=$path;$cursor;$cursor=Split-Path -Parent $cursor){
            if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Fixture inputs cannot cross links.'}
        }
        $locks.Add([IO.File]::Open($path,'Open','Read','Read'))
        $rows+=@{path=$path;relativePath=$name;sha256=Hash $path;bytes=(Get-Item -LiteralPath $path).Length}
    }
    $manifest.sourceFiles=$rows
    $archive=Join-Path $output 'source-inputs.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{foreach($row in $rows){$entry=$zip.CreateEntry($row.relativePath,[IO.Compression.CompressionLevel]::Optimal)
        $source=[IO.File]::OpenRead($row.path);$destination=$entry.Open()
        try{$source.CopyTo($destination)}finally{$source.Dispose();$destination.Dispose()}
    }}finally{$zip.Dispose()}
    $manifest.sourceArchive=@{path=$archive;sha256=Hash $archive;bytes=(Get-Item -LiteralPath $archive).Length}
    $native=Join-Path $repo 'native/RizToolbox.JudgeContext';$fixture=Join-Path $output 'TweenSourceFixture.exe'
    $command=Join-Path $output 'build-source-fixture.cmd'
    $flags='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_TWEEN_SOURCE_FIXTURE'
    $lines=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\entry.obj`" `"$native\TweenSourceEntry.asm`"",'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\caller.obj`" `"$native\fixture\TweenSourceFixtureCaller.asm`"",'if errorlevel 1 exit /b 1',
        "cl $flags /c /Fo`"$output\core.obj`" /Fd`"$output\core.pdb`" `"$native\TweenSourceContext.cpp`"",'if errorlevel 1 exit /b 1',
        "cl $flags /c /Fo`"$output\references.obj`" /Fd`"$output\references.pdb`" `"$native\TweenSourceReferences.cpp`"",'if errorlevel 1 exit /b 1',
        "cl $flags /DRIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY /c /Fo`"$output\retirement.obj`" /Fd`"$output\retirement.pdb`" `"$native\TweenRetirementContext.cpp`"",'if errorlevel 1 exit /b 1',
        "cl $flags /c /Fo`"$output\fixture.obj`" /Fd`"$output\fixture-compiler.pdb`" `"$native\fixture\TweenSourceFixture.cpp`"",'if errorlevel 1 exit /b 1',
        "link /nologo `"$output\core.obj`" `"$output\references.obj`" `"$output\retirement.obj`" `"$output\fixture.obj`" `"$output\entry.obj`" `"$output\caller.obj`" /OUT:`"$fixture`" /PDB:`"$output\fixture.pdb`" /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1')
    [IO.File]::WriteAllLines($command,$lines,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $command 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log')
    $manifest.compileExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw "Strict owned source fixture compilation failed: $LASTEXITCODE"}
    $manifest.fixture=@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}
    foreach($mode in @('happy','capacity','broken-pair','wrong-parent','foreign-tail','abnormal-exit','unbound','unfinished-window',
        'references-grow','references-fault','references-abnormal','references-before-changed','references-after-changed')){
        $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.ArgumentList.Add($mode);$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $child=[Diagnostics.Process]::new();$child.StartInfo=$start
        try{
            if(-not $child.Start()){throw 'Could not start the owned source fixture.'}
            $stdout=$child.StandardOutput.ReadToEndAsync();$stderr=$child.StandardError.ReadToEndAsync()
            if(-not $child.WaitForExit(60000)){$child.Kill();$null=$child.WaitForExit(10000);throw "Source fixture $mode timed out."}
            $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$code=$child.ExitCode
        }finally{$child.Dispose()}
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stdout.log')),$out)
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stderr.log')),$err)
        if($code -ne 0){throw "Source fixture $mode failed with exit $code : $err $out"}
        $value=$out|ConvertFrom-Json -AsHashtable
        if($value.mode -cne $mode -or $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or
            $value.machineCalls -le 0 -or $value.gameCodeExecuted -ne $false -or $value.gameRuntimeVerified -ne $false -or
            $value.officialMetadataVerified -ne $false){throw 'Actual owned source fixture result has an inconsistent execution contract/scope.'}
        if($mode.StartsWith('references-',[StringComparison]::Ordinal)){
            if($value.kind -cne 'owned-joint-tween-source-references-fixture' -or $value.actualPublisherReadyVerified -ne $false -or
                $value.referenceCallbacks -le 0 -or $value.referencesRooted -le 0 -or $value.metadataReads -le 0 -or $value.beforeNotices -le 0){
                throw 'The actual joint preserving-entry/reference observer evidence is incomplete.'
            }
            if($mode -ceq 'references-grow'){
                if($value.growthReadRestored -ne 1 -or $value.nestedOperations -ne 1 -or $value.maximumCallbackDepth -lt 2 -or
                    $value.afterNotices -le 0 -or $value.abnormalNotices -ne 0 -or $value.unknown -ne 0 -or
                    $value.retainedReferenceScopes -ne 0 -or $value.retainedSourceTransactions -ne 0){throw 'Actual source growth/nested reference publication did not complete.'}
            }elseif($mode -ceq 'references-fault'){
                if($value.growthReadRestored -ne 1 -or $value.afterNotices -le 0 -or $value.unknown -ne 1 -or
                    $value.retainedReferenceScopes -ne 1 -or $value.retainedSourceTransactions -ne 1){throw 'An observer fault suppressed actual After references or lost retained responsibility.'}
            }elseif($mode -ceq 'references-before-changed'){
                if($value.beforeCommittedMutationChecked -ne $true -or $value.growthReadRestored -ne 1 -or $value.afterNotices -le 0 -or
                    $value.abnormalNotices -ne 1 -or $value.modeledEffects -ne 1 -or $value.unknown -ne 1 -or
                    $value.retainedReferenceScopes -ne 1 -or $value.retainedSourceTransactions -ne 1){throw 'A reentrant BeforeCommitted mutation was accepted or suppressed the real source body.'}
            }elseif($mode -ceq 'references-after-changed'){
                if($value.afterCommittedMutationChecked -ne $true -or $value.originalPopTokenPreserved -ne $true -or
                    $value.originalRaxPreserved -ne $true -or $value.redirects -ne 0 -or $value.abnormalNotices -ne 1 -or
                    $value.modeledEffects -ne 1 -or $value.unknown -ne 1 -or $value.retainedReferenceScopes -ne 1 -or
                    $value.retainedSourceTransactions -ne 1){throw 'A reentrant AfterCommitted mutation changed the original pop result, return value, selector or retained responsibility.'}
            }elseif($value.abnormalNotices -ne 1 -or $value.afterNotices -ne 0 -or $value.unknown -ne 1 -or
                $value.retainedReferenceScopes -ne 1 -or $value.retainedSourceTransactions -ne 1){throw 'The actual abnormal source lost its live-parent notification or retained responsibility.'}
        }elseif($value.kind -cne 'owned-native-tween-source-fixture' -or $value.siteCount -ne 22 -or $value.bindingCount -ne 43 -or
            $value.recordBytes -ne 192 -or $value.infoBytes -ne 96 -or $value.fullStackAliasProof -ne $false){
            throw 'The actual original preserving-entry ABI evidence differs.'
        }
        if($mode -eq 'happy' -and ($value.coveredBindings -ne 43 -or $value.unknown -ne 0 -or $value.events -le 8192 -or $value.retainedRecords -ne 8192)){
            throw 'The actual owned fixture omitted source bindings or normal ring-window rollover.'
        }
        $modes.Add($value);Json $value (Join-Path $output ($mode+'-result.json'))
        Write-Output "PASS owned source $mode ($($value.checks) checks)."
    }
    foreach($row in $rows){if((Hash $row.path) -cne $row.sha256){throw 'A declared native fixture source changed.'}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{
    $manifest.modes=@($modes)
    $result=[ordered]@{schemaVersion=1;kind='owned-native-tween-source-fixture-summary';passed=$manifest.passed;
        independentProcesses=$modes.Count;checks=($modes|Measure-Object checks -Sum).Sum;modes=@($modes);
        sourceArchive=$manifest.sourceArchive;sourceHashesUnchanged=$manifest.sourceHashesUnchanged;
        actualPreservingEntryExecuted=$true;productionHelperExecuted=$false;gameExecuted=$false;officialMetadataVerified=$false;failure=$manifest.failure}
    $resultPath=Join-Path $output 'fixture-result.json';Json $result $resultPath
    $manifest.result=@{path=$resultPath;sha256=Hash $resultPath};Json $manifest (Join-Path $output 'fixture-manifest.json')
    foreach($lease in $locks){$lease.Dispose()}
}
