"""Check an official-chart run using existing host diagnostics and Resume evidence."""
from datetime import datetime
import json
import math
import re


PROBE_ID = "probe.r14.official-control"
PROBE_PREFIX = f"[{PROBE_ID}] N2StockPlayback "
JUDGEMENT_PROBE = "probe.r24.stock-judgement"
JUDGEMENT_PREFIX = f"[{JUDGEMENT_PROBE}] StockJudgementProbe POST_RESUME "
START = re.compile(
    r"Observed game event game\.run\.started: RunStarted \{ "
    r"RunId = (?P<run>[^, ]+), ChartId = (?P<chart>[^, ]+), "
    r"SessionId = (?P<session>[^, ]+), Revision = (?P<revision>\d+), "
    r"SceneGeneration = (?P<scene>\d+) \}"
)
RESUMED = re.compile(
    r"Observed lifecycle Resumed: session=(?P<session>[^; ]+); revision=(?P<revision>\d+); "
    r"sceneGeneration=(?P<scene>\d+); phase=Playing; run=(?P<run>[^; ]+); evidence=.+"
)
FINISHED = re.compile(
    r"Observed game event game\.run\.finished: RunFinished \{ "
    r"RunId = (?P<run>[^, ]+), ChartId = (?P<chart>[^, ]+), .+, "
    r"SessionId = (?P<session>[^, ]+), Revision = (?P<revision>\d+), "
    r"SceneGeneration = (?P<scene>\d+), AssociatedWithObservedStart = True, "
    r"EndReason = (?P<reason>Completed|Failed|Skipped) \}"
)


class MissingEvidence(ValueError):
    pass


def require(condition, reason):
    if not condition:
        raise MissingEvidence(reason)


def obj(value):
    return value if isinstance(value, dict) else {}


def integer(value, minimum=1):
    return type(value) is int and value >= minimum


def finite_number(value):
    if type(value) not in (int, float):
        return False
    try:
        return math.isfinite(value)
    except OverflowError:
        return False


def timestamp(value):
    if isinstance(value, str):
        try:
            parsed = datetime.fromisoformat(value)
            if parsed.utcoffset() is not None:
                return parsed
        except ValueError:
            pass
    raise MissingEvidence("An evidence timestamp is missing or has no valid UTC offset.")


def record(raw, label):
    try:
        value = json.loads(raw)
    except ValueError:
        raise MissingEvidence(f"{label} is not complete JSON.") from None
    require(isinstance(value, dict), f"{label} is not a JSON object.")
    return value


def load(path, label):
    try:
        return record(path.read_bytes(), label)
    except OSError:
        raise MissingEvidence(f"Cannot read {label}.") from None


def host_rows(path, host):
    if path.is_dir():
        session_file = path / "session.json"
        if session_file.exists():
            require(load(session_file, "diagnostic session.json").get("sessionId") == host,
                    "Diagnostic session.json does not identify the requested host.")
        files = sorted(path.glob("events-*.jsonl"))
    else:
        files = [path]
    require(files, "No diagnostic event files were found.")
    rows = {}
    for file in files:
        try:
            data = file.read_bytes()
        except OSError:
            raise MissingEvidence("Cannot read a diagnostic event file.") from None
        lines = data.splitlines()
        # A live writer may still be appending the last record. An archived JSONL
        # file without a final newline can still supply its last complete object.
        for index, line in enumerate(lines):
            try:
                row = record(line, "A diagnostic event")
            except MissingEvidence:
                if index == len(lines) - 1 and not data.endswith(b"\n"):
                    break
                raise
            require(type(row.get("schemaVersion")) is int and row["schemaVersion"] == 1
                    and row.get("sessionId") == host and integer(row.get("sequence"))
                    and isinstance(row.get("eventCode"), str) and isinstance(row.get("message"), str),
                    "A diagnostic event has an unsupported format or a different host session.")
            sequence = row["sequence"]
            require(sequence not in rows or rows[sequence] == row,
                    "Conflicting events claim the same host sequence.")
            rows[sequence] = row
    require(rows, "No complete diagnostic events were found.")
    return [rows[sequence] for sequence in sorted(rows)]


def plugin_event(row, label):
    return (row["eventCode"] == "log" and obj(row.get("context")).get("pluginId") == PROBE_ID
            and row["message"].startswith(PROBE_PREFIX + label))


def payload(row, prefix, label):
    return record(row["message"][len(prefix):], label)


def resume_dispatch(result, run, generation, pascal=False):
    def field(value, name):
        key = name[:1].upper() + name[1:] if pascal else name
        return obj(value).get(key)

    modification = field(field(result, "value"), "modification")
    request = field(modification, "request")
    return (field(result, "success") is True and field(result, "error") is None
            and field(modification, "owner") == PROBE_ID and field(modification, "dispatched") is True
            and field(modification, "observedRunId") == run
            and field(request, "observedRunId") == run
            and integer(field(modification, "controllerGeneration"))
            and field(modification, "controllerGeneration") == generation
            and integer(field(request, "controllerGeneration"))
            and field(request, "controllerGeneration") == generation
            and integer(field(modification, "action")) and field(modification, "action") == 1
            and integer(field(request, "action")) and field(request, "action") == 1)


