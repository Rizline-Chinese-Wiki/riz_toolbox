"""Wait read-only for the existing observer, then verify once. Never collects/tests."""
from pathlib import Path
from datetime import datetime, timezone
import hashlib
import json
import os
import subprocess
import time
import traceback

R=Path(__file__).resolve().parent
U=R.parent
OUT=R/'review-watcher-result.json'
assert not OUT.exists(),'Preserve the existing review watcher.'
now=lambda:datetime.now(timezone.utc).isoformat()
read=lambda p:json.loads(p.read_text(encoding='utf-8-sig'))
fact=lambda p:dict(path=str(p),sha256=hashlib.sha256(p.read_bytes()).hexdigest(),bytes=p.stat().st_size)

def save(value):
    temporary=OUT.with_suffix('.writing')
    temporary.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    temporary.replace(OUT)

result=dict(kind='wait-existing-sdk-observer-then-independent-read-only-review',state='waiting',passed=False,
  runnerPid=os.getpid(),parentPid=os.getppid(),startedAtUtc=now(),validator=fact(R/'Verify-ResourceSdkDisposal.py'),
  testsStarted=0,buildsStarted=0,collectorsStarted=0,sourceWrites=False,protectedPackageWrites=False,
  processesKilled=False,processesRestarted=False,mainApplied=False,fullIncrementAcceptanceComplete=False)
try:
    while True:
        observer_path=U/'sdk-disposal-observer-r1/observer-result.json'
        observer=read(observer_path)
        result.update(observedAtUtc=now(),originalObserverPid=observer['runnerPid'],originalObserverState=observer['state'])
        if observer.get('finishedAtUtc'):
            result['originalObserver']=fact(observer_path)
            if not (observer['state']=='collected' and observer['passed'] is True and observer['collectorActualExitCode']==0):
                result.update(state='original-observer-not-collected-retained',passed=False)
                break
            assert fact(R/'Verify-ResourceSdkDisposal.py')==result['validator'],'Independent validator changed after watcher start.'
            with (R/'final-verifier.stdout.log').open('xb') as stdout,(R/'final-verifier.stderr.log').open('xb') as stderr:
                process=subprocess.Popen(['C:/Python314/python.exe','-B',str(R/'Verify-ResourceSdkDisposal.py')],
                  cwd=R,stdout=stdout,stderr=stderr,creationflags=subprocess.CREATE_NO_WINDOW)
                result.update(state='verifying',validatorPid=process.pid)
                save(result)
                result['validatorActualExitCode']=process.wait()
            result.update(validatorStdout=fact(R/'final-verifier.stdout.log'),validatorStderr=fact(R/'final-verifier.stderr.log'))
            closure_path=R/'final-closure-review.json'
            if closure_path.is_file():
                closure=read(closure_path)
                result['finalReview']=fact(closure_path)
                result.update(state='verified' if result['validatorActualExitCode']==0 and closure.get('autoCoverageClosed') is True else 'validation-failed-retained',
                  passed=result['validatorActualExitCode']==0 and closure.get('autoCoverageClosed') is True)
            else:
                result.update(state='validation-failed-no-closure',passed=False)
            break
        save(result)
        time.sleep(15)
except BaseException:
    result.update(state='review-watcher-error-retained',passed=False,failure=traceback.format_exc())
finally:
    result['finishedAtUtc']=now()
    save(result)
    (R/'review-watcher.exit.txt').write_text('0' if result['passed'] else '1',encoding='utf-8')
    manifest_path=R/'review-hash-manifest.json'
    files=[]
    for path in sorted(R.rglob('*')):
        if path.is_file() and path!=manifest_path:
            row=fact(path);row['path']=path.relative_to(R).as_posix();files.append(row)
    manifest_path.write_text(json.dumps(dict(kind='independent-read-only-sdk-review-final-sha256',files=files),indent=2)+'\n',encoding='utf-8')
raise SystemExit(0 if result['passed'] else 1)
