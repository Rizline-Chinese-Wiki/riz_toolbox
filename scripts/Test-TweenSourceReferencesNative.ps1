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
if(Test-Path -LiteralPath $output){throw 'Preserve earlier reference publication evidence.'}
foreach($path in @($output,$repo,$VcVars)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported native command path.'}}
$null=New-Item -ItemType Directory -Path $output
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Json($Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))}
$files=@('native/RizToolbox.JudgeContext/TweenRetirementContext.h','native/RizToolbox.JudgeContext/TweenRetirementContext.cpp','native/RizToolbox.JudgeContext/TweenSourceReferences.h','native/RizToolbox.JudgeContext/TweenSourceReferences.cpp',
    'native/RizToolbox.JudgeContext/TweenSourceContext.h','native/RizToolbox.JudgeContext/fixture/TweenSourceReferencesFixture.cpp',
    'scripts/Test-TweenSourceReferencesNative.ps1')
$locks=[Collections.Generic.List[IDisposable]]::new();$rows=@();$modes=[Collections.Generic.List[object]]::new()
$manifest=[ordered]@{schemaVersion=1;kind='owned-tween-source-references-native-build';passed=$false;compileExitCode=$null;
    sourceFiles=@();sourceArchive=$null;sourceHashesUnchanged=$false;fixture=$null;result=$null;failure=$null;
    gameExecuted=$false;productionHelperExecuted=$false;officialMetadataVerified=$false;actualPublisherReadyVerified=$false}
try{
    foreach($name in $files){
        $path=Join-Path $repo $name
        for($cursor=$path;$cursor;$cursor=Split-Path -Parent $cursor){if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Reference fixture sources cannot cross links.'}}
        $locks.Add([IO.File]::Open($path,'Open','Read','Read'))
        $rows+=@{path=$path;relativePath=$name;sha256=Hash $path;bytes=(Get-Item -LiteralPath $path).Length}
    }
    $manifest.sourceFiles=$rows
    $archive=Join-Path $output 'reference-sources.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{foreach($row in $rows){$entry=$zip.CreateEntry($row.relativePath,[IO.Compression.CompressionLevel]::Optimal)
        $inputFile=[IO.File]::OpenRead($row.path);$entryFile=$entry.Open()
        try{$inputFile.CopyTo($entryFile)}finally{$inputFile.Dispose();$entryFile.Dispose()}
    }}finally{$zip.Dispose()}
    $manifest.sourceArchive=@{path=$archive;sha256=Hash $archive;bytes=(Get-Item -LiteralPath $archive).Length}
    $native=Join-Path $repo 'native/RizToolbox.JudgeContext';$fixture=Join-Path $output 'ReferencesFixture.exe'
    $command=Join-Path $output 'build.cmd';$flags='/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
    $lines=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
        "cl $flags /c /Fo`"$output\references.obj`" /Fd`"$output\references.pdb`" `"$native\TweenSourceReferences.cpp`"",'if errorlevel 1 exit /b 1',
        "cl $flags /DRIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY /c /Fo`"$output\retirement.obj`" /Fd`"$output\retirement.pdb`" `"$native\TweenRetirementContext.cpp`"",'if errorlevel 1 exit /b 1',
        "cl $flags /Fo`"$output\fixture.obj`" /Fd`"$output\fixture-compiler.pdb`" `"$native\fixture\TweenSourceReferencesFixture.cpp`" `"$output\references.obj`" `"$output\retirement.obj`" /link /OUT:`"$fixture`" /PDB:`"$output\fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1')
    [IO.File]::WriteAllLines($command,$lines,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $command 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log');$manifest.compileExitCode=$LASTEXITCODE
    if($LASTEXITCODE -ne 0){throw "Strict native reference fixture compilation failed: $LASTEXITCODE"}
    $manifest.fixture=@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}
    foreach($mode in @('happy','cpp','seh','nonzero','original-cpp','original-seh','capacity','unpaired','empty')){
        $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.ArgumentList.Add($mode);$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start
        try{
            if(-not $process.Start()){throw 'Could not start the exact owned references fixture.'}
            $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
            if(-not $process.WaitForExit(60000)){$process.Kill();$null=$process.WaitForExit(10000);throw 'Owned references fixture timeout.'}
            $out=$stdout.GetAwaiter().GetResult();$err=$stderr.GetAwaiter().GetResult();$code=$process.ExitCode
        }finally{$process.Dispose()}
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stdout.log')),$out)
        [IO.File]::WriteAllText((Join-Path $output ($mode+'.stderr.log')),$err)
        if($code -ne 0){throw "Native reference fixture $mode failed with exit $code : $err $out"}
        $value=$out|ConvertFrom-Json -AsHashtable
        if($value.kind -cne 'owned-native-tween-source-references-fixture' -or $value.mode -cne $mode -or $value.passed -ne $true -or
            $value.failures -ne 0 -or $value.checks -le 0 -or $value.controlledOwnedRoots -ne $true -or
            $value.gameCodeExecuted -ne $false -or $value.officialMetadataVerified -ne $false -or
            $value.actualPublisherReadyVerified -ne $false -or $value.hardwareCetVerified -ne $false){throw 'Native reference fixture scope/result differs.'}
        $modes.Add($value);Json $value (Join-Path $output ($mode+'-result.json'))
        Write-Output "PASS owned references $mode ($($value.checks) checks)."
    }
    foreach($row in $rows){if((Hash $row.path) -cne $row.sha256){throw 'An exact reference fixture source changed.'}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{
    $result=[ordered]@{schemaVersion=1;kind='owned-tween-source-references-native-summary';passed=$manifest.passed;
        independentProcesses=$modes.Count;checks=($modes|Measure-Object checks -Sum).Sum;modes=@($modes);
        sourceArchive=$manifest.sourceArchive;sourceHashesUnchanged=$manifest.sourceHashesUnchanged;failure=$manifest.failure;
        gameExecuted=$false;productionHelperExecuted=$false;officialMetadataVerified=$false;actualPublisherReadyVerified=$false}
    $resultPath=Join-Path $output 'fixture-result.json';Json $result $resultPath
    $manifest.result=@{path=$resultPath;sha256=Hash $resultPath};Json $manifest (Join-Path $output 'fixture-manifest.json')
    foreach($lease in $locks){$lease.Dispose()}
}
