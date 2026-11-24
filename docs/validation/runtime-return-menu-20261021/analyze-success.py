"""Validate the two frozen 789 sessions and the saved stable-ID preferences."""
from pathlib import Path
import hashlib, json
root=Path(__file__).resolve().parent
def load(prefix):
    folders=list(root.glob(prefix+'-*')); assert len(folders)==1
    folder=folders[0]; events=[]
    for path in sorted(folder.glob('*.jsonl')):
        events.extend(json.loads(line) for line in path.read_text(encoding='utf-8-sig').splitlines())
    assert events and [x['sequence'] for x in events]==list(range(1,len(events)+1))
    assert not [x for x in events if x['level'] in ('Error','Fatal')]
    assert not [x for x in events if any(p in x['message'] for p in ('LocalCatalogProbe WINDOW REFUSED', 'LocalCatalogProbe WINDOW RELEASE FAILED', 'LocalCatalogProbe OBSERVATION FAILED'))]
    return folder,events
def rows(events,label):
    prefix='LocalCatalogProbe '+label+' '
    return [dict(sequence=x['sequence'],value=json.loads(x['message'].split(prefix,1)[1])) for x in events if prefix in x['message']]
returned,a=load('menu-return-green'); restarted,b=load('restart-restoration-green')
windows=rows(a,'WINDOW CREATED'); selections=rows(a,'SELECTED'); released=rows(a,'SELECTION RELEASED')
return_windows=[x for x in windows if x['value']['_sceneGeneration']==4]; assert return_windows
assert any('PreviousScene = LevelList, Scene = FakeMainMenu' in x['message'] for x in a)
assert any(x['value']['generation']==2 and x['value']['reason']=='scene-change' for x in released)
restored=[x for x in selections if x['sequence']>return_windows[-1]['sequence']]
assert restored and restored[-1]['value']['Song']['Id']=='validation-song-1' and restored[-1]['value']['Generation']>2
play=rows(a,'PLAY RECEIPT'); assert play and play[-1]['value']['Generation']==restored[-1]['value']['Generation']
assert not play[-1]['value']['Success'] and play[-1]['value']['Error']['Code']==18
loaded=rows(b,'PREFERENCES LOADED'); assert loaded and loaded[0]['value']['LastSong']['Id']=='validation-song-1' and loaded[0]['value']['favorites']==1
selected=rows(b,'SELECTED'); assert selected and all(x['value']['Song']['Id']=='validation-song-1' and x['value']['IsCurrent'] for x in selected)
prefs=json.loads((root/'preferences-before-restart.json').read_text(encoding='utf-8-sig'))['data']
assert prefs['lastSong']['id']=='validation-song-1' and [x['id'] for x in prefs['favorites']]==['validation-song-0']
assert (root/'preferences-before-restart.json').read_bytes()==(root/'observed-local-catalog-selection.json').read_bytes()
restoration=json.loads((root/'probe-files-restoration.json').read_text(encoding='utf-8'))
assert all(x['restored']=='Absent' and not Path(x['path']).exists() for x in restoration['files'])
result=dict(installedBuild='../foundation-return-menu-final-20261021',
    sessions=[dict(id=events[0]['sessionId'],snapshot=folder.name,records=len(events),contiguous=True,errors=0,
                   files=[dict(name=p.name,sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in sorted(folder.glob('*.jsonl'))]) for folder,events in ((returned,a),(restarted,b))],
    returnedMenuWindows=return_windows,selectionChanges=selections,sceneRelease=released,returnedPlayReceipts=play,
    restartPreferences=loaded,restartSelections=selected,persistedPreferences=prefs,preferencesBytesUnchanged=True,
    temporaryFilesRestored=restoration,limitations=['Native local-song play and preview remain unsupported.',
    'One return flow and one process restart are proven; full B14 input/focus/gamepad and stress matrices remain incomplete.',
    'The separate 2-record Steam restart/options handoff was not a usable game session and remains preserved.'])
(root/'runtime-proof.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(sessions=[len(a),len(b)],returnRemounted=True,restartRestored=True,temporaryFilesRestored=6)))
