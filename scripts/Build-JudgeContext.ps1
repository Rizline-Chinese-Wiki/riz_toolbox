#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [string]$DobbyPath,
    [switch]$SkipFixture,
    [string]$Python = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'native\RizToolbox.JudgeContext'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $sourceRoot 'out' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
if (-not (Test-Path -LiteralPath $vcvarsPath -PathType Leaf)) { throw "Missing existing VS x64 environment: $vcvarsPath" }
foreach ($path in @($sourceRoot, $nativeOutput, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
if(Test-Path -LiteralPath $nativeOutput){throw 'Preserve earlier native evidence; use a fresh output directory.'}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
function Hash([string]$File){(Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()}
$sourceLocks=[Collections.Generic.List[IDisposable]]::new()
$buildEvidence=[ordered]@{schemaVersion=1;kind='judge-context-operation-native-build';passed=$false;buildExitCode=$null;
    inputHashesUnchanged=$false;sources=@();files=@();sourceArchive=$null;expectedExports=@();actualExportValidation=$null;
    publicationVersion=7;operationCount=26;expectedWrites=64;tweenSourceCount=22;fixtureExecuted=$false;gameExecuted=$false;installed=$false;failure=$null;
    playbackAttestationVersion=1;playbackAttestationHeaderBytes=120;playbackAttestationRangeBytes=184;playbackAttestationOriginalCount=28;helperExportCount=88;
    playbackAttestationExports=@('RizFirstInstall_GetPlaybackAttestationV1','RizFirstInstall_ReadPlaybackAttestedRangeV1','RizFirstInstall_ReadPlaybackAttestedSnapshotV1')}
try{
    $declared=@(& rg --files $sourceRoot -g '*.cpp' -g '*.h' -g '*.asm' -g '*.inl' -g '!**/out/**' -g '!**/obj/**' -g '!**/bin/**')
    if($LASTEXITCODE){throw 'Native source inventory failed.'}
    $declared=@($declared+@($PSCommandPath,(Join-Path $PSScriptRoot 'verify-judge-context-exports.py'))|Sort-Object -Unique)
    foreach($path in $declared){
        $absolute=[IO.Path]::GetFullPath($path)
        for($cursor=$absolute;$cursor;$cursor=Split-Path -Parent $cursor){
            if((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Native source paths cannot cross filesystem links.'}
        }
        $sourceLocks.Add([IO.File]::Open($absolute,'Open','Read','Read'))
        $buildEvidence.sources+=@{path=$absolute;relativePath=[IO.Path]::GetRelativePath($repoRoot,$absolute).Replace('\','/');sha256=Hash $absolute;bytes=(Get-Item -LiteralPath $absolute).Length}
    }
    $archive=Join-Path $nativeOutput 'judge-context-sources.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{foreach($row in $buildEvidence.sources){$entry=$zip.CreateEntry($row.relativePath,[IO.Compression.CompressionLevel]::Optimal)
        $inputFile=[IO.File]::OpenRead($row.path);$entryStream=$entry.Open();try{$inputFile.CopyTo($entryStream)}finally{$inputFile.Dispose();$entryStream.Dispose()}}}finally{$zip.Dispose()}
    $buildEvidence.sourceArchive=@{path=$archive;sha256=Hash $archive}

$dll = Join-Path $nativeOutput 'RizToolbox.JudgeContext.dll'
$fixture = Join-Path $nativeOutput 'JudgeContext.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-native.cmd'
$commands = @(
    '@echo off',
    'chcp 65001 >nul',
    "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\JudgeEntry.obj`" `"$sourceRoot\JudgeEntry.asm`"", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\MidEntry.obj`" `"$sourceRoot\MidEntry.asm`"", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\ListenerEntry.obj`" `"$sourceRoot\ListenerEntry.asm`"", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\TweenSourceEntry.obj`" `"$sourceRoot\TweenSourceEntry.asm`"", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\TweenSingleEventEntry.obj`" `"$sourceRoot\TweenSingleEventEntry.asm`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\TweenSingleEventContext.obj`" /Fd`"$nativeOutput\TweenSingleEventContext.compiler.pdb`" `"$sourceRoot\TweenSingleEventContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\InitializationContext.obj`" /Fd`"$nativeOutput\InitializationContext.compiler.pdb`" `"$sourceRoot\InitializationContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\CoroutineContext.obj`" /Fd`"$nativeOutput\CoroutineContext.compiler.pdb`" `"$sourceRoot\CoroutineContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\OperationContext.obj`" /Fd`"$nativeOutput\OperationContext.compiler.pdb`" `"$sourceRoot\OperationContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\TweenAdmission.obj`" /Fd`"$nativeOutput\TweenAdmission.compiler.pdb`" `"$sourceRoot\TweenAdmission.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\TweenRetirementContext.obj`" /Fd`"$nativeOutput\TweenRetirementContext.compiler.pdb`" `"$sourceRoot\TweenRetirementContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /DRIZ_TWEEN_SOURCE_CONTEXT_BUILD /Fo`"$nativeOutput\TweenSourceContext.obj`" /Fd`"$nativeOutput\TweenSourceContext.compiler.pdb`" `"$sourceRoot\TweenSourceContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /DRIZ_TWEEN_SOURCE_REFERENCES_BUILD /Fo`"$nativeOutput\TweenSourceReferences.obj`" /Fd`"$nativeOutput\TweenSourceReferences.compiler.pdb`" `"$sourceRoot\TweenSourceReferences.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\TweenSourcePublication.obj`" /Fd`"$nativeOutput\TweenSourcePublication.compiler.pdb`" `"$sourceRoot\TweenSourcePublication.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\EngineNodeContext.obj`" /Fd`"$nativeOutput\EngineNodeContext.compiler.pdb`" `"$sourceRoot\EngineNodeContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\VoidContext.obj`" /Fd`"$nativeOutput\VoidContext.compiler.pdb`" `"$sourceRoot\VoidContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\MidContext.obj`" /Fd`"$nativeOutput\MidContext.compiler.pdb`" `"$sourceRoot\MidContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\BootstrapVoidContext.obj`" /Fd`"$nativeOutput\BootstrapVoidContext.compiler.pdb`" `"$sourceRoot\BootstrapVoidContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\ListenerContext.obj`" /Fd`"$nativeOutput\ListenerContext.compiler.pdb`" `"$sourceRoot\ListenerContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\RuntimeInvokeContext.obj`" /Fd`"$nativeOutput\RuntimeInvokeContext.compiler.pdb`" `"$sourceRoot\RuntimeInvokeContext.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\FirstInstall.obj`" /Fd`"$nativeOutput\FirstInstall.compiler.pdb`" `"$sourceRoot\FirstInstall.cpp`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$nativeOutput\JudgeContext.obj`" /Fd`"$nativeOutput\JudgeContext.compiler.pdb`" `"$sourceRoot\JudgeContext.cpp`" `"$nativeOutput\JudgeEntry.obj`" `"$nativeOutput\MidEntry.obj`" `"$nativeOutput\ListenerEntry.obj`" `"$nativeOutput\ListenerContext.obj`" `"$nativeOutput\InitializationContext.obj`" `"$nativeOutput\CoroutineContext.obj`" `"$nativeOutput\OperationContext.obj`" `"$nativeOutput\TweenAdmission.obj`" `"$nativeOutput\TweenRetirementContext.obj`" `"$nativeOutput\TweenSourceContext.obj`" `"$nativeOutput\TweenSourceReferences.obj`" `"$nativeOutput\TweenSourcePublication.obj`" `"$nativeOutput\TweenSourceEntry.obj`" `"$nativeOutput\TweenSingleEventEntry.obj`" `"$nativeOutput\TweenSingleEventContext.obj`" `"$nativeOutput\EngineNodeContext.obj`" `"$nativeOutput\VoidContext.obj`" `"$nativeOutput\MidContext.obj`" `"$nativeOutput\BootstrapVoidContext.obj`" `"$nativeOutput\RuntimeInvokeContext.obj`" `"$nativeOutput\FirstInstall.obj`" bcrypt.lib /link /OUT:`"$dll`" /IMPLIB:`"$nativeOutput\RizToolbox.JudgeContext.lib`" /PDB:`"$nativeOutput\RizToolbox.JudgeContext.pdb`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\FixtureCaller.obj`" `"$sourceRoot\fixture\FixtureCaller.asm`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\Fixture.obj`" /Fd`"$nativeOutput\Fixture.compiler.pdb`" `"$sourceRoot\fixture\Fixture.cpp`" `"$nativeOutput\FixtureCaller.obj`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\JudgeContext.Fixture.pdb`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$dll`" > `"$nativeOutput\exports.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$dll`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
$buildEvidence.buildExitCode=$LASTEXITCODE
if ($LASTEXITCODE -ne 0) { throw "Native C++/MASM build failed: $LASTEXITCODE" }
$expectedExports = @(
    'RizEngineNodeContext_GetAbiVersion', 'RizEngineNodeContext_GetLimit', 'RizEngineNodeContext_GetFrameSize', 'RizEngineNodeContext_GetCompletionSize',
    'RizEngineNodeContext_GetEntry', 'RizEngineNodeContext_Configure', 'RizEngineNodeContext_Attach', 'RizEngineNodeContext_GetInfo',
    'RizOperationBootstrap_Configure', 'RizOperationBootstrap_Attach', 'RizOperationBootstrap_GetInfo',
    'RizOperationContext_Configure', 'RizOperationContext_GetEntry', 'RizOperationContext_GetFrameSize',
    'RizOperationContext_GetLimit', 'RizOperationContext_GetStats',
    'RizCoroutineContext_Configure', 'RizCoroutineContext_GetEntry', 'RizCoroutineContext_GetFrameSize',
    'RizCoroutineContext_GetLimit', 'RizCoroutineContext_GetStats', 'RizCoroutineContext_ReadConfigurationV1',
    'RizInitializationContext_Configure', 'RizInitializationContext_GetConfigSize',
    'RizInitializationContext_GetEntry', 'RizInitializationContext_GetStats',
    'RizJudgeContext_Configure', 'RizJudgeContext_GetEntry', 'RizJudgeContext_GetFrameSize',
    'RizJudgeContext_GetStats', 'RizJudgeContext_SetEnabled', 'RizJudgeContext_ReadConfigurationV1',
    'RizVoidContext_Configure', 'RizVoidContext_GetEntry', 'RizVoidContext_GetFrameSize',
    'RizVoidContext_GetLimit', 'RizVoidContext_GetStats',
    'RizVoidContext_Close', 'RizVoidContext_Verify', 'RizVoidContext_Release', 'RizVoidContext_GetGateStats',
    'RizEventMidContext_Configure', 'RizEventMidContext_GetEntry', 'RizEventMidContext_GetFrameSize',
    'RizEventMidContext_GetLimit', 'RizEventMidContext_GetStats',
    'RizVoidBootstrap_Configure', 'RizVoidBootstrap_Attach', 'RizVoidBootstrap_GetInfo',
    'RizRuntimeInvokeContext_Configure', 'RizRuntimeInvokeContext_Attach', 'RizRuntimeInvokeContext_GetInfo',
    'RizFirstInstall_BeforeIl2CppInit', 'RizFirstInstall_GetInfoV7',
    'RizFirstInstall_GetPlaybackAttestationV1', 'RizFirstInstall_ReadPlaybackAttestedRangeV1',
    'RizFirstInstall_ReadPlaybackAttestedSnapshotV1',
    'RizFirstInstall_GetTweenSourceInfoV1', 'RizFirstInstall_BindTweenSourceMetadata',
    'RizFirstInstall_AttachTweenSourceReferencesV1', 'RizTweenSourceReferences_GetInfo', 'RizTweenSourceReferences_ReadCurrentNotice',
    'RizTweenSourceContext_GetEntry', 'RizTweenSourceContext_GetInfo', 'RizTweenSourceContext_ReadRecord',
    'RizTweenAdmission_Configure', 'RizTweenAdmission_Claim', 'RizTweenAdmission_Read', 'RizTweenAdmission_Seal',
    'RizTweenAdmission_Unregister', 'RizTweenAdmission_GetInfo',
    'RizTweenRetirement_Acquire', 'RizTweenRetirement_Verify', 'RizTweenRetirement_VerifyReferenceRoots',
    'RizTweenRetirement_UnregisterBatch', 'RizTweenRetirement_Release', 'RizTweenRetirement_GetInfo',
    'RizListenerContext_GetPreEntry', 'RizListenerContext_GetPostEntry',
    'RizListenerContext_Configure', 'RizListenerContext_Attach', 'RizListenerContext_GetInfo'
    'RizTweenSingleEventContext_GetMidEntry', 'RizTweenSingleEventContext_GetPreEntry', 'RizTweenSingleEventContext_GetPostEntry',
    'RizTweenSingleEventContext_Attach', 'RizTweenSingleEventContext_GetInfo', 'RizTweenSingleEventContext_ReadCurrent'
)
$buildEvidence.expectedExports=$expectedExports
$exportText = Get-Content -LiteralPath (Join-Path $nativeOutput 'exports.txt') -Raw
$actualExports = @([regex]::Matches($exportText,
    '(?m)^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+([A-Za-z_][A-Za-z0-9_@?$]*)(?:\s|$)') |
    ForEach-Object { $_.Groups[1].Value })
if ($actualExports.Count -ne $expectedExports.Count -or
    @(Compare-Object -ReferenceObject $expectedExports -DifferenceObject $actualExports -CaseSensitive).Count -ne 0) {
    throw "Unexpected JudgeContext export set. Expected: $($expectedExports -join ', '); actual: $($actualExports -join ', ')"
}
$expectedPath=Join-Path $nativeOutput 'expected-exports.json';[IO.File]::WriteAllText($expectedPath,($expectedExports|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$exportProof=Join-Path $nativeOutput 'actual-export-validation.json'
& $Python -B (Join-Path $PSScriptRoot 'verify-judge-context-exports.py') --image $dll --expected-json $expectedPath --output-json $exportProof
if($LASTEXITCODE -ne 0){throw 'Independent actual native PE export validation failed.'}
$buildEvidence.actualExportValidation=@{path=$exportProof;sha256=Hash $exportProof}
if (-not $SkipFixture) {
    $buildEvidence.fixtureExecuted=$true
    & $fixture $dll 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'fixture-result.json')
    if ($LASTEXITCODE -ne 0) { throw "Native ABI fixture failed: $LASTEXITCODE" }
    if ($DobbyPath) {
        $pinnedDobby = [IO.Path]::GetFullPath($DobbyPath)
        if ((Get-FileHash -LiteralPath $pinnedDobby -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
            throw 'UnsupportedBuild: the two-phase publication fixture requires the inspected Dobby image.'
        }
        & $fixture $dll $pinnedDobby 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'publication-fixture-result.json')
        if ($LASTEXITCODE -ne 0) { throw "Real Dobby publication fixture failed: $LASTEXITCODE" }
    }
}
$buildEvidence.files=@(Get-ChildItem -LiteralPath $nativeOutput -File | Where-Object Extension -in '.dll','.exe','.obj' | ForEach-Object {
    @{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}
})
foreach($row in $buildEvidence.sources){if((Hash $row.path) -cne $row.sha256){throw 'A native declared source changed during its build.'}}
$buildEvidence.inputHashesUnchanged=$true;$buildEvidence.passed=$true
}catch{$buildEvidence.failure=$_.Exception.Message;throw}
finally{
    foreach($held in $sourceLocks){$held.Dispose()}
    [IO.File]::WriteAllText((Join-Path $nativeOutput 'native-manifest.json'),($buildEvidence|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
}
Write-Output "Native output: $nativeOutput"
