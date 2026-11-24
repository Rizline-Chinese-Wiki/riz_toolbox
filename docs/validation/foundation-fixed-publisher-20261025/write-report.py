"""Generate this standalone component report from final frozen evidence only.

Reopens archives and independently rechecks existing PE/receipt/memory/unwind/
call records. Does not compile, execute children, load DLLs or touch game state.
"""
import hashlib
import importlib.util
import json
import re
import zipfile
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]
destination = repo / "docs/2026-10-25_底层-固定发布组件验证-report.md"


def read(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def link(name,label=None):
    return f"[{label or name}](validation/{batch.name}/{name})"


def source_link(name):
    return f"[{name}](../{name})"


def raw_summary(path):
    rows = [line for line in path.read_text(encoding="utf-8-sig").splitlines() if line.startswith("{")]
    assert len(rows) == 1,path
    return json.loads(rows[0])


def check_archive(proof,manifest,current):
    rows = manifest["files"]
    assert proof["allEntryHashesMatched"] and proof["entries"] == len(rows)
    assert sha(batch/proof["archive"]) == proof["archiveSha256"]
    if "manifestSha256" in proof:
        assert sha(batch/("source-manifest.json" if current else "binary-manifest.json")) == proof["manifestSha256"]
    with zipfile.ZipFile(batch/proof["archive"]) as archive:
        assert archive.testzip() is None and set(archive.namelist()) == {row["path"] for row in rows}
        for row in rows:
            assert hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"]
            if current:
                assert sha(repo/row["path"]) == row["sha256"],row["path"]


def main():
    run,wrapper = read(batch/"component-run.json"),read(batch/"component.log.run.json")
    assert run["exitCode"] == run["terminalExitCodeObserved"] == wrapper["exitCode"] == 0
    assert run["rootSoleBuilder"] and run.get("finalCandidate",True) is not False
    assert not run["gameExecuted"] and not run["installed"] and not wrapper["gameExecuted"] and not wrapper["installed"]
    assert sha(batch/"build.log") == wrapper["buildLogSha256"]
    assert sha(batch/"test.log") == wrapper["testLogSha256"]
    assert run["buildLogSha256"] == wrapper["buildLogSha256"] and run["testLogSha256"] == wrapper["testLogSha256"]
    frozen = read(batch/"component-inputs-before.json")
    source,binary = read(batch/"source-manifest.json"),read(batch/"binary-manifest.json")
    source_check,binary_check = read(batch/"source-archive-check.json"),read(batch/"binary-archive-check.json")
    assert len(source["files"]) == len(frozen["files"]) == 12 and source["files"] == frozen["files"]
    assert len(binary["files"]) == 4
    check_archive(source_check,source,True)
    check_archive(binary_check,binary,False)
    integrity = read(batch/"component-input-integrity-check.json")
    assert integrity["checked"] == 12 and integrity["preBuildMatchesArchived"] and integrity["currentChanged"] == []
    source_hashes = {row["path"]:row["sha256"] for row in source["files"]}
    assert "native/RizToolbox.JudgeContext/EngineNodeContext.h" in source_hashes
    assert "docs/validation/foundation-fixed-publisher-20261025/verify-fixed-publisher.py" in source_hashes
    assert "scripts/Build.ps1" not in source_hashes

    raw_build,raw = batch/"raw/build",batch/"raw/cases"
    build = read(raw_build/"fixture-build-manifest.json")
    aggregate = read(raw/"fixture-result.json")
    assert build["kind"] == "fixed-publisher-fixture-build" and len(build["files"]) == 4
    for row in build["files"]:
        assert sha(raw_build/row["name"]) == row["sha256"]
        assert next(item["sha256"] for item in binary["files"] if Path(item["path"]).name == row["name"]) == row["sha256"]
    for row in build["sources"]:
        path = Path(row["path"]).resolve()
        assert sha(path) == row["sha256"] == source_hashes[path.relative_to(repo).as_posix()]
    assert aggregate["kind"] == "fixed-publisher-parent-evidence" and aggregate["passed"] and aggregate["error"] is None
    assert aggregate == read(batch/"fixed-publisher-evidence.json")
    assert aggregate["verifiedCases"] == aggregate["expectedCases"] == len(aggregate["caseResults"]) == 57
    assert aggregate["allInputHashesUnchanged"] and all(not aggregate[key] for key in
        ["gameExecuted","productionFirstInstallVerified","loaderStageVerified","engineCancellationCompleted","installed"])
    for row in aggregate["files"]:
        assert row["beforeSha256"] == row["afterSha256"] == sha(raw_build/row["name"])
    dependency = read(batch/"research/publisher-dependency-evidence.json")
    final_publisher_hash = next(row["sha256"] for row in build["files"] if row["name"] == "RizFixedPublisher.dll")
    assert dependency["image"]["sha256"] == final_publisher_hash and dependency["boundaries"]["diskOnly"]
    assert dependency["boundaries"]["localExactCfgAndImportSitesReviewed"]
    assert all(dependency["boundaries"][key] is False for key in ["nativeExecutedByThisReview","fullIndirectCallClosureProved",
        "osImplementationAudited","helperImplementationAudited","allTransitiveDependenciesLoaderSafeProved",
        "earlyLoaderAdmissionVerified","arbitraryGprOrFlagsTransparencyProved","productionV4Issued","gameReady","engineCancellationCompleted"])
    spec = importlib.util.spec_from_file_location("fixed_component_offline",batch/"verify-fixed-publisher.py")
    verifier = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(verifier)
    pe = read(raw/"pe-proof.json")
    assert pe["passed"] and pe["kind"] == "owned-fixed-publisher-pe-proof"
    for name in ("publisher","targets","helper"):
        assert sha(raw_build/pe[name]["name"]) == pe[name]["sha256"]
    keys = set()
    cases = []
    for row in aggregate["caseResults"]:
        name,process,host = row["name"],row["process"],row["host"]
        folder = raw/name
        assert name not in keys
        keys.add(name)
        assert process == read(folder/"process.json") and process["exitCode"] == 0 and process["started"] and not process["timedOut"]
        assert host == raw_summary(folder/"child.stdout.log") and host["passed"] and host["failures"] == 0
        assert (process["stage"],process["kind"]) == (host["faultStage"],host["faultKind"])
        assert host["controlledUnwindCases"] == 50 and not host["actualExceptionsAtEveryPc"] and not host["gameExecuted"] and not host["firstInstallVerified"]
        prefix = folder/"case"
        receipt = verifier.verify_receipt(Path(str(prefix)+".receipt.bin"),raw/"pe-proof.json",folder/"child.stdout.log",Path(str(prefix)+".memory.bin"))
        unwind = verifier.verify_unwind(Path(str(prefix)+".unwind.jsonl"),raw/"pe-proof.json",Path(str(prefix)+".prepared.bin"))
        assert row["receipt"] == read(folder/"receipt-proof.json") == receipt
        assert row["unwind"] == read(folder/"unwind-proof.json") == unwind
        assert receipt["memory"]["actualSourceBytesVerified"] and receipt["memory"]["actualSharedPageProtectionVerified"]
        cases.append(row)
    assert keys == {"normal"}|{f"fault-{stage}-{kind}" for stage in range(1,15) for kind in range(4)}
    normal = next(row for row in cases if row["name"] == "normal")
    calls = verifier.verify_calls(raw/"normal/case.calls.jsonl",raw/"normal/child.stdout.log")
    assert calls == read(raw/"normal/calls-proof.json") and calls["actualOriginalCalls"] == 34 and calls["observerPairs"] == 20
    assert calls["originalExceptionsCaught"] == 16 and calls["baselineExceptionCalls"] == 8
    total_host = sum(row["host"]["checks"] for row in cases)
    total_receipt = sum(row["receipt"]["checks"] for row in cases)
    total_unwind = sum(row["unwind"]["checks"] for row in cases)
    controlled_contexts = sum(row["host"]["controlledUnwindCases"] for row in cases)
    assert controlled_contexts == 2850
    summary = read(batch/"build-summary.json")
    assert ((summary["sessionId"],summary["componentSources"],summary["binaryFiles"],summary["independentProcesses"],
            summary["verifiedCases"],summary["nativeHostChecks"],summary["independentReceiptAssertions"],
            summary["controlledUnwindCases"],summary["independentUnwindAssertions"],summary["independentCallAssertions"],
            summary["actualOriginalCalls"],summary["observerPairs"],summary["originalExceptionsCaught"],summary["physicalMemorySnapshots"]) ==
            (run["sessionId"],12,4,57,57,total_host,total_receipt,controlled_contexts,total_unwind,calls["checks"],34,20,16,57))
    assert all(summary[key] is False for key in ["fullManagedSuitesRerun","loaderStageVerified","productionFirstInstallVerified","gameExecuted","installed"])

    history_root = batch/"history"
    history = read(history_root/"observed-runs.json")
    assert [row["exitCode"] for row in history["buildSessions"]] == [1,1,1,0,0,0]
    assert [row["exitCode"] for row in history["normalProcesses"]] == [1,1,0]
    first_dir = history_root/history["normalProcesses"][0]["directory"]
    second_dir = history_root/history["normalProcesses"][1]["directory"]
    third_dir = history_root/history["normalProcesses"][2]["directory"]
    first_rows = [json.loads(line) for line in (first_dir/"normal.unwind.jsonl").read_text().splitlines() if line]
    assert len(first_rows) == 46
    first_red = [row for row in first_rows if not row["passed"]]
    assert {(row["kind"],row["originalStub"],row["offset"]) for row in first_red} == {(1,True,18),(2,True,16),(3,True,15)}
    assert all(row["afterRip"] == 0 and row["afterRsp"] != row["expectedRsp"] for row in first_red)
    second_rows = [json.loads(line) for line in (second_dir/"normal.unwind.jsonl").read_text().splitlines() if line]
    assert len(second_rows) == 50 and all(row["passed"] for row in second_rows)
    # Archive filenames are from the actual normal command, not reconstructed
    # children. Preserve their raw result even when host assertions were wrong.
    history_stdout = {}
    for label,folder in [("first",first_dir),("second",second_dir),("third",third_dir)]:
        stdout_candidates = [path for path in folder.glob("*.log") if "stdout" in path.name]
        if stdout_candidates:
            text = stdout_candidates[0].read_text(encoding="utf-8-sig")
            json_rows = [line for line in text.splitlines() if line.startswith("{")]
            history_stdout[label] = json.loads(json_rows[0]) if len(json_rows)==1 else None
    if history_stdout.get("second"):
        assert history_stdout["second"]["failures"] == 4 and not history_stdout["second"]["passed"]
    assert history["matrixPreflight"]["sessionId"] == 7435 and history["matrixPreflight"]["exitCode"] == 0
    assert history["matrixPreflight"]["physicalMemorySnapshotIncluded"] is False

    binary_rows = "\n".join(f"| `{row['name']}` | `{row['sha256']}` |" for row in build["files"])
    source_rows = "\n".join(f"| {source_link(row['path'])} | `{row['sha256']}` |" for row in source["files"])
    target_rows = "\n".join(f"| {row['kind']} / {row['name']} | `{row['prefix']}` | {len(bytes.fromhex(row['prefix']))} | {', '.join(map(str,row['boundaries']))} |" for row in pe["targetRows"])
    unwind_rows = "\n".join(f"| {row['kind']} / {verifier.KIND_NAMES[row['kind']]} | {row['function']['unwind']['prologSize']} | {', '.join(map(str,row['boundaries']))} | `{row['tailBytes']}` |" for row in normal["receipt"]["staticOriginals"])
    fault_rows = "\n".join(f"| `{row['name']}` / {row['receipt']['case']['faultName']} | {row['receipt']['info']['stateName']} | {row['receipt']['info']['failure']} | {row['receipt']['info']['sourceWriteAttempts']}/{row['receipt']['info']['sourceWriteCompletions']} | {row['receipt']['info']['protectionOutstanding']} | {row['receipt']['receipts'][row['host']['faultKind']]['sourceBytesWritten']} |" for row in cases if row["name"] != "normal")
    import_rows = "\n".join(f"| `{row['module']}` | {', '.join('`'+item.get('name',str(item.get('ordinal')))+'`' for item in row['symbols'])} |" for row in pe["publisher"]["imports"])
    seh_baseline = [json.loads(line) for line in (raw/"normal/case.calls.jsonl").read_text().splitlines() if line]
    baseline_rows = "\n".join(f"| {row['kind']} | `0x{row['exceptionFlags']:x}` | `0x{row['exceptionCode']:x}` | {row['exceptionParameters']} |" for row in seh_baseline[:4])
    previous_report = repo/"docs/2026-10-25_底层-自有早期加载验证-report.md"
    assert previous_report.exists() and (batch/"scope.md").exists()
    component_command = f"& 'C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' -NoProfile -File docs/validation/{batch.name}/run-component.ps1"
    report_command = f"C:/Python314/python.exe docs/validation/{batch.name}/write-report.py"
    specs = [
        ("E-001","root观察独立组件完整终态","component-run.json",f"session={run['sessionId']}; terminal0; standalone12inputs/4binaries; no25suite rerun claim"),
        ("E-002","实际PE和固定非NOP前缀","raw/cases/pe-proof.json","publisher noTLS/noDelay; Kernel32 allowlist + NTDLL native SEH; exact four nonNOP prefixes/RF/xdata"),
        ("E-003","实际source bytes与共享页保护","raw/cases/normal/receipt-proof.json","GetInfo21640; memory240; independent bytes and same-page protection checked per every fault"),
        ("E-004","50PC受控上下文实际RtlVirtualUnwind","raw/cases/normal/case.unwind.jsonl","50 actual API calls with raw CONTEXT/stack; before states independently replayed; tail MOV/JMP included; not actual exception at everyPC"),
        ("E-005","真实调用/异常基线与副作用","raw/cases/normal/case.calls.jsonl","28outer=8baseline+20after; originals34; observers20; caught16; complete Execute EAX/Release oneDEC + CMP ZF"),
        ("E-006","57个独立进程和内部故障矩阵","raw/cases/fixture-result.json",f"57 children; hostChecks{total_host}; receiptAssertions{total_receipt}; unwindAssertions{total_unwind}; forced API failures explicitly injected"),
        ("E-007","真实红和预跑历史","history/observed-runs.json","three compile/link failures; first46PC three tailFF25 red; second50PC green but four bad zero-flags assertions; third exact baseline; firstmatrix no memorysnapshot"),
        ("E-008","12项组件源码冻结归档","source-inputs.zip",f"entries12; includes verifier and shared EngineNodeContext.h; helper DLL reused exact prior binary; source archive{source_check['archiveSha256']}"),
        ("E-009","四个组件输入binary归档","fixture-binaries.zip",f"entries4; archive{binary_check['archiveSha256']}; not installed or game patch"),
        ("E-010","最终publisher本地调用有限静态研究","research/publisher-dependency-evidence.json",
            f"actual final image SHA{final_publisher_hash}; local exact CFG/import sites reviewed; Bind externalhelper calls3; full indirect/helper/OS/loader closure false"),
    ]
    evidence = "\n".join(f"""### {eid}：{title}

- observed_at: `{run['recordedAtUtc']}`（root最终wrapper终态记录时间；原件保存各进程/PC事件）。
- source_type: `file`；source_ref: {link(path)}；content_hash: `{sha(batch/path)}`。
- artifact_path: `docs/validation/{batch.name}/{path}`。
- repro_command: `{report_command}`（只离线复核；运行组件需root唯一builder执行正文命令）。
- raw_excerpt: `{excerpt}`。
- linked_workitem: `n/a`（兼容工程组件）；supersedes: `none`，历史红保留。
""" for eid,title,path,excerpt in specs)
    body = f"""# 固定四入口最小发布组件验证报告

2026-10-25。flavor=null，overlay=null。作者实现独立离线验证器并提出展开、真实异常基线和共享页保护审查；同组代理实现publisher/ASM，root实现自有host与构建/执行。本文是参与者的有限交叉验证，不称外部独立审计。

## 本批达到的边界

固定容量、无Dobby/动态CRT/堆/logger的自有四入口publisher已完成组件验证。root实际观察单一build+test wrapper session `{run['sessionId']}` terminal0，12项组件输入、4个binary、57个独立自有child归档匹配。正常进程实际34次original、20份helper观察配对、16次原异常catch（含8个发布前基线），并通过50个受控PC的RtlVirtualUnwind。56个内部故障模式另核真实部分字节、保护恢复和不可重试责任。

它是独立单进程组件，尚未接入Build.ps1、early-loader或生产coordinator，没有证明任何首次调用/线程历史关闭。上一里程碑[自有早期加载报告](2026-10-25_底层-自有早期加载验证-report.md)的session99557、576源码和25套1617项是另外的冻结证据；本批没有重跑这些25套，也不将57×50或父断言数混入1617。新包未安装，游戏/UI未执行，V4/gameReady/EngineCancelled和根退休权限仍未授予，完整B01–B19继续。

## 输入、工具和范围

见 {link('scope.md')}。本批只新增自有组件，不修改旧批576项源码及其ZIP/manifest/原件；12项声明输入包括8个native文件、2个专用构建/测试脚本、独立Python验证器，以及编译时实际包含的既有EngineNodeContext.h。helper DLL复用上一99557已通过的精确binary，不声称本批重编helper或验证整个旧runtime。已存在Windows x64/MSVC2019工具链负责root构建，Python3.14标准库仅读实际PE和原始记录；没有IDA执行、动态解码引擎或游戏DLL调用。

publisher用/NOENTRY、/NODEFAULTLIB、固定POD/永久槽及静态original，不分配可执行trampoline、不用通用指令重定位器。组件父runner对4个binary计算hash并持有只读禁止写/删句柄，逐child固定绝对Host路径，只终止亲自启动且超时的child。编译失败或父验证失败均不计passed。

| binary | SHA256 |
| --- | --- |
{binary_rows}

publisher实际普通imports如下，无TLS/delay-import、无Dobby/CRT/heap/迟到LoadLibrary imports。GetModuleHandle/GetProcAddress只核已加载固定模块/helper，不把它们描述为“无任何loader API”。NTDLL的`__C_specific_handler`是本组件native SEH系统入口，明确列出；不是CRT导入。无这些imports只是必要证据，不能单独证明所有静态实现/间接分支/OS内部或loader-lock安全已关闭。

补充作者自查的 {link('research/publisher-dependency-evidence.json')} 已重新读取最终publisher `{final_publisher_hash}`，{link('research/before-final-publisher-dependency-evidence.json')} 保留旧第六编研究。最终本地直接CFG/import site研究没有将Bind的3个固定helper外部查询展开，helper、OS、所有间接和传递loader闭包均false；本文仅读该JSON并核最终image hash/边界，不声称独立复做全部反汇编。[依赖审查叙述](validation/{batch.name}/publisher-dependency-review.md)将最终产物与第六编历史分列，明确作者身份与有限范围，历史hash不当最终binary。

| 依赖 | 实际imports |
| --- | --- |
{import_rows}

## 四个真实固定前缀和静态original

自有RizFixedOwnedTargets.dll含只读16byte `RIZFIXEDOWNEDV1,NUL` marker及四个真实非NOP entry，均按actual loaded module/name/hash/RVA/完整字节校验；不接受任意module、prefix、original或callback作为通用patch API。Start使用原要求的栈home槽+08/+18/+20。Execute保存RBP/RBX/RDI/R14并分配178h，LEA RBP是已保存临时寄存器，不伪加UWOP_SET_FPREG；Release前缀实际DEC node+60一次，CMP结果由continuation第一条SETZ消费；Detach先save RBP、push RDI/sub20，再清EBP。

| kind | 精确prefix hex | 长度 | 已验证prefix PC偏移 |
| --- | --- | --- | --- |
{target_rows}

source patch仍为14byte `FF25 + addr64`，在完整指令边界N结束并以NOP填满N；原stub逐条重放prefix后使用7byte `MOV R11,[RIP+continuationSlot]`及3byte `JMP R11`，R11是这组typed ABI允许改写的volatile寄存器。MOV/JMP不增加flags副作用；原prefix自身的DEC/CMP/XOR等副作用必须保持。四种prefix没有待支持的任意RIP-relative源指令，不宣称已做泛用重定位；仅此尾MOV的实际RIP displacement指向固定独立可写、非执行continuation数组。生产UnityPlayer需另行固定真实前缀、machine ABI、任意PC/隐藏依赖验证，不能由typed约定猜通用R11许可。

| kind | stub实际prologSize/RF长度 | 检查的stub PC偏移 | 尾MOV/JMP bytes |
| --- | --- | --- | --- |
{unwind_rows}

### 真实展开失败驱动的修正

最初exact-prefix后直接FF25间接JMP，虽然把末JMP纳入SizeOfProlog=N+6，第一次实际46个RtlVirtualUnwind样本仍有3红：Execute+18、Release+16、Detach+15，afterRip=0且RSP不是caller。测试在任何source publication之前停止；不能把声明正确当实际Windows展开通过，也没有把46-3伪算全过。原件在 {link('history/'+history['normalProcesses'][0]['directory']+'/normal.unwind.jsonl')}。

替换为MOV R11/JMP R11后，RF/prolog实际长度N+10，50个PC含每条prefix边界、尾MOV首PC=N、尾JMP首PC=N+7和原target continuation=N，RF exclusive End不当可执行PC。父独立解析实际PE .pdata/.xdata，核Start source/stub不同save偏移、Detach SAVE延后到sub后记账、全部push/alloc/save CodeOffset及无伪frame register，再用raw CONTEXT1232bytes和真实受控stack2048bytes复算八个非易失寄存器、RSP和returnPC。尾JMP的输入R11必须确等于preceding MOV加载的该kind continuation。

这些是每进程实际50次系统API调用、受控上下文展开，57进程共{controlled_contexts}条重复观察，不等于2850个独立PC或2850次真实异常。每PC上下文按固定指令进度构造，部分Start非易失在当时仍保持caller原值，故还必须核真实xdata，不能只看输出寄存器恰好相等。真实SEH/C++执行是另外的8前/8后异常样本，不声称任意指令PC、完整RFLAGS/RAX或所有Windows版本的异常透明性。

## 真实调用、flags和发布前异常基线

第二次正常进程50个展开均过，但原测试错误假定SEH ExceptionFlags=0，4个实际SEH均带本机实际0x80而失败；code/payload/kind未变。root保留该exit1原件，没有直接mask掉该bit。第三次在Prepare及受控unwind全部通过之后、任何Bind/Commit之前，对4kind SEH和4kind C++做同进程实际基线，后续逐kind/异常类别精确比较code/flags/parameters/payload/kind。最终父验证不硬编码允许0x80、不忽略任何bit；此次观察到的基线如下，不推广为Windows普遍flags。

| SEH kind | 实际baseline flags | code | parameters |
| --- | --- | --- | --- |
{baseline_rows}

最终正常进程28条outer raw：8个baseline，随后未Attach四kind、Attach后四kind、实际worker四kind及8个原异常。父逐条复核共34次original（含Start同步Execute/Release）、20份观察Enter/Exit、16次catch（8基线+8发布后）；完整Execute EAX=`a5c3fe81`、Start AL81/输出node、Release ALe7/准确一次DEC及0和非0的CMP ZF、Detach清EBP诊断均有实际raw。C++字段来自原typed catch，不冒充捕获到原始MSVC SEH record。helper observer内部由host检查actual frame/token/LIFO，独立父从每outer原件验证callback增量；本批没有另外导出每份完整frame，因此不将该部分写成独立frame逐byte复验。

## Prepare、Bind、真实bytes与页面保护责任

Header ABI1：Config128、Binding120、Info21640，Receipt272×4位于72，Event80×256位于1160。publisher epoch1与helper epoch/slot28分开；Prepare全4校验完成后才发布immutable continuation，Bind核真实已配置helper ABI2的target/original/entry/epoch及零观察活动，四行完成才允许逐kind Commit。永久claimed/prepared/bound没有reset/unbind/retry；此组件机制不检查所有早期执行历史，故不能取得loader首发许可。

每Commit分别记Protect、实际volatile byte Write、Readback、Restore、Flush API前后及实际GetLastError。sourceWriteAttempts计发布尝试、sourceBytesWritten在真实byte store后逐一计，partial-write模式真实写3byte；不把全量completion等同已committed flag。首source attempt后任何失败Unknown；源写0但保护已改时先实际恢复，成功才可DisabledNoSourceWrite，永久准备/绑定责任仍保留。Restore失败则保护责任独立保持，不因为source或receipt字段缺值而消失。

父读取240byte实际memory snapshot：header含本机GetSystemInfo.pageSize，4行各包含actual VirtualQuery.Protect、target、AllocationBase和真实32bytes。每行必须等于精确已写patch前缀+未写原始余部，不仅相信receipt.observedBytes（故障早期可能全0）。保护按所有outstanding receipt影响的实际页计算；四target可能同页，某行恢复失败会使同页其他行也RWX，即使其他行本地outstanding=0。实际页快照和责任记录分开，不能靠改变ASM页布局掩盖该问题。

| 故障stage/kind | 最终state | failure | source attempts/full completions | 保护outstanding | 当前row实际已写byte |
| --- | --- | --- | --- | --- | --- |
{fault_rows}

stage1..10为真实API/写操作前后受控注入；stage11实际partial3byte；stage12/13/14是强制Protect/Restore/Flush失败selector，明确Injected且不设ApiCalled，实际OS API调用计数保持0。因此它们验证内部故障处理，不冒充OS真的返回ERROR_ACCESS_DENIED/ERROR_GEN_FAILURE，也没有穷尽内核、磁盘、query、SEH、并发写、OOM或全部部分写组合。GetLastError数值和actual call flag要一起读，不能仅靠error数字断言实测OS失败。

## 构建、历史和归档

最终单一组件wrapper session `{run['sessionId']}`由root实际观察exit0，完成 `{wrapper['completedAtUtc']}`；build.log SHA256 `{wrapper['buildLogSha256']}`，test.log `{wrapper['testLogSha256']}`。57个child各实际exit0，原件与父离线逐案复验一致：native host检查共{total_host}、receipt/memory独立断言共{total_receipt}、unwind独立断言共{total_unwind}，正常calls独立断言{calls['checks']}；这些重复合同断言不是同一种测试单位，不合并成“新增若干功能”。

{link('history/observed-runs.json')}保留最初3个链接/编译exit1：native SEH import与编译器生成memset、剩余patch fill memset、/WX下目标alignment padding。其后6个build日志、3次正常进程原件均保留：46/3尾JMP红、50/0但4个flags错误预期红、8baseline后的成功；早期57矩阵session7435实际exit0，但不含memory snapshot，仅为历史预跑，不替代最终memory增强候选。

12输入源码ZIP `{source_check['archiveSha256']}`，4binary ZIP `{binary_check['archiveSha256']}`。本文生成器重开ZIP逐entry核冻结manifest/当前输入，且对最终57份receipt+真实memory、50PC raw及正常28条calls重新运行离线断言，不build、不启动child。4binary包括复用helper；component输入集不是完整仓库发布源包，不宣称换机干净构建、完整B19发布矩阵或本批重跑旧576源/22包。

| 声明组件输入 | SHA256 |
| --- | --- |
{source_rows}

## Evidence

{evidence}
## Findings

### F-001：直接尾FF25的展开声明不足，实际红后改为固定R11尾序列

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-002, E-004, E-007, E-008]`。
- impact: `在本机固定自有四prefix/50控制上下文内，实际展开恢复caller正确；不宣称任意PC异常或通用R11/复制函数支持。`
- repro_steps: `先核历史46行3红，再读最终PE RF/xdata与50 rawcontext/stack，用离线脚本独立复算全部状态。`
- remediation: `保留固定代码/metadata强合同，后续实际非自有prefix必须重新验证，不把prologSize声明当系统行为。`
- optional_attack: `n/a`。

### F-002：字节写入和共享页保护责任可独立核对，强制API失败未冒充OS失败

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-003, E-006, E-008]`。
- impact: `56个已测内部fault保精确partial/full字节和同页保护责任；恢复未完/source尝试后保Unknown与不可重试槽。未覆盖全部真实API内部失败/并发/任意PC异常矩阵。`
- repro_steps: `按fault stage和kind重算actual API flags/count，再以240byte memory原件核patch前缀/原suffix与shared page RWX。`
- remediation: `生产前继续扩真实query/SEH/失败组合及早期执行准入，不回滚假装无写。`
- optional_attack: `n/a`。

### F-003：真实异常flags应与原始基线精确比较，不能硬编码0或随意mask

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-004, E-005, E-007]`。
- impact: `本机4kind受控SEH/C++前后type/flags/payload保持；8个基线和后续样本不等于每PC全部异常flags透明。`
- repro_steps: `核第二红的4个flags假设错误，再核最终28calls逐kind基线tuple完全相等、worker和完整EAX/Release副作用。`
- remediation: `保留原始基线与后样本，不mask未经分析差异；生产场景另定异常PC与来源合同。`
- optional_attack: `n/a`。

### F-004：该组件尚未取得early-loader或游戏首次发布权限

- severity: `n/a_re`；category: `other`；status: `candidate`；confidence: `high`。
- evidence_ids: `[E-001, E-002, E-006, E-009, E-010]`。
- impact: `无Dobby/CRT/heap imports及57个自有模式通过不能推出完整静态/间接/OS调用闭包、TLS/CRT之前的首调用历史或游戏V4；旧25/1617也没有本批重跑。`
- remediation: `先在自有early-loader替换publisher并重新验证依赖/实际先后与故障/worker；然后才研究固定游戏原始入口、WinHTTP与production coordinator，保持取消/退休限制。`
- optional_attack: `n/a`。

## Path与下一步

P-001，path_type=`callflow`：从四个固定自有非NOP目标与静态metadata出发（E-002→F-001），先Prepare固定original并真实RtlVirtualUnwind/异常基线，Bind actualhelper后逐步source写入/回读/恢复/Flush（E-003/E-004/E-005→F-001/F-002/F-003），最后57个独立child和12/4归档关闭当前组件合同（E-001/E-006/E-008/E-009）。终点是自有单进程component，未升级production权力（F-004）。

下一步把该固定组件接入自有early-loader fixture，仍先核无动态CRT/堆/logger依赖和真实加载/worker历史；首次发布必须在Importer TLS/CRT/首调用之前，逐写错误继续保Unknown并有限退出。再独立完成真实WinHTTP命名/转发、固定依赖路径与身份、真实游戏四入口非NOP/RIP/栈/unwind/异常准入和coordinator采用。scheduler登记/消费/drain、Start内部最终清理、async/futureStart封口与root/owner/provider真实来源尚未闭合，不能发EngineCancelled或解除Pending/根退休，完整B01–B19继续。

仓库根离线生成报告（只核现有原件）：

```powershell
{report_command}
```

组件构建/执行复现（root唯一builder，先保全当前原件，fresh输出目录）：

```powershell
{component_command}
```

参阅 {link('README.md')}、{link('next-integration.md')}和[完整验收清单](../.docs/foundation-expansion/CHECKLIST.md)。
"""
    assert "TODO" not in body and "placeholder" not in body
    destination.write_text(body,encoding="utf-8")
    print(json.dumps(dict(report=str(destination),sha256=sha(destination),session=run["sessionId"],componentInputs=12,binaries=4,
        ownedProcesses=57,independentlyReverified=True,controlledContexts=controlled_contexts,
        actualOriginalCalls=34,observerPairs=20,exceptionsCaught=16),ensure_ascii=False))


if __name__ == "__main__":
    main()
