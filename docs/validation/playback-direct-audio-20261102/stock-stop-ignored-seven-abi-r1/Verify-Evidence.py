from pathlib import Path
import datetime, difflib, hashlib, importlib.util, json, os, sys
R=Path(__file__).resolve().parent
N=Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102/native-r3')
EXPECTED='75a41e3e715e7025386a75a993e369e8e22aef3e66eb5de9becd5899a0424490'
def read(p): return json.loads(Path(p).read_text(encoding='utf-8-sig'))
def sha(p): return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def fact(p):
    p=Path(p); return dict(path=str(p),sha256=sha(p),bytes=p.stat().st_size)
def save(p,v): Path(p).write_text(json.dumps(v,indent=2),encoding='utf-8')
O=R/'evidence-r1'; O.mkdir(exist_ok=False)
reader=Path('C:/Users/71957/Desktop/riz_toolbox/scripts/Verify-PlaybackSourcePrototype.py')
spec=importlib.util.spec_from_file_location('native_source_proof',reader)
module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
r1=read(R/'run-n3-r1/test-result.json'); r2=read(R/'run-n3-r2-worker/test-result.json')
b1=read(R/'build-r1/build-result.json'); b2=read(R/'build-r2/build-result.json')
main=[x for x in r1['rows'] if not x['actuals'][-1]['mode'].startswith('worker')]
firstWorker=[x for x in r1['rows'] if x['actuals'][-1]['mode'].startswith('worker')]
assert len(main)==136 and all(x['passed'] and x['actualExitCode']==0 for x in main)
assert len(firstWorker)==28 and sum(not x['passed'] for x in firstWorker)==14
assert len(r2['rows'])==28 and r2['passed'] and all(x['passed'] and x['actualExitCode']==0 for x in r2['rows'])
assert r1['counts']==dict(cases=164,passed=150,checks=18494,failures=14) and r1['passed'] is False
assert b1['passed'] and b2['passed'] and b1['build']['actualExitCode']==b2['build']['actualExitCode']==0
assert r1['inputUnchanged'] and r2['inputUnchanged']
images=[module.image_identity(image,pdb) for image,pdb in [
    (N/'RizToolbox.PlaybackSourcePrototype.dll',N/'PlaybackSourcePrototype.pdb'),
    (R/'build-r1/IgnoredSevenAbiFixture.exe',R/'build-r1/IgnoredSevenAbiFixture.pdb'),
    (R/'build-r2/IgnoredSevenAbiFixture.exe',R/'build-r2/IgnoredSevenAbiFixture.pdb')]]
objects=[module.compiled_sources(p) for p in [
    N/'PlaybackSourceContext.obj',N/'PlaybackWriterCall.obj',
    R/'build-r1/IgnoredSevenAbiFixture.obj',R/'build-r1/IgnoredSevenAbiCaller.obj',
    R/'build-r2/IgnoredSevenAbiFixture.obj',R/'build-r2/IgnoredSevenAbiCaller.obj']]
assert images[0]['imageSha256']==EXPECTED
actualSources=[s for obj in objects for s in obj['sources']]
for p in [N/'frozen-source/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp',
          N/'frozen-source/native/RizToolbox.PlaybackSourcePrototype/PlaybackPlayerWatch.inc',
          N/'frozen-source/native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc',
          R/'build-r1/frozen-source/IgnoredSevenAbiFixture.cpp',R/'build-r1/frozen-source/IgnoredSevenAbiCaller.asm',
          R/'build-r2/frozen-source/IgnoredSevenAbiFixture.cpp',R/'build-r2/frozen-source/IgnoredSevenAbiCaller.asm']:
    assert any(Path(x['path']).resolve()==p.resolve() and x['matchesActualCompiledObject'] for x in actualSources),str(p)
