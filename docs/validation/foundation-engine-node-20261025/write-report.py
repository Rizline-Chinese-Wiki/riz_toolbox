"""Generate the ordinary Chinese report only from final, matching evidence.

Reads logs, JSON, source and archives; writes the designated Markdown report.
Never builds, executes fixtures/game code, installs or changes frozen source.
Missing full-build, raw fixture or archive proofs are a hard failure, not a
reason to describe a planned run as passed.
"""
import hashlib
import json
import re
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path


batch = Path(__file__).resolve().parent
repo = batch.parents[2]
destination = repo / "docs/2026-10-25_底层-引擎协程节点观察-report.md"
previous = batch.parent / "foundation-operation-preinit-20261024"


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def link(name, label=None):
    return f"[{label or name}](validation/{batch.name}/{name})"


def source_link(name):
    return f"[{name}](../{name})"


def raw_json(path):
    lines = [line for line in path.read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]
    assert len(lines) == 1, path
    return json.loads(lines[0])


def choose(*names):
    for name in names:
        if (batch / name).is_file():
            return name, read(name)
    raise FileNotFoundError(f"Final evidence missing: {names}")


def local_time(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(
        timezone(timedelta(hours=8))).strftime("%Y-%m-%d %H:%M:%S +08:00")


def verify_archive(proof, manifest, current):
    rows = manifest["files"]
    assert proof["allEntryHashesMatched"] and proof["entries"] == len(rows)
    assert sha(batch / proof["archive"]) == proof["archiveSha256"]
    with zipfile.ZipFile(batch / proof["archive"]) as archive:
        assert set(archive.namelist()) == {row["path"] for row in rows}
        assert archive.testzip() is None
        for row in rows:
            assert hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"], row["path"]
            if current:
                assert sha(repo / row["path"]) == row["sha256"], row["path"]


def verify_modes(aggregate, directory):
    modes = aggregate["modes"]
    assert aggregate["passed"] and aggregate.get("failures", 0) == 0
    assert aggregate["independentProcesses"] == len(modes)
    assert aggregate["checks"] == sum(row["checks"] for row in modes)
    for row in modes:
        assert row["passed"] and row["failures"] == 0 and row["modeVerified"]
        assert read(f"{directory}/{row['mode']}-result.json") == row
        stdout = batch / f"{directory}/{row['mode']}.stdout.log"
        if not stdout.is_file():
            stdout = batch / f"{directory}/{row['mode']}.log"
        assert raw_json(stdout) == row
    for process in aggregate.get("processes", []):
        assert process["exitCode"] == 0
        if "logSha256" in process:
            assert sha(batch / f"{directory}/{process['mode']}.log") == process["logSha256"]
        else:
            for stream in ["stdout", "stderr"]:
                assert sha(batch / f"{directory}/{process['mode']}.{stream}.log") == process[f"{stream}Sha256"]


def require_false(value, keys):
    assert all(value[key] is False for key in keys)


def main():
    run, summary, wrapper = read("build-run.json"), read("build-summary.json"), read("build.log.run.json")
    assert run["exitCode"] == run["terminalExitCodeObserved"] == wrapper["exitCode"] == 0
    assert run["rootSoleBuilder"] and wrapper["rootSoleBuilder"] and not run["installed"] and not run["gameExecuted"]
    assert not wrapper["installed"] and not wrapper["gameExecuted"]
    assert summary["projects"] == len(summary["suites"]) == 25 and summary["passed"] == summary["total"]
    assert all(row["passed"] == row["total"] for row in summary["suites"])
    assert sha(batch / "build.log") == run["logSha256"] == wrapper["logSha256"] == summary["logSha256"]
    completed = wrapper["completedAtUtc"]
    observed = run.get("recordedAtUtc", completed)
    log_text = (batch / "build.log").read_text(encoding="utf-8-sig")

    groups = [
        ("tests/RizToolbox.Runtime.Tests/NativeEngineNodeDispatcherTests.cs", 10),
        ("tests/RizToolbox.AudioContent.Tests/NativeEngineCoroutineNodeLedgerTests.cs", 8),
    ]
    for name, count in groups:
        cases = re.findall(r'^\s*\("([^"\r\n]+)",\s*[A-Za-z][A-Za-z0-9_]*\)', (repo / name).read_text(encoding="utf-8"), re.M)
        assert len(cases) == count and all(f"PASS {case}" in log_text for case in cases), name

    native_name, native = choose("engine-node-native-evidence.json", "native-build/engine-node-native/fixture-result.json")
    joint_name, joint = choose("engine-node-managed-evidence.json", "native-build/engine-node-managed/fixture-result.json")
    assert native["kind"] == "native-engine-node-fixture" and native["passed"] and native["failures"] == 0
    assert native["checks"] > 0 and native["actualOriginalCalls"] == 128
    assert native["entered"] == native["exited"] == native["uniqueSequences"] == 116
    assert native["unobservedCalls"] == 12 and native["frameSize"] == 112 and native["completionSize"] == 32
    assert native["kinds"] == [0, 1, 2, 3] and native["preparedTargets"] == native["committedTargets"] == 4
    for key in ["realDobbyPreparedCommit", "pinnedDobbyVerified", "exactOuterSourceCallerVerified",
                "nestedSourceCallerModuleVerified", "oldCallsNeverBorrowObserver", "deadNodeAfterReadAvoided", "rawAlByteVerified"]:
        assert native[key] is True, key
    require_false(native, ["fullRaxPreservationVerified", "exceptionFlagsTransparencyVerified", "engineCancellationCompleted",
        "gameCodeExecuted", "gameRuntimeVerified", "installed", "firstInstallSafetyVerified",
        "concurrentFirstPublicationVerified", "partialPublicationFailureInjected"])
    assert read("native-build/engine-node-native/fixture-result.json") == native
    assert raw_json(batch / "native-build/engine-node-native/fixture-stdout.log") == native
    fixture_manifest = read("native-build/engine-node-native/fixture-manifest.json")
    assert fixture_manifest["exitCode"] == 0 and fixture_manifest["allMatched"]
    for row in fixture_manifest["files"]:
        assert row["beforeSha256"] == row["afterSha256"] == sha(Path(row["path"]))

    assert read("native-build/engine-node-managed/fixture-result.json") == joint
    assert summary["engineNodeNative"] == native and summary["engineNodeManaged"] == joint
    verify_modes(joint, "native-build/engine-node-managed")
    modes = {row["mode"]: row for row in joint["modes"]}
    assert set(modes) == {"normal", "original-seh", "original-cpp", "detach", "observer-fault"}
    assert joint["independentProcesses"] == 5
    for mode, row in modes.items():
        assert row["configuredChannels"] == row["attachedChannels"] == row["sharedPeerSinks"] == 4
        assert row["actualDobbyPreparedTargets"] == row["actualDobbyCommittedTargets"] == 4
        assert row["physicalCaptures"] == row["completionCount"] == row["disposalCount"] == row["peerCompletionCount"] == row["peerDisposalCount"]
        assert row["nativeInFlight"] == row["dispatcherInFlight"] == row["ledgerOutstanding"] == 0
        assert row["ledgerPending"] and not row["engineCancelled"]
        assert row["ledgerUnknown"] == (mode in {"original-seh", "original-cpp", "observer-fault"})
        for key in ["nativeToClrCallbackVerified", "actualDobbyPublicationVerified", "productionBridgeAndDispatcherVerified",
                    "actualNodeLedgerCaptureVerified", "oneSharedLedgerAcrossFourKinds", "actualFrameSharedByTwoSinks",
                    "nativeFixtureScalarValuesControlled", "metadataIdentityControlled", "nativeExceptionsCaughtBeforePInvoke", "diskHashesUnchanged"]:
            assert row[key] is True, (mode, key)
        require_false(row, ["actualIl2CppMetadataResolved", "productionObjectBackendExecuted", "firstInstallReceiptUsed",
            "firstInstallSafetyVerified", "concurrentFirstPublicationVerified", "productionEngineHooksPublished",
            "exceptionFlagsTransparencyVerified", "engineCancellationExecuted", "schedulerClosureVerified",
            "futureStartClosureVerified", "gameCodeExecuted", "gameRuntimeVerified", "installed"])
        for result in row["results"]:
            assert result["captures"] == result["peerCaptures"] == result["completions"] == result["peerCompletions"] == result["disposals"] == result["peerDisposals"]
            assert result["Entered"] == result["Exited"] and result["InFlight"] == 0 and result["sourceInstructionsUnchangedAfterAdoption"]
            native_unknown = mode in {"original-seh", "original-cpp"} or mode == "observer-fault" and result["kind"] == 3
            assert result["Unknown"] == (1 if native_unknown else 0)
            assert result["bridgeKnown"] == (not native_unknown)
            assert result["dispatcherKnown"] == (mode not in {"original-seh", "original-cpp", "observer-fault"})
            for frame in result["frames"]:
                assert frame["completed"] and frame["disposed"] and 0 <= frame["rawByte"] <= 255
        for row_file in row["files"]:
            assert row_file["beforeSha256"] == row_file["afterSha256"] == sha(Path(row_file["path"]))

    source, package = read("source-archive-check.json"), read("package-archive-check.json")
    manifest, package_manifest = read("source-manifest.json"), read("package-manifest.json")
    verify_archive(source, manifest, True); verify_archive(package, package_manifest, False)
    assert sha(batch / "source-manifest.json") == source["manifestSha256"]
    assert package["entries"] == 22 and not package["installed"]
    integrity = read("build-input-integrity-check.json")
    assert integrity["preBuildManifestMatchesArchivedManifest"] and integrity["currentChanged"] == []
    assert read("source-integrity-check.json")["changed"] == []
    assert read("staging-check.json")["allMatched"]
    game, old_installation = read("original-game-check.json"), read("previous-installation-check.json")
    assert game["allUnchanged"] and old_installation["allMatched"]
    assert len(game["files"]) == 3 and len(old_installation["files"]) == 18
    assert old_installation["archivedSourceInputsChecked"] == 326
    for proof in [game, old_installation]:
        assert all(row["expected"] == row["actual"] for row in proof["files"])
    old = json.loads((previous / "source-manifest.json").read_text(encoding="utf-8-sig"))
    old_hashes = {row["path"]: row["sha256"] for row in old["files"]}
    hashes = {row["path"]: row["sha256"] for row in manifest["files"]}
    added = sorted(set(hashes) - set(old_hashes)); removed = sorted(set(old_hashes) - set(hashes))
    changed = sorted(name for name in old_hashes.keys() & hashes.keys() if hashes[name] != old_hashes[name])
    change_proof = read("changes-from-operation-preinit.json")
    assert change_proof["bothFrozenArchivesVerified"] and change_proof["added"] == added
    assert change_proof["changed"] == changed and change_proof["removed"] == removed
    assert (batch / "scope.md").is_file()

    # Preserve and disclose the actual preflight failures separately. A later
    # green full build never rewrites these earlier observations into success.
    compiler_red = (batch / "runtime-preflight.log").read_text(encoding="utf-8-sig")
    audio_red = (batch / "audio-preflight.log").read_text(encoding="utf-8-sig")
    runtime_green = (batch / "runtime-preflight-final.log").read_text(encoding="utf-8-sig")
    clr_red = (batch / "clr-build-preflight.log").read_text(encoding="utf-8-sig")
    clr_green = (batch / "clr-build-preflight-final.log").read_text(encoding="utf-8-sig")
    assert "error CS0266" in compiler_red and "621/627 audio/content checks passed." in audio_red
    assert "Native runtime checks: 170/170 passed." in runtime_green
    assert "error CS0170" in clr_red and "0 个错误" in clr_green
    diagnostics = len(summary["compilerDiagnosticLines"])
    suite_rows = "\n".join(f"| {row['summary']} | {row['passed']}/{row['total']} |" for row in summary["suites"])
    mode_purpose = {
        "normal": "真实 Start 同步 Execute/Release、两代节点、正常原返回与两 sink 清理",
        "original-seh": "实际四签名原调用 SEH；native catch；账本与 dispatcher 保 Unknown",
        "original-cpp": "实际四签名原调用 C++；native typed catch；不跨 P/Invoke 展开",
        "detach": "捕获时逻辑解绑；已捕获 peer/ledger 上下文仍完成与释放；后续调用不借旧 sink",
        "observer-fault": "实际 peer 完成故障与不可读 Before；原调用继续、局部责任排空但 Unknown 永久保留",
    }
    mode_rows = "\n".join(f"| `{mode}` | {row['checks']} | {row['actualOriginalCalls']} | {row['physicalCaptures']} | {mode_purpose[mode]} |" for mode, row in modes.items())
    normal_nodes = modes["normal"]["nodeGenerations"]
    assert len(normal_nodes) == 2 and len({row["Generation"] for row in normal_nodes}) == 2
    node_rows = "\n".join(f"| {row['Generation']} | `0x{row['Node']:x}` | `0x{row['NativeOwner']:x}` | `0x{row['Enumerator']:x}` | `{row['EnumeratorHandle']}` | {row['ActiveExecutions']}/{row['OpenCalls']} |" for row in normal_nodes)
    if normal_nodes[0]["Node"] == normal_nodes[1]["Node"]:
        assert normal_nodes[0]["NativeOwner"] != normal_nodes[1]["NativeOwner"]
        assert normal_nodes[0]["Enumerator"] != normal_nodes[1]["Enumerator"]
        assert normal_nodes[0]["EnumeratorHandle"] != normal_nodes[1]["EnumeratorHandle"]
        aba_text = "这次normal原件实际出现相同node地址的ABA复用，但Owner、Enumerator、script handle和本地Generation均不同；旧代次已观察最后Release并排空后才建立新代次。这里只证明这两条自有记录的复用处理，不能泛化为引擎所有分配/复用路径。"
    else:
        aba_text = "这次normal的两个实际node地址不同，实际联合记录证明两代身份关联；地址ABA边界另由受控复用用例覆盖，不能将其写成当前实际fixture已出现相同地址复用。"
    joint_originals = sum(row["actualOriginalCalls"] for row in modes.values())
    joint_captures = sum(row["physicalCaptures"] for row in modes.values())
    paths = added + changed
    source_rows = "\n".join(f"| {source_link(name)} | `{'新增' if name in added else '修改'}` | `{hashes[name]}` |" for name in paths)
    full_command = f"& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/{batch.name}/run-build.ps1"
    report_command = f"C:/Python314/python.exe docs/validation/{batch.name}/write-report.py"
    native_command = "执行 scripts/Build.ps1；其已登记脚本 scripts/Test-EngineNodeNative.ps1 对当次 helper/fixture 运行自有进程"
    managed_command = "执行 scripts/Build.ps1；其已登记脚本 scripts/Test-EngineNodeManaged.ps1 在5个独立CLR子进程运行 --engine-node-managed"
    evidence_specs = [
        ("E-001", "完整构建与 root 实际终态", "build-run.json", "log", full_command,
            f"session={run['sessionId']}; observed terminalExitCode=0; 25 suites {summary['passed']}/{summary['total']}; installed/gameExecuted=false"),
        ("E-002", "真实原生 typed Dobby 观察", native_name, "log", native_command,
            f"checks={native['checks']}; actualOriginalCalls={native['actualOriginalCalls']}; observedPairs={native['entered']}; frame112/completion32; rawAlByteVerified=true"),
        ("E-003", "实际 native→CLR dispatcher→生产节点账本", joint_name, "log", managed_command,
            f"independentProcesses=5; checks={joint['checks']:g}; actualNodeLedgerCaptureVerified=true in every mode; all ledgerOutstanding=0; engineCancelled=false"),
        ("E-004", "dispatcher 的受控形状、配对、故障边界", "runtime-preflight-final.log", "log", full_command,
            "Native runtime checks: 170/170 passed.; 10 new controlled dispatcher cases; not production UnityPlayer publication"),
        ("E-005", "本次全部声明源码的冻结归档", "source-inputs.zip", "file", report_command,
            f"entries={source['entries']}; all archive/manifest/current hashes match; +{len(added)}/~{len(changed)}/-{len(removed)} from preceding 546-input batch"),
        ("E-006", "22项暂存包归档与未安装事实", "package-archive-check.json", "file", report_command,
            "entries=22; allEntryHashesMatched=true; installed=false; no game runtime evidence"),
        ("E-007", "首次预编译的真实 CS0266 失败", "runtime-preflight.log", "log", full_command,
            "NativeEngineNodeDispatcherTests.cs(78,21): error CS0266; corrected int ternary constants to uint suffixes; retained original log"),
        ("E-008", "缺少 native 环境的 Audio 预跑", "audio-preflight.log", "log", full_command,
            "621/627 audio/content checks passed.; six existing native cases missing fixture environment; eight new ledger cases passed; retained original log"),
        ("E-009", "root 核对旧安装与原游戏文件", "original-game-check.json", "file", report_command,
            "original game 3 hashes allUnchanged; separate previous-installation-check.json verifies 18 old installation files; no new installation"),
        ("E-010", "联合CLI预编译的真实 CS0170 失败", "clr-build-preflight.log", "log", full_command,
            "NativeEngineNodeManagedRegression.cs(91,60): error CS0170; definite-assignment correction retains VirtualQuery validation; original red log retained"),
    ]
    evidence = "\n".join(f"""### {eid}：{title}

- observed_at: `{observed}`（root full终态记录时间；子进程/预跑原件另有各自记录）。
- source_type: `{kind}`；source_ref: {link(name)}。
- content_hash: `{sha(batch / name)}`。
- artifact_path: `docs/validation/{batch.name}/{name}`。
- repro_command: `{command}`（须保持本机依赖与测试隔离前提；失败预跑日志仅保留历史，不要求重造缺环境）。
- raw_excerpt: `{excerpt}`。
- linked_workitem: `n/a`（本批工程工作）；supersedes: `none`。
""" for eid, title, name, kind, command, excerpt in evidence_specs)

    body = f"""# 底层引擎协程节点观察报告

日期：2026-10-25。普通兼容底层工程报告，flavor=null，overlay=null。作者参与 typed CLR bridge／dispatcher 和受控测试实现；本文包含交叉复核，不能称为外部独立审计。

## 概述

本批把上批静态研究确认的 UnityPlayer Start、Execute、Release、Detach 四种实际 native 签名实现为独立观察通道，并建立关联同步首 Execute、node 地址代次、摘链与最后引用清理事实的 Adapter 标量账本。真实自有 Dobby fixture、真实 CLR 联合 fixture和受控边界测试均有原始记录。root 观察完整构建 session {run['sessionId']} 实际 exit0，25套 {summary['passed']}/{summary['total']}；{source['entries']}项声明源码与22项暂存文件已经逐项归档核验。

这些结果证明自有目标的传输、观察和节点账本配对；它们没有证明固定 UnityPlayer 四入口的安全首次发布、真实游戏 scheduler 封口或引擎取消完成。新桥未取得 V4 UnityPlayer 发布凭据，尚未挂入生产 coordinator；`EngineCancelled=false`，已有 Pending 和根退休限制不被这批局部节点事实解除。新包未安装，B01–B19完整目标仍未完成。

## 范围、授权与复现依赖

范围见 {link('scope.md')}。保留用户“先继续代码，实测稍后”的安排。本批执行本地实现、root唯一构建、自有 native/CLR 子进程及既有隔离回归；没有游戏/UI操作、安装、游戏进程终止、commit/push或原游戏文件修改。本报告生成器只读证据、源码和ZIP，并写本文。

本机依赖 Windows x64、MSVC2019 14.29.30133、.NET8测试、.NET6生产目标及已有 BepInEx bundle。Dobby限固定 SHA256 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`；Prepare/Commit合同只对该固定构建成立。报告生成用 `C:/Python314/python.exe` 标准库。源码归档不含游戏文件、完整缓存依赖或编译工具链；没有因此宣称换机干净复现或全部 B19 发布矩阵验收。

四个 RVA 及字段由上批[固定 UnityPlayer 静态研究](validation/foundation-operation-preinit-20261024/cancellation-research.md)和其[原始指令证据](validation/foundation-operation-preinit-20261024/research/cancellation-engine-evidence.json)提供。固定 UnityPlayer SHA256 `43fb0aea1b80c74963dae88ec7fc1c3c1993d893ce94d12972000fcc9b653ad1`，GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；它们是静态输入身份，不能替代实测。本批是对已知四入口的实现和验证，没有重新开展UnityPlayer导入表分析，也没有将自有 native fixture当成游戏二进制认定来源。

## 实现与证明范围

### 四种 native ABI 和永久通道

| 入口 | 固定 UnityPlayer RVA | 精确实际签名 | 观察事实与边界 |
| --- | --- | --- | --- |
| Start | `792240` | `uint8(owner, enumerator, nameArg, node** output)` | 四个实际参数；正常后仅读真实输出槽的地址值，不读输出 node |
| Execute | `78F840` | `uint8(node, byte* output)` | Before复制节点标量，独立记录实际执行窗口；原始 refcount不作为执行深度 |
| Release | `78F680` | `uint8(node)` | 保留原始AL；正常AL=0仅记最后清理局部事实；After不读可能free的node |
| Detach | `793840` | `void(node)` | Before记录实际node；正常返回仅记摘链观察完成，不签发取消完成 |

这是 UnityPlayer native ABI，没有 IL2CPP hidden MethodInfo，也没有把 raw AL 布尔归一化。四表各32个永久槽；Config24保存实际 target/original，Info112返回真实 kind/slot/target/original/entry/epoch、附加状态及计数，Observer32只允许一次附加。原始配置与入口发布分离，配置函数不写源指令；没有卸载、替换、native detach 或槽复用路径。

Frame112保留实际参数、caller、全四kind共享的正 sequence，以及 Before 复制的 node、owner、enumHandle/type/cache、referenceCount。Start没有Before node；其快照字段清零。其他入口只在 native SEH 内复制有界标量，读取故障时保留实际node参数、清零其余快照并记录 Faulted/Unknown，原函数仍按实际参数执行一次。原异常在native栈继续展开，Exit由native finally观察Abnormal；不把原异常带过CLR P/Invoke边界。Completion32独立记录 Returned/Abnormal/EnterFaulted、AL有效位和真实Start输出槽读取状态。所有After均不解引用节点。

entry最开始只读取一次nullable observer，Enter/Exit沿用同一快照。晚Attach不会给已经进入的原调用补造 CLR Enter/Exit。本批 native fixture实际 {native['actualOriginalCalls']}次 original，{native['entered']}组观察配对，{native['unobservedCalls']}次无观察原调用，{native['checks']}项检查。原异常{native['originalExceptionCases']}项、观察异常{native['observerExceptionCases']}项、worker{native['nativeWorkerCalls']}次、显式嵌套{native['explicitNestedCalls']}次、晚附加{native['lateAttachCases']}类、Before快照故障{native['snapshotFaultCases']}类、输出槽故障{native['outputFaultCases']}类、原函数释放节点{native['originalFreeNodeCases']}类。Caller证明区分 exact outer source和nested module范围，没有声称全部nested PC都逐点等值。

### CLR采用、dispatcher和节点账本

`NativeEngineNodeContextBridge.AdoptConfigured`只接受真实已配置身份，Attach前后重读helper Info并核全部身份；一次Attach、永久DLL/反向回调root与粘性Unknown。它不调用 Configure、DobbyPrepare、DobbyCommit或生产首次安装。公开构造identity值不构成Ready凭据；没有V4生产owner就不能借 fixture接口授权游戏安装。

独立 `NativeEngineNodeObserverDispatcher`避开了既有IL2CPP broker的MethodInfo/class合同。它对一次物理调用冻结sink成员快照，保留token、完整Frame、线程和跨4kind共享物理父栈；错误Frame/线程/父/token不消耗原上下文。已捕获上下文在逻辑解绑、其他sink故障或原函数异常后仍履行Complete和逆序Dispose。Native activity与托管责任合并读取，任一故障永久Unknown；该dispatcher本身不触碰游戏对象、发取消receipt或释放根。

`NativeEngineCoroutineNodeLedger`使用一个真实共享账本记录四类Frame。Start在输出前已可能同步执行：仅最近尚未完成Start的实际owner与type2 enumCache精确匹配时，为Execute登记node代次。裸地址复用要求旧代次已有最后清理事实、执行数/OpenCalls为0、旧Start完成且Dispose；活地址复用或身份漂移转Unknown。foreign nested Start不能借外层parent，未见真实Start的节点不被虚构纳入。Release的正常AL=0、Detach正常返回、Execute窗口退出与scheduler封口是不同事实。

账本先登记物理责任再匹配标量身份；异常、不可读Before和损坏completion保留Unknown，但精确已捕获Execute仍退出自己的物理执行数、配对Dispose仍清理本地OpenCalls。它不把node `+60` refcount当作执行数，不在CLR读取node，不释放真实root。Start同步路径的内联清理只标记 `InlineCleanupUnproven`，不能凭空补齐。8项账本边界回归和10项dispatcher受控回归均出现在完整构建原日志中。

native实现代理对自己实现的传输与联合CLI做了实现自查，fixture代理实现另一个自有native验证器；二者不能统称完全独立的外部审核。作者只读交叉复核了root ledger和8项测试，以及native Frame/Completion布局、nullable snapshot、finally和有界Before读取。复核确认上述最近Start匹配、地址复用闸口、精确scope排空和After无node访问有源码依据；不是对全部Unity写者、全部异常PC、真实IL2CPP GC或游戏生命周期的独立审核。

### 真实 Dobby→CLR→实际账本联合证据

{joint['independentProcesses']}个隔离CLR子进程，{joint['checks']:g}项检查，共{joint_originals}次实际original调用与{joint_captures}个账本capture。每进程实际Prepare/Configure/Commit四个自有native目标，通过真实helper、新bridge、dispatcher的两sink和实际Adapter node ledger；实际指令在采用后保持原有detour且没有重新安装。fixture输入标量、native node内存、owner/enum身份和main dispatcher仍由自有测试控制，不能称为真实游戏metadata或GC。

| 模式 | 检查 | 实际original调用 | 账本capture | 证明内容 |
| --- | --- | --- | --- | --- |
{mode_rows}

每模式actual captures、主/peer Complete和Dispose数分别相等，native与dispatcher inFlight以及ledger outstanding均归零。normal/detach的账本可保持已知局部事实，原异常或观察故障模式保持Unknown；所有模式Pending仍为true、EngineCancelled仍为false。SEH/C++模式四个native Info均Unknown=1；observer-fault模式只有Detach因真实Before读取故障而native Unknown=1，其余三种native通道仍Known，但四种dispatcher均因CLR contained observer fault永久Unknown。native故障和CLR观察故障属于不同层，没有将它们混为一份原生异常计数。

本次normal模式的两个实际节点记录如下。{aba_text}

| Generation | node地址 | native Owner | 实际Enumerator | script handle | 最终执行数/OpenCalls |
| --- | --- | --- | --- | --- | --- |
{node_rows}

实际node代次关联与原函数free后的After行为在原件逐Frame记录，不能拿这里的成功代替生产coordinator关联实际tracker receiver/Enumerator/Coroutine或Stop请求。

## 构建、失败保留和归档

完整wrapper完成时间：{local_time(completed)}。root记录终态时间：{local_time(observed)}。session `{run['sessionId']}`，实际exit0；完整日志SHA256 `{run['logSha256']}`。统计由当前原始日志与逐suite JSON读取，未以预估数量替代终态。

| 完整构建套件原文 | 结果 |
| --- | --- |
{suite_rows}

本批首次Runtime预编译真实失败：`NativeEngineNodeDispatcherTests.cs(78,21)`的条件表达式两个int常量不能隐式赋给uint，已改为 `0u/0xfeu`，原红日志保留。Audio缺native fixture环境的预跑真实为621/627，6项既有native用例失败；8项新增账本用例通过。新增联合CLI还遇到一次 `NativeEngineNodeManagedRegression.cs(91,60)` 的CS0170：短路条件中的out结构可能未赋值；修正确定赋值后仍保留实际VirtualQuery验证，CLR编译单次结果为0warning/0error，前后日志均保留。之后Runtime受控预跑170/170，以及本次完整环境的25套结果是各自独立观察，不抹去前述失败。完整日志中保留{diagnostics}条去重的原始编译诊断行；这是日志统计，不等同新增bug数量，也不把一次CLR编译的零warning推广到全部native构建。

本次声明源码相对前批546项：新增{len(added)}、修改{len(changed)}、删除{len(removed)}，当前{source['entries']}项。生成器重新打开源码ZIP和packageZIP，逐entry核对实际内容、manifest及当前声明源码；root的构建前manifest和归档manifest一致。22项暂存包SHA256 `{package['archiveSha256']}`；源码ZIP SHA256 `{source['archiveSha256']}`；source manifest SHA256 `{source['manifestSha256']}`。原游戏3项、旧安装18项及旧安装326项归档源码检查引用root当批证明；本作者没有重复打开旧326项归档，不将其冒充另一次游戏实测或独立复核。

| 本批源码输入 | 变更 | 当前SHA256 |
| --- | --- | --- |
{source_rows}

## Evidence

{evidence}

## Findings

### F-001：四种native实际签名能在自有目标保留原调用和观察配对

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-002, E-003, E-004, E-005]`。
- location: `native/RizToolbox.JudgeContext/EngineNodeContext.cpp`，`src/RizToolbox.Runtime/Hooks/NativeEngineNodeContextBridge.Types.cs`。
- impact: `raw AL、参数、不可读快照、原异常与已释放node的After边界均有有限自有fixture证明；未证明全部RAX/异常flags透明性。`
- repro_steps: `保持固定Dobby和本机依赖，运行本批run-build.ps1，再核原始native/managed模式JSON与日志；报告脚本独立核冻结输入。`
- remediation: `生产采用仍需独立固定UnityPlayer首发合同；不能使用IL2CPP void签名代替。`
- optional_attack: `n/a`，兼容性底层工程，没有攻击链主张。

### F-002：真实首执行与节点代次能进入实际Adapter账本，且局部完成不授引擎取消

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-001, E-003, E-005]`。
- location: `src/RizToolbox.Adapter/AudioContent/NativeEngineCoroutineNodeLedger.cs`。
- impact: `同步Execute早于Start输出、执行窗口与最后Release分开记账；所有新联合模式仍Pending=true/EngineCancelled=false。`
- repro_steps: `读取每模式nodeGenerations和逐Frame原始数据，再运行完整构建中8项账本边界与10项dispatcher边界。`
- remediation: `关联生产tracker及Stop精确请求，并另证scheduler/inline/future Start闭包后再设计逐记录取消完成转换。`
- optional_attack: `n/a`。

### F-003：完整底层验收仍未达到

- severity: `n/a_re`；category: `other`；status: `candidate`；confidence: `high`。
- evidence_ids: `[E-001, E-002, E-003, E-006]`。
- location: `.docs/foundation-expansion/SPEC.md`，`.docs/foundation-expansion/CHECKLIST.md`，生产coordinator和首次安装路径。
- impact: `当前证据不包含V4 UnityPlayer安全首发、真实metadata/GC和游戏运行；不能宣布B01–B19、冷启动、整局回收或取消完成。`
- repro_steps: `逐项核对本批false能力位与完整SPEC，不能将自有Dobby成功或suite数量提升为游戏验收。`
- remediation: `按下一节逐步补全生产合同并保留已有Pending/Unknown/根退休规则。`
- optional_attack: `n/a`。

## Path与下一步

### P-001：自有目标的真实调用与观察路径

- path_type: `callflow`。
- start: `四个Never-invoked自有native目标，固定Dobby。`
- goal: `实际typed调用进入同一node ledger并完整配对，保留取消完成为未证明。`
- steps:
  1. action: `实际Prepare→不可变Configure→Commit，读取真实slot/epoch/target/original/entry`；evidence: `E-002, E-003`；finding: `F-001`。
  2. action: `一次Attach，新bridge仅核身份，dispatcher冻结两sink并登记Frame/thread/parent`；evidence: `E-003, E-004`；finding: `F-001`。
  3. action: `Start original同步Execute/Release，native复制Before标量与Start输出槽；actual ledger匹配代次`；evidence: `E-003, E-005`；finding: `F-002`。
  4. action: `完成原调用/异常事实、主peer Complete及逆序Dispose；排空物理责任且保留Pending/Unknown边界`；evidence: `E-001, E-003`；finding: `F-002, F-003`。
- residual_risks: `自有fixture不是生产UnityPlayer首次发布；标量复制不是并发一致性快照；没有全部scheduler、inline清理、未来Start、实际tracker/metadata/GC及整局闭环证明。`

下一阶段先完成固定UnityPlayer四入口的PE/body/unwind/指令发布校验与首次原init前安全事务，形成不能由普通identity构造的生产V4凭据；不能在运行中的引擎直接晚装Dobby。随后由coordinator采用该凭据，关联实际tracker receiver/Enumerator/Coroutine、native owner/node代次和精确Stop请求。还须观察真实scheduler撤销、Start同步inline收尾、nested/continuation与未来生产者封口，并证明执行窗口全部退出；这些证据齐全后才能设计逐记录EngineCancelled转换与独立root退休，不能直接清整个launch Pending。按用户安排，真实游戏冷启动、重试、返回菜单、再次开局和整局资源回收放到后续实测；完整B01–B19逐项验收保持未完成。

## 时间线与复现入口

| 观察 | 原件 |
| --- | --- |
| 2026-10-25 首次Audio缺native环境预跑、Runtime条件常量类型错误、CLI确定赋值错误 | {link('audio-preflight.log')}、{link('runtime-preflight.log')}、{link('clr-build-preflight.log')} |
| 2026-10-25 修正后Runtime170项预跑和typed native自有fixture | {link('runtime-preflight-final.log')}、{link(native_name)} |
| 2026-10-25 五进程真实native／CLR／实际账本联合回归 | {link(joint_name)} |
| {local_time(completed)} 完整wrapper终态；{local_time(observed)} root记录真实终态 | {link('build.log.run.json')}、{link('build-run.json')} |
| 本报告生成时重新校验当前源码与ZIP | {link('source-archive-check.json')}、{link('package-archive-check.json')} |

在仓库根用以下命令复核；第一条为完整构建，会重写当批构建产物，需root唯一builder约束且证据归档已保全。第二条仅读现有证据/源码/ZIP，若终态或归档不齐会失败，不启动fixture或游戏。

```powershell
{full_command}
{report_command}
```

详见 {link('README.md')}、{link('next-integration.md')}、[总验收清单](../.docs/foundation-expansion/CHECKLIST.md)。本文只汇报本批已核证据；完整目标保持进行中。
"""
    assert "TODO" not in body and "placeholder" not in body
    destination.write_text(body, encoding="utf-8")
    print(json.dumps({"report": str(destination), "sha256": sha(destination), "fullSession": run["sessionId"],
        "suites": summary["projects"], "passed": summary["passed"], "inputs": source["entries"],
        "nativeChecks": native["checks"], "managedChecks": joint["checks"], "allChecksPassed": True}, ensure_ascii=False))


if __name__ == "__main__":
    main()
