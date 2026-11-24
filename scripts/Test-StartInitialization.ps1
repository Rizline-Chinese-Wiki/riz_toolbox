#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$SingleReturnHelperPath,
    [Parameter(Mandatory)][string]$SourceHelperPath,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$Modes=@('handoff','handoff-graph','handoff-crossrun','raw','seh','hold','late','interference','tracker','tracker-run-loss','tracker-stop-inflight','router-stop-inflight','router-run-loss','router-normal','router-busy')
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')

$ErrorActionPreference='Stop'
function Fact([string]$Path) {
    $item=Get-Item -LiteralPath $Path
    [ordered]@{path=$item.FullName;sha256=(Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$item.Length}
}
function Json($Value,[string]$Path) {
    [IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 30),[Text.UTF8Encoding]::new($false))
}
foreach($path in @($ManagedAssemblyPath,$FixturePath,$SingleReturnHelperPath,$SourceHelperPath,$DobbyPath,$OutputDirectory)) {
    if(-not [IO.Path]::IsPathFullyQualified($path)){throw 'Absolute input and fresh output paths are required.'}
}
if(Test-Path -LiteralPath $OutputDirectory){throw 'Preserve prior executions; select a fresh output directory.'}
$known=@('handoff','handoff-graph','handoff-crossrun','raw','seh','hold','late','interference','tracker','tracker-run-loss','tracker-stop-inflight','router-stop-inflight','router-run-loss','router-normal','router-busy','router-one-cycle',
    'first-normal','first-prepopulated','first-busy','first-hold','first-stop-inflight','first-run-loss','first-throw','first-clip-mismatch',
    'ctor-first-normal','ctor-first-prepopulated','ctor-first-busy','ctor-first-hold','ctor-first-stop-inflight','ctor-first-run-loss','ctor-first-throw','ctor-first-clip-mismatch',
    'ctor-root-drain','ctor-root-free-fault','ctor-sdk-worker','ctor-sdk-crossrun','ctor-sdk-normal','ctor-sdk-same','ctor-sdk-null','ctor-sdk-distinct','ctor-sdk-aba','ctor-sdk-null-mi','ctor-sdk-wrong-mi','ctor-sdk-throw-before','ctor-sdk-throw-after','ctor-sdk-runtime','ctor-sdk-foreign',
    'ctor-source-first','ctor-source-same','ctor-source-cross','ctor-sdk-reentry','ctor-sdk-inflight')
if($Modes.Count -eq 0 -or @($Modes|Sort-Object -Unique).Count -ne $Modes.Count -or @($Modes|Where-Object{$_ -notin $known}).Count) {
    throw 'Select unique known actual initialization modes.'
}
# Each frozen image has its own exact CALL profile. A recipe rebuild or
# a batch mixing constructor/first/predecessor fixtures cannot borrow one.
$families=@($Modes|ForEach-Object{
    if($_.StartsWith('ctor-',[StringComparison]::Ordinal)){'constructor'}
    elseif($_.StartsWith('first-',[StringComparison]::Ordinal)){'first'}else{'predecessor'}
}|Sort-Object -Unique)
if($families.Count -ne 1){throw 'Run constructor, first-capture and predecessor fixture families in separate batches.'}
$expectedFixture=switch($families[0]){
    'constructor' {'3b57292bec39c3e1a2b208fa5c63dd7382e55fb4aa1f8921c743d79dab85d693'}
    'first' {'7ef26e1c274cc1cf826fb8e7a9283e913224a6c9f9c6a35cb76103b943c19d7f'}
    'predecessor' {'9fe8cde32ae1ce1fe9426efafb46a758d4e37593007a64747a042fdcab51bd6e'}
}
if((Fact $FixturePath).sha256 -ne $expectedFixture){throw 'This selected mode batch requires its separately frozen exact owned fixture.'}
$managedDirectory=Split-Path -Parent $ManagedAssemblyPath
$files=@(Get-ChildItem -LiteralPath $managedDirectory -File|Where-Object{$_.Extension -in @('.dll','.pdb','.json')})
$inputs=@(@($files.FullName)+@($FixturePath,$SingleReturnHelperPath,$SourceHelperPath,$DobbyPath,$PSCommandPath)|Sort-Object -Unique|ForEach-Object{Fact $_})
$null=New-Item -ItemType Directory -Path $OutputDirectory
$result=[ordered]@{kind='actual-owned-source-v4-start-initialization';passed=$false;rows=@();inputs=$inputs;processes=0;checks=0;
    fixtureFamily=$families[0];playerConstructorIncluded=($families[0] -eq 'constructor');gameExecuted=$false;productionAdmission=$false;fullBCompleted=$false;failure=$null}
$manifestPath=Join-Path $OutputDirectory 'test-result.json'
try {
    foreach($mode in $Modes) {
        $stdout=Join-Path $OutputDirectory ($mode+'.stdout.log')
        $stderr=Join-Path $OutputDirectory ($mode+'.stderr.log')
        $start=[Diagnostics.ProcessStartInfo]::new()
        $start.FileName=(Get-RizDotnetPath)
        $start.UseShellExecute=$false;$start.CreateNoWindow=$true
        $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
        foreach($arg in @($ManagedAssemblyPath,$mode,$FixturePath,$SingleReturnHelperPath,$SourceHelperPath,$DobbyPath)){$start.ArgumentList.Add($arg)}
        $process=[Diagnostics.Process]::Start($start)
        $row=[ordered]@{mode=$mode;pid=$process.Id;actualExitCode=$null;startedAtUtc=[DateTime]::UtcNow.ToString('o');passed=$false;checks=0}
        Json $row (Join-Path $OutputDirectory ($mode+'.launch.json'))
        try {
            $readOut=$process.StandardOutput.ReadToEndAsync();$readErr=$process.StandardError.ReadToEndAsync()
            $process.WaitForExit();$row.actualExitCode=$process.ExitCode
            [IO.File]::WriteAllText($stdout,$readOut.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
            [IO.File]::WriteAllText($stderr,$readErr.GetAwaiter().GetResult(),[Text.UTF8Encoding]::new($false))
        } finally {$process.Dispose()}
        [IO.File]::WriteAllText((Join-Path $OutputDirectory ($mode+'.exit.txt')),[string]$row.actualExitCode)
        $actual=Get-Content -LiteralPath $stdout -Tail 1|ConvertFrom-Json
        $row.stdout=Fact $stdout;$row.stderr=Fact $stderr;$row.actual=$actual
        $row.passed=$row.actualExitCode -eq 0 -and $actual.passed -eq $true -and $actual.checks -gt 0
        $row.checks=[int]$actual.checks;$result.rows+=,$row;$result.processes=$result.rows.Count
        $result.checks+= $row.checks;Json $result $manifestPath
        if(-not $row.passed){throw "Actual initialization mode failed: $mode; retain its process output."}
    }
    foreach($row in $inputs){if((Fact $row.path).sha256 -ne $row.sha256){throw 'An actual execution input changed.'}}
    $result.passed=$true
} catch {$result.failure=$_.Exception.Message;throw}
finally {Json $result $manifestPath}
Write-Output "Owned initialization: $($result.processes) processes/$($result.checks) checks; no game executed."
