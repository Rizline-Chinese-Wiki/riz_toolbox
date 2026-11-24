from pathlib import Path
import hashlib, json
root=Path(__file__).resolve().parent
folders=list(root.glob('assetref-owner-dispatch-*')); assert len(folders)==1
snapshot=folders[0]; events=[]
for path in sorted(snapshot.glob('*.jsonl')):
    events.extend(json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines())
assert events and [x['sequence'] for x in events]==list(range(1,len(events)+1))
assert not [x for x in events if x['level'] in ('Error','Fatal')]
def parse(prefix):
    return [dict(sequence=x['sequence'],value=json.loads(x['message'].split(prefix,1)[1])) for x in events if prefix in x['message']]
header=parse('LocalResourceReferenceProbe existing-owner '); rows=parse('LocalResourceReferenceProbe owner-row ')
assert len(header)==1 and header[0]['value']['Stage']=='existing-owner:InsideLevelParams'
assert len(rows)==header[0]['value']['count']==10
assert [x['value']['index'] for x in rows]==list(range(10))
evidence={x['value']['evidence']['Id']:x['value']['evidence'] for x in rows}
for role,resource in [('cover','Sprite'),('music','AudioClip')]:
    dispatch=evidence['existing.'+role+'.dispatch']; release=evidence['existing.'+role+'.resolved.release']
    assert dispatch['Status']=='resolved-methodinfo'
    assert dispatch['Values']['ObjectType']=='AssetRefReference<UnityEngine.'+resource+'>'
    assert release['Values']['DeclaringType']==dispatch['Values']['ObjectType']
    assert release['Values']['Rva']=='0xB115D0' and release['Values']['MethodInfoSlot']=='5'
    assert release['Values']['EntryStatus'].startswith('Verified:')
    assert evidence['existing.'+role+'.interface.release']['Values']['MethodInfoSlot']=='1'
    assert evidence['existing.'+role+'.concrete']['Status']=='unknown'
starts=parse('Observed game.run.started: '); finishes=parse('Observed game.run.finished: ')
assert len(starts)==len(finishes)==1
assert starts[0]['value']['RunId']==finishes[0]['value']['RunId']=='eacc57ce75d1488e92e775cbd99ecf2c'
assert finishes[0]['value']['AssociatedWithObservedStart'] and finishes[0]['value']['EndReason']==2
assert finishes[0]['value']['Miss']==479 and finishes[0]['value']['Crashed']
restored=json.loads((root/'probe-file-restoration.json').read_text(encoding='utf-8'))
assert restored['restored']=='Absent' and not Path(restored['path']).exists()
result=dict(session=events[0]['sessionId'],snapshot=snapshot.name,records=len(events),contiguous=True,errors=0,
            packageManifest='../foundation-resource-owner-20261021/package-manifest.json',
            header=header[0],ownerRows=rows,start=starts[0],finish=finishes[0],probeRestoration=restored,
            limitations=['This game used AssetRefReference<T>; no live ResourceReference<AudioClip> was established.',
                         'Official virtual resolver proves this existing object dispatch; it is not a class/vtable memory walk.',
                         'No Release/getter invocation, wrapper construction, local music/cover playback, or final resource destruction proof.',
                         'The naturally reached result was Failed with 479 misses; it does not satisfy normal successful completion.'],
            files=[dict(name=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted(snapshot.glob('*.jsonl'))])
(root/'runtime-proof.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(records=len(events),ownerRows=10,actualTypes=['AssetRefReference<Sprite>','AssetRefReference<AudioClip>'],releaseRva='0xB115D0',result='Failed',markerRestored=True)))
