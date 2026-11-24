"""Seal only genuinely completed audit evidence; never runs or controls tests."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path

A = Path(__file__).resolve().parent
M = A.parent

def fact(path):
    raw = path.read_bytes()
    return dict(path=str(path),sha256=hashlib.sha256(raw).hexdigest(),bytes=len(raw))

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

def require(value,message):
    if not value:
        raise RuntimeError(message)

completed = A/'completed-audit.json'
require(completed.is_file(),'Mapped audit is pending; no completed evidence exists.')
audit = read(completed)
require(audit['evidenceIntegrityPassed'] is True and audit['mappedAcceptancePassed'] is True and not audit['issues'],'Audit has unresolved evidence issues or no mapped acceptance.')
require(audit['nativeR6']['passed'] is True and audit['mappedR3']['terminal'] is True and audit['mappedR3']['accepted'] is True,'Native evidence and mapped terminal acceptance both required.')
require(fact(Path(audit['nativeR6']['manifest']['path'])) == audit['nativeR6']['manifest'],'Native final package manifest changed after audit.')
require(not audit['mappedR3']['pending'] and not audit['mappedR3']['notLaunched'],'At least one mapped mode remains pending.')
result_path = M/'test-result.json'
require(fact(result_path) == audit['mappedR3']['resultSnapshot'],'Mapped result changed since the completed read-only audit.')
result = read(result_path)
require(result['passed'] is True and result['driverExitCode'] == 0 and (M/'driver.exit.txt').read_text().strip() == '0','Completed driver evidence required.')
require(len(result['rows']) == 6 and all(row['actualExitCode'] == 0 and row['passed'] is True for row in result['rows']),'All six actual test/build exits must be zero.')
require(all(result[k] is True for k in ['frozenSourcesUnchanged','nativeInputsUnchanged','buildProductsUnchanged']),'All final driver hash guards must be true.')
for row in [result['sourceManifest'],result['scope'],result['buildProducts'],result['nativeInputManifest']]:
    require(fact(Path(row['path'])) == row,'A manifest changed after completion: '+row['path'])
products = read(M/'build-products.json')
inputs = read(M/'native-inputs.json')
for row in products + [r[k] for r in inputs for k in ['origin','copy']]:
    require(fact(Path(row['path'])) == row,'A completed build/native fact changed: '+row['path'])
closure = dict(
    kind='mapped-r3-and-native-r6-completed-source-and-hash-close-evidence',
    sealedAtUtc=datetime.now(timezone.utc).isoformat(),
    completedAudit=fact(completed),
    mappedResult=fact(result_path),
    sourceManifest=result['sourceManifest'],scope=result['scope'],
    frozenSourceFiles=audit['mappedR3']['frozenSourceFiles'],
    modeChecks={row['name']:row['checks'] for row in audit['mappedR3']['completedEvidence'] if row['checks'] is not None},
    totalMappedChecks=audit['mappedR3']['completedChecks'],
    actualExitCodes={row['name']:row['actualExitCode'] for row in result['rows']},
    driverExitRecorded=0,
    driverOperatingSystemExitSeparatelyCaptured=False,
    finalHashGuards={key:result[key] for key in ['frozenSourcesUnchanged','nativeInputsUnchanged','buildProductsUnchanged']},
    freshBuildProducts=products,nativeInputs=inputs,
    jointProcessIdentityEvidence=audit['mappedR3']['jointProcessIdentityEvidence'],
    nativeR6=audit['nativeR6'],
    currentProductSourceChangedPaths=audit['mappedR3']['currentProductSourceChangedPaths'],
    laterTestDeltasExcluded=audit['mappedR3']['currentUSourceChangedPaths'],
    acceptanceScope=audit['mappedR3']['acceptanceScope'],
    previousMetadataR2UsedForAcceptance=False,
    fullNativeRerun=False,
    gameCodeExecuted=False,dllMainExecuted=False,gameLaunched=False,gameUiAccessed=False,accountAccessed=False,productionAdmission=False,
    testsStartedByAuditor=0,buildsStartedByAuditor=0,killsPerformed=False,restartsPerformed=False,
    sourceWritesPerformed=False,nativePackageWritesPerformed=False,
)
(A/'CLOSE-EVIDENCE.json').write_text(json.dumps(closure,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
manifest_path = A/'audit-hash-manifest.json'
rows = []
for path in sorted(A.rglob('*')):
    if path.is_file() and path != manifest_path:
        row = fact(path)
        row['path'] = path.relative_to(A).as_posix()
        rows.append(row)
manifest_path.write_text(json.dumps(dict(kind='read-only-audit-r4-artifact-sha256',files=rows),ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(sealed=True,closeEvidence=fact(A/'CLOSE-EVIDENCE.json'),auditManifest=fact(manifest_path),files=len(rows),mappedChecks=closure['totalMappedChecks'],actualExitCodes=closure['actualExitCodes']),ensure_ascii=False,indent=2))
