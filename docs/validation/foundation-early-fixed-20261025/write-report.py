"""Render the integration report from preserved evidence; never runs a fixture."""
import hashlib
import json
import re
import zipfile
from pathlib import Path


BATCH = Path(__file__).resolve().parent
REPO = BATCH.parents[2]
REPORT = REPO / "docs/2026-10-25_底层-固定发布早期链验证-report.md"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def require(value, message):
    if not value:
        raise ValueError(message)


def link(path, label=None):
    path = path.resolve()
    relative = Path(__import__("os").path.relpath(path, REPORT.parent)).as_posix()
    return f"[{label or path.name}]({relative})"


def archive_check(manifest_name, proof_name, archive_name, current_root):
    manifest_path, proof_path, archive_path = [BATCH / name for name in (manifest_name, proof_name, archive_name)]
    manifest, proof = read(manifest_path), read(proof_path)
    require(proof["allEntryHashesMatched"] and proof["entries"] == len(manifest["files"]), "Incomplete archive proof")
    require(proof["manifestSha256"] == digest(manifest_path) and proof["archiveSha256"] == digest(archive_path), "Archive/manifest proof hash mismatch")
    with zipfile.ZipFile(archive_path) as archive:
        require(sorted(archive.namelist()) == sorted(row["path"] for row in manifest["files"]), "Archive entry set differs from declared manifest")
        for row in manifest["files"]:
            require(hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"], "Archive entry hash mismatch")
            require(digest(current_root / row["path"]) == row["sha256"], "Current source/binary differs from archive")
    return manifest, proof


def generate():
    run, wrapper, summary, evidence = [read(BATCH / name) for name in ("component-run.json", "component.log.run.json", "build-summary.json", "early-fixed-evidence.json")]
    require(run["sessionId"] == summary["sessionId"] == 34351 and run["terminalExitCodeObserved"] == run["exitCode"] == wrapper["exitCode"] == 0,
            "An actual successful final terminal observation is required")
    require(run["finalCandidate"] and run["rootSoleBuilder"] and evidence["passed"] and evidence["allInputHashesUnchanged"], "Candidate or held input identity is incomplete")
    for name, field in (("build.log", "buildLogSha256"), ("test.log", "testLogSha256")):
        require(digest(BATCH / name) == run[field] == wrapper[field], "Preserved final log hash mismatch")
    sources, source_proof = archive_check("source-manifest.json", "source-archive-check.json", "source-inputs.zip", REPO)
    binaries, binary_proof = archive_check("binary-manifest.json", "binary-archive-check.json", "fixture-binaries.zip", BATCH / "raw/build")
    integrity = read(BATCH / "component-input-integrity-check.json")
    require(len(sources["files"]) == summary["componentSources"] == integrity["checked"] == 14 and integrity["preBuildMatchesArchived"] and not integrity["currentChanged"], "Fourteen-input freeze is incomplete")
    require(len(binaries["files"]) == summary["binaryFiles"] == 7, "Seven binary identities are required")
    expected = ["mode-14"] + [f"mode-{mode}" for mode in range(16) if mode != 14] + [f"fault-{stage}-{kind}" for stage in range(1, 15) for kind in range(4)] + ["normal-debugger"]
    cases = []
    require(len(expected) == summary["verifiedCases"] == evidence["verifiedCases"] == evidence["independentProcesses"] == 73, "Final matrix is incomplete")
    for name, saved in zip(expected, evidence["caseResults"], strict=True):
        path = BATCH / "raw/cases" / name
        case = read(path / "result.json")
        require(case == saved and case["passed"], "Preserved case differs from the aggregate")
        require(digest(path / "mapping-final.bin") == case["mappingSha256"] and digest(path / "mapping-initial.bin") == case["initialMappingSha256"], "Raw mapping hash mismatch")
        require(len(case["actualMemoryRows"]) == 4 and case["ownedChildPid"] > 0, "Actual memory/owned child evidence missing")
        cases.append(case)
    require(evidence["processes"][0]["name"] == "mode-14" and evidence["baselineFirst"], "Actual unpublished baseline did not execute first")
    checks = sum(case["checks"] for case in cases)
    originals = sum(case["workload"]["actualOriginalCalls"] for case in cases)
    pairs = sum(case["workload"]["actualObserverPairs"] for case in cases)
    caught = sum(case["workload"]["originalCaughtCalls"] for case in cases)
    require(checks == summary["independentAssertions"] == evidence["checks"], "Assertion total differs from raw cases")
    require(not summary["fullManagedSuitesRerun"] and not summary["gameExecuted"] and not summary["installed"], "Report scope changed")
    preservation = read(BATCH / "preservation-check.json")
    require(preservation["allMatched"] and len(preservation["installedFiles"]) == 18 and preservation["installedSourceArchiveEntries"] == 326
            and len(preservation["originalGameFiles"]) == 4 and all(row["expected"] == row["actual"] for row in preservation["installedFiles"] + preservation["originalGameFiles"])
            and [row["currentSourcesChecked"] for row in preservation["priorFrozenMilestones"]] == [576, 12]
            and all(not row["changed"] for row in preservation["priorFrozenMilestones"]), "Recorded historical/current identity preservation mismatch")
    by_name = dict(zip(expected, cases))
    baseline = by_name["mode-14"]
    flags = {fault: sorted({row["rawFlags"] for row in baseline["exceptionRecords"] if row["faultMode"] == fault}) for fault in (1, 2)}
    require(flags == {1: [0x80], 2: [0x81]}, "Baseline raw exception flags changed")
    for name in ("mode-0", "mode-3", "normal-debugger"):
        comparison = by_name[name]["exceptionBaselineComparison"]
        require(comparison["exactRawFieldsMatched"] and not comparison["sameProcessOrObjectClaim"], "Same-binary cross-child exception comparison missing")
    pe_path = BATCH / "raw/cases/pe-imports.json"
    pe = read(pe_path)
    require(pe["passed"] and pe["ordinaryStaticImportsVerified"], "Actual PE imports proof missing")
    busy = by_name["mode-15"]
    busy_event = next(row for row in busy["events"] if row["type"] == 65)
    require((busy_event["result"], busy_event["value0"], busy_event["value1"], busy_event["epoch"]) == (1, 2, 1, 3)
            and busy["publisherSnapshot"]["captureCalls"] == 4 and busy["publisherSnapshot"]["flags"] == 7, "Actual recursive Busy evidence differs")
    debug = by_name["normal-debugger"]
    require(debug["debuggerProof"]["instrumented"] and debug["debuggerProof"]["observedRows"] == 17, "Debugger supplement incomplete")
    require(digest(BATCH / "raw/cases/normal-debugger/debug-events.jsonl") == debug["debuggerProof"]["rawJsonlSha256"], "Debugger JSONL hash mismatch")

    artifact_rows = [
        ("E-01", "真实终态和日志", BATCH / "component-run.json", "C1；另核 build.log/test.log"),
        ("E-02", "73个子进程总原件", BATCH / "early-fixed-evidence.json", "C1；逐case mapping/result核对"),
        ("E-03", "真实PE普通依赖与固定前缀", pe_path, "C2；也见raw/build/*.pe.txt"),
        ("E-04", "未发布同binary基线", BATCH / "raw/cases/mode-14/result.json", "C3(mode-14)"),
        ("E-05", "正常真实加载链", BATCH / "raw/cases/mode-0/result.json", "C3(mode-0)"),
        ("E-06", "初始化请求worker", BATCH / "raw/cases/mode-3/result.json", "C3(mode-3)"),
        ("E-07", "实际递归Capture Busy", BATCH / "raw/cases/mode-15/result.json", "C3(mode-15)"),
        ("E-08", "加载器断点补充原件", BATCH / "raw/cases/normal-debugger/debug-events.jsonl", "C3(normal-debugger，加--debug-events)"),
        ("E-09", "实际一字节部分写入", BATCH / "raw/cases/fault-11-0/result.json", "C3(fault-11-0)"),
        ("E-10", "强制restore失败与同页责任", BATCH / "raw/cases/fault-13-0/result.json", "C3(fault-13-0)"),
        ("E-11", "14项源ZIP逐项证明", BATCH / "source-archive-check.json", "C1；ZIP逐项hash"),
        ("E-12", "7项二进制ZIP逐项证明", BATCH / "binary-archive-check.json", "C1；ZIP逐项hash"),
        ("E-13", "作者有限直接依赖复核", BATCH / "native-dependency-evidence.json", "只读PE/固定offset复核，见对应MD方法；非额外child运行"),
        ("E-14", "历史及现场静态hash保全", BATCH / "preservation-check.json", "只读hash比对；不启动游戏"),
    ]
    evidence_table = "\n".join(f"| {eid} | {title} | {link(path)} | `{digest(path)}` | {command} |" for eid, title, path, command in artifact_rows)
    case_table = "\n".join(f"| {name} | {case['ownedChildPid']} | {case['workload']['actualOriginalCalls']} | {case['workload']['actualObserverPairs']} | {case['workload']['originalCaughtCalls']} | {case['checks']} | {link(BATCH/'raw/cases'/name/'result.json', '实际结果')} |" for name, case in by_name.items() if name in ("mode-14", "mode-0", "mode-3", "normal-debugger", "mode-1", "mode-2", "mode-15"))
    binary_table = "\n".join(f"| {row['path']} | `{row['sha256']}` | {'精确复用，未重编' if row['path'] in ('RizFixedPublisher.dll', 'RizToolbox.JudgeContext.dll') else '本批编译'} |" for row in binaries["files"])
    source_table = "\n".join(f"| {link(REPO/row['path'], row['path'])} | `{row['sha256']}` |" for row in sources["files"])
    review_path = BATCH / "native-dependency-review.md"
    require(review_path.exists(), "Final author review is not yet saved")
    review_note = f"另见作者只读交叉审查 {link(review_path)}（SHA256 `{digest(review_path)}`）及E-13结构化原件。该作者同时实现Proxy/Recorder，审查没有额外运行child，不是对所有间接/OS/传递loader路径的独立全审。"
    text = f"""# 固定发布器接入真实早期宿主的验证报告

2026-10-25 · flavor=null · 自有Windows x64 fixture · 最终session {run['sessionId']}。

本批已把固定四入口publisher、helper与自有普通静态依赖链实际接通：{len(cases)}个独立子进程通过，父级从原始共享映射核对{checks:,}条断言，全部保存实际源页快照。正常冷启中的PrepareGroup、四个helper Configure、BindGroup、四次Commit与Ready确实先于Importer的TLS/C/C++/DllMain首次目标调用。原件合计{originals}次原调用、{pairs}对观察回调、{caught}次异常捕获，其中包含独立未发布基线和一个debugger补充进程。新增Capture重入故障保留永久Busy并在原生准入处终止；不同子进程间的异常flags使用真实基线精确比较。本批没有重跑上批25套1617回归、没有启动或安装游戏，也没有签发生产V4、取消完成或资源退休凭据。

## 范围与可复核结果

授权与范围：{link(BATCH/'scope.md')}。`auth`沿用用户继续底层代码与多代理工作的授权；`in_scope`为自有EarlyFixed组件及其父验证；`network_profile`为本地文件和自有child，不启动游戏、不访问QQ或在线系统；保全检查只读现有安装和原游戏文件hash。根代理独占构建/执行，父进程只终止自己启动的Process；失败和超时不能算通过。

| 项目 | 实际结果 | 证据 |
|---|---:|---|
| 最终wrapper | session{run['sessionId']}，实际terminal exit0 | E-01、{link(BATCH/'component.log.run.json')} |
| 当前声明源/二进制 | 14 / 7，逐项ZIP hash一致 | E-11、E-12、{link(BATCH/'component-input-integrity-check.json')} |
| 独立进程/已验证case | {len(cases)} / {len(cases)} | E-02 |
| 模式/内部fault/debugger | 16 / 56 / 1 | {link(BATCH/'build-summary.json')} |
| 父级断言 | {checks:,} | E-02；不等同游戏流程或原调用数量 |
| 实际源页快照 | 73份×4目标，mapping内保留 | E-02；每份含实际GetSystemInfo pageSize |
| 原调用/观察配对/异常捕获 | {originals} / {pairs} / {caught} | 逐case workload汇总，单位分列 |
| 完整托管回归 | 本批未重跑 | 历史99557才是25套1617、576源/22包 |

原调用分布可直接追到各child，正常/worker/debug各有8异常，基线另有8；prior-call与late各30次，所有其余早期拒绝/56fault均无原调用。

| child | 实际PID | 原调用 | 配对 | 捕获 | 父断言 | 原件 |
|---|---:|---:|---:|---:|---:|---|
{case_table}

## 普通依赖与实际发布顺序

E-03核实必需普通静态边：Host→Targets、Targets→Proxy、Proxy→Recorder/helper/publisher、Recorder→publisher；Host还实际直接导入Proxy/Recorder/helper，完整导入表保留，未把必需子图冒充唯一依赖。所有这些映像没有Dobby普通或delay导入。Proxy/Recorder没有CRT和delay导入，保留Kernel32及唯一NTDLL `__C_specific_handler` 原生SEH依赖；publisher本体也是Kernel32+该SEH handler，entryRva=0、无TLS。Host/Targets/helper仍有真实CRT及其他依赖，不能把整条进程称为无CRT。{review_note} {link(BATCH/'review.md','另一代理的有限交叉审查')}另核原始映射/页快照；该代理同时实现Importer/Host，相关部分仍是作者自检。

**P-01，path_type=callflow**：父进程锁住并hash核验7个映像、创建V2 mapping→Windows实际映射普通依赖→Recorder挂接共享区→Proxy一次PrepareGroup全部四项→四个helper Configure→一次BindGroup→按kind四次Commit→实际GetInfo/源页检查和Ready→Targets真实TLS、C、C++、DllMain工作→loader外Attach→嵌套、worker、SEH/C++→最终Capture与Complete。各步分别关联E-03、E-05、E-06和E-08。作用域仅本机自有启动链；不含真实系统WinHTTP放置、游戏首次调用时机或生产coordinator。

V2映射449120字节，事件仍是104字节定长row；末尾追加实际publisher Info21640、实际Memory240、SnapshotMeta32。父验证从保存字节切片读取，旧tuple不替代实际receipt。publisher epoch与helper epoch/slot28分别保存；原生Start/Release保留byte，Execute保留完整`0xa5c3fe81`，Detach为void。每个真实调用有永久callId，观察frame sequence/token与物理同线程LIFO清理分别核对。phase9用初始ref2，真实Release DEC到0时ZF=1；其他路径也核ZF=0，Detach实际清EBP。这些有限哨兵/返回/副作用样本不能泛化为所有寄存器或完整flags透明。

额外debugger child有{debug['debuggerProof']['observedRows']}条实际装载/入口/TLS进入返回记录（E-08）。其helper TLS返回sequence10、PROCESS_ATTACH返回12，均早于ProxyAttachBegin14；Ready50早于Targets TLS进入52。publisher只有实际Load序号5，PE本身无entry/TLS，因此没有伪造“publisher初始化返回”。Debugger仅创建自己的DEBUG_ONLY_THIS_PROCESS child，短暂内存断点属于仪器化补充，主要72个case不依赖debugger，不是游戏冷启或全部依赖闭包证明。

## 原始异常基线与Capture永久责任

mode14首先运行同一Targets二进制和相同导出RVA，经过真实四初始化及四类SEH/四类C++调用；零Prepare/Configure/Bind/Commit/Ready/Attach，68原调用、零观察。mode0、mode3及debugger随后逐kind、逐异常类别比较原始code、flags、参数数量、payload和kind。实际SEH flags为`0x80`，C++为`0x81`，没有mask为0；SEH三参数与C++四参数、真实typed catch另行核实。对象/类型/模块地址和callId按各自child局部事实检查，未要求跨进程地址相等，也未声称同进程、同对象或同run的完全等价。基线及三组匹配记录均在E-04～E-06、E-08关联case中保存。

mode15受控one-shot在外层**实际获得Capture gate后**递归调用同一API：内层CompareExchange真实遇Busy返回2；外层和gate释放后的独立Capture均返回1。E-07 event65实际字段是`result=outer1,value0=inside2,value1=later1,epoch=captureCalls3`；随后Fail前第四次实际Capture仍保flags7（Info1|Memory2|Busy4），原生stateUnknown5/failure22。实际publisher Fresh、零写/Prepare/目标调用，Proxy/Importer未进入；header里的module0表示未记录初始化，不能解释为未映射，独立ActualMemory已读到实际映射Targets的4个地址和源字节。这是受控真实重入冲突，未声称自然线程race、任意并发故障或OS失败都已验证。

## 56个内部故障与独立页快照

14个内部stage各在四个kind独立运行：Protect/Write/Readback/Restore/Flush前后十个边界、真实PartialWrite、三个强制API结果分支。每次都先实际Prepare/Configure/Bind，故障后阻止Importer继续；只有实际之前成功的kind带committed权限。源尝试、全写、逐byte计数、readback、实际API调用/成功、恢复/flush及permanentprepared/bound责任分别核；API完成意图不能充当真实写。

stage11真实写**1字节**，其余31字节与持有PE原始字节逐项比较，不能沿用独立组件的partial3字节计数。stage12/13/14分别是protect/restore/flush的**强制结果注入**：未把selector执行成真实OS API失败，也未伪增actual API调用数。stage13保留protectionOutstanding，所有与它共享系统page的实际目标可读到RWX；父用当前240字节快照的VirtualQuery.Protect和实际地址独立核所有受影响row，不仅相信receipt。尚未证明自然OOM、真实系统API失败、CPU在部分写期间并发执行或任意SEH中断窗口。

其他拒绝项按实际literal facts区分：wrong-module给实际Prepare错误模块；wrong-hash给实际Prepare翻一位SHA；wrong-architecture、prefix、ABI是实际值对受控错误expected；missing-dependency是查受控缺失名字，实际publisher仍普通静态映射；dependency-not-ready是明确admission selector。prior-call/late有真实先前原调用，late还保留实际已运行且未结束worker；reentry是实际producer递归claim；duplicate是在四写/历史Ready之后再次claim，外层Unknown保留而publisher自身仍记录此前真实Ready。日志溢出是实际填满固定容量，不能授Ready。

## Evidence → Finding

以下Evidence均为`source_type=file`；observed_at以E-01实际终态记录、case内sequence及E-13/14自带analysisUtc/checkedAtUtc为准；`artifact_path/source_ref`使用表中本地原件，`content_hash`为SHA256。`linked_workitem=EarlyFixed集成`、`supersedes=none`。原始摘录：E-01 `exitCode=0,sessionId=34351`；E-02 `passed=true,verifiedCases=73,checks=13031`；E-03 `ordinaryStaticImportsVerified=true`；E-04/05/06实际originals为68/86/92；E-07 `flags=7,captureCalls=4`；E-08有17row；E-09 `sourceBytesWritten=1`；E-10 `protectionOutstanding=1`；E-11/12 `allEntryHashesMatched=true,entries=14/7`；E-13 `authorAlsoWroteProxyRecorder=true,independentFullAudit=false`；E-14 `allMatched=true`。C1/C2/C3在复核命令节有上下文。

| E-id | title | source_ref / artifact_path | content_hash | repro_command |
|---|---|---|---|---|
{evidence_table}

| F-id | severity / category | status / confidence | evidence_ids | location | 结论及影响 |
|---|---|---|---|---|---|
| F-01 | n/a_re / design | validated / high | E-01,E-03,E-05,E-08 | Proxy.Install、实际普通PE imports | 无Dobby固定四入口在同一自有早期链完成发布；仅本binary/system样本 |
| F-02 | n/a_re / other | validated / high | E-04,E-05,E-06,E-08 | Importer.Filter/typed catch、父exception_records | 原始异常稳定字段跨child精确匹配；保留局部指针差异和基线边界 |
| F-03 | n/a_re / design | validated / high | E-03,E-07 | Recorder_Capture/Recorder_Fail | 实际递归Busy触发原生永久责任并拒绝继续；非自然race证明 |
| F-04 | n/a_re / design | validated / high | E-02,E-09,E-10 | fixed receipt + ActualMemory | 56故障保留真实写/保护责任，73份实际页快照独立核对；不扩张OS故障证明 |
| F-05 | n/a_re / other | validated / high | E-01,E-11,E-12 | 构建manifest、ZIP、实际日志 | 冻结14项声明源/7二进制，精确复用两DLL；不是全部工具链归档或生产构建准入 |

## 输入闭合与版本身份

14源包含新builder/parent、V2协议和实际Importer/Host/debugger，以及编译使用的旧FixedTargets.asm、FixedPublisher.h、EngineNodeContext.h；旧冻结fixed verifier是新Python实际import的传递输入，已显式归档，未修改。源码ZIP `{source_proof['archiveSha256']}`，二进制ZIP `{binary_proof['archiveSha256']}`；原件见{link(BATCH/'source-inputs.zip')}、{link(BATCH/'fixture-binaries.zip')}。manifest证明14项与构建前冻结/当前源均一致，7项与raw/build实际文件均一致；声明闭包不含MSVC/SDK或整套Windows。

| 二进制 | SHA256 | 来源 |
|---|---|---|
{binary_table}

publisher与helper分别精确复用session5523组件和99557早期里程碑DLL，**本批未重编这两个DLL**。Targets二进制本批含实际Importer工作与固定ASM，同上批独立Targets不是同一binary。旧12/576 source ZIP、manifest、证据均保持历史，不把当前14输入扩称完整源码发布包。上批控制50PC展开及红样本仍属于{link(REPO/'docs/2026-10-25_底层-固定发布组件验证-report.md','独立组件报告')}；本批PE核同一静态桩布局，未重跑逐PC RtlVirtualUnwind或声称任意PC真实异常。

E-14只读保全结果还核对旧576/12项当前声明源、旧1021安装的18文件与326源ZIP均保持匹配，Rizline.exe、GameAssembly.dll、global-metadata.dat、UnityPlayer.dll四项原游戏hash匹配。静态读取与未启动游戏并不冲突；没有把这项hash保全当成真实游戏运行验收。

| 声明源 | SHA256 |
|---|---|
{source_table}

## 复核与复现

C1是离线报告重建：只读JSON、ZIP及逐项hash，核73份mapping/result与aggregate一致，不启动任何child。报告生成器仅写本报告。

```powershell
Set-Location 'C:/Users/71957/Desktop/riz_toolbox'
& 'C:/Python314/python.exe' 'docs/validation/foundation-early-fixed-20261025/write-report.py'
```

C2按冻结验证器复核实际PE；C3按具体case重建离线结果。以下输出使用全新临时目录，不覆盖归档原件；normal/worker/debug必须传实际mode14基线，debug额外传其原始JSONL。mapping在归档，PE proof中路径指向实际持有的原binary缓存；若迁移机器，先按manifest恢复binary并重建PE proof路径。没有归档的编译器/SDK需另行准备。

```powershell
$repo='C:/Users/71957/Desktop/riz_toolbox'
$batch=Join-Path $repo 'docs/validation/foundation-early-fixed-20261025'
$out=Join-Path ([IO.Path]::GetTempPath()) ('RizEarlyFixedReview-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$verify=Join-Path $batch 'verify-early-fixed.py'
& 'C:/Python314/python.exe' $verify --inspect-images (Join-Path $batch 'raw/build') --output (Join-Path $out 'pe.json')
$case=Join-Path $batch 'raw/cases/mode-0'
$result=Get-Content -LiteralPath (Join-Path $case 'result.json') -Raw | ConvertFrom-Json
Copy-Item -LiteralPath (Join-Path $case 'mapping-final.bin') -Destination (Join-Path $out 'mapping-final.bin')
& 'C:/Python314/python.exe' $verify --mapping (Join-Path $out 'mapping-final.bin') --initial (Join-Path $case 'mapping-initial.bin') --pe-proof (Join-Path $out 'pe.json') --child-pid $result.ownedChildPid --child-exit $result.actualExitCode --baseline (Join-Path $batch 'raw/cases/mode-14/result.json') --output (Join-Path $out 'mode-0.json')
```

C3底层验证器会在`--mapping`旁写两个派生切片（publisher-info/fixed-pe），示例已先复制mapping到临时目录，归档保持只读。本报告生成过程未重新运行该验证器，未产生归档变更。实际执行矩阵的入口为{link(REPO/'scripts/Build-EarlyFixedFixture.ps1')}和{link(REPO/'scripts/Test-EarlyFixedFixture.ps1')}，root当次参数见{link(BATCH/'component.log.run.json')}及{link(BATCH/'run-component.ps1')}；重新执行需新build/cases目录与精确两DLL输入，测试runner自己不重建共享项目。

## Timeline与下一步

| 时间/记录 | 实际事项 |
|---|---|
| case内sequence | mode14先执行未发布基线；mode0/3实际Ready先于四初始化；mode15保留真实Busy递归/拒绝 |
| {wrapper['completedAtUtc']} | 构建与73个case wrapper实际exit0，build/test日志hash已写入原件 |
| {run['recordedAtUtc']} | root记录session34351真实terminal0及finalCandidate |
| {summary['completedAtUtc']} | 14源/7binary/73页快照汇总与逐项ZIP证明完成 |

本批完成的是同一自有加载链的准入与责任验证。下一步应给真实游戏建立独立固定映像/V4生产凭据、系统WinHTTP转发与真实冷启时机，再让{link(REPO/'src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs','NativeEngineNodeContextBridge.AdoptConfigured')}从该凭据只读采用；当前该API仍是internal受控桥，fixture tuple不能成为生产授权。随后按{link(REPO/'docs/validation/foundation-engine-binding-20261025/next-integration.md','身份接线计划')}把实际owner/root/provider、三类tracker与coordinator接到同一真实链，分别验证IL2CPP对象、GC与代次。scheduler/async/futureStart、Start内联清理、所有消费者drain和资源退休仍须补齐。

本批{link(BATCH/'next-production-contract.md','下一生产接入合同')}已将顺序固定为bootstrap/系统转发与独立producer→V4只读采用→coordinator/GC与调度责任→完整流程验收；按该合同继续实现，不能只改自有模块名或删除marker后进入游戏。

完整{link(REPO/'.docs/foundation-expansion/SPEC.md','B01–B19规格')}保持active，当前checked acceptance仍仅B03整项/B18a子项；本报告不增加勾选。`gameFirstInstallVerified=false`、`productionV4Issued=false`、`allTransitiveLoaderDependenciesSafeVerified=false`、`EngineCancelled=false`；Pending保留、RequestPlay关闭，实测按用户“稍后”的安排继续。当前集成未写入游戏，也未作为新的游戏安装包。
"""
    # Resolve every report-local Markdown target. Anchors do not require files.
    for target in re.findall(r"\]\(([^)]+)\)", text):
        if "://" in target or target.startswith("#"):
            continue
        require((REPORT.parent / target.split("#", 1)[0]).resolve().exists(), f"Missing report link: {target}")
    REPORT.write_text(text, encoding="utf-8")
    print(json.dumps(dict(report=str(REPORT), sha256=digest(REPORT), sourceEntries=len(sources["files"]), binaryEntries=len(binaries["files"]), cases=len(cases), parentAssertions=checks, originals=originals, pairs=pairs, caught=caught), ensure_ascii=False))


if __name__ == "__main__":
    generate()
