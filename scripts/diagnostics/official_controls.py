"""Verify the four official control steps and the existing post-Resume run evidence."""
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path
import math
import re
import official_resume as stock


@dataclass(frozen=True)
class OfficialRun:
    watch_output: Path
    host: str
    probe: str
    run: str
    chart: str

    @property
    def before_resume(self):
        return self.watch_output / "4-before-resume.json"

    @property
    def resume_receipt(self):
        return self.watch_output / "4-resume.log"


MAILBOX = stock.PROBE_PREFIX + "MAILBOX RESULT "
SAMPLE = stock.PROBE_PREFIX + "SAMPLE "
PAUSED = re.compile(
    r"Observed lifecycle Paused: session=(?P<session>[^; ]+); revision=(?P<revision>\d+); "
    r"sceneGeneration=(?P<scene>\d+); phase=Paused; run=(?P<run>[^; ]+); evidence=.+"
)
# The SDK's value tolerance covers the Single media conversion at the 100-second stock target.
VALUE_TOLERANCE = .00001


@dataclass(frozen=True)
class Control:
    action: str
    sdk_action: int
    value: float | None = None
    unit: int = 0


CONTROLS = (
    Control("pause", 0),
    Control("set-speed", 4, .75, 1),
    Control("seek", 5, 100, 2),
    Control("resume", 1),
)


@dataclass
class Step:
    number: int
    control: Control
    commands_consumed: int | None = None
    generation: int | None = None
    before: datetime | None = None
    completed: datetime | None = None
    nonce: str | None = None
    stage: str | None = None
    result: dict | None = None
    mailbox_sequence: int | None = None
    confirmed_value: float | None = None
    dispatch_verified: bool = False
    observed_paused_sequence: int | None = None
    missing: list = field(default_factory=list)

    def paths(self, output):
        name = self.control.action
        return output / f"{self.number}-before-{name}.json", output / f"{self.number}-{name}.log"

    def summary(self):
        return {
            "number": self.number, "action": self.control.action,
            "status": "verified" if self.dispatch_verified and not self.missing else "partial",
            "commandsConsumed": self.commands_consumed, "controllerGeneration": self.generation,
            "beforeUtc": self.before.isoformat() if self.before else None,
            "completedUtc": self.completed.isoformat() if self.completed else None,
            "nonce": self.nonce, "mailboxSequence": self.mailbox_sequence,
            "observedPausedSequence": self.observed_paused_sequence,
            "sdkStage": self.stage, "sdkSuccess": self.result.get("success") if self.result else None,
            "expectedValue": self.control.value, "expectedUnit": self.control.unit,
            "confirmedValue": self.confirmed_value,
            "confirmedUnit": self.control.unit if self.confirmed_value is not None else None,
            "missing": self.missing,
        }


def exact_integer(value, expected):
    return type(value) is int and value == expected


def near(value, expected):
    return stock.finite_number(value) and math.isclose(value, expected, rel_tol=0, abs_tol=VALUE_TOLERANCE)


def camel_keys(value):
    # The probe serializes the same SDK result to storage in camel case and to the host log in Pascal case.
    if isinstance(value, dict):
        return {key[:1].lower() + key[1:]: camel_keys(item) for key, item in value.items()}
    if isinstance(value, list):
        return [camel_keys(item) for item in value]
    return value


def load_step(step, args):
    before_path, receipt_path = step.paths(args.watch_output)
    before = stock.load(before_path, before_path.name)
    state = stock.obj(before.get("state"))
    generation = state.get("controllerGeneration")
    count = before.get("commandsConsumed")
    expects_pause = step.control.action != "pause"
    stock.require(
        exact_integer(before.get("schemaVersion"), 1) and before.get("sessionId") == args.probe
        and before.get("active") is True and stock.integer(count, 0)
        and state.get("observedRunId") == args.run and stock.integer(generation)
        and state.get("controllerAvailable") is True and state.get("levelOver") is False
        and state.get("paused") is expects_pause and (expects_pause or state.get("playing") is True),
        f"{before_path.name} does not bind the expected probe, run, controller, command count and phase.",
    )
    step.commands_consumed, step.generation = count, generation
    step.before = stock.timestamp(before.get("updatedUtc"))

    receipt = stock.load(receipt_path, receipt_path.name)
    nonce = receipt.get("nonce")
    result = receipt.get("result")
    stock.require(
        exact_integer(receipt.get("schemaVersion"), 1) and receipt.get("sessionId") == args.probe
        and isinstance(nonce, str) and re.fullmatch(r"[0-9a-f]{32}", nonce)
        and receipt.get("action") == step.control.action
        and receipt.get("stage") in ("validation", "acquire", "execute", "exception")
        and isinstance(result, dict) and type(result.get("success")) is bool,
        f"{receipt_path.name} does not identify the expected probe command and result.",
    )
    completed = stock.timestamp(receipt.get("completedUtc"))
    stock.require(step.before < completed, f"{receipt_path.name} does not follow its pre-command status.")
    step.nonce, step.stage, step.result, step.completed = nonce, receipt["stage"], result, completed


