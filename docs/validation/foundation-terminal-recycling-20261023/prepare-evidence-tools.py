"""Prepare new batch-local tooling without changing historical frozen evidence."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-terminal-coroutines-20261023"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py", "direct-call-candidates.json"]:
    (root / name).write_bytes((previous / name).read_bytes())
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig").replace("terminal-coroutines-final-20261023", "terminal-recycling-final-20261023")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig").replace("foundation-initialization-coroutines-20261023", "foundation-terminal-coroutines-20261023")
body = body.replace('len(added) == 3', 'len(added) == 1').replace('fromfile="1343/"', 'fromfile="1360/"').replace('tofile="1360/"', 'tofile="1367/"').replace("changes-from-1343", "changes-from-1360")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-documentation.py").read_text(encoding="utf-8-sig").replace("2026-10-23_底层-结算协程与按名启动-report.md", "2026-10-23_底层-协程观察根回收-report.md").replace("== 1360 and", "== 1367 and")
(root / "check-documentation.py").write_text(body, encoding="utf-8")
body = (previous / "inspect-terminal.py").read_text(encoding="utf-8-sig")
needle = '    "Normal.LevelOverCoroutine.MoveNext": 0x3C5C80,'
assert body.count(needle) == 1
body = body.replace(needle, '    "Normal.Cutout.Reset": 0x3C5A00, "Normal.Fade.Reset": 0x3C5C40,\n' + needle)
needle = 'assert decoded[0x3BEAA5].get("destinationRva") == "0x153d490"'
body = body.replace(needle, needle + '\nassert decoded[0x3C5A22].get("destinationRva") == "0xf1b1c0"\nassert decoded[0x3C5A39].get("destinationRva") == "0x264fb0"\nassert decoded[0x3C5C62].get("destinationRva") == "0xf1b1c0"\nassert decoded[0x3C5C79].get("destinationRva") == "0x264fb0"')
(root / "inspect-terminal.py").write_text(body, encoding="utf-8")
print("Prepared new archive, comparison, link and bounded static-evidence tools.")
