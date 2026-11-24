"""Prepare evidence for this batch without changing older build archives."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-cutout-async-20261023"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py", "direct-call-candidates.json", "event-trigger-research.md", "next-listener-plan.md"]:
    (root / name).write_bytes((previous / name).read_bytes())
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig").replace("cutout-async-final-20261023", "unity-events-final-20261023")
body = body.replace("initial/terminal/destruction/EventTrigger/async tracker/backends", "initial/terminal/destruction/EventTrigger/async/UnityEvent tracker/backends and listener metadata")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig").replace("foundation-active-destruction-20261023", "foundation-cutout-async-20261023")
body = body.replace('len(added) == 3', 'len(added) == 5').replace('fromfile="1393/"', 'fromfile="1412/"').replace('tofile="cutout-async/"', 'tofile="unity-events/"').replace("changes-from-1393", "changes-from-1412")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "inspect-terminal.py").read_text(encoding="utf-8-sig")
needle = '    "Normal.LevelOverCoroutine.MoveNext": 0x3C5C80,'
assert body.count(needle) == 1
body = body.replace(needle, '    "UnityEventBase.PrepareInvoke": 0x1563A60, "InvokableCallList.PrepareInvoke": 0x154E1D0,\n' + needle)
needle = 'assert decoded[0x3BEAA5].get("destinationRva") == "0x153d490"'
body = body.replace(needle, needle + '\n# Neither standalone Prepare method is a direct-call boundary of stock Invoke.\nfor scope in ranges:\n    if scope["name"] in {"UnityEvent.Invoke", "UnityEventBase.PrepareInvoke", "InvokableCallList.PrepareInvoke"}:\n        assert not any(row.get("destinationRva") in {"0x1563a60", "0x154e1d0"} for row in scope["instructions"] if row["mnemonic"] == "call")')
(root / "inspect-terminal.py").write_text(body, encoding="utf-8")
print("Prepared independent source/archive, comparison and inlined-route evidence tools.")
