"""Verify and freeze a complete stopped private LOCAL-bundle probe session.

Example from the repository root (supply the actual completed package manifest):
  python docs/validation/runtime-local-bundle-20261022/freeze-proof.py SESSION_DIR \
    --package-manifest PACKAGE_MANIFEST --stopped --expected-phase verified

This script never launches or operates the game. --stopped records the operator's independent
process-stop confirmation: this log format has no shutdown event and cannot establish a normal exit. It verifies the
complete session, including all warnings, rather than trimming a successful-looking excerpt.
No session ID, package hash, regression count, or prior package is hardcoded.
"""
import argparse
import hashlib
import json
import math
import re
import shutil
import struct
from collections import Counter
from datetime import datetime, timezone
from pathlib import Path, PureWindowsPath


PHASES = (
    "waiting", "fixture-directory", "constructed-first", "constructed-second",
    "withdrawn-and-first-destroy-requested", "first-verified-second-destroy-requested", "verified",
)
PREFIX = "LocalNativeBundleProbe "


def check(condition, message):
    if not condition:
        raise ValueError(message)


def integer(value, label, minimum=0):
    check(type(value) is int and value >= minimum, label + " must be an integer >= " + str(minimum))
    return value


def boolean(value, expected, label):
    check(value is expected, label + " must be " + str(expected))


def fields(value, expected, *names):
    for name in names:
        boolean(value[name], expected, name)


def scalar(value, label, positive=False):
    check(type(value) in (int, float) and math.isfinite(value), label + " must be finite")
    if positive:
        check(value > 0, label + " must be positive")
    return value


def sha(path):
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def unique_object(pairs):
    result = {}
    for name, value in pairs:
        check(name not in result, "Duplicate JSON property: " + name)
        result[name] = value
    return result


def load(text):
    def invalid_constant(value):
        raise ValueError("Nonfinite JSON constant: " + value)
    return json.loads(text, object_pairs_hook=unique_object, parse_constant=invalid_constant)


def freeze_file(source, target, expected):
    if source.resolve() != target.resolve():
        if target.exists():
            check(sha(target) == expected, "An earlier frozen file differs; refusing overwrite: " + str(target))
        else:
            shutil.copy2(source, target)
    check(sha(target) == expected, "Frozen copy changed: " + str(target))


parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
parser.add_argument("session", type=Path, help="Complete original events-*.jsonl and session.json directory after confirmed process stop.")
parser.add_argument("--package-manifest", required=True, type=Path, help="Actual manifest for the tested installed package; must contain exactly 18 files.")
parser.add_argument("--game-dir", type=Path, default=Path(r"D:\SteamLibrary\steamapps\common\Rizline"))
parser.add_argument("--output-dir", type=Path, default=Path(__file__).resolve().parent)
parser.add_argument("--stopped", required=True, action="store_true", help="Operator independently confirmed process stop; does not imply normal exit.")
parser.add_argument("--expected-phase", action="append", choices=PHASES + ("not-ready",),
                    help="Expected observed phase, repeatable. Complete success still requires every canonical phase through verified.")
