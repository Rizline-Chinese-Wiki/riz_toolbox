# Lower-time 三成员物理机械验证 R4

本 S2 物理机械范围通过：fresh Runtime/Sdk/net8 harness 构建 W0/E0，30个独立进程全部自然exit0，共37,256checks。19个完整安装case各自实际Prepare3/Commit3，三个成员均Committed并通过Current。同一 Module retained owner包含固定 lower-pitch、engine-pitch、lower-time完整body；部分准备失败实测1/0，保持failed owner并拒绝重试及第二owner。

真实 original 共调用190次，每个完整安装case在Prepare后和Installed后各5次，共380个store前后边界。raw32覆盖正零、负零、有限值和NaN payload，验证实际call/store计数、正确target+6 continuation、无MethodInfo和真实字段readback。只自然加载和执行固定自有fixture，未运行真实游戏DLL/DllMain/方法、UI、账号、网络或生产操作。

授权与边界见 [scope-r4.md](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/scope-r4.md) 和 [合同R4](D:/riz-build-cache-next/playback-lower-time-r1-20261104/contract-r4/contract.md)。此前R2的5B relocation假设、R3单RF及E9安装假设的真实失败与诊断全部保留；本报告只凭新scope的新源码、新DLL/PDB、新进程验收。报告 flavor=null。

## 实际机械与篡改结果

| 项目 | 实际通过内容 |
| --- | --- |
| 固定私有issuer | 两完整文件SHA、固定导出/RVA、495/640/305 B完整body、原RF/UW/engine chain、wrapper80 B pin、注册函数/name数组、正确BSS零填充cache与双读 |
| 第三original | 六字节MOV+PUSH RBP；实际20 B RX Dobby original回target+6；私有19 B RX R11 thunk；完整60 B只读metadata及两个RF/两个UW |
| 实际OS展开 | PC0/5返回RF1，PC6/16/18返回RF2；新产品与独立harness完整断言RIP/RSP/RBX/RBP、flags、其余6个nonvolatile GPR、160 B XMM，安装前后复核 |
| 安装入口 | 全部实际6 B FF25 nonzero signed RIP disp32；8 B retained pointer slot精确等于各自真实reserved RX entry；第三tail从byte6严格原样 |
| 完整映像 | 同一owner投影三补丁，完整file+两个真实非零ASLR DIR64 rebase及其余code/metadata验证 |
| tamper | 三成员patch/tail/actual Dobby relocation/private original、原UW、注册/name、wrapper/cache、完整image/DIR64、两个RF分别、两个UW分别、metadata两处padding均实际拒绝并恢复后Current通过 |
| slot tamper | 三个真实slot的值、installed displacement/address、RW→RO保护分别篡改，验证拒绝，恢复后Current通过 |
| 失败责任 | 第二entry非执行导致实际Prepare1/Commit0；failed owner保留，重试/替代owner拒绝，无平行owner |
| near/far | 两case实际通过；far仍是pointer slot间接转发，没有发生E9 absolute relay，不签该覆盖 |

旧独立UW诊断 `uw-observation-r2` 的 passed 只断言RIP/RSP/RBX/RBP；flags只是记录，其他GPR/XMM未诊断。覆盖更正见 [clarification](D:/riz-build-cache-next/playback-lower-time-r1-20261104/contract-r3/diagnostic-coverage-clarification.md)。本次完整展开结论来自新family及新harness的实际断言。

## 精确源码和PDB归属

相对R3唯一产品变更为 `NativePlaybackEnginePitchPatchPair.cs` 的两个分支条件：第三不再接纳未实证E9，并采用已实证FF25 nonzero slot6；原两个pitch成员严格分支保持。测试Program更新真实slot及负例，source仍215文件。相对原R1最小源完整产品差异仅7文件：Target.Profile、Target、EnginePreflight、LowerTimePreflight、PatchPair、LowerTimeTrampolineProof、LowerTimeTrampolineUnwind。

精确差异见 [final-managed-delta-r4.json](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/final-managed-delta-r4.json) 与 [r3-to-r4-delta.json](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/r3-to-r4-delta.json)。215项 actualCompilePath/frozenPath/SHA/owner见 [final-source-ownership215.json](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/final-source-ownership215.json)：未变输入引用原R3 frozen，Pair和Program独立冻结在本scope，无旧二进制代签。三新DLL的CodeView GUID/stamp与各自portable PDB ID相等；PDB每个文档SHA256匹配实际编译文件和冻结归属，详见 [pdb-source-ownership-r4.json](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/pdb-source-ownership-r4.json)。实际document数量：Sdk31、Runtime183、harness4，包含生成源各3项。

