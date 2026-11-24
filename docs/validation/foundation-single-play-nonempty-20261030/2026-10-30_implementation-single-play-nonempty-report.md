# actual outer Update/Execute、Single Play 与非空 native queue/source 联合验证

最终 `automatic-duplicates` 在同一自有 DLL 内实际执行 `Update24 → Execute19 → Single31 → Play25 → Add20×2`。两次 Add 各自做 pointer store、barrier、count/version 变化，产生同 Tween 的两个独立 admission occurrence；后续两次 bounded native Update 各退出一个 occurrence，第一次仍 Pending，第二次才可借原 ordinary completion interval。native 严格构建、managed build 与最终独立进程均 **actual exit 0**，严格 JSON 合同通过；managed 保留 26 个原 fixture warning。live 生产/测试源码 **0 改动**，没有发现须修正式 reader 的故障。

范围见 [scope.md](scope.md)，失败与迭代见 [timeline.md](timeline.md)，所有原件见 [evidence-index.json](evidence-index.json)。`flavor = null`。本包仍是自有对象/metadata 和 controlled admission/death 下的离线联合证明；不签发生产 Ready，不安装或执行游戏，不释放根或 Unregister，不代表完整 LOCAL 或 B01–B19 已完成。

## 实际链与存储

outer parent 由真实 `Void0/24` native entry 自动产生，owned Update original 执行 source14 Before，然后进入 `Ref1/19` 的真实 Execute entry。D-only ASM 复用 `TweenSourceFixtureCaller.asm` 的全部 43 个 CALL return labels 与原 store+barrier；只在 S14Before 的真实 return label 后紧接一个五字节 direct CALL，经 owned jump thunk 转入 actual Execute。运行中读取该标签的字节确认为 `E8`，三个 Execute 的 actual Caller 均等于该标签地址加 5；Single 的源 occurrence 是初始 native playing slot 自动重建的 occurrence1。

实际 Single ASM/TLS `ReadCurrent`、Bridge.Delivery、physical parent loan、正式 `NativeTweenSingleDeliveryReader` 与正式 `NativeTweenTypedPlayConsumerReader` 均执行。准确闭 UnityAction 从 owned SEC_IMAGE 的 Play metadata entry 转入 `Void0/25`；原 Play original 内自动调用两次 `Ref1/20`，没有托管 OnPlay、人工 Begin/End、SetReady、source row、occurrence、transfer 或已完成条目注入。

Tween 分配为 0x100 字节，含 cached pointer、onStart@0x88、onComplete@0x90、onTween@0x98 与 isOver@0xd8。manager、playing array、scheduled list、ready stack 和 backing arrays 均是完整 owned native 存储。Capture/Refresh 逐次从当时的 native queue/field 存储读图，类型/代次与 effect 字段的 fixture 输入仍受控；不是生产官方 graph metadata 验证。

| 项目 | r4 实际结果 |
|---|---|
| native lane entered/exited | Update24 3/3，Execute19 3/3，Single31 1/1，Play25 1/1，Add20 2/2 |
| native disposition | 所有 lane InFlight/Unknown/EnterFaults/ExitFaults=0；所有保留 operation Returned，exitFlags=1 |
| native originals | Update3、Execute3、Single1、listener1、Play1、Add2；Single Delivery3、consumer reads2 |
| Add 1，native sequence5 | ready count/version `0/1 → 1/2`，原有前缀不变，新增槽为真实 Tween 指针 |
| Add 2，native sequence6 | ready count/version `1/2 → 2/3`，两个有序槽为同一 Tween 指针，独立 admission1/2 |
| source active | Update Before/After=1，Execute=2，Play与Add=3；每次 native Update 返回后恢复0 |
| nonempty source | epoch1，43 bindings；20 retained rows、10 transactions/completed、pending/unknown/faults=0 |
| source 站点 | 仅 Update sites0/2/6/14，分别为两次 pop、两次 insert、三次 clear、三次 Execute；未执行其他站点 |
| Play source site rows | **0**；不把 Update 的非空行计成 PlayOnStart/PlayOnComplete 19/20 覆盖 |
| actual references | 83 ReadCurrentNotice/TLS deliveries、5 个独立 direct reference roots、81 次 rooted class validation；10 native scopes 全部正常结束 |
| reference final | active/retained/callback-in-flight/unknown/faults/rejected-reads=0 |
| native store+barrier | 9 次 store 后立即 CALL，9 次 barrier 实际观察到已写值，mismatch0 |

