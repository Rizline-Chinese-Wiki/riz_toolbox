"""Record sole-builder terminal statuses actually observed in this session."""
import json
from pathlib import Path

batch = Path(__file__).resolve().parent
full = json.loads((batch / "build.log.run.json").read_text(encoding="utf-8-sig"))
targeted = json.loads((batch / "targeted.log.run.json").read_text(encoding="utf-8-sig"))
assert full["exitCode"] == targeted["exitCode"] == 0
assert not full["installed"] and not full["gameExecuted"]
full.update(sessionId=45961, terminalExitCodeObserved=0,
    terminalObservation="Root write_stdin session45961 returned exit_code0 after the complete Build.ps1, SDK distribution and Package.",
    targetedSessionId=69797, targetedExitCodeObserved=0,
    targetedLogSha256=targeted["logSha256"],
    scope="Local build and self-owned isolated native/CLR children; game validation remains deferred.")
(batch / "build-run.json").write_text(json.dumps(full, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(fullSession=45961, fullExit=0, targetedSession=69797, targetedExit=0)))
