"""Create new batch evidence tools; keep the previous frozen build unchanged."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-active-destruction-20261023"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py", "direct-call-candidates.json", "event-trigger-research.md", "next-async-evidence.md"]:
    (root / name).write_bytes((previous / name).read_bytes())
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig").replace("active-destruction-final-20261023", "cutout-async-final-20261023")
body = body.replace("initial/terminal/destruction/EventTrigger tracker/backends", "initial/terminal/destruction/EventTrigger/async tracker/backends")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig").replace("foundation-terminal-recycling-20261023", "foundation-active-destruction-20261023")
body = body.replace('len(added) == 7', 'len(added) == 3').replace('fromfile="1367/"', 'fromfile="1393/"').replace('tofile="active-destruction/"', 'tofile="cutout-async/"').replace("changes-from-1367", "changes-from-1393")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "inspect-terminal.py").read_text(encoding="utf-8-sig")
needle = '    "Normal.LevelOverCoroutine.MoveNext": 0x3C5C80,'
assert body.count(needle) == 1
body = body.replace(needle, '    "EventTrigger.EventFactory": 0x38A3F0, "EventTrigger.ResetAction": 0x38A700,\n    "SimpleCoroutine.Delay.MoveNext": 0x3AF0A0, "SimpleCoroutine.Delay.Reset": 0x3AF250,\n    "EventTrigger.Delayed.Reset": 0x39B5A0,\n' + needle)
needle = 'assert decoded[0x3BEAA5].get("destinationRva") == "0x153d490"'
body = body.replace(needle, needle + '\nassert decoded[0x38A700]["bytes"] == "c6413000" and decoded[0x38A704]["mnemonic"] == "ret"\nassert decoded[0x38A436]["operands"].endswith("0")\nassert decoded[0x38A440]["operands"] == "qword ptr [rbx + 0x20], rdi"\nassert decoded[0x3AF116]["operands"] == "qword ptr [rax + 0x18]"')
(root / "inspect-terminal.py").write_text(body, encoding="utf-8")
print("Prepared new batch archive, comparison and pinned async evidence tools.")
