"""Freeze the exact declared source input set before the sole root build."""
import hashlib,json
from pathlib import Path
from datetime import datetime,timezone
batch=Path(__file__).resolve().parent
repo=batch.parents[2]
paths=[]
for directory in ["src","tests","examples","scripts","native","templates","tools","packaging"]:
    for path in (repo/directory).rglob("*"):
        if path.is_file() and path.suffix in {".cs",".csproj",".props",".targets",".ps1",".cpp",".c",".h",".asm",".json"} and not {"bin","obj","out"}.intersection(path.relative_to(repo).parts):
            paths.append(path)
paths.append(repo/"native/RizToolbox.Doorstop/RIZLINE-MODIFICATIONS.md")
for name in ["Directory.Build.props","Directory.Build.targets","RizToolbox.sln","dependencies.lock.json","global.json"]:
    if (repo/name).is_file(): paths.append(repo/name)
files=sorted([dict(path=path.relative_to(repo).as_posix(),sha256=hashlib.sha256(path.read_bytes()).hexdigest()) for path in paths],key=lambda row:row["path"])
proof=dict(frozenAtUtc=datetime.now(timezone.utc).isoformat(),files=files)
(batch/"build-inputs-before.json").write_text(json.dumps(proof,indent=2)+"\n",encoding="utf-8")
print(json.dumps(dict(frozenInputs=len(files))))
