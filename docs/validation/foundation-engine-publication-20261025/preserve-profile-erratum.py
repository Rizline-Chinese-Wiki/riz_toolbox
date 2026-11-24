from pathlib import Path
import hashlib
import json
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
old=batch.parent/'foundation-doorstop-cold-20261025'
paths=['native/RizToolbox.EngineBootstrap/EngineGameProfile.h',
    'docs/validation/foundation-doorstop-cold-20261025/research/extract-engine-game-profile.py',
    'docs/validation/foundation-doorstop-cold-20261025/research/engine-game-profile.json',
    'docs/validation/foundation-doorstop-cold-20261025/research/engine-game-profile.md']
history=batch/'history/profile-cache-type-erratum'
history.mkdir(parents=True,exist_ok=False)
rows=[]
with zipfile.ZipFile(old/'source-final-inputs.zip') as archive:
    for name in paths:
        data=archive.read(name);destination=history/name;destination.parent.mkdir(parents=True,exist_ok=True);destination.write_bytes(data)
        rows.append(dict(path=name,sha256=hashlib.sha256(data).hexdigest()))
(history/'erratum.json').write_text(json.dumps(dict(originalBatch='foundation-doorstop-cold-20261025',
    incorrectGeneratedFacts=dict(enumeratorCache=24,enumeratorType=32),
    actualCanonicalScalarReads=dict(enumeratorType=24,enumeratorCache=32),
    executableKernelImplementationWasCorrect=True,publicationCodeReadsTheseFields=False,originals=rows),indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps(dict(preservedOriginalGeneratedFacts=4,canonicalImplementationChanged=False)))