helperCopies=[fact(R/x/'RizToolbox.PlaybackSourcePrototype.dll') for x in ('run-n3-r1','run-n3-r2-worker')]
assert all(x['sha256']==EXPECTED for x in helperCopies)
old=(R/'build-r1/frozen-source/IgnoredSevenAbiFixture.cpp').read_text(encoding='utf-8')
new=(R/'build-r2/frozen-source/IgnoredSevenAbiFixture.cpp').read_text(encoding='utf-8')
assert new==old.replace('h.Flags==(nestedEnabled?RtSourceUnknown:RtSourceArmed)','h.Flags==(nestedEnabled?(RtSourceUnknown|RtSourceDormant):RtSourceArmed)')
diff=''.join(difflib.unified_diff(old.splitlines(keepends=True),new.splitlines(keepends=True),fromfile='build-r1/frozen-source/IgnoredSevenAbiFixture.cpp',tofile='build-r2/frozen-source/IgnoredSevenAbiFixture.cpp'))
(O/'fixture-only-normalized.diff').write_text(diff,encoding='utf-8')
def counts(rows): return dict(cases=len(rows),checks=sum(x['actuals'][-1]['checks'] for x in rows),failures=sum(x['actuals'][-1]['failures'] for x in rows),pids=[x['pid'] for x in rows])
mainProof=counts(main); workerProof=counts(r2['rows'])
assert mainProof['checks']==16660 and workerProof['checks']==1834
proof=dict(kind='independent-seven-abi-signed-boundary-source-binary-proof',passed=True,observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),verifierPid=os.getpid(),
    primaryEvidence=dict(firstMainThread=mainProof,correctedWorker=workerProof,
        firstAggregatePreserved=dict(passed=False,cases=164,passedCases=150,failures=14),
        sameProductHelperSha256=EXPECTED,correction='Only fixture ReadOwner Flags assertion: Unknown|Dormant instead of Unknown. Python write_text also normalized source-r2 to CRLF; normalized source has exactly one assertion-line difference.'),
    images=images,compiledObjects=objects,compiledObjectCount=len(objects),compiledSourceRecordCount=len(actualSources),helperCopies=helperCopies,
    sourceInputs=[fact(reader),fact(__file__),fact(Path(sys.executable))],buildResults=[fact(R/'build-r1/build-result.json'),fact(R/'build-r2/build-result.json')],
    runResults=[fact(R/'run-n3-r1/test-result.json'),fact(R/'run-n3-r2-worker/test-result.json')],
    fixtureOnlyNormalizedDiff=fact(O/'fixture-only-normalized.diff'),gameExecuted=False,installed=False,productionAdmission=False)
save(O/'verification-result.json',proof)

