from pathlib import Path
import datetime, hashlib, json, os, subprocess, sys, time, traceback

O = Path(__file__).resolve().parent
S = O / 'source'
sys.stdout = (O / 'driver.stdout.log').open('x', encoding='utf-8', buffering=1)
sys.stderr = (O / 'driver.stderr.log').open('x', encoding='utf-8', buffering=1)
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
def save(p, v):
    t = p.with_suffix(p.suffix + '.writing')
    t.write_text(json.dumps(v, indent=2), encoding='utf-8')
    t.replace(p)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
preparation = read(O / 'preparation-result.json')
result = dict(kind='actual-stock-stop-after-handoff-with-final-native-r3', passed=False,
    runnerPid=os.getpid(), startedAtUtc=now(), preparation=fact(O / 'preparation-result.json'),
    sourceManifest=fact(O / 'source-manifest.json'), nativeInputs=preparation['nativeInputs'],
    rows=[], mainModified=False, productModified=False, sourceOriginModified=False,
    sdkValueTransactions=0, sdkSelectedRateRecovery=False, testedRate=1, gameExecuted=False, productionAdmission=False)
save(O / 'test-result.json', result)
env = {**os.environ, 'DOTNET_NOLOGO':'1', 'DOTNET_CLI_TELEMETRY_OPTOUT':'1',
       'MSBUILDDISABLENODEREUSE':'1', 'DOTNET_CLI_UI_LANGUAGE':'en', 'RIZ_INITIALIZER_TRACE':'1'}
def run(mode, command):
    row = dict(mode=mode, command=list(map(str, command)), startedAtUtc=now(), actualExitCode=None)
    with (O / (mode + '.stdout.log')).open('xb') as so, (O / (mode + '.stderr.log')).open('xb') as se:
        p = subprocess.Popen(row['command'], cwd=S, env=env, stdout=so, stderr=se, creationflags=subprocess.CREATE_NO_WINDOW)
        row['pid'] = p.pid
        save(O / (mode + '.launch.json'), row)
        print(json.dumps(dict(launched=mode, pid=p.pid)), flush=True)
        while p.poll() is None:
            result.update(livePid=p.pid, liveMode=mode, lastObservedAliveUtc=now())
            save(O / 'test-result.json', result)
            time.sleep(3)
    row.update(actualExitCode=p.returncode, finishedAtUtc=now(),
               stdout=fact(O / (mode + '.stdout.log')), stderr=fact(O / (mode + '.stderr.log')))
    (O / (mode + '.exit.txt')).write_text(str(p.returncode), encoding='utf-8')
    try:
        row['actual'] = json.loads((O / (mode + '.stdout.log')).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
    except (ValueError, IndexError):
        pass
    save(O / (mode + '.result.json'), row)
    print(json.dumps(dict(mode=mode, actualExitCode=p.returncode, checks=row.get('actual', {}).get('checks'))), flush=True)
    return row
try:
    config = O / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
    project = S / 'tests/RizToolbox.PlaybackStartInitialization.Tests/RizToolbox.PlaybackStartInitialization.Tests.csproj'
    dotnet = 'C:/Program Files/dotnet/dotnet.exe'
    result['build'] = run('build', [dotnet, 'build', project, '-c', 'Release', '-m:1', '--configfile', config,
                                  '-p:UseSharedCompilation=false', '--nologo', '-v:minimal'])
    save(O / 'test-result.json', result)
    assert result['build']['actualExitCode'] == 0
    app = project.parent / 'bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'
    save(O / 'build-products.json', [fact(p) for p in sorted(app.parent.iterdir()) if p.is_file()])
    native = [row['frozen']['path'] for row in preparation['nativeInputs']]
    for mode in ['stock-stop-handoff-same', 'stock-stop-handoff-cross']:
        row = run(mode, [dotnet, app, mode, *native])
        result['rows'].append(row)
        save(O / 'test-result.json', result)
        # Quota failures are preserved and never retried.
        if 'quota' in (O / (mode + '.stderr.log')).read_text(encoding='utf-8-sig', errors='replace').lower():
            result['quotaFailureNoRetry'] = True
            break
    manifest = read(O / 'source-manifest.json')['files']
    result['inputsUnchanged'] = all(sha(S / row['path']) == row['sha256'] for row in manifest)
    result['inputsUnchanged'] &= all(sha(Path(row['frozen']['path'])) == row['frozen']['sha256'] for row in preparation['nativeInputs'])
    origins = read(O / 'origin-source-manifest.json')['files']
    result['sourceOriginUnchanged'] = all(sha(O.parent / 'source' / row['path']) == row['sha256'] for row in origins)
    result['allModesPassed'] = len(result['rows']) == 2 and all(row['actualExitCode'] == 0 and row.get('actual', {}).get('passed') is True for row in result['rows'])
    result['checks'] = sum(row.get('actual', {}).get('checks', 0) for row in result['rows'])
    result['passed'] = bool(result['allModesPassed'] and result['inputsUnchanged'] and result['sourceOriginUnchanged'])
except BaseException:
    result['exception'] = traceback.format_exc()
    traceback.print_exc()
finally:
    result.update(finishedAtUtc=now(), livePid=None, liveMode=None)
    save(O / 'test-result.json', result)
    (O / 'driver.exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
raise SystemExit(0 if result['passed'] else 1)