def check_dispatch(step, run):
    result = step.result
    if step.stage != "execute" or result.get("success") is not True or result.get("error") is not None:
        error = stock.obj(result.get("error"))
        detail = error.get("message")
        raise stock.MissingEvidence(
            f"The {step.control.action} command did not execute successfully"
            + (f". {detail}" if isinstance(detail, str) and detail else ".")
        )
    value = stock.obj(result.get("value"))
    modification = stock.obj(value.get("modification"))
    request = stock.obj(modification.get("request"))
    control = step.control
    stock.require(
        modification.get("owner") == stock.PROBE_ID and modification.get("dispatched") is True
        and all(
            entry.get("observedRunId") == run
            and exact_integer(entry.get("controllerGeneration"), step.generation)
            and exact_integer(entry.get("action"), control.sdk_action)
            for entry in (request, modification)
        )
        and exact_integer(request.get("unit"), control.unit)
        and (
            request.get("value") is None if control.value is None
            else stock.finite_number(request.get("value")) and request["value"] == control.value
        ),
        "The SDK receipt does not dispatch the expected request to the sampled run and controller.",
    )
    if control.value is None:
        return
    confirmed = modification.get("confirmedValue")
    stock.require(
        value.get("immediatePostconditionObserved") is True
        and exact_integer(modification.get("transactionState"), 1)
        and exact_integer(modification.get("confirmedUnit"), control.unit)
        and near(confirmed, control.value),
        "The SDK did not confirm the requested value and unit by immediate readback.",
    )
    step.confirmed_value = confirmed


def collect_timeline(args, steps, report, rows):
    starts = [
        row for row in rows if row["eventCode"] == "game.run.started"
        and stock.obj(row.get("context")).get("runId") == args.run
    ]
    stock.require(len(starts) == 1, "The host timeline does not identify one start for the requested run.")
    start = starts[0]
    identity = stock.bound_event(start, stock.START, args.run)
    stock.require(identity and identity["chart"] == args.chart, "The host start has a different chart identity.")
    start_at = stock.timestamp(start.get("timestamp"))
    result_sequence = (report["postResume"] or {}).get("resultSequence")
    timeline = [
        row for row in rows if row["sequence"] > start["sequence"]
        and (result_sequence is None or row["sequence"] < result_sequence)
    ]
    commands = [
        (row, stock.payload(row, MAILBOX, "A probe mailbox event"))
        for row in timeline if stock.plugin_event(row, "MAILBOX RESULT ")
    ]

    previous = None
    for step in steps:
        if step.before is not None and step.before < start_at:
            step.missing.append("The pre-command status precedes the host's observed chart start.")
        if previous and previous.before is not None and step.before is not None:
            if step.commands_consumed != previous.commands_consumed + 1:
                step.missing.append("The command count does not follow the preceding step.")
            if previous.completed is not None and step.before < previous.completed:
                step.missing.append("The pre-command status precedes the preceding command's completion.")
        if step.nonce is not None:
            try:
                candidates = [(row, data) for row, data in commands if data.get("nonce") == step.nonce]
                stock.require(len(candidates) == 1, "The host does not contain one mailbox result for the receipt nonce.")
                row, data = candidates[0]
                stock.require(
                    data.get("SessionId") == args.probe and data.get("action") == step.control.action
                    and data.get("stage") == step.stage
                    and camel_keys(data.get("result")) == step.result
                    and stock.timestamp(row.get("timestamp")) >= step.completed,
                    "The host mailbox result disagrees with the receipt or its completion time.",
                )
                step.mailbox_sequence = row["sequence"]
                check_dispatch(step, args.run)
                step.dispatch_verified = True
            except stock.MissingEvidence as issue:
                step.missing.append(str(issue))
        previous = step

    pause, speed, seek, resume = steps
    numbers = [step.mailbox_sequence for step in steps]
    if all(number is not None for number in numbers):
        observed = [
            row["sequence"] for row, _ in commands if row["sequence"] <= resume.mailbox_sequence
            and stock.timestamp(row.get("timestamp")) >= pause.before
        ]
        if observed != numbers:
            report["missing"].append("The host does not show exactly the four recorded commands in order.")
    if pause.mailbox_sequence is not None and resume.mailbox_sequence is not None:
        for row in timeline:
            sequence = row["sequence"]
            if not pause.mailbox_sequence < sequence < resume.mailbox_sequence or row["eventCode"] != "game.lifecycle":
                continue
            resumed = stock.bound_event(row, stock.RESUMED, args.run)
            if resumed and resumed["session"] == identity["session"] and resumed["scene"] == identity["scene"]:
                report["missing"].append("The host observed an additional Resume between the recorded Pause and Resume commands.")
                break
            paused = stock.bound_event(row, PAUSED, args.run)
            if paused and paused["session"] == identity["session"] and paused["scene"] == identity["scene"]:
                if speed.mailbox_sequence is not None and sequence < speed.mailbox_sequence:
                    pause.observed_paused_sequence = sequence

    check_clock(args, timeline, identity, speed, seek, resume, report)