`NativeTweenSourceReferences` 在实际 current-notice callback 中先读 native TLS 证明，再记录独立 Root，随后验证 owned class；通过 `BindControlledReferencesForTests` 在初始 root set 创建前登记 reader。新引用由这些真实 receipt 送入 reader，journal DTO 只作运行后的证据输出，不能据其晚补 Root。20 条 actual ReadRecord 的完整显式字段、83 条 notice、root/validate 顺序、actual frame/self/MethodInfo/caller/flags、native lane 信息和全部 Before/After queue 图都在 E-NONEMPTY-001/006。

## 私有移交与逐项等待

实际 Play 的 native sequence4、owner PlayObservation3 签出唯一 listener child1 的私有 transfer：`SourceEpoch1/JournalCutoff1 → [2,3,4]`。occurrence2 是该 actual Play 自动创建的 InstantWork；occurrence3/4 分别来自 Add admission1/2。三个条目引用同一 Tween，不能合并；初始 playing occurrence1 在原普通账本内独立退出，不归该 child 新建 transfer 所有。

| 阶段 | ready count/version | admission1 / admission2 QueueExited | 原 ordinary completion interval |
|---|---|---|---|
| actual listener/Play 返回，controlled death 完成后 | 2/3 | false / false | 不能借用 |
| 第一个 bounded native Update 退出栈顶 occurrence4 | 1/4 | false / true | **仍 Pending** |
| 第二个 bounded native Update 退出 occurrence3 | 0/5 | true / true | **可借用并完整验证** |

两次 drain 都实际执行 source0 pop → source2 playing insert → source14 Execute → source6 clear。每个事务的 Before/After 行、pop transaction 到 insertion occurrence 的真实关联、slot/count/version 与原 occurrence exit 均由现有 reader/owner 检验，没有手工清空 graph 或补造退出。private transfer 没有替代这些义务，也没有给 root/native admission release 权限。最终 work 的 retirement Pending 仍保留，未开始退休。

## 冻结输入与复现

最终目录：`D:/riz-build-cache-next/single-play-automatic-duplicates-frozen-r4-20261030/`。执行观察时间 `2026-10-30T14:16:46.615785+00:00`。664 个最终源码文件前后 hash 相同；660 个旧 r4 基底文件不变；4 个 D-only overlay 源输入不变。完整 header/ASM/C#/project/runner 均在 source archive，不能只拿旧 overlay 当完整 native 输入。

逐 TU flags 写在 E-NONEMPTY-001：`OperationContext.cpp` 不带 `RIZ_TWEEN_ADMISSION_FIXTURE`，实际 admission Enter、source Enter、Before/original/PrepareExitReferences/After/source Exit/admission Exit 都保留；仅 `TweenAdmission.cpp` 使用该既有宏。`TweenSourceContext.cpp` 使用既有 `RIZ_TWEEN_SOURCE_FIXTURE` 只替代 metadata 校验，真实 ticket/pair/journal 留在同 DLL；`TweenSourceReferences.cpp` 和原 Single/source ASM 同时链接。沿用 reservations-only retirement 基底，没有退休或释放验证。

```powershell
C:/Python314/python.exe D:/riz-build-cache-next/single-play-automatic-duplicates-work-20261030/run-automatic-duplicates.py --output D:/riz-build-cache-next/single-play-automatic-duplicates-replay-20261030
```

输出目录必须原先不存在。工具使用本机已有 Python3.14、.NET8 与 VS2019 BuildTools，完整实际命令、stdout/stderr 和 actual exits 均存档。r1/r2 失败原件保留；r3 是同一模式的先前成功版本，r4 只为补齐 raw record/E8/Caller 证据，不增加进程覆盖数。旧 instant/七模式/退休/其他旧矩阵未重跑，当前全树由 root 独立统一 compile。

## Evidence → Finding → Path

下表路径均相对最终 D 目录；E-NONEMPTY-008 在 D work 目录。每项 observed_at 为上面的实际执行观察时间，008 为执行后只读 hash 复核。source_type 为 file/log；完整大小、绝对路径、hash、actual exit 与 linked_workitem=`WI-SINGLE-PLAY-NONEMPTY` 见 evidence-index。

