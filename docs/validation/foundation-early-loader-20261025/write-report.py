"""Write the owned early-loader report only after final full/build/archive proof.

Rechecks existing bytes, JSON and ZIP entries. Replays only the offline verifier
against preserved mappings; never starts a child, builds or modifies inputs.
"""
import argparse
import hashlib
import importlib.util
import json
import re
import zipfile
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
previous = batch.parent / "foundation-engine-binding-20261025"
destination = repo / "docs/2026-10-25_底层-自有早期加载验证-report.md"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def link(name, label=None):
    return f"[{label or name}](validation/{batch.name}/{name})"


def must_false(value, keys):
    assert all(value[key] is False for key in keys), keys


def archive_check(proof, manifest, current):
    rows = manifest["files"]
    assert proof["allEntryHashesMatched"] and proof["entries"] == len(rows)
    assert sha(batch / proof["archive"]) == proof["archiveSha256"]
    with zipfile.ZipFile(batch / proof["archive"]) as archive:
        assert archive.testzip() is None and set(archive.namelist()) == {row["path"] for row in rows}
        for row in rows:
            assert hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"], row["path"]
            if current:
                assert sha(repo / row["path"]) == row["sha256"], row["path"]


def main():
    run = read(batch / "build-run.json")
    wrapper = read(batch / "build.log.run.json")
    summary = read(batch / "build-summary.json")
    assert run["exitCode"] == run["terminalExitCodeObserved"] == wrapper["exitCode"] == 0
    assert run["rootSoleBuilder"] and wrapper["rootSoleBuilder"]
    assert run.get("finalCandidate", True) is not False
    must_false(run, ["installed", "gameExecuted"])
    assert summary["projects"] == len(summary["suites"]) == 25 and summary["passed"] == summary["total"]
    assert all(row["passed"] == row["total"] for row in summary["suites"])
    assert sha(batch / "build.log") == run["logSha256"] == wrapper["logSha256"] == summary["logSha256"]
    assert not wrapper.get("targeted", False)

    source_proof, package_proof, source_manifest, package_manifest = [read(batch / name) for name in
        ["source-archive-check.json", "package-archive-check.json", "source-manifest.json", "package-manifest.json"]]
    archive_check(source_proof, source_manifest, True)
    archive_check(package_proof, package_manifest, False)
    assert sha(batch / "source-manifest.json") == source_proof["manifestSha256"]
    assert package_proof["entries"] == 22 and not package_proof["installed"]
    integrity = read(batch / "build-input-integrity-check.json")
    assert integrity["preBuildManifestMatchesArchivedManifest"] and integrity["currentChanged"] == []
    assert read(batch / "source-integrity-check.json")["changed"] == []
    assert read(batch / "staging-check.json")["allMatched"]
    old_install, game = read(batch / "previous-installation-check.json"), read(batch / "original-game-check.json")
    assert old_install["allMatched"] and old_install["archivedSourceInputsChecked"] == 326 and len(old_install["files"]) == 18
    assert game["allUnchanged"] and len(game["files"]) == 3
    assert all(row["expected"] == row["actual"] for proof in (old_install, game) for row in proof["files"])
    old_manifest = read(previous / "source-manifest.json")
    old_hashes = {row["path"]: row["sha256"] for row in old_manifest["files"]}
    source_hashes = {row["path"]: row["sha256"] for row in source_manifest["files"]}
    added = sorted(source_hashes.keys()-old_hashes.keys())
    changed = sorted(path for path in source_hashes.keys() & old_hashes.keys() if source_hashes[path] != old_hashes[path])
    removed = sorted(old_hashes.keys()-source_hashes.keys())
    assert "docs/validation/foundation-early-loader-20261025/verify-early-loader.py" in source_hashes
    diff = read(batch / "changes-from-engine-binding.json")
    assert diff["bothFrozenArchivesVerified"] and (diff["added"], diff["changed"], diff["removed"]) == (added, changed, removed)
    failed_root = batch / "before-overflow-event-align"
    failed = read(failed_root / "build-run.json")
    failed_wrapper = read(failed_root / "build.log.run.json")
    assert failed["sessionId"] == 46741 and run["sessionId"] != failed["sessionId"]
    assert failed["exitCode"] == failed["terminalExitCodeObserved"] == failed_wrapper["exitCode"] == 1
    assert failed["finalCandidate"] is False and failed["sourceArchiveCreated"] is False and failed["fullSuitesReached"] is False
    assert len(failed["sourceDrift"]) == 2 and failed["failedCase"] == "mode-12"
    assert sha(failed_root / "build.log") == failed["logSha256"] == failed_wrapper["logSha256"]
    failed_case = read(failed_root / "early-loader-cases/mode-12/result.json")
    assert failed_case["passed"] is False and failed_case["error"] == "EvidenceError: Unknown event id 17"
    assert not (failed_root / "source-inputs.zip").exists()
    failed_inputs = read(failed_root / "build-inputs-before.json")["files"]
    failed_hashes = {row["path"]: row["sha256"] for row in failed_inputs}
    assert len(failed_inputs) == failed["declaredInputs"] == len(source_hashes)
    for row in failed["sourceDrift"]:
        assert failed_hashes[row["path"]] == row["beforeSha256"] and source_hashes[row["path"]] == row["currentSha256"]
    research = read(batch / "research/dobby-loader-evidence.json")
    assert research["kind"] == "pinned-dobby-loader-static-audit" and research["boundaries"]["diskOnly"]
    must_false(research["boundaries"], ["nativeExecuted", "loaderLockSafeProved", "gameReady", "fullIndirectClosureProved",
        "allFailurePathsSafeProved", "osImportImplementationAudited", "ordinaryImportPresenceIsActualCallProof",
        "debugNormalRunClosesUntakenBranches", "linearRFDecodeIsInstructionReachability"])
    assert research["source"]["sha256"] == "8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded"
    assert research["selectedImportPaths"]["prepareDirect"]["LoadLibraryExW"]["rva"] == "0x10d50"
    research_rows = "\n".join(f"| {link(name)} | `{sha(batch / name)}` |" for name in
        ["dobby-loader-audit.md", "research/dobby-loader-evidence.json", "research/inspect-dobby-loader.py"])

    raw_build = batch / "native-build/early-loader-fixture"
    raw = batch / "native-build/early-loader-cases"
    build_manifest = read(raw_build / "fixture-build-manifest.json")
    evidence = read(batch / "early-loader-evidence.json")
    assert summary["earlyLoader"] == evidence == read(raw / "fixture-result.json")
    assert evidence["passed"] and evidence["kind"] == "owned-early-loader-parent-evidence" and evidence["rootSoleBuilder"]
    assert evidence["independentProcesses"] == evidence["verifiedCases"] == evidence["expectedCases"] == 39
    assert evidence["allInputHashesUnchanged"] and evidence["debuggerSupplementIncluded"]
    must_false(evidence, ["sharedProjectsRebuiltByRunner", "realSystemWinHttpVerified", "gameFirstInstallVerified",
        "productionV4Issued", "allTransitiveLoaderDependenciesSafeVerified", "schedulerClosureVerified",
        "engineCancellationCompleted", "installed", "gameCodeExecuted"])
    assert len(evidence["caseResults"]) == len(evidence["processes"]) == 39
    assert evidence["checks"] == sum(row["checks"] for row in evidence["caseResults"])
    assert len(build_manifest["files"]) == 7
    for file in build_manifest["files"]:
        assert sha(raw_build / file["name"]) == file["sha256"]
    for file in build_manifest["sources"]:
        source_path = Path(file["path"]).resolve()
        assert sha(source_path) == file["sha256"]
        assert source_hashes[source_path.relative_to(repo).as_posix()] == file["sha256"]
    for file in evidence["files"]:
        assert file["beforeSha256"] == file["afterSha256"] == sha(raw_build / file["name"])
    for file in evidence["sources"]:
        assert sha(Path(file["path"])) == file["sha256"]

    # This executes only the parser/assertions over already captured files.
    verifier_path = batch / "verify-early-loader.py"
    spec = importlib.util.spec_from_file_location("owned_early_offline_verifier", verifier_path)
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    pe = read(raw / "pe-imports.json")
    assert pe["passed"] and pe["ordinaryStaticImportsVerified"] and len(pe["images"]) == 6
    images = {row["name"]: row for row in pe["images"]}
    for file in build_manifest["files"]:
        if file["name"] in images:
            assert images[file["name"]]["sha256"] == file["sha256"]
    assert len(pe["edges"]) == 5
    names = set()
    modes = []
    for process, case in zip(evidence["processes"], evidence["caseResults"]):
        name = process["name"]
        assert name not in names
        names.add(name)
        folder = raw / name
        assert process == read(folder / "process.json")
        assert case == read(folder / "result.json")
        assert process["started"] and process["exactOwnedProcess"] and not process["timedOut"]
        assert case["passed"] and case["checks"] > 0
        assert (case["mode"], case["faultStage"], case["faultIndex"]) == (process["mode"], process["faultStage"], process["faultIndex"])
        assert process["debugger"] == (name == "normal-debugger")
        actual = verifier.verify_mapping(argparse.Namespace(mapping=folder/"mapping-final.bin", initial=folder/"mapping-initial.bin",
            pe_proof=raw/"pe-imports.json", child_pid=case["ownedChildPid"], child_exit=process["exitCode"],
            debug_events=folder/"debug-events.jsonl" if process["debugger"] else None))
        assert actual == case, name
        if not process["debugger"]:
            assert case["ownedChildPid"] == process["pid"]
        modes.append((name, case))
    expected_names = {f"mode-{mode}" for mode in range(14)} | {f"fault-{stage}-{index}" for stage in range(1,7) for index in range(4)} | {"normal-debugger"}
    assert names == expected_names
    normal = next(case for name, case in modes if name == "mode-0")
    worker = next(case for name, case in modes if name == "mode-3")
    debug = next(case for name, case in modes if name == "normal-debugger")
    assert normal["workload"]["actualOriginalCalls"] == 86 and worker["workload"]["actualOriginalCalls"] == 92
    assert normal["workload"]["actualObserverPairs"] == worker["workload"]["actualObserverPairs"] == 56
    assert debug["debuggerProof"]["actualDependencyInitializationReturnBeforeProxyVerified"]
    original_total = sum(case["workload"]["actualOriginalCalls"] for _, case in modes)
    observer_total = sum(case["workload"]["actualObserverPairs"] for _, case in modes)
    caught_total = sum(case["workload"]["originalCaughtCalls"] for _, case in modes)
    assert all((raw / name / "child.stdout.log").is_file() and (raw / name / "child.stderr.log").is_file() for name in names)
    assert (batch / "scope.md").is_file()

    suite_rows = "\n".join(f"| {row['summary']} | {row['passed']}/{row['total']} |" for row in summary["suites"])
    mode_rows = "\n".join(f"| `{name}` / {case['modeName']} | {case['checks']} | {case['header']['state']} / {case['header']['failure']} | {case['header']['writeAttempts']}/{case['header']['writeCompletions']} | {case['workload']['actualOriginalCalls']} / {case['workload']['actualObserverPairs']} |" for name, case in modes if not case["faultStage"])
    fault_rows = "\n".join(f"| `{name}` / {case['faultName']} | {case['faultIndex']} | {case['header']['state']} | {case['header']['writeAttempts']}/{case['header']['writeCompletions']} | {sum(row['prepared'] for row in case['tuples'])}/{sum(row['configured'] for row in case['tuples'])}/{sum(row['committed'] for row in case['tuples'])} |" for name, case in modes if case["faultStage"])
    debug_rows = []
    debug_module_names = {1:"dobby.dll",2:"RizToolbox.JudgeContext.dll",3:"RizOwnedRecorder.dll",4:"RizOwnedProxy.dll",5:"RizOwnedImporter.dll"}
    for module_id, name in debug_module_names.items():
        rows = [row for row in debug["events"] if row["kind"] == module_id and row["type"] >= 1000]
        sequence = lambda kind: ", ".join(str(row["sequence"]) for row in rows if row["type"] == kind) or "无（PE中无该项）"
        debug_rows.append(f"| `{name}` | {sequence(1002)} → {sequence(1003)} | {sequence(1004)} → {sequence(1005)} |")
    debug_table = "\n".join(debug_rows)
    source_rows = "\n".join(f"| [{name}](../{name}) | {'新增' if name in added else '修改'} | `{source_hashes[name]}` |" for name in added+changed)
    binary_rows = "\n".join(f"| `{row['name']}` | `{row['sha256']}` |" for row in build_manifest["files"])
    imports = "\n".join(f"| `{row['name']}` | {', '.join('`'+entry['module']+'`' for entry in row['imports'])} | {len(row['delayImports'])} |" for row in pe["images"])
    command = f"& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/{batch.name}/run-build.ps1"
    report_command = f"C:/Python314/python.exe docs/validation/{batch.name}/write-report.py"
    specs = [
        ("E-001","root实际完整构建终态","build-run.json",command,f"session={run['sessionId']}; terminal=0; suites25 {summary['passed']}/{summary['total']}; additional owned processes39"),
        ("E-002","七个自有宿主输入与实际构建清单","native-build/early-loader-fixture/fixture-build-manifest.json",command,"7 binaries; pinned Dobby; 8 native sources plus build script; no fixture success inferred from compilation"),
        ("E-003","普通PE导入、TLS与真实导出元数据","native-build/early-loader-cases/pe-imports.json",report_command,"AMD64 images6; actual ordinary import edges5; no corresponding delay import; real importer TLS callbacks and four nonforwarded target exports"),
        ("E-004","无调试器normal实际初始化与调用链","native-build/early-loader-cases/mode-0/result.json",command,f"checks={normal['checks']}; originals86; observers56; Ready before TLS/C/Cpp/Dll; Execute EAX0xa5c3fe81"),
        ("E-005","38主进程及24个逐点故障原件","early-loader-evidence.json",command,f"39 including separate debug; independent assertions={int(evidence['checks'])}; exact NoWrite/Unknown/tuples/commits; no game"),
        ("E-006","真实先前调用与live worker的迟到拒绝","native-build/early-loader-cases/mode-2/result.json",command,"actual initializers4 + worker target calls; worker started/not finished at late admission; stateNoWrite; attempts0"),
        ("E-007","单独调试器真实DLL/TLS入口及返回","native-build/early-loader-cases/normal-debugger/debug-events.jsonl",command,f"debugInstrumented=true; rows={debug['debuggerProof']['observedRows']}; same global sequences; helper/Dobby returns before ProxyEnter {debug['header']['proxyEnterSequence']}"),
        ("E-008","完整声明源码ZIP逐项冻结","source-inputs.zip",report_command,f"entries={source_proof['entries']}; includes offline Python verifier; diff +{len(added)}/~{len(changed)}/-{len(removed)}"),
        ("E-009","22项包归档和未安装边界","package-archive-check.json",report_command,"22 entries matched; old installation18/source326 and originalgame3 checks; new package uninstalled"),
        ("E-010","真实失败的event17不匹配与源码漂移历史","before-overflow-event-align/build-run.json",report_command,
            "session46741 terminal1; mode12 Unknown event id17; sourceDrift2; fullSuitesReached=false; sourceArchiveCreated=false; finalCandidate=false"),
        ("E-011","固定Dobby loader闭包有限静态审查","research/dobby-loader-evidence.json",
            f"C:/Python314/python.exe docs/validation/{batch.name}/research/inspect-dobby-loader.py",
            "actual HeapAlloc/logging/LoadLibraryExW/locks candidates; RF/CHAININFO and selected fresh vtable; loaderLockSafeProved/fullIndirectClosureProved/allFailurePathsSafeProved/gameReady=false"),
    ]
    evidence_text = "\n".join(f"""### {eid}：{title}

- observed_at: `{run['recordedAtUtc']}`（本报告采用root终态记录时间；逐进程时刻/序号见原件）。
- source_type: `file`；source_ref: {link(path)}。
- content_hash: `{sha(batch / path)}`；artifact_path: `docs/validation/{batch.name}/{path}`。
- repro_command: `{repro}`（root唯一builder；本文生成器只离线复核）。
- raw_excerpt: `{excerpt}`。
- linked_workitem: `n/a`（普通兼容工程）；supersedes: `none`，上批冻结原件不改。
""" for eid, title, path, repro, excerpt in specs)
    body = f"""# 自有早期加载与四入口首次发布验证报告

2026-10-25。flavor=null，overlay=null。作者实现父进程runner、独立PE/共享字节验证器并只读复核debugger/build；native/Importer/Host和debugger由同组代理实现。本文是参与实施者的有限交叉核对，不称外部独立审计。

## 已完成的具体结果

本批在独立自有进程中实现普通静态依赖链，实际四项Prepare全部完成，再全部不可变Configure，随后逐项DobbyCommit及发布字节/保护复核；FixtureReady实际早于importer TLS、C/C++ initializer、DllMain及最早目标调用。无调试器normal、worker、24个逐点故障及其他准入分支共38进程通过；另有1个独立debugger normal样本，实际记录helper/Dobby的DLL入口和存在的TLS callback正常返回早于Proxy首次安装。父进程独立断言共{int(evidence['checks'])}项，原始MMF与JSONL均保留。

root观察最终完整构建session `{run['sessionId']}` exit0：原25套{summary['passed']}/{summary['total']}继续全部通过，新增39个自有子进程另列，不将两种计数混成同一测试套。声明源码{source_proof['entries']}项、暂存包22项逐entry匹配归档。新包未安装，无游戏/UI操作。

这证明这组已构建自有模块的实际加载/首发与拒绝路径。它没有签发真实UnityPlayer V4，没有测试真实系统WinHTTP代理和转发、游戏cold-start、完整传递loader依赖安全性、scheduler/async闭包或EngineCancelled；`EngineCancelled=false`及Pending/根退休限制不变。完整B01–B19尚未完成。

## 范围、依赖与复核方法

{link('scope.md')}沿用用户“先继续代码，实测稍后”。只执行自有fixture，root唯一构建和运行；没有加载游戏DLL执行其函数、修改游戏文件、安装、操作现有进程或commit/push。依赖Windows x64、已有MSVC2019工具链、Python3.14标准库及固定Dobby。7个二进制由{link('native-build/early-loader-fixture/fixture-build-manifest.json')}固定。源码ZIP不包含全部工具/依赖，不能宣称异机完整发布矩阵或干净复现。

父进程首先以同一只读FileStream计算hash并在全部子进程存活期间持有FileShare.Read，拒绝写入、删除与换名；和completed-build manifest逐个核相同绝对路径及SHA256，Dobby另核固定hash。每例预创建唯一named mapping，写真实Importer/helper/Dobby的fileBytes、PE TimeDateStamp和SHA256；child还从实际loaded module路径读取磁盘原字节独立计算SHA。普通PE import/export/TLS由Python标准库离线解析，不加载映像。动态CRT、helper及Dobby的传递依赖只记录，不能由直接依赖已返回推出全部闭包安全。

| 二进制 | SHA256 |
| --- | --- |
{binary_rows}

| 实际PE | 普通导入模块 | delay模块数 |
| --- | --- | --- |
{imports}

父验证器要求真实普通边`Host → Importer → Proxy → Recorder/helper/Dobby`存在且符号表非空，相关边不能是delay-load；Importer四个target导出不可forward，TLS callback必须真实存在。Host和Importer可另有直接helper/Recorder边，不能把示意链误作唯一导入路径。它还拒绝从proxy/依赖反向静态导入Importer/Host；没有据此枚举全部动态或传递依赖。

## 共享记录和四入口的实际合同

固定协议Version1：Header360，config176位于152，Tuple216×4位于360，Event104×4096位于1224，总427208字节。row.sequence最后原子发布；物理slot与sequence由两次原子预留产生，父按唯一序号重建全序，不能假定二者相等。正常日志要求无缺口、无未发布半行、无overflow；故意耗尽日志的模式单独要求真实overflow和Unknown，绝不授完整正常日志结论。每条PID必须属于本次实际启动的child，TID非零。

| kind | 自有实际签名 | 验证返回/输出 |
| --- | --- | --- |
| 0 Start | uint8(owner,enum,name,outNode) | AL=`81`，输出该work的node；同步调用Execute/Release |
| 1 Execute | uint32(node,outByte) | 完整EAX=`a5c3fe81`，输出byte=`9d` |
| 2 Release | uint8(node) | AL=`e7` |
| 3 Detach | void(node) | 不制造返回byte |

四目标是自有MASM入口，磁盘前32字节NOP。每个tuple记录实际module/hash/RVA/kind/slot29/target/original/entry/epoch、原字节/发布字节和保护及四个阶段序号。父将原字节与受锁磁盘映像逐项比对，已commit行必须确实改变字节并恢复保护；无效/部分tuple不能取得发布authority。源码CommitBegin事件只是调用意图：before-Commit故障发生在实际attempt计数前，不能把该事件当成已写。writeAttempts/Completions计四次源目标Commit发布操作，不宣称监测Dobby所有内部CPU store或分配区写入。

四个初始化阶段各执行所有四typed original，Start再同步执行Execute/Release；每个work和callId永久唯一。父以实际callId核四实参、原始scalar、Start/Execute输出、exact物理父子关系和同线程LIFO，按phase/work界定独立代次。正常Host随后先无观察转发，再在loader窗口外Attach四通道，验证原始frame/snapshot、native sequence、captured token、completion/output及双向LIFO。token=`callId XOR 9123456789abcdef`；不得以其他最近调用替代。SEH和C++异常只在loader外发生，8次实际原异常经native继续到原catch，不能伪造正常返回。

normal实际{normal['workload']['actualOriginalCalls']}次original，分kind={normal['workload']['originalCallsByKind']}，56份观察Enter/Exit；worker模式实际{worker['workload']['actualOriginalCalls']}次original，分kind={worker['workload']['originalCallsByKind']}，同为56观察配对。39进程合计实际{original_total}次original、{observer_total}份观察配对、{caught_total}次原异常catch，与父断言数是不同计数。worker模式在DllMain只请求线程、不等待；Host在loader外join，实际worker TID不同于安装线程。另一个迟到模式保留已执行目标且尚未结束的真实worker，让后到producer实际拒绝。

## 所有独立模式及故障结果

状态4=DisabledNoWrite，5=Unknown，6=Completed，仅是自有fixture状态。prepare/configure故障尚无源目标Commit；首attempt之后任何失败保持Unknown，终止这个owned child，不能放行Importer、卸载、假回滚或重试。Duplicate是在4次真实Commit和历史Ready之后实际二次claim拒绝，历史Ready事件保留，同时Importer仍未初始化；不把它改写成“从未Ready”。

| 模式 | 独立父断言 | 最终state/failure | attempt/complete | original/observer |
| --- | --- | --- | --- | --- |
{mode_rows}

| 故障位置 | kind索引 | 最终state | attempt/complete | prepared/configured/committed |
| --- | --- | --- | --- | --- |
{fault_rows}

WrongHash对实际读取的磁盘SHA与受控错误expected作比较；WrongArchitecture/Prefix/ABI分别使用受控错误expectation。MissingDependency查询明确不存在的依赖名，静态固定Dobby仍由loader正常加载；DependencyNotReady是明确准入故障注入。它们验证实际拒绝分支，不伪称已加载真正32位/损坏DLL、真实系统依赖丢失或实测了dependency初始化竞争。IncompleteLog实际耗尽4096行容量，保Unknown；无观察历史或不完整日志均不能推成NoWrite Ready许可。

24个故障点位于实际成功API调用前后：after点确已Prepare/Configure/Commit成功，before点在调用之前终止。它们没有注入Dobby内部OOM、API自身非零返回、Commit内部中间部分写、保护恢复失败或任意并发写竞争；不能把API边界故障表升级为全部发布失败矩阵。实际WrongArchitecture是AMD64映像对受控I386 expectation、WrongPrefix是90对CC、WrongABI是2对3。

## 独立debugger样本证明的时序和限制

主38例没有debugger。第39例仅CreateProcess `DEBUG_ONLY_THIS_PROCESS`创建自身Host，核所有debug event PID，绝不attach现有进程。debugger从实际LOAD_DLL文件句柄取得映像身份，在真实AddressOfEntryPoint与PE TLS callbacks处短暂插入INT3；恢复原byte及RIP后继续，在入口真实栈取returnPC，以同TID、Rsp+8和returnPC捕获正常返回。非TLS DLL入口正常返回核EAX==TRUE；其余原SEH/C++使用DBG_EXCEPTION_NOT_HANDLED继续给原handler。

调试器事件同时写独立JSONL和同一共享全局sequence，flag=`80000000`，父逐条核二者一致及实际PE RVA/入口数/返回配对。helper/Dobby所有存在的被观察初始化返回均早于本次ProxyEnter序号 `{debug['header']['proxyEnterSequence']}`。没有TLS的模块按PE零项报告，不伪造callback。以下仅是本次实际序号，不写成Windows所有版本的普遍加载顺序。

| 自有模块 | DLL入口 → 返回sequence | TLS入口 → 返回sequence |
| --- | --- | --- |
{debug_table}

该样本临时修改自有child的入口/return页；磁盘文件不变，主无调试器样本另行通过。它不是无扰动测量、任意合法PC异常透明性或完整RAX/flags证明。遇到重叠returnPC、未配对返回、漏入口、异常或超时会明确失败，不能外推递归loader、所有early worker或全部传递CRT/回调。helper和固定Dobby实际自有启动顺序通过，也不等于其调用闭包适合移入真实游戏loader lock。

## 固定Dobby仍不能直接变成生产loader publisher

{link('dobby-loader-audit.md')}及 {link('research/dobby-loader-evidence.json')}记录磁盘上的有限静态调用图，采用实际RF/CHAININFO分段和CFG，保留未解间接边，作者同时参与native实现，属于作者自查。这里引用该有限原件并核hash/边界，不声称独立复做全部反汇编或证明路径运行可行。

Prepare新记录确经分配链到`114F6`的HeapAlloc，堆句柄来自CRT全局；`3D10/3D50`还有lazy list。logger `4700`实际为五段合链，错误/配置分支存在到`10D50 LoadLibraryExW`、CRT文件流和锁的保守静态路径；补固定fresh vtable后仍有未解vcall、guard/new-handler等。Commit写页辅助还存在当前进程OpenProcess/VirtualProtectEx fallback。普通import仅KERNEL32、正常39子进程通过或debug DLL attach已返回，均不能排除这些未走故障分支/OS内部/并发锁序。研究的loaderLockSafeProved、fullIndirectClosureProved、allFailurePathsSafeProved和gameReady全为false。

| 有限静态研究原件 | SHA256 |
| --- | --- |
{research_rows}

## 完整构建和原件

最终session `{run['sessionId']}`真实terminal0；wrapper完成 `{wrapper['completedAtUtc']}`，root观察 `{run['recordedAtUtc']}`。完整日志SHA256 `{run['logSha256']}`。预跑39通过与首次编译诊断由root另外保留；本报告只以最终full归档作为当前候选，没有把编译成功当测试成功，也不覆盖旧批565源码/1617证据。

首轮full `{failed['sessionId']}`实际terminal1，日志SHA256 `{failed['logSha256']}`。native在root开始该full、冻结576输入后追加了专用OverflowProbe事件17，Header/Proxy两文件发生已记录sourceDrift，旧严格parser在mode12真实拒绝`Unknown event id 17`；当次未到完整suite终点，也没有制造source ZIP，`finalCandidate=false`。root保留 {link('before-overflow-event-align/build-run.json')}、旧input manifest、原日志和失败raw树；实际终态后才允许字典仅增加17=OverflowProbe，重新冻结576并使用另一个cache完整构建。最终改动不放宽overflow/Unknown/失败计数合同；无关游戏/依赖阻塞，也不能把失败写成“已全绿”。

| 原25套suite | 结果 |
| --- | --- |
{suite_rows}

新增39进程/{int(evidence['checks'])}个父断言另列于 {link('early-loader-evidence.json')}；这些计数包含对同一真实事件的不同合同检查，不等于原函数调用数。生成器对最终39份raw mapping重新调用离线验证函数，核生成结果与各result和aggregate完全相等，没有重新启动child或构建。

声明源码{source_proof['entries']}：相对前批565新增{len(added)}、修改{len(changed)}、删除{len(removed)}。源码ZIP SHA256 `{source_proof['archiveSha256']}`，manifest `{source_proof['manifestSha256']}`；22项包ZIP `{package_proof['archiveSha256']}`。本文生成器实际重开两个ZIP逐entry校验manifest/current。构建manifest本身在编译后记录source hash；编译前冻结由root独立input manifest/integrity及最终源码归档证明，不能只从build manifest推出。

旧安装18项、其326项归档源码与原游戏3项核对由root当批staged检查记录，本作者仅读其proof/字段，没有再运行旧游戏或独立重开全部旧归档。新包未安装；原游戏3项核对不包含UnityPlayer DLL，不能借此宣称实际UnityPlayer首发经过验证。

| 源码变更 | 类型 | SHA256 |
| --- | --- | --- |
{source_rows}

## Evidence

{evidence_text}
## Findings

### F-001：自有静态依赖链中，真实四入口发布早于Importer首执行

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-001, E-002, E-003, E-004, E-007, E-008]`。
- location: `RizToolbox.EarlyLoaderFixture`及父日志验证器。
- impact: `自有目标在TLS/C/C++/DllMain及首调用前已有真实four-way passthrough，完整Execute EAX和异常/嵌套/worker在受控进程保持。debugger额外给直接依赖初始化返回时序，仍不推出真实游戏或完整传递loader安全。`
- repro_steps: `root完整构建，核normal无debug与normal-debugger分别通过；离线重读PE/RVA/原mapping和JSONL匹配实际序号。`
- remediation: `保留自有证据，将真实系统proxy/搜索路径/依赖闭包作为独立后续工作。`
- optional_attack: `n/a`。

### F-002：源写前后故障有精确责任，迟到历史不能授Ready

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-005, E-006, E-008]`。
- location: `Proxy.Install`的claim/Prepare/Configure/Commit及Recorder.Fail。
- impact: `24个逐点故障按实际成功prefix保tuple，零写可NoWrite，有attempt必Unknown；重入、已发布后重复和真实早调用/liveworker均拒绝。仅证明这些自有分支，不宣称所有并发首次写和Dobby内部故障全覆盖。`
- repro_steps: `核各rawMMF、故障前后实际End/tuple数、source字节及Importer0；Late保真实worker开始但未结束。`
- remediation: `生产迁移前继续补真实依赖身份/运行时历史与全部失败入口，不重试/假回滚。`
- optional_attack: `n/a`。

### F-003：真实UnityPlayer首次写和取消闭包仍缺独立凭据

- severity: `n/a_re`；category: `other`；status: `candidate`；confidence: `high`。
- evidence_ids: `[E-001, E-003, E-005, E-007, E-009, E-011]`。
- location: `WinHTTP生产proxy/固定UnityPlayer V4/coordinator/scheduler`。
- impact: `本批normal绿色不能授权在游戏运行后首次Dobby写，也不能关闭scheduler/async/future Start或Pending、EngineCancelled、GC根退休。真实WinHTTP及依赖闭包仍false。`
- repro_steps: `核aggregate/result中productionV4Issued/gameFirstInstallVerified/allTransitiveLoaderDependenciesSafeVerified/engineCancellationCompleted等false及scope。`
- remediation: `先实现固定容量、无动态CRT/logger/泛用虚路由的最小四入口publisher并完成自有非NOP矩阵；再验证真实生产proxy、固定游戏早期历史与V4采用，同时继续scheduler/async及身份接线。`
- optional_attack: `n/a`。

### F-004：event17协议不匹配被严格拒绝并保留后重新构建

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-001, E-005, E-008, E-010]`。
- location: `EarlyLoaderShared.h/Proxy.cpp`的OverflowProbe事件与离线event字典。
- impact: `首轮full没有把未知事件或2项源码漂移当成功；最终在终态后对齐已定义事件17、重新冻结和完整构建，其他严格合同保留。`
- repro_steps: `核46741原日志、failed mode12及sourceDrift，再核当前576归档和不同最终session的39原件；不覆写失败原件或伪造失败sourceZIP。`
- remediation: `统一发布前冻结schema与所有声明输入，构建期间禁止各作者漂移；出现差异保原件并在终态后另次构建。`
- optional_attack: `n/a`。

## Path与下一步

P-001，path_type=`callflow`。从受锁且固定hash的自有普通依赖开始，先实际完成依赖初始化，再全4Prepare/全4Configure/逐Commit与复核，最后Ready才放行Importer（E-002/E-003/E-004/E-007→F-001）；故障按真实tuple/writeprefix拒绝并保责任（E-005/E-006→F-002）；25套、39个独立进程、源包归档相互印证实现身份，但生产边界不升级（E-001/E-008/E-009→F-003）。目标是为后续固定游戏V4建立可审查的早期机制证据，当前终点只是自有fixture。

下一步先实现自有最小四入口publisher：编译期4项POD和固定容量原始/发布存储，消除lazy list、动态堆、新handler、stdio/logger和泛用虚路由；离线生成并审查固定非NOP指令重定位计划，只用已映射模块的固定stub/预留空间；当前自有进程固定范围写页、明确保护/恢复/缓存处理，每次真实写和错误留账，写后Unknown有限退出，不重试假回滚。需独立验证非NOP/RIP相对修正、四ABI、trampoline/unwind/异常、初始化/worker/迟到与逐写故障；不能把本批32个NOP前缀直接套在UnityPlayer。

之后再由独立合同决定真实系统WinHTTP命名/转发、唯一静态依赖身份与路径、传递初始化调用闭包及失败终态，不能将当前复杂Doorstop初始化直接搬入DllMain。真实游戏cold-start/四入口此前完整调用和线程历史、coordinator真实root/owner/provider采用仍需独立证据；scheduler登记/消费/drain、Start内部最终清理、async普通派发和future producer封口继续实施。用户安排的游戏实测另行恢复，完整B01–B19不因本批通过而结束。

仓库根只读汇编报告：

```powershell
{report_command}
```

完整构建复现命令（root唯一builder；会产生新原件，先保留当前证据）：

```powershell
{command}
```

参阅 {link('README.md')}、{link('next-integration.md')}与[完整验收清单](../.docs/foundation-expansion/CHECKLIST.md)。
"""
    assert "TODO" not in body and "placeholder" not in body
    destination.write_text(body, encoding="utf-8")
    print(json.dumps(dict(report=str(destination), sha256=sha(destination), session=run["sessionId"], suites=25,
        passed=summary["passed"], sourceInputs=source_proof["entries"], ownedProcesses=39,
        independentParentAssertions=int(evidence["checks"]), offlineAllMappingsReverified=True), ensure_ascii=False))


if __name__ == "__main__":
    main()
