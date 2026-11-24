"""Batch-local tooling; never alters older frozen evidence or installed files."""
from pathlib import Path

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-coroutine-boundaries-20261023"
for name in ["freeze-source.py", "freeze-package.py", "verify-coroutine-evidence.py"]:
    (root / name).write_bytes((previous / name).read_bytes())
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig").replace("coroutine-boundaries-final-20261023", "initialization-coroutines-final-20261023")
body = body.replace("actual game metadata/coroutine ownership, full launch, active consumer retirement and game acceptance remain separate/unverified.",
    "actual game metadata/coroutine ownership, the new Adapter tracker/backend and launch/menu composition, full launch, active consumer retirement and game acceptance remain separate/unverified.")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace("foundation-menu-handoff-20261023", "foundation-coroutine-boundaries-20261023")
body = body.replace("len(added) == 7", "len(added) == 3")
body = body.replace('fromfile="1315/"', 'fromfile="1324/"').replace('tofile="1324/"', 'tofile="1343/"').replace("changes-from-1315", "changes-from-1324")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
body = (previous / "check-documentation.py").read_text(encoding="utf-8-sig")
body = body.replace("2026-10-23_底层-协程原生观察边界-report.md", "2026-10-23_底层-真实初始化协程追踪-report.md").replace("== 1324 and", "== 1343 and")
(root / "check-documentation.py").write_text(body, encoding="utf-8")
body = (previous / "inspect-coroutines.py").read_text(encoding="utf-8-sig")
body = body.replace('    "TouchBlocker.Release": 0x3827E0,', '    "TouchBlocker.Release": 0x3827E0,\n    "ClosedAction.Constructor": 0x377890,\n    "ClosedPredicate.Constructor": 0x8D3510,\n    "Getter.DelayTodo": 0x3C1930,\n    "Getter.WaitPredicate": 0x3C1960,')
body = body.replace('[(93742, 93755), (93817, 93829)]', '[(7680, 7694), (7933, 7936), (93742, 93755), (93817, 93829), (95574, 95583), (95802, 95803)]')
body = body.replace('    end = starts[bisect.bisect_right(starts, start)]', '    end = {0x377890: 0x377920, 0x8D3510: 0x8D35A0}.get(start, starts[bisect.bisect_right(starts, start)])')
body = body.replace('constants = []', '''for start in [0x377890, 0x8D3510]:
    constructor = next(item for item in ranges if int(item["start"], 16) == start)
    operands = [row["operands"] for row in constructor["instructions"]]
    assert any("[rcx + 0x20], rdx" in value for value in operands)
    assert any("[rcx + 0x28]" in value for value in operands)
constants = []''')
(root / "inspect-coroutines.py").write_text(body, encoding="utf-8")
print("Prepared seven batch-local tools; older archives unchanged.")
