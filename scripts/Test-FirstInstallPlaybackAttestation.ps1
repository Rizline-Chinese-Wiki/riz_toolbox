#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$GameAssemblyPath,[Parameter(Mandatory)][string]$DobbyPath,
    [string]$VcVars = '',
    [string[]]$Modes=@('normal','prewrite-foreign','patch','original','unwind','outside','continuation'))
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve prior evidence; use a fresh output directory.'}
foreach($path in @($output,$HelperDirectory,$GameAssemblyPath,$DobbyPath,$VcVars)){
    if(-not [IO.Path]::IsPathFullyQualified($path) -or $path -match '[\r\n"%&|<>^]'){throw 'Explicit bounded absolute paths are required.'}}
New-Item -ItemType Directory -Path $output | Out-Null
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$helper=Join-Path ([IO.Path]::GetFullPath($HelperDirectory)) 'RizToolbox.JudgeContext.dll'
$source=Join-Path $root 'native/RizToolbox.JudgeContext/fixture/FirstInstallPlaybackAttestationFixture.cpp'
$evidence=[ordered]@{schemaVersion=1;kind='first-install-private-preprepare-attestation';passed=$false;buildExitCode=$null;failure=$null;
    headerBytes=120;rangeBytes=184;originalCount=28;writeCount=64;gameCodeExecuted=$false;originalInitInvoked=$false;dllMainInvoked=$false;
    installed=$false;productionFirstWriteAdmission=$false;sources=@();inputs=@();modes=@()}
try{
    if((Hash $GameAssemblyPath) -cne '853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79' -or
        (Hash $DobbyPath) -cne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'){throw 'Only exact inspected input images are supported.'}
    $game=Join-Path $output 'GameAssembly.dll';Copy-Item -LiteralPath $GameAssemblyPath -Destination $game
    foreach($path in @($source,$PSCommandPath,(Join-Path $root 'native/RizToolbox.JudgeContext/FirstInstallPlaybackAttestation.h'))){$evidence.sources+=@{path=$path;sha256=Hash $path}}
    foreach($path in @($helper,$game,$DobbyPath)){$evidence.inputs+=@{path=$path;sha256=Hash $path}}
    $fixture=Join-Path $output 'FirstInstallPlaybackAttestation.Fixture.exe'
    $commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi `"$source`" /Fo`"$output/fixture.obj`" /Fd`"$output/compiler.pdb`" /Fe`"$fixture`" /link /PDB:`"$output/fixture.pdb`"",'if errorlevel 1 exit /b 1')
    $batch=Join-Path $output 'build.cmd';[IO.File]::WriteAllLines($batch,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $batch 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $evidence.buildExitCode=$LASTEXITCODE;if($LASTEXITCODE){throw "Attestation fixture build failed: $LASTEXITCODE"}
    foreach($mode in $Modes){
        if($mode -notin @('normal','prewrite-foreign','patch','original','unwind','outside','continuation')){throw 'Unknown finite fixture mode.'}
        $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($argument in @($helper,$DobbyPath,$game,$mode)){$start.ArgumentList.Add($argument)}
        $process=[Diagnostics.Process]::new();$process.StartInfo=$start;if(-not $process.Start()){throw 'Could not start the owned mapped fixture.'}
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(60000)){$process.Kill($true);$process.WaitForExit()}
        $exit=$process.ExitCode;$outText=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult();$process.Dispose()
        [IO.File]::WriteAllText((Join-Path $output "$mode.stdout.log"),$outText,[Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $output "$mode.stderr.log"),$errorText,[Text.UTF8Encoding]::new($false))
        $lines=@($outText -split '\r?\n' | Where-Object{$_.StartsWith('{')})
        $row=[ordered]@{mode=$mode;actualExitCode=$exit;result=$null};if($lines.Count -eq 1){$row.result=$lines[0]|ConvertFrom-Json};$evidence.modes+=@($row)
        if($exit -ne 0 -or $null -eq $row.result -or -not $row.result.passed){throw "Actual attestation $mode failed (exit $exit): $errorText"}
        Write-Output "$mode exit=$exit checks=$($row.result.checks)"
    }
    foreach($row in $evidence.inputs+$evidence.sources){if((Hash $row.path) -cne $row.sha256){throw 'A frozen fixture input changed.'}}
    $evidence.passed=$true
}catch{$evidence.failure=$_.Exception.Message;throw}
finally{[IO.File]::WriteAllText((Join-Path $output 'test-result.json'),($evidence|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
