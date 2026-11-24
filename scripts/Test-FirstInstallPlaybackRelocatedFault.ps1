#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string]$HelperDirectory,[Parameter(Mandatory)][string]$DobbyPath,
    [string]$VcVars = '')
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot;$native=Join-Path $root 'native/RizToolbox.JudgeContext'
$output=[IO.Path]::GetFullPath($OutputDirectory);$helper=[IO.Path]::GetFullPath($HelperDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve earlier evidence; use a fresh output directory.'}
foreach($path in @($output,$helper,$DobbyPath,$VcVars)){if(-not [IO.Path]::IsPathFullyQualified($path) -or $path -match '[\r\n"%&|<>^]'){throw 'Bounded absolute build paths are required.'}}
New-Item -ItemType Directory -Path $output | Out-Null
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
$evidence=[ordered]@{schemaVersion=1;kind='first-install-private-normalized-read-fault';passed=$false;buildExitCode=$null;actualExitCode=$null;failure=$null;
    compileTimeFixtureMacro='RIZ_FIRST_INSTALL_ATTESTATION_FIXTURE';actualHelperObjectsLinked=$true;productionOwnerIssued=$false;
    gameCodeExecuted=$false;gameImageLoaded=$false;installed=$false;sources=@();objects=@();result=$null}
try{
    if((Hash $DobbyPath) -cne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'){throw 'The inspected actual Dobby binary is required.'}
    $fixture=Join-Path $output 'FirstInstallPlaybackRelocatedFault.Fixture.exe'
    $sources=@($PSCommandPath,(Join-Path $native 'FirstInstall.cpp'),(Join-Path $native 'FirstInstallPlaybackAttestation.inl'),
        (Join-Path $native 'FirstInstallPlaybackAttestation.h'),(Join-Path $native '../../tests/native/FirstInstallPlaybackRelocatedFaultFixture.cpp'),
        (Join-Path $native 'fixture/FirstInstallPlaybackMachineCaller.asm'))
    foreach($path in $sources){$evidence.sources+=@{path=$path;sha256=Hash $path}}
    $names=@('JudgeContext','JudgeEntry','MidEntry','ListenerEntry','ListenerContext','InitializationContext','CoroutineContext','OperationContext',
        'TweenAdmission','TweenRetirementContext','TweenSourceContext','TweenSourceReferences','TweenSourcePublication','TweenSourceEntry',
        'TweenSingleEventContext','TweenSingleEventEntry','EngineNodeContext','VoidContext','MidContext','BootstrapVoidContext','RuntimeInvokeContext')
    $arguments=@($names|ForEach-Object{$path=Join-Path $helper "$_.obj";$evidence.objects+=@{path=$path;sha256=Hash $path};'"'+$path+'"'}) -join ' '
    $evidence.dobby=@{path=$DobbyPath;sha256=Hash $DobbyPath}
    $commands=@('@echo off',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /DRIZ_FIRST_INSTALL_ATTESTATION_FIXTURE /Fo`"$output/FirstInstallUnderTest.obj`" /Fd`"$output/producer.pdb`" `"$native/FirstInstall.cpp`"",'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output/Caller.obj`" `"$native/fixture/FirstInstallPlaybackMachineCaller.asm`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /Fo`"$output/Fixture.obj`" /Fd`"$output/fixture.compiler.pdb`" `"$root/tests/native/FirstInstallPlaybackRelocatedFaultFixture.cpp`" `"$output/FirstInstallUnderTest.obj`" `"$output/Caller.obj`" $arguments bcrypt.lib /link /OUT:`"$fixture`" /PDB:`"$output/fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT",'if errorlevel 1 exit /b 1')
    $batch=Join-Path $output 'build.cmd';[IO.File]::WriteAllLines($batch,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /c $batch 2>&1|Tee-Object -FilePath (Join-Path $output 'build.log')
    $evidence.buildExitCode=$LASTEXITCODE;if($LASTEXITCODE){throw "Owned machine fixture build failed: $LASTEXITCODE"}
    $start=[Diagnostics.ProcessStartInfo]::new($fixture);$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true;$start.ArgumentList.Add($DobbyPath)
    $process=[Diagnostics.Process]::new();$process.StartInfo=$start;if(-not $process.Start()){throw 'Could not start the owned fixture.'}
    $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
    if(-not $process.WaitForExit(60000)){$process.Kill($true);$process.WaitForExit()}
    $evidence.actualExitCode=$process.ExitCode;$outText=$stdout.GetAwaiter().GetResult();$errorText=$stderr.GetAwaiter().GetResult();$process.Dispose()
    [IO.File]::WriteAllText((Join-Path $output 'stdout.log'),$outText,[Text.UTF8Encoding]::new($false));[IO.File]::WriteAllText((Join-Path $output 'stderr.log'),$errorText,[Text.UTF8Encoding]::new($false))
    $lines=@($outText -split '\r?\n'|Where-Object{$_.StartsWith('{')});if($lines.Count -eq 1){$evidence.result=$lines[0]|ConvertFrom-Json}
    if($evidence.actualExitCode -ne 0 -or $null -eq $evidence.result -or -not $evidence.result.passed){throw "Actual owned machine fixture failed (exit $($evidence.actualExitCode)): $errorText"}
    foreach($row in $evidence.sources+$evidence.objects+@($evidence.dobby)){if((Hash $row.path) -cne $row.sha256){throw 'An owned fixture input changed.'}}
    $evidence.passed=$true;Write-Output $lines[0]
}catch{$evidence.failure=$_.Exception.Message;throw}
finally{[IO.File]::WriteAllText((Join-Path $output 'test-result.json'),($evidence|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))}
