from pathlib import Path
import datetime, hashlib, json, os
O=Path(__file__).resolve().parent;R=O.parent
sha=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
fact=lambda p:dict(path=str(p),sha256=sha(p),bytes=p.stat().st_size)
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
assert not (O/'independent-audit.json').exists()
result=read(O/'test-result.json');assert 'finishedAtUtc' in result
manifest=read(O/'source-manifest.json')['files'];original=read(O/'origin-source-manifest.json')['files']
stable=all(sha(O/'source'/row['path'])==row['sha256'] for row in manifest)
native=read(O/'preparation-result.json')['nativeInputs']
stable &= all(sha(Path(row['frozen']['path']))==row['frozen']['sha256'] for row in native)
products={Path(row['path']).name:row for row in read(O/'build-products.json')}
positive={Path(row['path']).name:row for row in read(R/'positive-r2/build-products.json')}
names=[f'RizToolbox.{name}.{suffix}' for name in ['Sdk','Core','Runtime','Adapter'] for suffix in ['dll','pdb']]
matches=[dict(name=name,actual=products[name],positiveR2=positive[name],exactMatch=products[name]['sha256']==positive[name]['sha256']) for name in names]
native_source=O/'source/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp'
built_native_source=R/'native-r3/frozen-source/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp'
native_source_match=sha(native_source)==sha(built_native_source)
summary=dict(kind='final-native-r3-actual-stock-stop-after-same-and-cross-run-handoff',
 passed=bool(result['passed'] and stable and native_source_match and all(row['exactMatch'] for row in matches)),
 auditedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),auditPid=os.getpid(),
 driverResult=fact(O/'test-result.json'),sourceManifest=fact(O/'source-manifest.json'),
 originSourceCount=len(original),frozenSourceCount=len(manifest),frozenInputsUnchanged=bool(stable),
 productManagedPePdb=matches,productManagedPePdbExactMatchPositiveR2=all(row['exactMatch'] for row in matches),
 actualCompiledNativeSource=fact(built_native_source),frozenNativeSource=fact(native_source),nativeSourceExactMatch=native_source_match,
 nativeBuildReceipt=fact(R/'native-r3/native-build-result.json'),nativeInputs=native,
 actualModeResults=[dict(mode=row['mode'],pid=row['pid'],actualExitCode=row['actualExitCode'],passed=row.get('actual',{}).get('passed'),checks=row.get('actual',{}).get('checks')) for row in result['rows']],
 checks=result.get('checks'),actualReplayStops=sum(row.get('actual',{}).get('actualReplayStops',0) for row in result['rows']),
 actualResumeStops=sum(row.get('actual',{}).get('actualResumeStops',0) for row in result['rows']),
 actualReservedRestorers=sum(row.get('actual',{}).get('actualReservedRestorers',0) for row in result['rows']),
 previousAssertionFailuresPreserved=fact(R/'handoff-stop-r1/test-result.json'),
 previousOriginRunnerDeltaPreserved=fact(R/'handoff-stop-r1/source-origin-delta-r1.json'),
 testedRate=1,sdkValueTransactions=0,sdkSelectedRateRecovery=False,sdkMatrixRepeated=False,
 mainModified=False,productModified=False,gameExecuted=False,productionAdmission=False)
(O/'independent-audit.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(dict(passed=summary['passed'],checks=summary['checks'],frozenInputsUnchanged=bool(stable),nativeSourceExactMatch=native_source_match,
 productManagedPePdbExactMatchPositiveR2=summary['productManagedPePdbExactMatchPositiveR2'])))
raise SystemExit(0 if summary['passed'] else 1)
