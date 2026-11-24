"""Finite offline initialization-order evidence; no game/process/native execution.

Reuses the already audited local-launch PE/import/export and Capstone extractor.
The single LevelControl.Start range is 10,656 bytes; its explicit per-scope cap
is 0x3000. No image-wide code scan or inferred scene scheduling is performed.
"""
import hashlib
import json
import re
import sys
from pathlib import Path

here = Path(__file__).resolve().parent
base = here.parent / "local-launch-20261022" / "inspect-launch.py"
symbols = Path(r"C:\Users\71957\Desktop\绿豆诡计v2\output\il2cpp_output\script.json")
entries = json.loads(symbols.read_text(encoding="utf8"))["ScriptMethod"]
targets = {
    "LevelControl.Start": "InLevel.LevelControl$$Start",
    "LevelInformation.ctor": "InLevel.LevelInformation$$.ctor",
    "Progress.ctor": "InLevel.ProgressControl$$.ctor",
    "Progress.Start": "InLevel.ProgressControl$$Start",
    "Progress.Start.MoveNext": "<Start>d__25$$MoveNext",
    "Progress.Start.WaitPredicate": "InLevel.ProgressControl$$<Start>b__25_0",
    "Progress.Update": "InLevel.ProgressControl$$Update",
    "NormalMode.GetInsideLevelParams": "NormalModeInLevelController$$GetInsideLevelParams",
    "SteamTimeline.SetTimelineTooltip": "Steamworks.SteamTimeline$$SetTimelineTooltip",
}
scopes = {}
for name, target in targets.items():
    found = {x["Address"] for x in entries if x["Name"] == target}
    if len(found) != 1:
        raise SystemExit("Missing or ambiguous finite symbol: " + target)
    scopes[name] = found.pop()
source = base.read_text(encoding="utf8")
source, count = re.subn(r"^SCOPES = .*$", "SCOPES = " + repr(scopes), source, count=1, flags=re.M)
assert count == 1
source, count = re.subn(r"end - start > 0x1800", "end - start > (0x3000 if name == 'LevelControl.Start' else 0x1800)", source, count=1)
assert count == 1
classes = {"LevelControl", "LevelInformation", "ProgressControl", "<Start>d__25", "InLevelControl", "InLevelController", "NormalModeInLevelController", "GameManager", "SteamTimeline"}
source, count = re.subn(r"classes = \{.*?\}\nexcerpt", "classes = " + repr(classes) + "\nexcerpt", source, count=1, flags=re.S)
assert count == 1
sys.argv = [str(__file__), "--game", r"D:\SteamLibrary\steamapps\common\Rizline\GameAssembly.dll",
            "--dump", str(symbols.with_name("dump.cs")), "--symbols", str(symbols), "--out", str(here)]
(here / "extractor-provenance.json").write_text(json.dumps({
    "baseExtractor": str(base), "baseExtractorSha256": hashlib.sha256(base.read_bytes()).hexdigest(),
    "wrapperSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    "scopeSelection": scopes, "mode": "bounded-static-read-only", "runtimeAcceptance": False,
    "perScopeException": {"LevelControl.Start": "Known 10,656-byte single method; hard cap 0x3000"},
}, indent=2) + "\n", encoding="utf8")
exec(compile(source, str(base), "exec"))