| Evidence | 原件 | SHA-256 |
|---|---|---|
| E-NONEMPTY-001 | `test-result.json`，actual exits/strict contract/frames/graphs/source/ref/transfer | `21edb225fd52382808968e2f54c05720505a29d1d2c2698d173f5ed95b86904e` |
| E-NONEMPTY-002 | `source-snapshot.zip`，664 完整冻结文件 | `f4c4050e7c8838fd9a296170936a37fd7a842099c5115cf73d34f4f88a55151d` |
| E-NONEMPTY-003 | `native/RizToolbox.SinglePlayAutomaticDuplicatesFixture.dll` | `b8e20b221758c76b4484e5ace59314232de6924292bc19733282cd5b687dc310` |
| E-NONEMPTY-004 | `source-before.json` = `source-after.json` | `d8fa07a2600123681c78c3f797f64e46a3d94cbf402ab52c1d21b7d009fac82d` |
| E-NONEMPTY-005 | `overlay-inputs.json`，4 项 D fixture/runner | `dcbb8f6679eff5b90a1b1cd0a406c0c0754913bdc344c6ab759e2bd441625157` |
| E-NONEMPTY-006 | `automatic-duplicates.stdout.log`，实际 JSON 原始输出 | `5669184dd2e94d773464b43e140e7740a20b24aa0790534d1cf17907b5eef178` |
| E-NONEMPTY-007 | `artifacts/bin/RizToolbox.AudioContent.Tests/release/RizToolbox.AudioContent.Tests.dll` | `d1bf23d0b9f254a05a0230ff783ee83be3e85a3eb00d2fe17489c28348a7cf23` |
| E-NONEMPTY-008 | D work `live-relevant-readonly-audit.json`；本目录同字节 [复制件](readonly-live-source-audit.json) | `1fba362020c3c4754f0e3e2ee012fd9b16015fe0e7eba3327ac0b472981e8b92` |

F-NONEMPTY-001：status=validated，category=other，severity=n/a_re，confidence=high，evidence_ids=[E-NONEMPTY-001,E-NONEMPTY-002,E-NONEMPTY-003,E-NONEMPTY-006,E-NONEMPTY-007]。位置为 actual OperationContext 24/19/31/25/20、正式 Single/Play readers、`CompleteTypedListenerEffect`、`ObserveSourceAdmission` 和 source replay。影响：该自有 actual outer/source/reference/Add 联合链能为精确 Play subtree 移交一个 instant 与两个 distinct queue occurrence；两个相同指针 occurrence 的首次退出不会完成另一项。repro_steps 为上述新目录 runner；remediation=n/a，仅离线实现验证。

F-NONEMPTY-002：status=validated，category=other，severity=n/a_re，confidence=high，evidence_ids=[E-NONEMPTY-001,E-NONEMPTY-002,E-NONEMPTY-004,E-NONEMPTY-005,E-NONEMPTY-008]。正式 reader/native 文件无需改写，全部新增实现和对已有测试的三处接线变换发生于 D 快照；production/source/reference 的 Production 均保持 false、native admission 验证 false，不能取得生产 FirstInstall authority。位置为 frozen runner、controlled factories 与只读 hash 清单。impact 是明确区分实际 native 操作/存储与受控生产准入边界；repro_steps 为核对 source manifests/overlay/live audit；remediation=n/a。

P-NONEMPTY-001：path_type=callflow。start=owned actual Update24；goal=同 owner 的 private Play transfer 和原 ordinary occurrence completion interval。步骤为 actual source14 CALL/Execute19 → actual Single31 TLS Delivery → 两个正式 reader → owned SEC_IMAGE 闭 UnityAction/Play25 → actual Add20 两次 append → private transfer[2,3,4] → 两次 source0/2/14/6 bounded Update 各退出一个 occurrence → 首次仍 Pending、第二次 interval 验证。每步的 evidence_ids=[E-NONEMPTY-001,E-NONEMPTY-002,E-NONEMPTY-003,E-NONEMPTY-006]，finding=F-NONEMPTY-001。

剩余边界：source 注册/metadata、native graph metadata/generation 与 effect 字段、managed admission lease 和 death 都受控；native Claim/Seal/Unregister、真实 Unity death、官方 GameAssembly、生产 FirstInstall/Ready、真实游戏对象及可见/可闻效果未验证。未执行的 source 站点不计覆盖，Play19/20、Execute21 仍为0；不签整体 LOCAL 完成、retirement 或释放结论。
