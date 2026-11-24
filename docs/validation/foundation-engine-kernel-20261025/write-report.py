"""Write the final report from archived full-build evidence, without executing code under test."""
import hashlib
import json
import os
import re
import zipfile
from pathlib import Path

BATCH = Path(__file__).resolve().parent
REPO = BATCH.parents[2]
REPORT = REPO / "docs/2026-10-25_底层-独立协程运行核验证-report.md"
INSTALL = BATCH.parent / "foundation-kernel-install-20261025"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def need(value, reason):
    if not value:
        raise ValueError(reason)


def link(path, label=None):
    path = path.resolve()
    return f"[{label or path.name}]({Path(os.path.relpath(path, REPORT.parent)).as_posix()})"


def archive(manifest_name, proof_name, zip_name, current_root, current_overlay=None):
    manifest_path, proof_path, zip_path = [BATCH / name for name in (manifest_name, proof_name, zip_name)]
    manifest, proof = read(manifest_path), read(proof_path)
    need(proof["allEntryHashesMatched"] and proof["entries"] == len(manifest["files"]), "Archive count/proof is incomplete")
    need(sha(manifest_path) == proof["manifestSha256"] and sha(zip_path) == proof["archiveSha256"], "Archive proof identity mismatch")
    with zipfile.ZipFile(zip_path) as data:
        need(sorted(data.namelist()) == sorted(row["path"] for row in manifest["files"]), "ZIP exact entry set differs")
        for row in manifest["files"]:
            need(hashlib.sha256(data.read(row["path"])).hexdigest() == row["sha256"], "ZIP item hash mismatch")
            expected_current = (current_overlay or {}).get(row["path"], row["sha256"])
            need(sha(current_root / row["path"]) == expected_current, "Current source/staging differs from its exact final or overlay identity")
    return manifest, proof


def installation_overlay():
    summary = read(INSTALL / "summary.json")
    runs = read(INSTALL / "terminal-observations.json")
    need(summary["coreFullSessionId"] == 23528 and summary["safetySessionId"] == runs["safetySessionId"] == 66373
         and summary["recoverySessionId"] == runs["recoverySessionId"] == 67106
         and runs["safetyExitCode"] == runs["recoveryExitCode"] == 0, "Installer supplement actual terminals are incomplete")
    need(read(INSTALL / "safety.run.json")["exitCode"] == read(INSTALL / "recovery.run.json")["exitCode"] == 0,
         "Installer supplement wrapper results are incomplete")
    need(summary["secondFullBuildClaim"] is False and summary["realGameExecuted"] is False and summary["realGameModified"] is False,
         "Installer overlay overstates full build or game execution")
    before, changes, final = [read(INSTALL / name) for name in ("inputs-before.json", "source-overlay-manifest.json", "source-final-manifest.json")]
    allowed = {"scripts/Install.ps1", "scripts/Test-InstallSafety.ps1", "scripts/Test-InstallRecovery.ps1"}
    need(len(changes["files"]) == 3 and {row["path"] for row in changes["files"]} == allowed and changes["baseSessionId"] == 23528,
         "Only the three explicitly tested installation scripts may override core source identities")
    core = {row["path"]:row["sha256"] for row in read(BATCH / "source-manifest.json")["files"]}
    current = {row["path"]:row["sha256"] for row in final["files"]}
    need(len(core) == len(current) == 599 and set(core) == set(current), "Composite current source set is not exactly core599")
    overlay = {row["path"]:row["sha256"] for row in changes["files"]}
    need({name for name in core if core[name] != current[name]} == allowed,
         "Composite source differs from core beyond the three installer scripts")
    for row in changes["files"]:
        need(row["beforeSha256"] == core[row["path"]] and row["sha256"] == current[row["path"]], "Installer before/after identity mismatch")
    need(len(before["files"]) == 7 and all(sha(REPO / row["path"]) == row["sha256"] for row in before["files"]), "Installer seven frozen inputs drifted")
    integrity = read(INSTALL / "input-integrity-check.json")
    need(integrity["checked"] == 7 and integrity["beforeMatchesAfter"] and not integrity["changed"]
         and integrity["coreSourcesUnchanged"] == 596 and integrity["installerChanges"] == 3, "Installer supplement integrity is incomplete")
    proof = read(INSTALL / "source-archive-check.json")
    need(proof["entries"] == 599 and proof["allEntryHashesMatched"] and proof["compositeValidation"]
         and proof["manifestSha256"] == sha(INSTALL / "source-final-manifest.json")
         and proof["archiveSha256"] == sha(INSTALL / "final-source-inputs.zip"), "Composite source ZIP proof mismatch")
    with zipfile.ZipFile(INSTALL / "final-source-inputs.zip") as data:
        need(sorted(data.namelist()) == sorted(current), "Composite source ZIP exact file set mismatch")
        for name, expected in current.items():
            need(hashlib.sha256(data.read(name)).hexdigest() == expected == sha(REPO / name), "Composite ZIP/current source hash mismatch")
    safety, recovery = read(INSTALL / "safety-results.json"), read(INSTALL / "recovery/results.json")
    need(safety["count"] == len(safety["passed"]) == summary["safetyChecks"] == 16 and not safety["gameWasLaunched"], "Actual safety matrix incomplete")
    need(recovery["count"] == recovery["passed"] == summary["recoveryChecks"] == 13
         and all(row["passed"] for row in recovery["results"]), "Actual recovery matrix incomplete")
    need(read(INSTALL / "before-fix-run.json")["exitCode"] == 1 and "Invalid package" in (INSTALL / "before-fix-safety.log").read_text(encoding="utf-8-sig"),
         "Installer actual before-fix red evidence is missing")
    need(read(INSTALL / "package-check.json")["unchangedSinceCore23528"], "23 core package bytes changed in installation supplement")
    preservation = read(INSTALL / "real-game-preservation-check.json")
    need(preservation["allMatched"] and not preservation["realGameExecuted"] and not preservation["realGameModified"], "Real game preservation mismatch after fake-game tests")
    return summary, changes, proof, overlay


