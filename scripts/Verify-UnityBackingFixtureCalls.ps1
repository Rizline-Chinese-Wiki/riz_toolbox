#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$BuildDirectory,
      [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$image = Join-Path $BuildDirectory 'RizToolbox.PlaybackStockFixture.dll'
$imageHash = (Get-FileHash -LiteralPath $image -Algorithm SHA256).Hash.ToLowerInvariant()
if ($imageHash -cne '22c3527d3a0be10ec1b5681f70855e0690c6396b7738533bbd62e841bae35c87') { throw 'Only the independently frozen Unity backing r2 image is described here.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$continuation = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..')) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($continuation,[StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $output)) { throw 'Proof output must be a fresh directory in this continuation.' }
$null = New-Item -ItemType Directory -Path $output
[byte[]]$pe = [IO.File]::ReadAllBytes($image)
function U16([int]$at) { [BitConverter]::ToUInt16($pe,$at) }
function U32([int]$at) { [BitConverter]::ToUInt32($pe,$at) }
$nt = [int](U32 0x3c)
if ((U32 $nt) -ne 0x4550 -or (U16 ($nt+4)) -ne 0x8664) { throw 'Expected an x64 PE image.' }
$sectionCount = U16 ($nt+6)
$optionalBytes = U16 ($nt+20)
$opt = $nt+24
if ((U16 $opt) -ne 0x20b) { throw 'Expected PE32+.' }
$imageBase = [BitConverter]::ToUInt64($pe,$opt+24)
$headers = U32 ($opt+60)
$sections = @()
for ($i=0; $i -lt $sectionCount; ++$i) {
    $at = $opt+$optionalBytes+40*$i
    $sections += [pscustomobject]@{rva=(U32 ($at+12));virtualBytes=(U32 ($at+8));rawBytes=(U32 ($at+16));raw=(U32 ($at+20))}
}
function Raw([uint32]$rva,[uint32]$bytes) {
    if ($rva -lt $headers -and [uint64]$rva+$bytes -le $headers) { return [int]$rva }
    foreach ($section in $sections) {
        if ($rva -ge $section.rva -and [uint64]$rva+$bytes -le [uint64]$section.rva+$section.rawBytes) {
            return [int]($section.raw+$rva-$section.rva)
        }
    }
    throw ('RVA is not wholly backed by file bytes: 0x{0:x}, {1}' -f $rva,$bytes)
}
function Slice([uint32]$rva,[uint32]$count) {
    $at = Raw $rva $count
    [byte[]]$result = [byte[]]::new($count)
    [Array]::Copy($pe,$at,$result,0,$count)
    return ,$result
}
function Hex([byte[]]$bytes) { [Convert]::ToHexString($bytes).ToLowerInvariant() }
function Sha([byte[]]$bytes) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
$pdataRva = U32 ($opt+112+3*8)
$pdataBytes = U32 ($opt+116+3*8)
if (($pdataBytes % 12) -ne 0) { throw 'Malformed exception directory length.' }
$runtimeFunctions = @()
for ($i=0; $i -lt $pdataBytes; $i+=12) {
    $at = Raw ($pdataRva+$i) 12
    $runtimeFunctions += [pscustomobject]@{begin=(U32 $at);end=(U32 ($at+4));unwind=(U32 ($at+8));record=($pdataRva+$i)}
}
function RuntimeFunction([uint32]$address,[int]$depth=0) {
    if ($depth -gt 8) { throw 'An unwind chain exceeds the fixture bound.' }
    $rows = @($runtimeFunctions | Where-Object { $address -ge $_.begin -and $address -lt $_.end })
    if ($rows.Count -ne 1) { throw ('No unique runtime function contains 0x{0:x}' -f $address) }
    $rf = $rows[0]
    [byte[]]$head = Slice $rf.unwind 4
    $flags = $head[0] -shr 3
    $codesLength = 4 + (($head[2]+1) -band -2)*2
    $length = $codesLength
    if ($flags -band 4) { $length += 12 } elseif ($flags -band 3) { $length += 4 }
    [byte[]]$recordBytes = Slice $rf.record 12
    [byte[]]$unwindBytes = Slice $rf.unwind $length
    [byte[]]$bodyBytes = Slice $rf.begin ($rf.end-$rf.begin)
    $result = [ordered]@{beginRva=$rf.begin;endRva=$rf.end;unwindRva=$rf.unwind;recordRva=$rf.record;
        rawRecord=(Hex $recordBytes);rawRecordSha256=(Sha $recordBytes);unwindHeaderAndCodes=(Hex $unwindBytes);
        unwindHeaderAndCodesSha256=(Sha $unwindBytes);bodyBytes=$bodyBytes.Length;bodySha256=(Sha $bodyBytes);chainedParent=$null;primaryBeginRva=$rf.begin}
    if ($flags -band 4) {
        $chainAt = Raw ($rf.unwind+$codesLength) 12
        $chain = RuntimeFunction (U32 $chainAt) ($depth+1)
        if ($chain.endRva -ne (U32 ($chainAt+4)) -or $chain.unwindRva -ne (U32 ($chainAt+8))) { throw 'The chained unwind record differs from its actual runtime-function entry.' }
        $result.chainedParent = $chain
        $result.primaryBeginRva = $chain.primaryBeginRva
    }
    return $result
}
$exports = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $BuildDirectory 'RizToolbox.PlaybackStockFixture.exports.txt')) {
    if ($line -match '^\s+\d+\s+[0-9A-F]+\s+([0-9A-F]{8})\s+(\S+)(?:\s*=.*)?$') { $exports[$Matches[2]] = [Convert]::ToUInt32($Matches[1],16) }
}
$disassembledCalls = @()
foreach ($line in Get-Content -LiteralPath (Join-Path $BuildDirectory 'RizToolbox.PlaybackStockFixture.disasm.txt')) {
    if ($line -match '^\s+([0-9A-F]{16}):\s+E8\s+(?:[0-9A-F]{2}\s+){4}\s*call\s+(.+)$') {
        $disassembledCalls += [pscustomobject]@{rva=([Convert]::ToUInt64($Matches[1],16)-$imageBase);symbol=$Matches[2]}
    }
}
function DirectCall([uint32]$callRva) {
    [byte[]]$bytes = Slice $callRva 5
    if ($bytes[0] -ne 0xe8) { throw ('Not an E8 direct CALL at 0x{0:x}' -f $callRva) }
    [ordered]@{callRva=$callRva;returnRva=($callRva+5);targetRva=([long]$callRva+5+[BitConverter]::ToInt32($bytes,1));
        bytes=(Hex $bytes);sha256=(Sha $bytes)}
}
$parents = @{}
foreach ($parent in @(
    @{name='StockPause';body='StockPauseBody';length=160},
    @{name='StockProgressPlay';body='StockProgressPlayBody';length=272},
    @{name='StockReplay';body='StockReplayBody';length=560},
    @{name='StockResume';body='StockResumeBody';length=720})) {
    $entry = [uint32]$exports[$parent.name]
    $calls = @($disassembledCalls | Where-Object { $_.rva -ge $entry -and $_.rva -lt $entry+$parent.length -and $_.symbol -eq $parent.body })
    if ($calls.Count -lt 1) { throw ('The actual wrapper has no direct CPP body CALL: '+$parent.name) }
    $call = DirectCall $calls[0].rva
    $wrapperCalls = @($calls | ForEach-Object { DirectCall $_.rva })
    if (@($wrapperCalls | Where-Object { $_.targetRva -ne $call.targetRva }).Count -ne 0) { throw 'A wrapper body CALL targets a different CPP function.' }
    $bodyRf = RuntimeFunction $call.targetRva
    if ($bodyRf.beginRva -ne $call.targetRva) { throw 'The CPP body entry does not begin its own actual runtime function.' }
    $parents[$parent.name] = [ordered]@{wrapper=$parent.name;wrapperRva=$entry;wrapperRuntimeFunction=(RuntimeFunction $entry);wrapperBodyCalls=$wrapperCalls;cppBody=$parent.body;cppBodyRuntimeFunction=$bodyRf}
}
$parents.StockStartMoveNext = [ordered]@{wrapper='StockStartMoveNext';wrapperRva=$exports.StockStartMoveNext;
    wrapperRuntimeFunction=(RuntimeFunction $exports.StockStartMoveNext);cppBody=$null;cppBodyRuntimeFunction=(RuntimeFunction $exports.StockStartMoveNext)}
