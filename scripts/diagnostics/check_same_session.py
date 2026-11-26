"""Read one official run and the following LOCAL result from one exported r24 host session."""
import argparse
import json
from pathlib import Path
import re
import official_resume as stock
from official_controls import OfficialRun, control_report


OWNER = "probe.sdk15.local-result-score"
PREFIX = f"[{OWNER}] Sdk15LocalResult "
OFFICIAL = "chart.PastelLines.RekuMochizuki.0.EZ"
SONG = {"Namespace": OWNER, "Id": "one-note-local"}
LOCAL_CHART = f"riztoolbox.local/{OWNER}/one-note-local/chart/one-note"


def official_run(rows, watch, host):
    before = stock.load(watch / "1-before-pause.json", "official Pause snapshot")
    probe = before.get("sessionId")
    run = stock.obj(before.get("state")).get("observedRunId")
    stock.require(all(isinstance(x, str) and x for x in (probe, run)), "Missing official probe or run identity.")
    return control_report(OfficialRun(watch, host, probe, run, OFFICIAL), rows)


def only(items, label):
    matches = list(items)
    stock.require(len(matches) == 1, f"Expected one {label}, found {len(matches)}.")
    return matches[0]


def local_events(rows):
    for row in rows:
        if (row["eventCode"] == "log" and stock.obj(row.get("context")).get("pluginId") == OWNER
                and row["message"].startswith(PREFIX)):
            event = stock.payload(row, PREFIX, "LOCAL probe event")
            stock.require(event.get("owner") == OWNER, "LOCAL probe owner mismatch.")
            if event.get("activation") != 1:
                stock.require(event.get("kind") == "activation-refused", "Unexpected LOCAL probe activation.")
                continue
            yield row["sequence"], event


