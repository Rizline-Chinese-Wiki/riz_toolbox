"""Record the terminal statuses actually observed by the sole root builder."""
import json
from pathlib import Path

batch = Path(__file__).resolve().parent
full = json.loads((batch / "build.log.run.json").read_text(encoding="utf-8-sig"))
targeted = json.loads((batch / "targeted.log.run.json").read_text(encoding="utf-8-sig"))
assert full["exitCode"] == targeted["exitCode"] == 0
assert not full["installed"] and not full["gameExecuted"]
full.update(sessionId=60161, terminalExitCodeObserved=0,
    terminalObservation="Root write_stdin session60161 returned exit_code0 after full Build.ps1, including SDK distribution and package.",
    targetedSessionId=32299, targetedExitCodeObserved=0,
    firstTargetedSessionId=11492, firstTargetedExitCodeObserved=1,
    targetedLogSha256=targeted["logSha256"],
    scope="Local build and isolated self-owned fixture children only; game validation remains deferred.")
(batch / "build-run.json").write_text(json.dumps(full, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(fullSession=60161, fullExit=0, targetedSession=32299, targetedExit=0)))
