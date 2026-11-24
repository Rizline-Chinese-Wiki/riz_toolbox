"""Inspect a selected exported session; no live-game reads or actions."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("directory", nargs="?", default="session-first-result")
parser.add_argument("--output", default="first-result-proof.json")
args = parser.parse_args()
root = Path(__file__).resolve().parent
selected = root / args.directory
events = [json.loads(line) for path in sorted(selected.glob("events-*.jsonl"))
          for line in path.read_text(encoding="utf-8-sig").splitlines()]
assert events and len({e["sessionId"] for e in events}) == 1
assert all(a["sequence"] < b["sequence"] for a,b in zip(events,events[1:]))

def values(marker, owner=None):
    result = []
    for e in events:
        text = e.get("message","")
        if marker+"{" not in text or owner and e.get("context",{}).get("pluginId") != owner: continue
        result.append(dict(sequence=e["sequence"],timestamp=e["timestamp"],
                           value=json.loads(text.split(marker,1)[1])))
    return result

status = values("ModuleProbe JUDGEMENT STATUS ")
samples = values("ModuleProbe JUDGEMENT ")
life = values("ObservationProbe LIFECYCLE ", "example.events")
ends = [x for x in life if x["value"]["Kind"] == 5]
starts = [x for x in life if x["value"]["Kind"] == 2]
assert status and samples and ends
latest = status[-1]["value"]
assert latest["Success"]
latest = latest["Value"]
assert latest["Installed"] and latest["UnassociatedCalls"] == 0
assert latest["Observations"] == sum(x["CapturedCalls"] for x in latest["Sources"])
assert not any("Judgement identity coverage withdrawn" in e.get("message","") for e in events)
by_run = {}
for e in samples:
    dto = e["value"]
    assert dto["OriginalReturned"] and dto["ChartAssociation"] == 1 and dto["ChartNote"]
    assert dto["CountersBefore"] and dto["CountersAfter"] and dto["DeltaUnit"] == 1
    identity = dto["RuntimeNote"]
    assert dto["NoteId"] == f'{identity["SessionId"]}/{identity["ObservedRunId"]}/{identity["Sequence"]}'
    by_run.setdefault(identity["ObservedRunId"],[]).append(e)
for run, observations in by_run.items():
    assert any(x["value"]["Snapshot"]["RunId"] == run for x in starts)

hooks = []
for e in events:
    text = e.get("message","")
    if "ObservationProbe HOOK COUNTS " in text and e.get("context",{}).get("pluginId") == "example.events":
        entries = json.loads(text.split("; ",1)[1])
        judge = next(x for x in entries if x["Id"] == "game.normal.judge")
        hooks.append(dict(sequence=e["sequence"],value=judge))
assert hooks and hooks[-1]["value"]["NativeCalls"] == hooks[-1]["value"]["OriginalCalls"] == latest["Observations"]
end_values = [x["value"]["Result"] for x in ends if x["value"].get("Result")]
for result in end_values:
    assert result["AssociatedWithObservedStart"]
proof = dict(
    diagnosticSession=events[0]["sessionId"],eventCount=len(events),
    files=[dict(path=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted(selected.iterdir()) if p.is_file()],
    packageManifestSha256=hashlib.sha256((root/"package-manifest.json").read_bytes()).hexdigest(),
    statusSnapshots=status,latestHookCounts=hooks[-1],starts=starts,ends=ends,
    sdkSampleCount=len(samples),sdkSamples=samples,
    sourceCoverage=[x for x in latest["Sources"] if x["CapturedCalls"]],
    uncoveredSources=[x["Id"] for x in latest["Sources"] if not x["CapturedCalls"]],
    observedResults=end_values,
    aggregateResultJudgements=sum(x["Bad"]+x["Miss"]+x["Perfect"]+x["MaxPerfect"] for x in end_values),
    errors=[dict(sequence=e["sequence"],message=e["message"],exception=e.get("exception")) for e in events if e.get("level") in {"Error","Critical"}],
    limitations=[
        "Only bounded first samples per source are logged; aggregate counts do not prove every DTO field independently.",
        "Caller-source labels are pinned code-path identifiers, not an assertion that challenge mode or an input flag caused a specific judgement.",
        "RawGameUnits remains unchanged; measured differences and sentinel values are distinguished but runtime seconds calibration is incomplete.",
        "No final native GC/root-release receipt, injected fault, all-source gameplay, complete control or late-result-origin matrix is inferred.",
        "Runtime evidence belongs only to this package manifest and this selected diagnostic session."
    ])
if args.directory == "session-first-result":
    assert len(events) == 667 and latest["Observations"] == 479 and len(proof["sourceCoverage"]) == 6
    assert len(ends) == 1 and end_values[0]["Crashed"] and end_values[0]["EndReason"] == 2
    assert proof["aggregateResultJudgements"] == 479
    matched = {}
    for item in samples:
        dto = item["value"]
        matched.setdefault(dto["NoteId"],set()).add(dto["SourceId"])
    assert any({"tap-hold.timeout-normal","tap-hold.timeout-challenge"} <= ids for ids in matched.values())
elif args.directory == "session-after-auto-retry-skip":
    previous = [json.loads(line) for p in sorted((root/"session-first-result").glob("events-*.jsonl"))
                for line in p.read_text(encoding="utf-8-sig").splitlines()]
    assert events[:len(previous)] == previous
    assert len(events) == 1985 and latest["Observations"] == 675 and len(proof["sourceCoverage"]) == 8
    assert len(starts) == 3 and len(ends) == 3
    assert len({x["value"]["Snapshot"]["RunId"] for x in starts}) == 3
    assert [x["value"]["EndReason"] for x in ends] == [2,4,3]
    paired = {}
    for item in samples:
        dto = item["value"]
        paired.setdefault(dto["NoteId"],set()).add(dto["SourceId"])
    pairs = [note for note,sources in paired.items() if {"auto.normal","hold.end"} <= sources]
    assert len(pairs) == 2
    assert len(proof["errors"]) == 1 and "VisualResourceService.ScopeGate.Dispose" in proof["errors"][0]["exception"]
    proof["unchangedPrefixEvents"] = len(previous)
    proof["holdHeadTailMatchedIds"] = pairs
    proof["actionAttribution"] = "Agent enabled the game's visible observation mode and started the second run. Retry and Skip occurred later in native logs but were not issued by the agent's recorded computer-use calls."
(root/args.output).write_text(json.dumps(proof,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
print(json.dumps(dict(events=len(events),observations=latest["Observations"],
    unassociated=latest["UnassociatedCalls"],sources=len(proof["sourceCoverage"]),
    sdkSamples=len(samples),runsEnded=len(ends),errors=len(proof["errors"])),ensure_ascii=False))