def clock_tick(sample, identity, args, generation):
    tick = stock.sample_tick(sample, identity["session"], args, int(identity["scene"]), generation)
    if (sample.get("sameRunAndGeneration") is True and sample.get("clockRunId") == args.run
            and sample.get("clockGeneration") == generation):
        return tick
    return None


def check_clock(args, timeline, identity, speed, seek, resume, report):
    post = report["postResume"] or {}
    pair_ticks = post.get("mediaAdvancingTicks") or []
    resumed_sequence = post.get("resumedSequence")
    resumed_at = next(
        (stock.timestamp(row.get("timestamp")) for row in timeline if row["sequence"] == resumed_sequence),
        None,
    )
    pair = {}
    for row in timeline:
        if not stock.plugin_event(row, "SAMPLE "):
            continue
        sequence = row["sequence"]
        sample = stock.payload(row, SAMPLE, "A probe clock sample")
        before_resume = resume.mailbox_sequence is not None and sequence < resume.mailbox_sequence
        after_speed = speed.mailbox_sequence is not None and sequence > speed.mailbox_sequence
        after_seek = seek.mailbox_sequence is not None and sequence > seek.mailbox_sequence
        if before_resume and after_speed and sample.get("paused") is True:
            paused_step = seek if after_seek else speed
            if clock_tick(sample, identity, args, paused_step.generation) is not None:
                check_paused_readback(sample, sequence, paused_step, speed, report)

        if resumed_at is None or sequence <= resumed_sequence or not pair_ticks:
            continue
        tick = clock_tick(sample, identity, args, resume.generation)
        if (
            tick in pair_ticks and tick not in pair and sample.get("playing") is True
            and sample.get("paused") is False and sample.get("playbackAvailable") is True
            and sample.get("playbackPhase") == "Playing" and stock.integer(sample.get("clockRevision"))
            and stock.finite_number(sample.get("mediaSeconds"))
            and stock.timestamp(row.get("timestamp")) >= resumed_at
        ):
            rate, progress = sample.get("playbackRate"), sample.get("progressSeconds")
            pair[tick] = {
                "sequence": sequence, "hostTick": tick,
                "playbackRate": rate if stock.finite_number(rate) else None,
                "progressSeconds": progress if stock.finite_number(progress) else None,
                "mediaSeconds": sample["mediaSeconds"],
            }
            if (seek.confirmed_value is not None and stock.finite_number(progress)
                    and progress < seek.confirmed_value - VALUE_TOLERANCE):
                report["missing"].append(f"The post-Resume sample at host sequence {sequence} is earlier than the confirmed Seek position.")
    report["postResumeRateReadbacks"] = [pair[tick] for tick in pair_ticks if tick in pair]
    if pair_ticks and (
        len(pair) != len(pair_ticks) or any(not near(sample["playbackRate"], speed.control.value) for sample in pair.values())
    ):
        report["missing"].append("The advancing post-Resume media ticks do not both confirm playback at 0.75×.")


def check_paused_readback(sample, sequence, step, speed, report):
    playback = sample.get("playbackAvailable") is True and sample.get("playbackPhase") == "Paused"
    rate = sample.get("playbackRate")
    if playback and speed.confirmed_value is not None:
        report["pausedRateReadbacks"].append({
            "sequence": sequence, "playbackRate": rate if stock.finite_number(rate) else None
        })
        if not near(rate, speed.confirmed_value):
            report["missing"].append(f"The paused playback sample at host sequence {sequence} disagrees with the confirmed rate.")
    progress = sample.get("progressSeconds")
    if step.control.action != "seek" or step.confirmed_value is None or not stock.finite_number(progress):
        return
    report["pausedSeekReadbacks"].append({"sequence": sequence, "progressSeconds": progress})
    if not near(progress, step.confirmed_value):
        report["missing"].append(f"The paused Seek sample at host sequence {sequence} disagrees with its confirmed position.")
    offset, media = sample.get("appliedOffsetSeconds"), sample.get("mediaSeconds")
    if playback and stock.finite_number(offset) and not near(media, progress + offset):
        report["missing"].append(f"The paused Seek media sample at host sequence {sequence} disagrees with progress plus applied offset.")

def control_report(args, rows):
    steps = [Step(number, control) for number, control in enumerate(CONTROLS, 1)]
    report = {
        "status": "partial", "host": args.host, "probe": args.probe, "run": args.run, "chart": args.chart,
        "steps": [], "postResume": None, "pausedSeekReadbacks": [], "pausedRateReadbacks": [],
        "postResumeRateReadbacks": [], "missing": [],
    }
    try:
        report["postResume"] = stock.resume_report(args, rows)
        report["missing"].extend(report["postResume"]["missing"])
    except stock.MissingEvidence as issue:
        report["missing"].append(str(issue))
    for step in steps:
        try:
            load_step(step, args)
        except stock.MissingEvidence as issue:
            step.missing.append(str(issue))
    try:
        collect_timeline(args, steps, report, rows)
    except stock.MissingEvidence as issue:
        report["missing"].append(str(issue))
    report["steps"] = [step.summary() for step in steps]
    report["missing"].extend(f"Step {step.number} {step.control.action}. {issue}" for step in steps for issue in step.missing)
    if not report["missing"]:
        report["status"] = "verified"
    return report