def bind_receipt(args):
    before = load(args.before_resume, "the pre-Resume control status")
    state = obj(before.get("state"))
    generation = state.get("controllerGeneration")
    require(before.get("schemaVersion") == 1 and before.get("sessionId") == args.probe
            and before.get("active") is True and state.get("observedRunId") == args.run
            and integer(generation) and state.get("controllerAvailable") is True
            and state.get("paused") is True,
            "The pre-Resume status does not bind the requested probe, run and paused controller.")
    receipt = load(args.resume_receipt, "the Resume receipt")
    nonce = receipt.get("nonce")
    require(receipt.get("schemaVersion") == 1 and receipt.get("sessionId") == args.probe
            and isinstance(nonce, str) and re.fullmatch(r"[0-9a-f]{32}", nonce)
            and receipt.get("action") == "resume" and receipt.get("stage") == "execute"
            and resume_dispatch(receipt.get("result"), args.run, generation),
            "The receipt does not record a matching successful Resume dispatch.")
    before_at = timestamp(before.get("updatedUtc"))
    completed_at = timestamp(receipt.get("completedUtc"))
    require(before_at < completed_at, "The pre-Resume status does not precede the completed receipt.")
    return generation, nonce, before_at, completed_at


def bound_event(row, pattern, run):
    if (obj(row.get("context")).get("runId") != run
            or obj(row.get("context")).get("pluginId") is not None):
        return None
    match = pattern.fullmatch(row["message"])
    if (match and match["run"] == run
            and obj(row.get("context")).get("sceneGeneration") == int(match["scene"])):
        return match
    return None


def sample_tick(sample, game_session, args, scene, generation):
    tick = sample.get("hostTick")
    if (sample.get("sessionId") == game_session and sample.get("runId") == args.run
            and sample.get("chartId") == args.chart and sample.get("sceneGeneration") == scene
            and sample.get("controlGeneration") == generation and integer(tick)):
        return tick
    return None


