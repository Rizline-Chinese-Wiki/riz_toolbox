"""Freeze current V7 dependencies and Single-owned inputs, then run focused real executables in fresh paths."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile, ZIP_DEFLATED
import argparse, hashlib, json, re, shutil, subprocess

parser=argparse.ArgumentParser()
parser.add_argument('--output',required=True)
args=parser.parse_args()
root=Path(__file__).resolve().parents[3]
output=Path(args.output).resolve()
if output.exists():raise SystemExit('Use a fresh output directory; existing runs are never overwritten.')
output.mkdir(parents=True)
source=output/'source-snapshot';source.mkdir()
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
owned=[
 'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventTypes.cs',
 'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs',
 'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventPhysicalParent.cs',
 'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventRootBatch.cs',
 'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventTestAssembly.cs',
 'src/RizToolbox.Runtime/NativeRuntime.SingleExecutingList.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedEvents.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.SingleDispatch.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedEvents.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedEffects.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedDispatch.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenSingleEventTracker.cs',
 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenSingleEventTestAssembly.cs']
owned.extend(str(p.relative_to(root)).replace('\\','/') for p in (root/'tests/RizToolbox.SingleEvent.Tests').glob('*') if p.suffix in ['.cs','.csproj'])
paths=[root/name for name in ['Directory.Build.props','global.json','dependencies.lock.json']]
for directory in ['src','tests']:
 paths.extend(p for p in (root/directory).rglob('*') if p.is_file() and p.suffix in ['.cs','.csproj','.json'] and not {'obj','bin'}.intersection(p.parts))
rows=[]
for path in sorted(set(paths)):
 relative=path.relative_to(root);target=source/relative;target.parent.mkdir(parents=True,exist_ok=True);before=digest(path);shutil.copyfile(path,target)
 assert digest(target)==before and digest(path)==before,relative
 rows.append({'path':relative.as_posix(),'sha256':before,'bytes':path.stat().st_size})
for row in rows:assert digest(root/row['path'])==row['sha256'],row['path']
(output/'source-before.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
with ZipFile(output/'source-snapshot.zip','w',ZIP_DEFLATED) as archive:
 for row in rows:archive.write(source/row['path'],row['path'])
manifest={'observedAtUtc':datetime.now(timezone.utc).isoformat(),'kind':'current-V7-managed-Single-controlled-source','gameExecuted':False,
 'nativeCallbackReadCurrentPositiveVerified':False,'effectsCompletionVerified':False,'sourceFiles':len(rows),
 'sourceArchiveSha256':digest(output/'source-snapshot.zip'),'ownedInputs':[r for r in rows if r['path'] in owned],
 'builds':[],'tests':[]}
artifacts=output/'artifacts'
for project in ['SingleEvent','AudioContent']:
 name=f'RizToolbox.{project}.Tests';command=['dotnet','build',str(source/'tests'/name/(name+'.csproj')),'-c','Release','--artifacts-path',str(artifacts),'--verbosity','minimal']
 print('BUILD',name,flush=True)
 result=subprocess.run(command,cwd=source,capture_output=True,text=True,encoding='utf-8',errors='replace')
 (output/f'{project}.build.stdout.log').write_text(result.stdout,encoding='utf-8');(output/f'{project}.build.stderr.log').write_text(result.stderr,encoding='utf-8')
 manifest['builds'].append({'project':name,'command':command,'exitCode':result.returncode,'warningCount':next((int(n) for n in re.findall(r'(\d+) 个警告',result.stdout)[-1:]),None)})
 if result.returncode:print(result.stdout[-5000:],flush=True);break
 dll=artifacts/'bin'/name/'release'/(name+'.dll')
 if project=='SingleEvent':names=[None]
 else:
  text=(source/'tests/RizToolbox.AudioContent.Tests/NativeTweenTypedEventTests.cs').read_text(encoding='utf-8').split('private static void CapturePlay()')[0]
  names=re.findall(r'\("([^"]+)",\s*\w+\)',text)+['Tween completion refuses an unassociated returned inline child and a typed invocation despite native return and disposal']
 for i,case in enumerate(names):
  command=['dotnet',str(dll)]+([] if case is None else ['--isolated-case',case]);ran=subprocess.run(command,cwd=source,capture_output=True,text=True,encoding='utf-8',errors='replace')
  (output/f'{project}.{i:02d}.stdout.log').write_text(ran.stdout,encoding='utf-8');(output/f'{project}.{i:02d}.stderr.log').write_text(ran.stderr,encoding='utf-8')
  manifest['tests'].append({'project':project,'case':case,'command':command,'exitCode':ran.returncode,'summary':[line for line in ran.stdout.splitlines() if re.search(r'\d+/\d+',line)],'assemblySha256':digest(dll)})
  print('TEST',project,i,ran.returncode,manifest['tests'][-1]['summary'],flush=True)
  (output/'test-result.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
after=[{'path':r['path'],'sha256':digest(source/r['path']),'bytes':(source/r['path']).stat().st_size} for r in rows]
manifest['sourceUnchanged']=rows==after
manifest['ownedWorktreeUnchanged']=all(digest(root/r['path'])==r['sha256'] for r in manifest['ownedInputs'])
manifest['passed']=len(manifest['builds'])==2 and len(manifest['tests'])==18 and all(r['exitCode']==0 for r in manifest['builds']+manifest['tests']) and manifest['sourceUnchanged'] and manifest['ownedWorktreeUnchanged']
(output/'source-after.json').write_text(json.dumps(after,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(output/'test-result.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps({'passed':manifest['passed'],'sourceFiles':len(rows),'sourceUnchanged':manifest['sourceUnchanged'],'ownedWorktreeUnchanged':manifest['ownedWorktreeUnchanged'],'sourceArchiveSha256':manifest['sourceArchiveSha256']}),flush=True)
raise SystemExit(0 if manifest['passed'] else 1)