$pins = @(
    @('StockPause','StockPauseLeaf',0x1c37), @('StockProgressPlay','StockPauseLeaf',0x1ee8),
    @('StockReplay','StockAudioStop',0x1f74), @('StockReplay','StockSetPitch',0x1fa2), @('StockReplay','StockPlayScheduled',0x1fb0),
    @('StockResume','StockAudioStop',0x2036), @('StockResume','StockSetTime',0x204e), @('StockResume','StockSetPitch',0x2076),
    @('StockResume','StockPlayScheduled',0x2084), @('StockStartMoveNext','StockSetClip',0x41a7))
$proofs = @()
foreach ($pin in $pins) {
    $call = DirectCall ($pin[2]-5)
    $parent = $parents[$pin[0]]
    $rf = RuntimeFunction $call.callRva
    if ($call.targetRva -ne $exports[$pin[1]] -or $rf.primaryBeginRva -ne $parent.cppBodyRuntimeFunction.primaryBeginRva -or $call.returnRva -ge $rf.endRva) {
        throw ('The pinned leaf CALL is not inside the actual parent body RF or does not target the exact leaf export: '+$pin[0]+' -> '+$pin[1])
    }
    $proofs += [ordered]@{parent=$pin[0];leaf=$pin[1];exactLeafRva=$exports[$pin[1]];call=$call;parentRuntimeFunction=$rf;passed=$true}
}
$result = [ordered]@{kind='owned-unity-backing-r2-static-parent-leaf-call-proof';passed=$true;image=$image;sha256=$imageHash;count=$proofs.Count;
    wrappers=@($parents.Values);calls=$proofs;gameExecuted=$false;imageLoaded=$false;installed=$false;productionAdmission=$false;
    recipeSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()}
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $output 'Verify-UnityBackingFixtureCalls.ps1')
Copy-Item -LiteralPath (Join-Path $BuildDirectory 'RizToolbox.PlaybackStockFixture.exports.txt'),(Join-Path $BuildDirectory 'RizToolbox.PlaybackStockFixture.unwindinfo.txt') -Destination $output
[IO.File]::WriteAllText((Join-Path $output 'call-proof.json'),($result | ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))
Write-Output ('Validated {0} exact direct parent/leaf CALLs from raw PE bytes and actual CPP body runtime functions.' -f $proofs.Count)
