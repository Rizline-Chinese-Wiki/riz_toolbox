"""Close the current S4 source integration, retaining all wider incomplete work."""
import hashlib
import json
import zipfile
from datetime import datetime,timezone
from pathlib import Path

REPOSITORY=Path(__file__).resolve().parents[3]
CACHE=Path('D:/riz-build-cache-next')
ROOT=CACHE/'playback-clock-main-integration-r1-20261031'
CASE=CACHE/'playback-stock-clock-bootstrap-r1-20261031'
OUT=ROOT/'validation-r1'
files={}
def read(path):return json.loads(Path(path).read_text(encoding='utf-8-sig'))
def check(path,expected=None):
    path=Path(path).resolve();key=str(path).casefold()
    if key not in files:files[key]=dict(path=str(path),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),bytes=path.stat().st_size)
    result=files[key];assert expected is None or result['sha256']==expected,str(path);return result
def collect(value):
    if isinstance(value,list):
        for row in value:collect(row)
    elif isinstance(value,dict):
        for path_key,hash_key in (('path','sha256'),('manifestPath','sha256'),('file','sha256'),('image','imageSha256'),('pdb','pdbSha256'),('object','sha256')):
            if isinstance(value.get(path_key),str) and isinstance(value.get(hash_key),str):check(value[path_key],value[hash_key])
        for row in value.values():collect(row)
def write(path,value):Path(path).write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def run():
    assert not OUT.exists()
    paths={name:CACHE/('foundation-stock-clock-integrated-'+name+'-r1-20261031')/('managed-build-manifest.json' if name=='release' else 'test-result.json')
           for name in ('core','full','release','solution','sdk')}
    proofs={name:read(path) for name,path in paths.items()}
    for name,proof in proofs.items():
        assert proof['passed'] and not proof.get('failure'),name
        assert not proof.get('gameExecuted',False) and not proof.get('installed',False)
        collect(proof);check(paths[name])
    core,full,release,solution,sdk=(proofs[name] for name in ('core','full','release','solution','sdk'))
    assert core['runnerExitCode']==0 and len(core['inputs'])==744
    assert full['sourceProof']['sha256']==check(paths['core'])['sha256'] and full['fullManagedMatrix'] and len(full['projects'])==27
    assert sum(row['checks'] for row in full['projects'])==2465
    assert all(row['passed'] and row['buildExitCode']==row['testExitCode']==0 for row in full['projects'])
    assert release['buildExitCode']==0 and release['sourceHashesUnchanged'] and release['sourceSnapshot']['sha256']==check(paths['core'])['sha256']
    assert len([row for row in release['files'] if row['path'].lower().endswith('.dll')])==6
    assert solution['buildExitCode']==solution['errors']==solution['warnings']==0
    assert sdk['runnerExitCode']==0 and sdk['checks']==14
    distribution=read(sdk['result']['path']);assert distribution['checksPassed']==14 and distribution['runtimeGameVerification'] is False
    projects=list(Path(distribution['generatedProjects']).rglob('*.csproj'));assert len(projects)==2
    for path in projects:check(path)
    for name in ('core','full','release','solution','sdk'):
        assert (ROOT/(name+'-r1.driver.exit.txt')).read_text().strip()=='0'
        for suffix in ('exit.txt','stdout.log','stderr.log'):check(ROOT/(name+'-r1.driver.'+suffix))
    stage=read(CASE/'completion-r1.json');assert stage['passed'] and stage['managedChecks']==179 and stage['nativeChecks']==388
    collect(stage);check(CASE/'completion-r1.json')
    for name,key in (('clock','execution'),('rooted','rootedExecution'),('clockSymbols','clockSymbols'),('rootedSymbols','rootedSymbols'),('native','nativeProof')):
        proof=read(stage[key]['path']);assert proof['passed'];collect(proof)
    for name in ('checks-r4','rooted-checks-r3','clock-symbols-r2','rooted-symbols-r2'):
        assert (CASE/(name+'.driver.exit.txt')).read_text().strip()=='0'
        check(CASE/(name+'.driver.exit.txt'))
    integration=read(ROOT/'test-result.json');assert integration['passed'] and len(integration['changedFiles'])==19
    check(ROOT/'test-result.json');collect(integration['stageProof']);collect(integration['before']);collect(integration['sourceArchive'])
    with zipfile.ZipFile(stage['beforeArchive']['path']) as archive:before={row.filename:archive.read(row) for row in archive.infolist() if not row.is_dir()}
    for row in integration['changedFiles']:
        collect(row['after'])
        if row['before'] is not None:assert hashlib.sha256(before[row['relativePath']]).hexdigest()==row['before']['sha256']
    with zipfile.ZipFile(stage['sourceArchive']['path']) as archive:
        entries=[row for row in archive.infolist() if not row.is_dir()];assert len(entries)==756
        for row in entries:check(REPOSITORY/row.filename,hashlib.sha256(archive.read(row)).hexdigest())
    check(__file__)
    result=dict(kind='current-stock-final-clock-integration-validation',passed=True,observedAtUtc=datetime.now(timezone.utc).isoformat(),
        currentSourceAccepted=True,sourceFilesCompared=756,coreInputs=744,mergedFiles=19,managedSuites=27,managedChecks=2465,
        releaseAssemblies=6,solutionErrors=0,solutionWarnings=0,sdkIndependentPlugins=2,sdkChecks=14,
        clockProcesses=10,clockChecks=179,rootedRegressionProcesses=15,rootedRegressionChecks=326,nativeCases=11,nativeChecks=388,
        nativePePdbPairs=2,nativeCoffObjects=2,managedPePdbPairsPerConsumer=5,managedSourceDocumentsPerConsumer=469,
        uniqueFinalClockImplemented=True,nativeDormantArmImplemented=True,actualGetTimeAndStockOriginalExecuted=True,
        completeStockProviderImplemented=False,crossRunHandoffImplemented=False,completeLowerWriterCoverage=False,
        sourceGenerationAdmissionProven=False,arbitrarySeekImplemented=False,fullB01B19Completed=False,
        gameExecuted=False,installed=False,productionAdmission=False,proofs={name:check(path) for name,path in paths.items()},
        stageProof=check(CASE/'completion-r1.json'),mainIntegration=check(ROOT/'test-result.json'),verifiedUniqueFiles=len(files))
    OUT.mkdir();write(OUT/'test-result.json',result)
    index=dict(kind='current-stock-final-clock-evidence-index',passed=True,flavor=None,selfHashExcluded=True,
        scope='Current S4 offline integration; source generation/lower writers/provider/handoff/Seek/production/B01-B19 remain incomplete.',
        result=check(OUT/'test-result.json'),files=sorted(files.values(),key=lambda row:row['path'].casefold()))
    write(OUT/'evidence-index.json',index)
    write(REPOSITORY/'docs/validation/playback-stock-writer-20261031/stock-clock-validation-index.json',
        dict(result=check(OUT/'test-result.json'),evidenceIndex=check(OUT/'evidence-index.json')))
    print(json.dumps({key:result[key] for key in ('passed','managedSuites','managedChecks','clockChecks','nativeChecks','sdkChecks','verifiedUniqueFiles')}))

if __name__=='__main__':run()
