"""Derive bounded claims from a frozen session, never from current toolbox.log."""
from pathlib import Path
import hashlib, json, sys

root = Path(__file__).resolve().parent
snapshot = Path(sys.argv[1]).resolve()
summary = json.loads((snapshot/'summary.json').read_text(encoding='utf-8'))
events=[]
for path in sorted(snapshot.glob('*.jsonl')):
    events.extend(json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines())
assert events and all(x['sessionId']==summary['session'] for x in events)
assert [x['sequence'] for x in events]==list(range(1,len(events)+1))
headers=[]; rows=[]; selections=[]; saves=[]; receipts=[]; released=[]; windows=[]
for event in events:
    message=event['message']
    for prefix, destination in [('LocalResourceReferenceProbe metadata ', headers),
                                ('LocalResourceReferenceProbe row ', rows),
                                ('LocalCatalogProbe SELECTED ', selections),
                                ('LocalCatalogProbe PREFERENCES SAVED ', saves),
                                ('LocalCatalogProbe PLAY RECEIPT ', receipts),
                                ('LocalCatalogProbe SELECTION RELEASED ', released),
                                ('LocalCatalogProbe WINDOW CREATED ', windows)]:
        if prefix in message:
            destination.append(dict(sequence=event['sequence'], value=json.loads(message.split(prefix,1)[1])))
assert len(headers)==1
count=headers[0]['value']['count']
assert len(rows)==count and [r['value']['index'] for r in rows]==list(range(count))
metadata={r['value']['evidence']['Id']:r['value']['evidence'] for r in rows}
assert metadata['sprite.concrete.ctor']['Values']['Rva']=='0xAEF2C0'
assert metadata['sprite.concrete.release']['Values']['Rva']=='0xAEF1F0'
assert metadata['sprite.concrete.release']['Values']['DeclaringType']=='ResourceReference<UnityEngine.Sprite>'
assert metadata['audio.concrete']['Status']=='unknown' and metadata['interface.dispatch']['Status']=='unknown'
assert [x['value']['Song']['Id'] for x in selections]==['validation-song-0','validation-song-1']
assert [x['value']['Generation'] for x in selections]==[1,2]
assert all(x['value']['IsCurrent'] for x in selections)
assert receipts and not receipts[0]['value']['Success'] and receipts[0]['value']['Error']['Code']==18
assert released and released[0]['value']==dict(reason='scene-change',generation=2)
result=dict(session=summary['session'],snapshot=str(snapshot),partialSession=True,
            records=len(events),errors=summary['errors'],
            packageManifest='../foundation-canvas-catalog-final-20261021/package-manifest.json',
            metadataHeader=headers[0],metadataRows=rows,selectionChanges=selections,
            preferenceSaves=saves,playReceipts=receipts,selectionReleases=released,windowsCreated=windows,
            limitations=['Original MainMenu window and clicks observed with Computer Use; returned FakeMainMenu did not remount.',
                         'No native local-song playback, live AudioClip concrete reference, or interface virtual dispatch proof.',
                         'Snapshot captured while game was running; normal exit and final cleanup are not established.'],
            inputs=[dict(name=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted(snapshot.glob('*.jsonl'))])
(root/'runtime-proof.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(records=len(events),metadataRows=count,selectionGenerations=[1,2],playUnsupported=True,sceneChangeRelease=True,partial=True)))