def collect(args, report, rows):
    generation, nonce, before_at, completed_at = bind_receipt(args)
    report["controllerGeneration"] = generation
    starts = [row for row in rows if row["eventCode"] == "game.run.started"
              and obj(row.get("context")).get("runId") == args.run]
    require(len(starts) == 1, "The host timeline does not contain one start for the requested run.")
    start = starts[0]
    identity = bound_event(start, START, args.run)
    require(identity and identity["chart"] == args.chart and timestamp(start.get("timestamp")) <= before_at,
            "The host's observed run start does not bind this chart before Resume.")
    game_session = identity["session"]
    scene = int(identity["scene"])
    report["startSequence"] = start["sequence"]

    command_prefix = PROBE_PREFIX + "MAILBOX RESULT "
    sample_prefix = PROBE_PREFIX + "SAMPLE "
    previous_tick = 0
    expected_sequence = start["sequence"] + 1
    anchor = None
    resumed = None
    previous_media = None
    judged_notes = set()
    partial = report["missing"]
    for row in rows:
        sequence = row["sequence"]
        if sequence <= start["sequence"]:
            continue
        if sequence != expected_sequence:
            partial.append("The host timeline has a sequence gap before the run evidence ends.")
            break
        expected_sequence += 1
        code = row["eventCode"]
        if plugin_event(row, "probe active for new runs.") or code == "game.run.started":
            partial.append("A probe activation or another run interrupted the evidence window.")
            break
        if plugin_event(row, "MAILBOX RESULT "):
            command = payload(row, command_prefix, "A probe mailbox event")
            if anchor:
                partial.append("Another mailbox command interrupted the evidence after Resume.")
                break
            if command.get("nonce") == nonce:
                require(command.get("SessionId") == args.probe and command.get("action") == "resume"
                        and command.get("stage") == "execute"
                        and resume_dispatch(command.get("result"), args.run, generation, pascal=True)
                        and timestamp(row.get("timestamp")) >= completed_at,
                        "The requested nonce does not bind this Resume to the host timeline.")
                anchor = row
                report["resumeCommandSequence"] = sequence
            elif command.get("SessionId") != args.probe:
                partial.append("A different probe session appears in the run timeline.")
                break
        if code == "game.run.finished" and obj(row.get("context")).get("runId") == args.run:
            end = bound_event(row, FINISHED, args.run)
            if (resumed and end and end["chart"] == args.chart and end["session"] == game_session
                    and int(end["revision"]) > resumed_revision
                    and timestamp(row.get("timestamp")) >= resumed_at):
                report["resultSequence"] = sequence
                report["endReason"] = end["reason"]
            break
        if not anchor:
            if plugin_event(row, "SAMPLE "):
                sample = payload(row, sample_prefix, "A probe clock sample")
                tick = sample_tick(sample, game_session, args, scene, generation)
                if tick is not None:
                    previous_tick = max(previous_tick, tick)
            continue
        if code == "game.lifecycle" and obj(row.get("context")).get("runId") == args.run:
            if row["message"].startswith("Observed lifecycle Paused:"):
                partial.append("A pause interrupted the evidence after the matching Resume dispatch.")
                break
            if row["message"].startswith("Observed lifecycle Resumed:"):
                change = bound_event(row, RESUMED, args.run)
                if (resumed or not change or change["session"] != game_session
                        or int(change["scene"]) != scene or int(change["revision"]) <= int(identity["revision"])
                        or timestamp(row.get("timestamp")) < timestamp(anchor.get("timestamp"))):
                    partial.append("A playing transition could not be associated with this Resume.")
                    break
                resumed = row
                resumed_at = timestamp(row.get("timestamp"))
                resumed_revision = int(change["revision"])
                report["resumedSequence"] = sequence
        if plugin_event(row, "SAMPLE "):
            sample = payload(row, sample_prefix, "A probe clock sample")
            tick = sample_tick(sample, game_session, args, scene, generation)
            if tick is None:
                continue
            if not resumed:
                previous_tick = max(previous_tick, tick)
                continue
            if (tick <= previous_tick or sample.get("playing") is not True or sample.get("paused") is not False
                    or timestamp(row.get("timestamp")) < resumed_at):
                continue
            previous_tick = tick
            report["postResumePlayingTicks"] += 1
            media = sample.get("mediaSeconds")
            clock_revision = sample.get("clockRevision")
            if (sample.get("sameRunAndGeneration") is not True or sample.get("clockRunId") != args.run
                    or sample.get("clockGeneration") != generation or not integer(clock_revision)
                    or sample.get("playbackAvailable") is not True or sample.get("playbackPhase") != "Playing"
                    or not finite_number(media)):
                continue
            report["postResumeMediaTicks"] += 1
            if (report["mediaAdvancingTicks"] is None and previous_media
                    and previous_media[2] == clock_revision and media > previous_media[1]):
                report["mediaAdvancingTicks"] = [previous_media[0], tick]
            previous_media = (tick, media, clock_revision)
        judgement_prefix = "Judgement identity sample: "
        if (resumed and code == "log" and obj(row.get("context")).get("pluginId") == JUDGEMENT_PROBE
                and row["message"].startswith(JUDGEMENT_PREFIX)):
            sample = payload(row, JUDGEMENT_PREFIX, "The post-Resume judgement probe sample")
            if (sample.get("originalReturned") is True and integer(sample.get("invocation"))
                    and integer(sample.get("runtimeNoteSequence")) and sample.get("runId") == args.run
                    and sample.get("sessionId") == game_session and sample.get("chartId") == args.chart
                    and sample.get("sceneGeneration") == scene and sample.get("resumedRevision") == resumed_revision
                    and integer(sample.get("lifecycleRevisionAtEntry"), resumed_revision)
                    and isinstance(sample.get("sourceId"), str) and sample["sourceId"]
                    and timestamp(row.get("timestamp")) >= resumed_at):
                judged_notes.add(sample["runtimeNoteSequence"])
                report["postResumeJudgements"] = len(judged_notes)
        if (resumed and code == "log" and obj(row.get("context")).get("pluginId") is None
                and row["message"].startswith(judgement_prefix)):
            judgement = payload(row, judgement_prefix, "A host judgement sample")
            note = obj(judgement.get("RuntimeNote"))
            if (judgement.get("OriginalReturned") is True and integer(judgement.get("Invocation"))
                    and judgement.get("SceneGeneration") == scene
                    and integer(judgement.get("LifecycleRevisionAtEntry"), resumed_revision)
                    and note.get("SessionId") == game_session and note.get("ObservedRunId") == args.run
                    and integer(note.get("Sequence")) and timestamp(row.get("timestamp")) >= resumed_at):
                judged_notes.add(note["Sequence"])
                report["postResumeJudgements"] = len(judged_notes)

    if not anchor:
        partial.append("The probe's matching Resume dispatch is missing from the requested host.")
    if not resumed:
        partial.append("The host did not record a matching transition to Playing after the receipt.")
    if report["mediaAdvancingTicks"] is None:
        partial.append("No increasing media-time pair from distinct post-Resume playing ticks was recorded.")
    if not judged_notes:
        partial.append("No same-run native judgement return was recorded after Resume.")
    if report["resultSequence"] is None:
        partial.append("No actual result associated with this run and chart was recorded after Resume.")


def resume_report(args, rows):
    report = {
        "status": "partial", "host": args.host, "probe": args.probe, "run": args.run, "chart": args.chart,
        "controllerGeneration": None, "startSequence": None, "resumeCommandSequence": None, "resumedSequence": None,
        "postResumePlayingTicks": 0, "postResumeMediaTicks": 0, "mediaAdvancingTicks": None,
        "postResumeJudgements": 0, "resultSequence": None, "endReason": None, "missing": [],
    }
    try:
        collect(args, report, rows)
    except MissingEvidence as issue:
        report["missing"].append(str(issue))
    if not report["missing"]:
        report["status"] = "verified"
    return report