report=f'''# 2026-11-02 StockStopV1 ignored 七种 ABI 独立验证

签收范围仅限固定 n3 helper 的 ignored 调用 TLS 屏障、透明 ABI 参数/AL 返回、SEH 恢复及线程责任。没有修改产品、安装或运行游戏，也未访问 UI、账号、提交或推送。

## 结果与证据

| 批次 | 实际结果 | 检查数 | 证据 |
| --- | --- | --- | --- |
| r1 主线程子集 | 136/136 进程 exit 0 | 16,660，0 失败 | `run-n3-r1/test-result.json` 的前 136 项 |
| r2 worker 复验 | 28/28 进程 exit 0 | 1,834，0 失败 | `run-n3-r2-worker/test-result.json` |
| r1 首轮 aggregate，原样保留 | 150/164 通过；14 个 worker nested 断言失败 | 18,494，14 失败 | `run-n3-r1/test-result.json` |

r1 的唯一失败断言把 ReadOwner 的 Flags 预期写成 Unknown(4)；InfoLocked 会追加 Dormant(2)，因此正确可见值为 6。r2 只修改该测试断言，产品 helper 完全相同。r2 源文本同时发生 Python 默认 CRLF 规范化；规范化后仅该一行语义差异，见 `evidence-r1/fixture-only-normalized.diff`。首轮的 false aggregate 未覆盖、未重新解释为全通过。

所有批次加载的实际 DLL SHA-256：`{EXPECTED}`。DLL 与 PDB 原生 GUID/age 对应，两个 fixture EXE/PDB 也均对应；6 个 COFF 对象、{len(actualSources)} 条 C13 编译源记录均与当前冻结文件 checksum 相符。见 `evidence-r1/verification-result.json`，其包含三个 PE/PDB 的 hash、GUID/age、原始 PDB 路径，以及 COFF 和源文件 hash。

## 已证边界

自有内存建立 current Player/AudioSource/Progress 图，通过实际 helper GetEntry 调用真实 outer Pause/Replay original。foreign Player 与 current Player 不同但共享 AudioSource；AudioSource ABI 使用另一 foreign source；BooleanReturn0 使用 foreign Progress/Start 内存。

7 种 ABI（Void0、Single1、Double1、Ref1、Bool1、UInt64_1、BooleanReturn0）各自的 foreign original 恰好执行一次。它们嵌套的 current AudioStop 全部 ParentSequence=0、Depth=1、Before Flags=1，没有借用外层 scope，没有 StockStop 128。foreign 自身无 observer/journal/fence，主线程每 case 仅真实 outer、独立 current stop、恢复后的 own child、outer 退出后的 current stop 共 4 条物理 journal。每条实际完成 receipt 具备 Returned/PhysicalExited。

normal 与 SEH 各 68 项：foreign 在其 current leaf 返回后抛出 `0xe2740317`，异常原码到达 outer original 的 catch；随后 own current child 的 ParentSequence=1、Depth=2，outer 退出后的 leaf ParentSequence=0、Depth=1。已产生独立 current 干扰，因此恢复后的 own child 仍不得签 StockStop 128；本测试明确断言它为 Flags=1。

Single/Double 的正负零、普通值、带 payload 的 quiet/signaling NaN、无穷、subnormal 与全 1 位模式均原位透传。Ref1 包括 null、1、高位模式，UInt64 包括 0/1/高位/全 1。Self 和非规范高位 MethodInfo 指针均逐位一致，未解引用 foreign 参数。Bool1 的 00/01/80/ff 原 byte 一致。BooleanReturn0 原函数由 MASM 直接设置 AL=00/01/80/ff，调用者在实际 helper 返回后立即 movzx AL，未经过 C++ bool 归一化，结果一致。

worker 每 ABI 的 ignored-only normal/SEH 都没有当前 owner 的 journal、fence、thread fault 或回调；foreign original 内若实际触达 current AudioStop，则该 leaf 承担恰好 1 个 thread fault，observer 抑制，实际 receipt ThreadId 为 worker、ParentSequence=0、Flags=45。ReadOwner 投影 Flags=6，原异常仍保持。

## 可复现命令和进程

命令在本目录执行，新的 build/run 目录已被保留；直接重执行会拒绝覆盖证据。每个 native case 的精确 argv、PID、起止时间、exit、stdout/stderr hash 已在对应 `*.launch.json` / `*.result.json` 中记录。

```powershell
& 'C:/Python314/python.exe' './Build-And-Run.py' build
& 'C:/Python314/python.exe' './Build-And-Run.py' run
& 'C:/Python314/python.exe' './Prepare-WorkerR2.py'
& 'C:/Python314/python.exe' './Build-And-Run-WorkerR2.py' build
& 'C:/Python314/python.exe' './Build-And-Run-WorkerR2.py' run
& 'C:/Python314/python.exe' './Verify-Evidence.py'
```

r1 build runner PID {b1['runnerPid']}，实际 cmd PID {b1['build']['pid']}，exit 0；r2 build runner PID {b2['runnerPid']}，实际 cmd PID {b2['build']['pid']}，exit 0。r1 test driver PID {r1['runnerPid']}（原 aggregate exit 1），r2 test driver PID {r2['runnerPid']}（exit 0）。本校验 PID {os.getpid()}。

编译参数 `/O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf`，ASM 具备 unwind 元数据。精确 cl/ml64/link/dumpbin 命令见 `build-r1/build.cmd`、`build-r2/build.cmd`，编译日志实际 exit 0、stderr 空；原始 PE headers、pdbpath、unwindinfo 均附带。

## Evidence → Finding → Path

- Evidence：r1 前 136 项物理帧/receipt；r2 28 项 worker receipt；原始进程命令与退出；PE/PDB/C13 源校验。
- Finding：在这些已执行的 7 ABI、参数位模式和 SEH 路径中，n3 ignored original 不借用 outer scope，恢复正确，原函数/参数/AL/异常保持，线程责任按实际 current leaf 归属。
- Path：`source*` → `build-*/frozen-source` → COFF/C13 → fixture PE/PDB → 固定 n3 DLL/PDB → 每 case `*.result.json` → 两个批次 `test-result.json` → `evidence-r1/verification-result.json`。

不扩大签收为游戏实机、所有槽位/全部角色组合、managed publication、writer TLS 或未执行的异常类别/运行时。本任务未重跑其他回归，没有对 n2 运行本夹具。
'''
reportPath=R/'2026-11-02_ignored-seven-abi-report.md'
reportPath.write_text(report,encoding='utf-8')
manifest=dict(kind='independent-seven-abi-artifacts',createdAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),files=[fact(p) for p in sorted(R.rglob('*')) if p.is_file() and p.name!='artifact-manifest.json'],gameExecuted=False)
save(R/'artifact-manifest.json',manifest)
print(json.dumps(dict(passed=True,mainThread=mainProof,worker=workerProof,compiledObjects=len(objects),compiledSourceRecords=len(actualSources),verification=fact(O/'verification-result.json'),report=fact(reportPath),artifactManifest=fact(R/'artifact-manifest.json'))),flush=True)
