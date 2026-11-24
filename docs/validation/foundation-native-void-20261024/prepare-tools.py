"""Prepare evidence tools for a new, unbuilt batch without changing prior evidence."""
from pathlib import Path
import json
import shutil

root = Path(__file__).resolve().parent
previous = root.parent / "foundation-unity-events-20261023"
for name in ["freeze-source.py", "freeze-package.py", "verify-terminal-evidence.py"]:
    shutil.copy2(previous / name, root / name)
body = (previous / "summarize-build.py").read_text(encoding="utf-8-sig")
body = body.replace("unity-events-final-20261023", "native-void-final-20261024")
body = body.replace("the new Adapter initial/terminal/destruction/EventTrigger/async/UnityEvent tracker/backends and listener metadata",
    "the new native void0 transport and real Dobby fixture additionally verify exact caller, physical probes and original unwind; the Adapter UnityEvent graph/parent metadata and listener metadata")
(root / "summarize-build.py").write_text(body, encoding="utf-8")
body = (previous / "compare-sources.py").read_text(encoding="utf-8-sig")
body = body.replace("foundation-cutout-async-20261023", previous.name).replace("1412", "1449")
body = body.replace("unity-events/", "native-void/").replace("assert len(added) == 5 and not removed", "assert len(added) == 7 and not removed")
(root / "compare-sources.py").write_text(body, encoding="utf-8")
names = ["terminal-evidence.json", "terminal-disassembly.txt", "terminal-metadata.txt", "direct-call-candidates.json"]
for name in names:
    shutil.copy2(previous / name, root / name)
(root / "static-evidence-origin.json").write_text(json.dumps(dict(
    previousBatch=previous.name, copiedFiles=names,
    scope="Same pinned game Invoke/EventTrigger/Coroutine ranges as 1449; this batch adds transport evidence, not new game disassembly or runtime acceptance."), indent=2) + "\n", encoding="utf-8")
print(json.dumps(dict(preparedTools=5, copiedStaticEvidence=len(names), built=False)))