## Evidence → Finding → Path

| Evidence | source_ref / artifact | 核心观察 | content_hash |
| --- | --- | --- | --- |
| E-install | attempt-r1/install-near/stdout.jsonl、test-result.json、exit.txt | Prepare/Commit3/3、三Committed/Current、自然0 | artifact-index-r4.json逐项SHA256 |
| E-uw | 同上 independent-os-unwind、prepared/installed snapshots | 五PC完整断言、真实RF1/RF2、60 B完整metadata | 同上 |
| E-original | 同上 actual-private-original-calls | prepared/installed各5次，真实10+10store边界 | 同上 |
| E-slot | tamper-slot-value/address/protection三case及far-entry | slot精确entry，value/address/protection实际拒绝；far仍slot | 同上 |
| E-fullfile | file-pin、file-pin-wrapper、tamper-image、tamper-dir64 | 双完整文件hash与完整image/真实DIR64负例 | 同上 |
| E-tamper | tamper-dynamic-rf、tamper-dynamic-uw、padding及三member相关case | RF1/RF2/UW1/UW2及其他机械证据分别篡改拒绝 | 同上 |
| E-retained | registry-order、partial-prepare | sole retained owner/ordered Commit、失败1/0保留 | 同上 |
| E-pdb | pdb-source-ownership-r4.json | 新DLL/PDB绑定与全部document SHA归属 | 同上 |
| E-guard | guard-r4.stdout.json | 新Runtime旧role正control通过，role22/kind8与child2048拒绝 | 同上 |

F-mechanics，severity=n/a_re，category=other，status=validated，confidence=high，evidence_ids=[E-install,E-uw,E-original,E-slot,E-fullfile,E-tamper,E-retained]：本固定owned两文件和pinned Dobby的三成员物理机械安装及当前证据校验通过。该结论覆盖本文列举的实际形态，不能扩展到其他Dobby/profile、未出现的E9 relay或真实游戏。

P-three-member，path_type=callflow：固定双文件receipt及三body preflight → 同Module retained Create3 → 实际Prepare3与双RF完全展开（E-uw）→ 真实prepared original（E-original）→ lower-pitch/engine-pitch/lower-time顺序Commit3 → 三member Current/fullimage+DIR64（E-install/E-fullfile）→ 逐项篡改拒绝与恢复 → installed original再次真实执行。

每个case的execution.json保留准确命令、输入、pid与UTC时间；stdout/stderr、exit、结果和各源/build/PDB都由manifest逐项SHA固定。配方 [run-mechanics-r4.ps1](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/run-mechanics-r4.ps1) 拒绝覆写旧case或build；重现需要另开fresh输出scope并满足预算。UTC 05:25完成新build与全部30case，随后完成新Runtime guard与证据冻结。

## 保留边界

本S2不构造或伪造SourceOwner/group/MemberReceipt/adopt票据。实际三个SourceOwner逐成员一次采用、managed23组、Adapter、provider/SDK、typed interruption正向/opaque retry、Progress/Unity backing动态替换、Seek、真实游戏和生产接纳仍未完成。新Runtime guard继续拒role22/2048；没有把物理三成员通过提升成23-hook或SDK接纳。

所有新产物位于D盘，scope cap16,000,000 B。终态同时保留freeD792,915,741 B、本scope剩余cap和最新后继8MiB元数据预算；未删除失败件、杀旧进程或复制包缓存。完整终态与最终预算见 [terminal-summary-r4.json](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-family-r4/terminal-summary-r4.json)。

## 同期私有 ABI 与后续工作

私有audio ABI独立fresh build W0/E0、5进程／98检查自然0，见[closure](D:/riz-build-cache-next/playback-lower-time-r1-20261104/managed-audio-abi-r1/closure-r1.json)。它只验证144B布局／helper导出和拒绝门，不证明SourceOwner采用、interruption正向或opaque retry。下一按S3分批接实际23组，保持任意Seek与真实游戏关闭。

根只读复核：[记录](D:/riz-build-cache-next/playback-lower-time-r1-20261104/root-review-r4-abi-r1/result.json)；机械终态SHA `3219e30dc04a1bb2fd4474e48116bbe1e1c5a9827880ee11fe1dd8e7857599a9`。新自动后继只在六case/E012实际全0后才合主与fresh验收；本报告生成时已真实启动supervisor／waiter并持原E01228384/42260精确句柄，waiter PID37492，state=waiting-original-e012-handles；还未运行App或fresh。