def generate():
    run, wrapper, summary, milestone = [read(BATCH / name) for name in ("build-run.json", "build.log.run.json", "build-summary.json", "milestone-summary.json")]
    need(run["sessionId"] == milestone["sessionId"] == 23528 and run["exitCode"] == run["terminalExitCodeObserved"] == wrapper["exitCode"] == 0,
         "Final actual full terminal observation missing")
    need(sha(BATCH / "build.log") == run["logSha256"] == wrapper["logSha256"] == summary["logSha256"], "Actual full log hash mismatch")
    need(summary["projects"] == len(summary["suites"]) == milestone["projects"] == 25 and summary["passed"] == summary["total"] == 1617,
         "Managed full suite count mismatch")
    need(sum(row["passed"] for row in summary["suites"]) == sum(row["total"] for row in summary["suites"]) == 1617, "Individual suite totals disagree")
    install_summary, install_changes, install_proof, overlay = installation_overlay()
    sources, source_proof = archive("source-manifest.json", "source-archive-check.json", "source-inputs.zip", REPO, overlay)
    package, package_proof = archive("package-manifest.json", "package-archive-check.json", "staged-package.zip", REPO / "artifacts/package")
    need(len(sources["files"]) == milestone["declaredSourceInputs"] == 599 and len(package["files"]) == milestone["packageFiles"] == 23, "599/23 final archive mismatch")
    for name in ("source-integrity-check.json", "build-input-integrity-check.json"):
        integrity = read(BATCH / name)
        need(integrity["checked"] == 599 and not integrity["changed"], "Final frozen source drift")
    need(read(BATCH / "build-input-integrity-check.json")["preBuildMatchesAfterBuild"], "Pre/post build source differs")

    cases = BATCH / "native-build/engine-node-kernel-cases"
    kernel = read(BATCH / "engine-kernel-evidence.json")
    need(kernel == summary["engineKernel"] == read(cases / "fixture-result.json") and kernel["passed"], "Kernel final evidence differs across aggregate/raw")
    proof, entry = read(cases / "kernel-entry-proof.json"), read(cases / "entries/fixture-result.json")
    need(kernel["entryProof"] == proof and proof["passed"] and proof["staticEntryTablesVerified"], "Kernel static table proof incomplete")
    need(proof["actualEntryFixtureSha256"] == sha(cases / "entries/fixture-result.json") and entry["checks"] == milestone["kernelEntryChecks"] == 1606,
         "Entry fixture identity/actual checks mismatch")
    need(proof["checks"] == milestone["kernelEntryParentAssertions"] == 1486 and len(proof["tableProofs"]) == 4 and proof["tableDir64Relocations"] == 128,
         "Four static tables /128 pointers proof mismatch")
    need(proof["entryPointerRejections"] == 256 and proof["invalidCoordinateRequests"] == 12 and entry["witnessCalls"] == 0,
         "Descriptor rejection/zero execution evidence incomplete")
    dll = BATCH / "native-build/engine-node-kernel/RizToolbox.EngineNodeKernel.dll"
    need(sha(dll) == proof["image"]["sha256"] and dll.stat().st_size == milestone["kernelBinaryBytes"] == 39424,
         "Final actual Kernel PE differs from proof")
    pkg = {row["path"]: row["sha256"] for row in package["files"]}
    need(pkg["BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll"] == sha(dll), "Staged package Kernel is a different DLL")
    native, managed = kernel["nativeRegression"], kernel["managedRegression"]
    need(native == read(cases / "native/fixture-result.json") and native["checks"] == 3402 and native["actualOriginalCalls"] == 136 and native["entered"] == native["exited"] == 124,
         "Native actual regression mismatch")
    need(managed == read(cases / "managed/fixture-result.json") and managed["checks"] == 889 and managed["actualOriginalCalls"] == 44 and managed["physicalCaptures"] == 28,
         "Managed actual regression mismatch")
    expected_modes = [("normal",227,9,9),("original-seh",158,8,4),("original-cpp",154,8,4),("detach",203,11,7),("observer-fault",147,8,4)]
    for row, (mode, checks, originals, captures) in zip(managed["modes"], expected_modes, strict=True):
        need(row == read(cases / "managed" / f"{mode}-result.json") and row["mode"] == mode and row["checks"] == checks
             and row["actualOriginalCalls"] == originals and row["physicalCaptures"] == captures, "Managed mode raw mismatch")
        need(row["completionCount"] == row["disposalCount"] == row["peerCompletionCount"] == row["peerDisposalCount"] == captures,
             "Captured responsibility pairing mismatch")
        need(row["ledgerPending"] and not row["engineCancelled"] and row["nativeInFlight"] == row["dispatcherInFlight"] == row["ledgerOutstanding"] == 0,
             "Mode overclaims cancellation or leaks reported callback scope")
        need(row["files"][0]["path"] == kernel["kernelPath"] and row["files"][0]["beforeSha256"] == row["files"][0]["afterSha256"] == sha(dll),
             "Mode did not use the exact actual Kernel path")
    need(native["actualOriginalCalls"] + managed["actualOriginalCalls"] == milestone["kernelActualOriginalCalls"] == 180
         and native["entered"] + managed["physicalCaptures"] == milestone["kernelObserverPairs"] == 152, "Original/pair totals mismatch")
    need(summary["earlyLoader"] == read(BATCH / "early-loader-evidence.json") and summary["earlyLoader"]["independentProcesses"] == 39
         and summary["earlyLoader"]["checks"] == 7316, "Legacy early loader rerun mismatch")
    need(len(summary["compilerDiagnosticLines"]) == milestone["compilerDiagnosticLines"] == 94, "Raw compiler diagnostic count mismatch")

    # Distinguish immutable *during-run* held snapshots from current files after
    # a later unrelated regression intentionally copied the legacy helper.
    held = kernel["files"]
    need(len(held) == 31 and kernel["allHeldInputHashesUnchanged"] and all(row["beforeSha256"] == row["afterSha256"] for row in held),
         "Kernel seven-child held window did not preserve all input identities")
    held_start = read(cases / "held-inputs.json")
    need({row["path"]:row["sha256"] for row in held_start["files"]} == {row["path"]:row["beforeSha256"] for row in held}, "Held window input sets differ")
    post = read(BATCH / "kernel-post-run-files.json")
    need(post["heldInputHashesUnchangedDuringKernelRun"] and len(post["changedAfterRunnerReturned"]) == 1
         and post["causeSource"] == "scripts/Test-FirstInstallManaged.ps1:29", "Post-run time boundary metadata incomplete")
    actual_changed = []
    for row in held:
        current = sha(Path(row["path"]))
        if current != row["beforeSha256"]:
            actual_changed.append(dict(path=row["path"],role=row["role"],duringRunSha256=row["beforeSha256"],currentSha256=current))
    need(actual_changed == post["changedAfterRunnerReturned"], "Current files differ beyond the explicitly recorded later helper copy")
    supplied = read(BATCH / "kernel-supplied-files.json")
    need(len(supplied["files"]) == 19, "Exact supplied binary/managed file set incomplete")
    for row in supplied["files"]:
        need(sha(BATCH / row["path"]) == row["sha256"], "Recovered supplied file hash mismatch")
        need(next(value for value in held if value["path"] == row["originalPath"])["beforeSha256"] == row["sha256"], "Supplied file is not actual during-run bytes")
    changed = actual_changed[0]
    recovered = next(row for row in supplied["files"] if row["originalPath"] == changed["path"])
    need(recovered["sha256"] == changed["duringRunSha256"] == "01e5171d2e7a824308e7e9053f6959fa7cbea98bb2c2742e1571d8bf75a66f48",
         "Exact old helper during-run version not restored")
    need(pkg["BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll"] == changed["currentSha256"], "Later legacy helper does not match final package identity")
    need("AssertionError" in (BATCH / "history/first-summary-failure.log").read_text(encoding="utf-8-sig"), "Actual first summary failure history is absent")

    before = read(BATCH / "build-inputs-before.json")
    need(before["files"] == sources["files"], "599 pre-build declaration differs from archived manifest")
    changes = read(BATCH / "intentional-source-changes.json")
    need(changes["previousDeclaredSources"] == 576 and changes["oldArchiveUnmodified"] and len(changes["changed"]) == 4,
         "Authorized baseline changes were not declared")
    previous = read(BATCH / "previous-installation-check.json")
    need(previous["allMatched"] and len(previous["files"]) == 18 and previous["archivedSourceInputsChecked"] == 326, "Old installation ownership mismatch")
    need(all(row["expected"] == row["actual"] for row in previous["files"]), "Old installed bytes mismatch")
    original, preservation = read(BATCH / "original-game-check.json"), read(BATCH / "preservation-check.json")
    need(original["allUnchanged"] and len(original["files"]) == 3 and preservation["originalUnityPlayer"]["matched"], "Original game static hashes differ")
    need(all(row["archiveUnchanged"] for row in preservation["priorMilestones"]), "Historical ZIP changed")
    staging = read(BATCH / "staging-check.json")
    need(staging["allMatched"] and len(staging["files"]) == 23 and all(row["expected"] == row["actual"] for row in staging["files"]), "Staging evidence mismatch")

    evidence_rows = [
        ("E-01", "完整真实终态", BATCH / "build-run.json", "C1，核build.log SHA"),
        ("E-02", "Kernel七进程汇总", BATCH / "engine-kernel-evidence.json", "C1/C3，核逐mode原件"),
        ("E-03", "实际descriptor与Fresh", cases / "entries/fixture-result.json", "C3；仅描述符，不调用typed入口"),
        ("E-04", "独立PE/磁盘四表", cases / "kernel-entry-proof.json", "C2，或C3附--entries"),
        ("E-05", "原生136实际调用", cases / "native/fixture-result.json", "既有native runner，C1离线读取"),
        ("E-06", "托管五mode实际账本", cases / "managed/fixture-result.json", "既有managed runner，C1离线读取"),
        ("E-07", "31输入窗口与结束后变化", BATCH / "kernel-post-run-files.json", "C1，区分during/current"),
        ("E-08", "准确当次供给文件19份", BATCH / "kernel-supplied-files.json", "C1，逐项SHA"),
        ("E-09", "实际首次summary失败", BATCH / "history/first-summary-failure.log", "只读实际错误原文，未伪造重跑"),
        ("E-10", "599源归档证明", BATCH / "source-archive-check.json", "C1，逐条ZIP SHA"),
        ("E-11", "23包归档证明", BATCH / "package-archive-check.json", "C1，逐条ZIP SHA"),
        ("E-12", "旧39早期链再跑", BATCH / "early-loader-evidence.json", "C1，39/7316另计"),
        ("E-13", "历史与现场静态hash", BATCH / "preservation-check.json", "只读hash，未启动游戏"),
        ("E-14", "安装补充真实终态与组合来源", INSTALL / "summary.json", "C1，独立Safety16/Recovery13，非第二次full"),
        ("E-15", "安装前真实红测", INSTALL / "before-fix-safety.log", "只读修前Invalid package manifest实际错误"),
        ("E-16", "现行599组合源逐项证明", INSTALL / "source-archive-check.json", "C1，三项精确overlay，其余596同core"),
    ]
    evidence_table = "\n".join(f"| {eid} | {title} | {link(path)} | `{sha(path)}` | {command} |" for eid,title,path,command in evidence_rows)
    mode_table = "\n".join(f"| {mode} | {checks} | {originals} | {captures} | {link(cases/'managed'/f'{mode}-result.json','原件')} |" for mode,checks,originals,captures in expected_modes)
    changes_table = "\n".join(f"| {link(REPO/row['path'],row['path'])} | `{row['beforeSha256']}` | `{row['afterSha256']}` |" for row in changes["changed"])
    suites_table = "\n".join(f"| {row['summary']} | {row['passed']}/{row['total']} |" for row in summary["suites"])
    overlay_table = "\n".join(f"| {link(REPO/row['path'],row['path'])} | `{row['beforeSha256']}` | `{row['sha256']}` |" for row in install_changes["files"])
    dependency_review = BATCH / "kernel-dependency-review.md"
    text = f"""# 独立协程运行核实现与验证报告

2026-10-25 · flavor=null · Windows x64自有fixture · full session{run['sessionId']}。

`RizToolbox.EngineNodeKernel.dll`已实现并纳入统一构建及23项暂存包：它独立编译同一份canonical EngineNodeContext实现，仅导出8个ABI2描述/配置/观察API，无PE入口、TLS、CRT或Dobby直接依赖。本次完整25套托管回归1617/1617通过；新Kernel另有七个独立进程，实际检查Entry1606、父PE1486、native3402与managed889，并保存180次原调用/152对观察。旧59-export helper继续按独立文件和状态工作，原有39个早期链进程7316父断言也重新通过。随后修复实际安装白名单漏Kernel，独立自有fakegame安装安全16项/恢复13项通过；现行599源由原full加三脚本精确overlay组成，23包字节不变，未声称重跑第二次full。构建保留94条去重原始编译诊断，不能称零warning。本批没有把Kernel接入早期producer/生产CLR准入，没有启动或安装真实游戏，也没有签发V4、EngineCancelled或资源退休凭据。

## 范围与实际交付

授权和任务边界见{link(BATCH/'scope.md')}：继续用户的PC Mod兼容底层目标、并行代理实现与“先继续代码，实测稍后”；`auth=用户既有授权`、`in_scope=自有Kernel/fixture/统一构建`、`network_profile=本地文件及自有child`。root独占构建执行，其他代理只读交叉审查或处理各自源码。静态hash读取游戏文件属于保全；没有启动游戏、QQ/Steam/UI、commit或push。

| 项目 | 实际结果 | 证据 |
|---|---:|---|
| full终态 | session23528实际exit0 | E-01，{link(BATCH/'build.log.run.json')} |
| 完整托管回归 | 25套1617/1617 | {link(BATCH/'build-summary.json')} |
| 旧早期链再跑 | 39进程 / 7316父断言 | E-12；与托管计数分列 |
| 新Kernel Entry /父PE | 1606 / 1486检查 | E-03、E-04；descriptor child没有typed调用 |
| 新Kernel native | 3402检查 / 136original / 124pairs | E-05 |
| 新Kernel managed | 5mode889检查 / 44original / 28pairs | E-06 |
| 新Kernel进程与调用合计 | 7进程 / 180original / 152pairs | E-02；不同计数不混加成覆盖率 |
| core声明源/暂存包 | 599 / 23 | E-10、E-11，逐项ZIP证明 |
| 当前组合源码 | 599（core596+安装3） | E-16，精确overlay和新源码ZIP |
| 独立安装补充 | Safety16 / Recovery13 | E-14～16；自有fakegame，三脚本overlay |
| 编译诊断 | 94条去重原始行 | build-summary.compilerDiagnosticLines；含既有警告 |

Kernel39424字节，最终SHA256 `{sha(dll)}`；它与预检96386的41e01c同名文件区分，正式结论使用当前full原件。暂存包中的旧JudgeContext为`{pkg['BepInEx/plugins/RizToolbox/RizToolbox.JudgeContext.dll']}`，仍是59-export旧组件；8-export Kernel不覆盖或重命名成旧DLL。二者各有静态槽、callback根和epoch，不能跨模块套用身份。

## 实现与实际加载边界

{link(REPO/'scripts/Build-EngineNodeKernel.ps1','独立builder')}直接编译{link(REPO/'native/RizToolbox.JudgeContext/EngineNodeContext.cpp','canonical源码')}，没有复制第二份传输逻辑。四表使用各自typed function pointer与constexpr factory，编译期限定恒量初始化；GetEntry和IsEntry仅在运行时边界转换void指针。此前MSVC也已实际折叠旧表，不能编造“旧代码动态初始化崩溃”的历史。Dispatch、Before/After、SEH、Configure/Attach/GetInfo和计数合同未借机更换。

两旧runner新增末尾可选绝对HelperPath，原HelperDirectory/JudgeContext默认路径兼容；除了参数/路径分支，原生136与托管五mode的执行和全部断言逐行保留。{link(REPO/'scripts/Test-EngineNodeKernel.ps1','新parent')}按真实Kernel绝对路径复用它们，先运行独立EntryFixture并核PE；Dobby实际用于fixture自有目标的Prepare/Commit，未成为Kernel直接依赖。新parent不构建共享项目，临时锁定完整供给managed输出目录及native输入后才启动自有child。

PE从持有DLL磁盘核AMD64/PE32+、exact8 nonforwarded exports、无额外ordinal exports；入口、TLS、delay、load-config和CLR目录全0。仅普通导入`Kernel32!RaiseFailFastException`与`ntdll!__C_specific_handler`，无CRT、Dobby、堆或LoadLibrary直接导入。COFF仍可含`DEFAULTLIB:libcpmt` pragma，最终`/NODEFAULTLIB`链接与实际无CRT PE是另一层事实；没有把pragma说成已装入CRT，也没有将两个OS导入推断为完整系统传递调用图安全。

ABI继续为返回版本2、record版本1、每kind32永久槽、Frame112/Completion32/Info112；Start/Release为uint8 AL，Execute完整uint32 EAX，Detach void。RAX高位、任意flags、任意回调/OS路径透明均不在本合同。After只使用已复制标量，不回读可能已free的node；观察异常、原异常、snapshot/output故障保留Unknown，配置与callback根永久保留，没有新增reset/unhook/根释放或取消完成能力。

## 128入口的双向磁盘证明

EntryFixture实际枚举4×32个GetEntry地址，查询初始/最终Info，验证每个entry在另一kind同slot作为target或original的拒绝。128入口互异且实际RX/MEM_IMAGE、属于加载的准确文件；512份Fresh输出（初始128+拒绝后256+最终128）正确回填kind/slot/entry，target/original/epoch/attached/Unknown/活动及错误计数全0。另12个无效坐标×4API拒绝，无效GetInfo保持poison输出，不能当Fresh数据。该child没有执行typed entry、original或observer；它的1606检查只属于descriptor/table合同。

父验证器用actualGetEntry−module.base取得真实RVA，再用**磁盘**OptionalHeader.ImageBase `0x180000000` 加RVA组成32个literal指针序列，在只读、非写、非执行section扫描，要求每kind唯一匹配。真实映射slot memcpy值也逐项对应；每个literal slot必须有自己的DIR64 relocation，当前128全覆盖且无额外DIR64，不按预设RVA或section名过关。

| kind | 本批唯一tableRVA | 指针/对应DIR64 | 实际section |
|---|---:|---:|---|
| Start0 | `0x9020` | 32/32 | .rdata |
| Execute1 | `0x9120` | 32/32 | .rdata |
| Release2 | `0x9220` | 32/32 | .rdata |
| Detach3 | `0x9320` | 32/32 | .rdata |

正式Entry实际base=`{hex(proof['actualModuleBase'])}`，delta=`{proof['actualRelocationDelta']}`，非0重定位样本。mapped OptionalHeader字段名现为`mappedHeaderImageBase`，可能已由loader改写，不能冒充磁盘preferred；旧首个sample的误导字段名保留为历史，随后明确更名，没有改写sample来伪造新schema成功。

## 真实native→CLR→账本回归

136实际native originals保留124enter/exit、124唯一sequence、12旧未观察调用、17worker、8嵌套、原异常16、观察异常34、snapshotFault3、outputFault2与free-node6等既有矩阵。五managed mode的原生入口/CLR bridge/dispatcher/双sink/Adapter node和identity ledger均实际被回调；每个mode四entry减当前Kernel相应slot25RVA都指向同一实际Kernel映像基址，结果files明确使用新DLL绝对路径与hash，未以旧helper结果替代。

| mode | checks | original | captures/Complete/Dispose各自数量 | 结果 |
|---|---:|---:|---:|---|
{mode_table}

五mode合计44original/28captured，双sink的Complete/Dispose都精确配对，native/dispatcher inFlight与ledger Outstanding归0。normal/detach保留同步Start首执行节点关联、同record后续handle升级、free-node之后不读的样本；逻辑detach仍让原调用继续并保永久配置。original-seh/cpp四channel原生Unknown1；observer-fault前三kind原生Unknown0但CLR dispatcherUnknown，Detach实际snapshot故障使nativeUnknown1，故障包含层级分开。所有mode仍Pending=true、EngineCancelled=false；即使Outstanding0也不能当引擎取消完成。

provider、metadata和receiver/native owner配对仍受控，真实三tracker backend、IL2CPP对象backend/GC及生产coordinator未在此链执行。receiver与native owner明确不同、tracker generation与engine generation独立，但不能将这些受控标量升级成真实游戏对象证明。{link(BATCH/'review-runtime.md','运行时作者有限复核')}、{link(BATCH/'review-entry.md','Entry作者/交叉复核')}和{link(dependency_review,'原生作者有限依赖复核')}分别标明作者身份、方法与证据版本；它们没有再执行child，也不是独立完整审计。

## 31输入的时间边界与真实摘要失败

Kernel parent从开始hash到全部七个child结束，持续持有FileShare.Read句柄；31项集合在该窗口before=after。该保证在runner返回释放句柄时结束，不能扩成整个full结束当前文件均不变。完整构建随后运行{link(REPO/'scripts/Test-FirstInstallManaged.ps1','Test-FirstInstallManaged.ps1')}第29行Copy-Item，把新编译的legacy helper复制到Runtime.Tests目录；第30行才是校验。该副本实际从`{changed['duringRunSha256']}`变为`{changed['currentSha256']}`，Kernel本体和其余30项当前仍与during相同。

首次offline summary把更早before/after与当前磁盘强行三等，实际触发E-09中的AssertionError。root保留原始错误与观察记录；full23528早已真实exit0，七case也已通过，这不是一次native功能红测。修正的E-07准确记录during窗口与之后唯一变化，没有篡改runner前后hash；摘要程序不再把时间不同的对象伪装为同一份不变文件。

E-08归档19份实际供给binary/managed文件。已变化的oldhelper准确during版本按hash从上批73进程raw/build恢复，当前归档字节仍是`01e5171d…a66f48`，不是后来398d版本；`recoveredExactSource`明确恢复出处，`originalPath`明确当次使用路径。完整工具链/系统dotnet/Windows未进入这19份文件或599声明源闭包，不声称干净换机环境完整复现。

## 冻结与历史保全

core23528的599源构建前后与其历史ZIP逐项一致；当前源码后来有三项安装overlay，不能再写全部599当前都等于core旧hash。相对旧576基线，下面4项是**core冻结前有意变更**；旧ZIP和manifest本身保持原件。新批同时纳入其余新组件与声明输入，599不是拿旧576哈希冒充无变更。

| 授权变化 | 旧576 SHA256 | 本批SHA256 |
|---|---|---|
{changes_table}

core源ZIP SHA256 `{source_proof['archiveSha256']}`，{link(BATCH/'source-inputs.zip')}；23包ZIP SHA256 `{package_proof['archiveSha256']}`，{link(BATCH/'staged-package.zip')}。{link(BATCH/'source-manifest.json')}和{link(BATCH/'package-manifest.json')}保存当时每一项hash，{link(BATCH/'staging-check.json')}核当前暂存23项。暂存新增Kernel不等于生产loader已经加载/采用它；旧helper与新Kernel共存，包未安装到真实游戏。

### 完整构建后的安装补充

实际安装检查在core完成后发现Install.ps1仍只有旧22项白名单，自有fakegame用真实23项包安装即以“Invalid package manifest”拒绝（E-15实际exit1），并非静态猜测。新增Kernel**精确路径**后继续核名单、hash、归属与事务，未放宽未知路径。Safety同时补真实Kernel安装/重复安装、旧22布局升级新增、被改Kernel拒绝覆盖、未改Kernel卸载及用户配置保留；Recovery合成仓库补足实际23项名单，硬中断、恢复被打断、坏备份、锁和卸载均在自建artifacts目录及进程中实际验证。恢复逻辑按journal实际替换项定位备份，不沿用固定索引；合成载荷不加载DLL，也不是游戏内容执行。

独立补充批{link(INSTALL/'README.md')}保存Safety session66373 actualexit0/{install_summary['safetyChecks']}检查，Recovery session67106 actualexit0/{install_summary['recoveryChecks']}检查，{link(INSTALL/'terminal-observations.json')}与两wrapper记录一致。7项实际相关输入冻结前后相同，{link(INSTALL/'input-integrity-check.json')}保留完整证明。只有下列三项脚本替换core源，其他596项逐字节保持；它是“核心full通过＋安装补充验证”的组合证据，未虚构新full或把1617重新运行。

| 安装补充脚本 | core23528 SHA256 | 当前已专项测试SHA256 |
|---|---|---|
{overlay_table}

现行599源manifest为{link(INSTALL/'source-final-manifest.json')}，精确overlay为{link(INSTALL/'source-overlay-manifest.json')}，现行源码ZIP为{link(INSTALL/'final-source-inputs.zip')}，SHA256 `{install_proof['archiveSha256']}`。本报告生成器把core599 ZIP逐项严格核验，当前三项只能等于上述精确overlay、其余596只能等于core；不存在任意豁免。23包DLL/JSON字节仍完全是core23528输出；{link(INSTALL/'real-game-preservation-check.json')}确认真实游戏18已装文件与四原件hash保持，不曾安装/启动真实游戏。

E-13及{link(BATCH/'previous-installation-check.json')}核旧1021安装18文件和326源归档仍匹配；{link(BATCH/'original-game-check.json')}核EXE、GameAssembly、metadata三原件，另核UnityPlayer原hash。旧576、独立组件12项和早期链14项的归档均未改；相对旧576，core当时4项源有意变化，后来的3项安装脚本变化另由E-16精确列明。以上是静态读取hash，不是启动游戏或验证游戏流程。

本批完整托管各套原始摘要如下，不能把Kernel/早期链父断言再加进1617分子：

| 原始suite摘要 | passed/total |
|---|---:|
{suites_table}

## Evidence → Finding → Path

`source_type=file`；`artifact_path/source_ref`为表中本地路径；observed_at对应E-01实际终态、wrapper完成时间及case输出，E-07还明确after-run边界；`linked_workitem=独立协程运行核`、`supersedes=none`。原始摘录：E-01 session23528/exit0；E-02 passed=true/180original；E-03 checks1606；E-04 checks1486/128DIR64；E-05 checks3402/136original；E-06 checks889/44original；E-07 during31matched/after1changed；E-09 AssertionError；E-10/11 entries599/23；E-12 39/7316。content_hash均为SHA256。

| E-id | title | source_ref / artifact_path | content_hash | repro_command |
|---|---|---|---|---|
{evidence_table}

| F-id | severity/category | status/confidence | evidence_ids | location | 结论和适用范围 |
|---|---|---|---|---|---|
| F-01 | n/a_re/design | validated/high | E-01,E-03,E-04 | Kernel PE / GetEntry | 8-export静态内核与128恒量表可独立加载查询；两个OS外部路径非全闭包 |
| F-02 | n/a_re/other | validated/high | E-02,E-05,E-06 | 实际typed transport→dispatcher/ledger | 新DLL路径完成既有实调矩阵；provider/metadata仍受控 |
| F-03 | n/a_re/other | validated/high | E-01,E-10,E-11,E-12 | full/源码/包 | 25/1617、旧39、新Kernel7分别通过并归档；包未安装 |
| F-04 | n/a_re/design | validated/high | E-07,E-08,E-09 | runner锁窗口/后续Copy | during31匹配与after1变化可同时成立；摘要误断言已有实际原件 |
| F-05 | n/a_re/design | validated/high | E-14,E-15,E-16 | 安装精确白名单/恢复fixture | 真实漏项红测后3脚本补正、Safety16/Recovery13另证；非第二次full/游戏安装 |

**P-01，path_type=callflow**：同一canonical源码→独立no-entry/no-defaultlib链接→父按exact path持锁→descriptor child actualGetEntry/拒绝→父heldDisk literal/reloc核验→自有Dobby目标的native136→五managed模式实际bridge/dispatcher/ledger→释放31输入句柄→其后legacy helper正常Copy→完整包与归档。每步关联E-03/04、E-05/06、E-07/08、E-10/11；终点仅自有验证与暂存包，不含生产首次写或取消完成。

## 复核命令与时间线

C1是本报告离线重建，逐项读取599/23 ZIP、当前源/暂存、Kernel原件和供给文件hash；只写本报告，不构建/运行child或重新输出测试结果。

```powershell
Set-Location 'C:/Users/71957/Desktop/riz_toolbox'
& 'C:/Python314/python.exe' 'docs/validation/foundation-engine-kernel-20261025/write-report.py'
```

C2/C3为独立PE/实际入口原件离线复核，只向全新临时目录写证明。manifest和entry JSON保留当次absolute path；本机cache还在可直接用原路径，搬迁机器需要恢复精确文件并显式调整核验上下文，不能把新路径自报当旧实际加载事实。

```powershell
$repo='C:/Users/71957/Desktop/riz_toolbox'
$batch=Join-Path $repo 'docs/validation/foundation-engine-kernel-20261025'
$case=Get-Content -LiteralPath (Join-Path $batch 'engine-kernel-evidence.json') -Raw | ConvertFrom-Json
$out=Join-Path ([IO.Path]::GetTempPath()) ('RizKernelReview-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $out | Out-Null
$verify=Join-Path $batch 'verify-engine-kernel.py'
& 'C:/Python314/python.exe' $verify --kernel $case.kernelPath --manifest $case.manifestPath --output (Join-Path $out 'pe.json')
& 'C:/Python314/python.exe' $verify --kernel $case.kernelPath --manifest $case.manifestPath --entries $case.entryProof.actualEntryFixturePath --output (Join-Path $out 'entry-proof.json')
```

实际构建与调用入口是{link(REPO/'scripts/Build.ps1')}、{link(REPO/'scripts/Build-EngineNodeKernel.ps1')}、{link(REPO/'scripts/Test-EngineNodeKernel.ps1')}；当次参数与日志见{link(BATCH/'run-build.ps1')}、E-01及{link(BATCH/'build.log')}。本次报告生成没有重跑它们，工具链/SDK前提仍须单独准备。

| 时间/实际记录 | 事项 |
|---|---|
| history/observed-runs | 11782 Kernel-only与73213首descriptor编译实际0；初sample误名字段留历史 |
| session96386 | 七case专项预跑实际0，后来纳入full再次执行 |
| {wrapper['completedAtUtc']} | full23528 wrapper完成exit0，25套/SDK/包流程结束 |
| {run['recordedAtUtc']} | root实际记录full terminal0 |
| history/first-summary-failure.log | 成功full后的首次offline摘要误断言失败，随后明确during/after分列 |
| {source_proof['verifiedAtUtc']} | 599源ZIP逐项证明完成；23包随后完成 |
| {install_summary['verifiedAtUtc']} | 另批Safety16/Recovery13终态、3脚本overlay及现行599源码ZIP证明完成 |

## 下一步与仍未完成的目标

旧73进程EarlyFixed链的Proxy/Recorder和FixedPublisher.Bind仍精确依赖JudgeContext；本批只验证独立Kernel，没有把新DLL绑到这条早期链。canonical代码共用也不意味着两个DLL状态相同。下一步按{link(BATCH/'next-integration.md','下一接入计划')}建立精确绑定Kernel的新自有早期producer/回执及全部故障/页责任同链证明，再进入独立的真实系统WinHTTP转发、固定UnityPlayer/GameAssembly早期准入与V4生产身份。{link(REPO/'src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.cs','AdoptConfigured')}仍是internal受控采用，不能用fixture JSON/tuple授生产权限。

随后才接真实coordinator、receiver root/native owner/Enumerator/Coroutine/engine node代次及IL2CPP/GC；补scheduler登记/消费/drain、async/futureStart、Start内联清理与全部writer/delegate/消费者退休。最后在用户恢复实测后验证冷启、暂停、正常/失败结算、重试、返回再开局与Loader重建。完整{link(REPO/'.docs/foundation-expansion/SPEC.md','B01–B19')}保持active，当前只B03整项/B18a子项验收；本报告不增加勾选。`productionKernelAdopted=false`、`kernelEarlyLoaderChainVerified=false`、`productionV4Issued=false`、`EngineCancelled=false`；Pending保留、RequestPlay关闭，新包未安装。
"""
    for target in re.findall(r"\]\(([^)]+)\)", text):
        if "://" not in target and not target.startswith("#"):
            need((REPORT.parent / target.split("#",1)[0]).resolve().exists(), f"Missing local report link: {target}")
    REPORT.write_text(text, encoding="utf-8")
    print(json.dumps(dict(report=str(REPORT),sha256=sha(REPORT),sources=599,package=23,projects=25,checks=1617,kernelProcesses=7,originals=180,pairs=152),ensure_ascii=False))


if __name__ == "__main__":
    generate()
