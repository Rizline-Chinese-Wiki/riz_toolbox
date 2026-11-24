"""Bind current game startup observations to exact installed candidate bytes."""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import shutil

CACHE=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115')
GAME=Path('D:/SteamLibrary/steamapps/common/Rizline')
sha=lambda path:hashlib.sha256(Path(path).read_bytes()).hexdigest()
read=lambda path:json.loads(Path(path).read_text(encoding='utf-8-sig'))
parser=argparse.ArgumentParser()
parser.add_argument('--run',choices=('runtime-r1','runtime-r2','runtime-r3'),required=True)
parser.add_argument('--install',choices=('install-r2','install-r3','install-r4'),required=True)
parser.add_argument('--pid',type=int,required=True)
parser.add_argument('--doorstop-log',required=True)
parser.add_argument('--observed-tool-chunk',required=True)
parser.add_argument('--exit-tool-chunk',required=True)
parser.add_argument('--exit-kind',choices=('normal','owned-unresponsive-test-terminated'),default='normal')
parser.add_argument('--ui-observation',required=True)
args=parser.parse_args()
OUT=CACHE / args.run
if OUT.exists():raise RuntimeError('Preserve previous startup observation')
install_path=CACHE / args.install / 'result.json';install=read(install_path)
if not(install['passed'] and install['installed'] and install['actualInstallerExitCode']==0):raise RuntimeError('Current installed candidate unverified')
if not all(sha(GAME / row['path'])==row['sha256'] for row in install['packageFiles']):raise RuntimeError('Installed candidate differs')
if '/' in args.doorstop_log or '\\' in args.doorstop_log or not args.doorstop_log.startswith('doorstop_'):raise RuntimeError('Unexpected Doorstop log filename')
native=GAME / 'BepInEx/RizToolbox/native-preinit.log'
records=[line for line in native.read_text(encoding='utf-8-sig').splitlines() if line.startswith('native-preinit-v1 pid='+str(args.pid)+' ')]
if not records:raise RuntimeError('Native diagnostic process identity absent')
OUT.mkdir()
shutil.copyfile(native,OUT / 'native-preinit-all.log')
(OUT / 'native-preinit-current.log').write_text('\n'.join(records)+'\n',encoding='utf-8')
shutil.copyfile(GAME / args.doorstop_log,OUT / 'doorstop.log')
before=CACHE / args.install / 'before/runtime-data'
log=GAME / 'BepInEx/RizToolbox/toolbox.log';status=GAME / 'BepInEx/RizToolbox/status.json'
delta=log.read_bytes()[(before / 'toolbox.log').stat().st_size:]
(OUT / 'toolbox-current-session.log').write_bytes(delta)
text=delta.decode('utf-8-sig',errors='replace')
changed=sha(status)!=sha(before / 'status.json')
if changed:shutil.copyfile(status,OUT / 'current-status.json')
data=dict(schema='current-combined-startup-observation/v1',recordedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
    gameProcessId=args.pid,installResultSha256=sha(install_path),exact23InstalledFilesVerified=True,
    observationToolChunk=args.observed_tool_chunk,exitObservationToolChunk=args.exit_tool_chunk,exitKind=args.exit_kind,
    nativeRecords=records,loaderStarted='RizToolbox 0.3 starting;' in text,unityTickBound='Unity tick bound;' in text,
    hostInitialized='Unity main-thread host initialized.' in text,statusChanged=changed,
    moduleManifestFailure='Module manifest or interval is invalid' in text,
    gameSdkStartupVerified=False,gameRoundPlayed=False,uiObservation=args.ui_observation,
    files={path.name:sha(path) for path in OUT.iterdir() if path.is_file()},
    noNewGameBinaryContentAnalysis=True,mainApplied=False,sdkGoalComplete=False,completeSeekPermitted=False)
(OUT / 'observation.json').write_text(json.dumps(data,indent=2)+'\n',encoding='utf-8')
print(json.dumps(dict(recorded=True,pid=args.pid,nativeRecords=records,loaderStarted=data['loaderStarted'],hostInitialized=data['hostInitialized'],
    statusChanged=changed,receiptSha256=sha(OUT / 'observation.json'))))
