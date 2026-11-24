from pathlib import Path
import hashlib, json, shutil
R=Path(__file__).resolve().parent
S=R/'source-r2'; S.mkdir(exist_ok=False)
def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
for p in (R/'source').iterdir(): shutil.copyfile(p,S/p.name)
p=S/'IgnoredSevenAbiFixture.cpp'
old='h.Flags==(nestedEnabled?RtSourceUnknown:RtSourceArmed)'
new='h.Flags==(nestedEnabled?(RtSourceUnknown|RtSourceDormant):RtSourceArmed)'
v=p.read_text(encoding='utf-8'); assert v.count(old)==1
p.write_text(v.replace(old,new),encoding='utf-8')
runner=R/'Build-And-Run.py'; text=runner.read_text(encoding='utf-8')
main="for outer in ('pause','replay'):\n    for kind,vs in values.items():\n        for value in vs:\n            for mode in ('normal','seh'):\n                cases.append((mode,outer,kind,value))\n"
assert text.count(main)==1
text=text.replace("(R/'source')","(R/'source-r2')").replace('build-r1','build-r2').replace('run-n3-r1','run-n3-r2-worker').replace(main,'')
out=R/'Build-And-Run-WorkerR2.py'; out.write_text(text,encoding='utf-8')
(R/'worker-r2-correction.json').write_text(json.dumps(dict(reason='Fixture health assertion correction: ReadOwner InfoLocked adds RtSourceDormant whenever Armed is absent. Actual worker physical receipt was already correctly asserted as 45; no product source or binary change.',oldExpectedFlags=4,newExpectedFlags=6,originalRunnerSha256=sha(runner),derivedRunnerSha256=sha(out),originalFixtureSha256=sha(R/'source/IgnoredSevenAbiFixture.cpp'),correctedFixtureSha256=sha(p),preservedFirstRun=str(R/'run-n3-r1/test-result.json'),preservedFirstBuild=str(R/'build-r1/build-result.json')),indent=2),encoding='utf-8')
print(str(out))