args = parser.parse_args()
session = args.session.resolve()
game = args.game_dir.resolve()
output = args.output_dir.resolve()
manifest = args.package_manifest.resolve()
check(session.is_dir(), "Session directory is absent.")
check(manifest.is_file(), "The actual tested package manifest is absent.")
logs = sorted(session.glob("events-*.jsonl"))
session_file = session / "session.json"
check(logs and session_file.is_file(), "Complete original events and session.json are required.")
sources = logs + [session_file]
source_hashes = {path: sha(path) for path in sources}
manifest_hash = sha(manifest)
metadata = load(session_file.read_text(encoding="utf-8-sig"))
check(metadata["sessionId"] == session.name, "Original session.json identity differs from its directory.")
events = [load(line) for path in logs
          for line in path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
check(events, "The complete event stream is empty.")
check(all(type(event["sequence"]) is int for event in events), "Event sequences must be integers.")
check([event["sequence"] for event in events] == list(range(1, len(events) + 1)),
      "Session is not continuous from sequence 1; do not supply filtered or partial logs.")
check({event["sessionId"] for event in events} == {session.name}, "Event/session identities differ.")
check(not any(event["level"] == "Error" for event in events), "Complete session contains Error events; success evidence is rejected.")
check(not any("LocalNativeBundleProbe unavailable" in event.get("message", "")
              or "LocalNativeBundleProbe cleanup is unconfirmed" in event.get("message", "") for event in events),
      "Bundle probe recorded an unavailable or unconfirmed outcome.")
rows = {name: [] for name in PHASES + ("not-ready",)}
for event in events:
    message = event.get("message", "")
    if not message.startswith(PREFIX):
        continue
    name, separator, body = message[len(PREFIX):].partition(" ")
    check(separator and name in rows, "Unknown or malformed bundle probe phase: " + message)
    check(event["level"] == "Info", "Successful probe phases must be full Info events.")
    check(len(body) <= 1800, "Probe JSON exceeds the source's 1800-character bound.")
    rows[name].append((event, load(body)))
for name in PHASES:
    check(len(rows[name]) == 1, "Expected exactly one " + name + " phase; found " + str(len(rows[name])))
for name in args.expected_phase or ["verified"]:
    check(rows[name], "Expected现场 phase was not recorded: " + name)
ordered = [rows[name][0] for name in PHASES]
check([event["sequence"] for event, _ in ordered] == sorted(event["sequence"] for event, _ in ordered),
      "Probe phase order differs from construction/withdrawal/retirement order.")
waiting_e, waiting = rows["waiting"][0]
fixture_e, fixture = rows["fixture-directory"][0]
first_e, first = rows["constructed-first"][0]
second_e, second = rows["constructed-second"][0]
withdraw_e, withdraw = rows["withdrawn-and-first-destroy-requested"][0]
retire_e, retire = rows["first-verified-second-destroy-requested"][0]
verified_e, verified = rows["verified"][0]
fields(waiting, True, "requiresObservedOrdinaryRunJudgeData")
fields(waiting, False, "controllerCreated")
check(waiting["maximumAttempts"] == 300, "Waiting attempt bound differs from the inspected probe.")
for event, value in rows["not-ready"]:
    check(waiting_e["sequence"] < event["sequence"] < fixture_e["sequence"], "not-ready must precede fixture/native construction.")
    check(1 <= integer(value["attempt"], "not-ready attempt", 1) <= 300, "Attempt exceeds its bounded capture window.")
fields(fixture, True, "originalTestData")
fields(fixture, False, "gameCatalogModified")
fixture_path = Path(fixture["path"]).resolve()
check(fixture_path.is_relative_to(game / "BepInEx" / "RizToolbox" / "probes")
      and fixture_path.name.startswith("bundle-fixtures-"), "Fixture path must remain in the private probe directory.")

created_frame = integer(first["frame"], "first construction frame")
check(integer(second["frame"], "second construction frame") == created_frame, "Both graphs must be constructed in the same recorded probe tick.")
withdraw_frame = integer(withdraw["frame"], "first destruction request frame")
retire_frame = integer(retire["frame"], "first completion/second destruction request frame")
verified_frame = integer(verified["frame"], "final completion frame")
check(created_frame < withdraw_frame < retire_frame < verified_frame,
      "GC/readback, first destruction confirmation, and second destruction confirmation need strictly later frames.")
check(withdraw["constructedFrame"] == verified["constructedFrame"] == created_frame, "Construction frame links differ.")
check(retire["firstDestructionRequestedFrame"] == verified["firstDestructionRequestedFrame"] == withdraw_frame,
      "First destruction request frame links differ.")
check(verified["secondDestructionRequestedFrame"] == retire_frame, "Second destruction request frame link differs.")
check(first["directRootRecords"] == second["directRootRecords"] == 5, "Each complete graph must retain five direct roots.")
fields(second, True, "distinctGraphs", "scoresAreSentinels")
fields(second, False, "nativeControllerCreated", "canLaunch")
source_controller = integer(second["realSourceController"], "ordinary source controller", 1)
source_judge = integer(second["realSourceJudgeData"], "ordinary source JudgeData", 1)
check(source_controller != source_judge, "Ordinary controller and JudgeData identities must differ.")


def graph(observation):
    parameters = observation["Parameters"]
    identities = [integer(observation["ParametersIdentity"], "owned InsideLevelParams", 1)]
    identities += [integer(parameters[key], key, 1) for key in ("Metadata", "CoverReference", "MusicReference", "Chart", "JudgeData")]
    identities += [integer(observation[key], key, 1) for key in ("Texture", "Sprite", "Audio")]
    check(len(set(identities)) == 9, "An owned graph aliases independent native objects.")
    fields(observation, False, "CanLaunch")
    for name in ("Speed", "Offset"):
        scalar(parameters[name], name)
    judge = observation["Judge"]
    for name in ("JudgeTime", "NormalChallengeTime", "MaxChallengeTime", "BadTime"):
        scalar(judge[name], name, positive=True)
    audio_frames = integer(observation["AudioFrames"], "audio frames", 1)
    channels = integer(observation["AudioChannels"], "audio channels", 1)
    frequency = integer(observation["AudioFrequency"], "audio frequency", 1)
    width = integer(observation["CoverWidth"], "cover width", 1)
    height = integer(observation["CoverHeight"], "cover height", 1)
    check(channels in (1, 2) and 8000 <= frequency <= 192000 and width <= 2048 and height <= 2048,
          "Recorded audio/cover geometry exceeds the validated input contract.")
    check(audio_frames * channels * 4 <= 32 * 1024 * 1024, "Recorded PCM exceeds its CPU decoding bound.")
    estimate = integer(observation["EstimatedUploadBytes"], "estimated upload bytes", 1)
    check(estimate == audio_frames * channels * 4 + width * height * 4 + 1024 and estimate <= 64 * 1024 * 1024,
          "Upload estimate differs from PCM+RGBA32+sprite accounting.")
    check(integer(observation["ChartLines"], "validated source lines", 1) <= 8192, "Chart line summary exceeds validation bounds.")
    check(integer(observation["ChartNotes"], "validated source notes") <= 32768, "Chart note summary exceeds validation bounds.")
    copied_metadata = observation["Metadata"]
    check(copied_metadata["MusicName"] == "LOCAL Bundle Sentinel"
          and copied_metadata["Artist"] == "RizToolbox original test data"
          and copied_metadata["LevelInternalName"] == "LOCAL" and copied_metadata["Lv"] == "TEST"
          and copied_metadata["Charter"] == "RizToolbox" and copied_metadata["Status"] == 0,
          "Metadata is not the declared private original-data sentinel.")
    for name in ("From", "LevelDisplayName"):
        check(type(copied_metadata[name]) is str and 0 < len(copied_metadata[name]) <= 256, "Absent bounded metadata getter: " + name)
    color = copied_metadata["ThemeColor"]
    for channel in ("R", "G", "B", "Alpha"):
        check(0 <= scalar(color[channel], channel) <= 1, "Theme Color channel exceeds normalized bounds.")
    return identities


one = first["observation"]
two = second["observation"]
identities_one, identities_two = graph(one), graph(two)
check(len(set(identities_one + identities_two + [source_controller, source_judge])) == 20,
      "Two complete owned graphs and their borrowed ordinary sources must have independent identities.")
check(one["Judge"] == two["Judge"] and one["Metadata"] == two["Metadata"], "Copied source judge values or private metadata differ across graphs.")
for name in ("AudioFrames", "AudioChannels", "AudioFrequency", "CoverWidth", "CoverHeight", "ChartLines", "ChartNotes", "EstimatedUploadBytes"):
    check(one[name] == two[name], "The two graphs differ in their shared exact CPU source geometry: " + name)
check(one["EstimatedUploadBytes"] + two["EstimatedUploadBytes"] <= 128 * 1024 * 1024, "Retained upload estimates exceed the process budget.")
fields(withdraw, True, "providerAndCatalogAndContentWithdrawn", "callerRequestDisposed", "nativeGcReadbacksUnchanged")
fields(withdraw, False, "destructionVerified", "canLaunch")
check(integer(withdraw["preparedRequests"], "withdrawn prepared requests") == 2
      and withdraw["firstDirectRoots"] == withdraw["secondDirectRoots"] == 5,
      "Withdrawal must retain precisely the two independent CPU forks and both direct native graphs.")
fields(retire, True, "firstClosed", "firstReadRejected", "secondAfterNativeGcUnchanged")
fields(retire, False, "destructionVerified")
check(integer(retire["preparedRequests"], "first-retired prepared requests") == 1
      and integer(retire["firstDirectRoots"], "first-retired direct roots") == 0 and retire["secondDirectRoots"] == 5,
      "First retirement must release exactly one CPU fork and its five direct roots.")
fields(verified, True, "allSixOwnedUnityResourcesDestroyed", "bothNativeGraphsClosed", "allTenDirectRootsReleased",
       "sharedCpuTemplateReleased", "sourceObservationRootsReleased")
fields(verified, False, "controllerCreated", "gameCatalogModified", "gameResultOrSavePathInvoked", "canLaunch")
for name in ("preparedRequests", "retainedSongs", "retainedContent", "sourceRootRecordsRemaining"):
    check(type(verified[name]) is int and verified[name] == 0, "Final retained count must be zero: " + name)
check(type(verified["sourceRootRecordsReleased"]) is int and verified["sourceRootRecordsReleased"] == 6,
      "All six actual source observation root records must be released.")

package = load(manifest.read_text(encoding="utf-8-sig"))
check(type(package["files"]) is list and len(package["files"]) == 18, "Manifest must contain the actual 18 installed files.")
installed = []
seen = set()
for entry in package["files"]:
    relative = Path(entry["path"].replace("\\", "/"))
    windows = PureWindowsPath(entry["path"])
    check(not relative.is_absolute() and not windows.drive and ".." not in relative.parts, "Manifest path must be relative and bounded.")
    target = (game / relative).resolve()
    check(target.is_relative_to(game), "Manifest target escaped the resolved game directory.")
    key = str(target).casefold()
    check(key not in seen, "Manifest repeats an installed target.")
    seen.add(key)
    expected = entry["sha256"].lower()
    check(re.fullmatch(r"[0-9a-f]{64}", expected) is not None and target.is_file(), "Absent installed target or malformed SHA256.")
    actual = sha(target)
    check(actual == expected, "Installed package differs from its actual manifest: " + entry["path"])
    installed.append(dict(path=entry["path"], expected=expected, actual=actual))

# Only verified complete evidence reaches the copy/write phase.
output.mkdir(parents=True, exist_ok=True)
snapshot = output / session.name
snapshot.mkdir(exist_ok=True)
for source in sources:
    freeze_file(source, snapshot / source.name, source_hashes[source])
freeze_file(manifest, snapshot / "package-manifest.json", manifest_hash)
check(sorted(session.glob("events-*.jsonl")) == logs, "Event segments changed during freezing; stop the game first.")
check(all(sha(source) == expected for source, expected in source_hashes.items()), "Session sources changed while copying.")
check(sha(manifest) == manifest_hash, "Package manifest changed while freezing.")
check(all(sha((game / entry["path"].replace("\\", "/")).resolve()) == entry["expected"] for entry in installed),
      "Installed package changed during freezing.")
proof = dict(
    verifiedAtUtc=datetime.now(timezone.utc).isoformat(), sessionId=session.name,
    expectedPhases=args.expected_phase or ["verified"], events=len(events),
    levels=dict(Counter(event["level"] for event in events)),
    warnings=[event for event in events if event["level"] == "Warning"],
    phases={name: dict(sequence=rows[name][0][0]["sequence"], values=rows[name][0][1]) for name in PHASES},
    frames=[created_frame, withdraw_frame, retire_frame, verified_frame],
    ownedGraphIdentities=[identities_one, identities_two], borrowedOrdinarySourceIdentities=[source_controller, source_judge],
    parameterScalarObservations=[{name + "Bits": struct.unpack("<i", struct.pack("<f", observation["Parameters"][name]))[0]
                                  for name in ("Speed", "Offset")} for observation in (one, two)],
    files=[dict(path=path.relative_to(output).as_posix(), sha256=sha(path))
           for path in sorted(snapshot.iterdir()) if path.is_file() and path.name != "runtime-proof.json"],
    installedFiles=installed, packageManifest=dict(originalPath=str(manifest), sha256=manifest_hash),
    stoppedSessionPrecondition="Operator supplied --stopped after independent process-stop confirmation. The event format has no shutdown marker; a normal exit or its cause is not established. Complete original sources and current installation were byte-stable during freezing.",
    scope="Two independent native resource/metadata/Chart/JudgeData/InsideLevelParams graphs over one private original-data CPU catalog, positive judging windows copied from a live ordinary controller, withdrawal/native GC readbacks, separate later-frame Unity destruction, CPU fork/template retirement and six source-root releases. Upload values are estimates, not actual native heap bounds. No LOCAL controller activation, settings initialization acceptance, result/save isolation wiring, or complete playable LOCAL run acceptance."
)
proof_targets = (snapshot / "runtime-proof.json", output / "runtime-proof.json")
for target in proof_targets:
    if target.exists():
        previous = load(target.read_text(encoding="utf-8-sig"))
        check(previous["sessionId"] == session.name and previous["packageManifest"]["sha256"] == manifest_hash,
              "An earlier proof belongs to another session/package; choose a separate --output-dir.")
for target in proof_targets:
    target.write_text(json.dumps(proof, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(sessionId=session.name, events=len(events), levels=proof["levels"], frames=proof["frames"],
                      independentOwnedIdentities=18, releasedDirectRoots=10, releasedSourceRoots=6,
                      preparedRequestCounts=[2, 1, 0], installedMatched=len(installed))))