def check_local(rows, finish_sequence, official_id, finish, report):
    events = list(local_events(rows))
    terminal_sequence, terminal = only(((seq, event) for seq, event in events if event.get("kind") == "score-isolation"),
                                       "LOCAL score comparison")
    data = stock.obj(terminal.get("data"))
    activation = terminal.get("activationId")
    stock.require(isinstance(activation, str) and activation, "Missing LOCAL activation identity.")
    report["local"] = {"outcome": data.get("outcome"), "reason": data.get("reason"),
                       "sequence": terminal_sequence, "processId": data.get("processId")}

    def event_of(kind, phase=None):
        return only(((seq, stock.obj(e.get("data"))) for seq, e in events
                     if e.get("kind") == kind and e.get("activationId") == activation
                     and (phase is None or stock.obj(e.get("data")).get("phase") == phase)), kind)

    seq_requested, requested = event_of("play-requested")
    seq_receipt, receipt = event_of("play-receipt")
    stock.require(finish_sequence < seq_requested < seq_receipt < terminal_sequence,
                  "The LOCAL request must follow the official result in the same host log.")
    stock.require(receipt.get("accepted") is True and receipt.get("request") == requested == data.get("request"),
                  "The LOCAL request does not match its acceptance and terminal receipt.")
    request_id = requested.get("Id")
    source = stock.obj(requested.get("Source"))
    song_revision = requested.get("SongRevision")
    stock.require(request_id and requested.get("Song") == SONG and requested.get("ChartId") == "one-note"
                  and stock.integer(song_revision) and source.get("Scene") == "LevelList"
                  and source.get("SessionId") == finish["session"], "LOCAL selection or lifecycle session differs.")
    run = stock.obj(data.get("run"))
    seq_started, started = event_of("local-run-observed")
    stock.require(seq_requested < seq_started < terminal_sequence and started.get("run") == run
                  and started.get("Id") == request_id and started.get("SessionId") == finish["session"]
                  and isinstance(run.get("Id"), str) and run["Id"] and run["Id"] != official_id,
                  "Expected a distinct LOCAL run for this request.")
    timeline = [row for row in rows if finish_sequence < row["sequence"] <= terminal_sequence]
    stock.require(len(timeline) == terminal_sequence - finish_sequence,
                  "The host timeline has a sequence gap between the two results.")
    host_start = only((row for row in timeline if row["eventCode"] == "game.run.started"
                       and stock.obj(row.get("context")).get("pluginId") is None), "intervening host run start")
    identity = stock.bound_event(host_start, stock.START, run["Id"])
    stock.require(identity and identity["session"] == finish["session"] and identity["chart"] == LOCAL_CHART
                  and int(identity["scene"]) == run.get("SceneGeneration")
                  and int(identity["revision"]) == run.get("Revision") and seq_requested < host_start["sequence"],
                  "The host did not observe the requested LOCAL chart and run.")

    seq_read, captured = event_of("local-result-read")
    result = stock.obj(data.get("result"))
    score = stock.obj(result.get("Result"))
    stock.require(max(seq_started, seq_receipt) < seq_read < terminal_sequence and captured.get("Id") == request_id
                  and captured.get("result") == result and result.get("Song") == SONG
                  and result.get("SongRevision") == song_revision and result.get("ChartId") == "one-note"
                  and result.get("LocalSessionId") and stock.integer(result.get("RunGeneration")),
                  "The captured SDK result differs from the requested LOCAL song or generation.")
    counters = {key: score.get(key) for key in ("Score", "Perfect", "MaxPerfect", "Bad", "Miss")}
    stock.require(all(stock.integer(n, 0) for n in counters.values()), "Incomplete LOCAL score or judgement counts.")
    report["local"].update(run=run["Id"], chart="one-note", result=counters)
    seq_judge, judge = event_of("local-judgement-observed")
    evidence = stock.obj(data.get("judgement"))
    stock.require(seq_started < seq_judge < seq_read and judge.get("Id") == request_id
                  and judge.get("SessionId") == finish["session"] and judge.get("run") == run
                  and judge.get("judgement") == evidence and stock.integer(evidence.get("Invocation"))
                  and stock.integer(evidence.get("RuntimeNoteSequence"))
                  and stock.integer(evidence.get("Revision"), run["Revision"]), "Missing same-run single-note judgement.")

    seq_window, shown = event_of("return-window-observed")
    seq_return, returned = event_of("returned-level-list")
    window = stock.obj(data.get("returnWindow"))
    generation = result["RunGeneration"]
    stock.require(seq_started < seq_window < seq_return < terminal_sequence and seq_read < seq_return
                  and shown.get("Id") == returned.get("Id") == request_id and shown.get("run") == returned.get("run") == run
                  and shown.get("SessionId") == returned.get("SessionId") == finish["session"]
                  and shown.get("window") == window and window.get("RunGeneration") == generation
                  and window.get("WindowId") == f"framework.local-result/local-result-{generation}",
                  "No matching LOCAL result window and normal return were recorded.")
    pre_seq, pre = event_of("score-sampled", "before")
    post_seq, post = event_of("score-sampled", "after")
    before, after = stock.obj(data.get("before")), stock.obj(data.get("after"))
    stock.require(finish_sequence < pre_seq < seq_requested and seq_return < post_seq < terminal_sequence
                  and pre.get("Id") == post.get("Id") == request_id
                  and pre.get("sample") == before and post.get("sample") == after,
                  "The two official score samples do not bracket the LOCAL run.")
    for sample in (before, after):
        stock.require(sample.get("SessionId") == finish["session"] and sample.get("Scene") == "LevelList"
                      and stock.integer(sample.get("Count")) and isinstance(sample.get("Sha256"), str)
                      and re.fullmatch(r"[a-f0-9]{64}", sample["Sha256"]), "Invalid official score sample.")
    scenes = [int(finish["scene"]), source.get("SceneGeneration"), run.get("SceneGeneration"),
              window.get("SceneGeneration"), after.get("SceneGeneration")]
    stock.require(all(stock.integer(n) for n in scenes) and scenes[0] <= scenes[1] < scenes[2] <= scenes[3] < scenes[4]
                  and before.get("SceneGeneration") == scenes[1] and returned.get("SceneGeneration") == scenes[4]
                  and stock.integer(data.get("processId")), "The scene generations do not describe one continuous host.")
    report["local"]["officialScores"] = {phase: {key: sample[key] for key in ("Count", "Sha256")}
                                         for phase, sample in (("before", before), ("after", after))}
    stock.require(before["Count"] == after["Count"] and before["Sha256"] == after["Sha256"],
                  "Official scores changed across the LOCAL run.")
    stock.require(data.get("outcome") == "verified" and data.get("passed") is True
                  and data.get("scoreComparison") == "unchanged", "The LOCAL probe did not confirm score isolation.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--events", type=Path, required=True, help="one host diagnostics directory including session.json")
    parser.add_argument("--watch-output", type=Path, required=True, help="the four-step official run receipts")
    args = parser.parse_args()
    report = {"status": "partial", "official": None, "local": None, "missing": []}
    try:
        host = stock.load(args.events / "session.json", "diagnostic session.json").get("sessionId")
        stock.require(isinstance(host, str) and host, "Missing diagnostic session ID.")
        report["host"] = host
        rows = stock.host_rows(args.events, host)
        official = official_run(rows, args.watch_output, host)
        post = stock.obj(official.get("postResume"))
        report["official"] = {
            "run": official["run"], "chart": OFFICIAL, "controls": official.get("status"),
            "confirmed": {step["action"]: step.get("confirmedValue") for step in official.get("steps", [])
                          if step.get("status") == "verified" and step.get("action") in ("set-speed", "seek")},
            "postResumeJudgements": post.get("postResumeJudgements"),
            "resultSequence": post.get("resultSequence"), "endReason": post.get("endReason"),
        }
        if official.get("status") != "verified":
            report["missing"].extend(official.get("missing") or ["The four official controls were not verified."])
        if post.get("endReason") != "Completed":
            report["missing"].append("A normal official clear was not recorded.")
        last = only((r for r in rows if r["sequence"] == post.get("resultSequence")), "official finish anchor")
        finish = stock.bound_event(last, stock.FINISHED, official["run"])
        stock.require(last["eventCode"] == "game.run.finished" and finish and finish["chart"] == OFFICIAL,
                      "The official result does not match the host log.")
        check_local(rows, last["sequence"], official["run"], finish, report)
    except stock.MissingEvidence as error:
        report["missing"].append(str(error))
    report["missing"] = list(dict.fromkeys(report["missing"]))
    if not report["missing"]:
        report["status"] = "verified"
    print(json.dumps(report, indent=2))
    return 0 if report["status"] == "verified" else 2


if __name__ == "__main__":
    raise SystemExit(main())
