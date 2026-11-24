"""Prepare independent evidence tools for the new active-object owners."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-terminal-recycling-20261023"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py", "direct-call-candidates.json", "event-trigger-research.md"]:
    (root / name).write_bytes((previous / name).read_bytes())
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig").replace("terminal-recycling-final-20261023", "active-destruction-final-20261023")
body = body.replace("the new Adapter initial/terminal tracker/backends and launch/result/menu composition", "the new Adapter initial/terminal/destruction/EventTrigger tracker/backends and launch/result/menu composition")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig").replace("foundation-terminal-coroutines-20261023", "foundation-terminal-recycling-20261023")
body = body.replace('len(added) == 1', 'len(added) == 7').replace('fromfile="1360/"', 'fromfile="1367/"').replace('tofile="1367/"', 'tofile="active-destruction/"').replace("changes-from-1360", "changes-from-1367")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "inspect-terminal.py").read_text(encoding="utf-8-sig")
needle = '    "Normal.LevelOverCoroutine.MoveNext": 0x3C5C80,'
assert body.count(needle) == 1
body = body.replace(needle, '    "EventTrigger.StartEvents": 0x38A460, "EventTrigger.Delayed.MoveNext": 0x39B4D0,\n    "SimpleCoroutine.SimpleDelay": 0x3AE340, "UnityEvent.Invoke": 0x15642E0,\n' + needle)
needle = 'assert decoded[0x3BEAA5].get("destinationRva") == "0x153d490"'
body = body.replace(needle, needle + '\nassert decoded[0x3C5963].get("destinationRva") == "0x38a460"\nassert decoded[0x38A607].get("destinationRva") == "0x3ae340"\nassert decoded[0x38A6AD].get("destinationRva") == "0x153d380"\nassert decoded[0x39B585].get("destinationRva") == "0x15642e0"\nassert decoded[0x156451F]["mnemonic"] == "call" and "0x178" in decoded[0x156451F]["operands"]')
(root / "inspect-terminal.py").write_text(body, encoding="utf-8")
print("Prepared this new batch's archive, comparison and bounded static-evidence tools.")
