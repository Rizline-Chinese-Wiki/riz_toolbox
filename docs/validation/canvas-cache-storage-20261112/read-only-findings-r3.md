# Canvas 缓存回调撤销：r2 遗漏与 r3 修复只读记录

记录时间：2026-11-12T04:20:30.360542+00:00。范围沿用 [scope.md](scope.md)，历史见 [timeline.md](timeline.md)。本次为普通源码正确性复核，flavor=null。

r2 的“无剩余必要阻断”源码结论漏掉了 Single[] class helper 内的回调撤销边界。主控既有 tail 执行在第10组触发 `OnlyCleanup` 失败，随后停止剩余执行并保留非零退出；该次不通过。只读源码核实缺口可达，主控随后仅修复两个 Runtime 文件。当前修复在第一个 native primitive 返回后检查 guard，可在继续下一个检查前终止；仅标记已在源码修复，执行验收另列。

原始 `independent-source-review.json`（SHA256 `44163b9e17a02b7111d3a59a84506573081deda9f76f9284d584f4bb6d20bfb5`）和 `independent-source-review-r2.json`（SHA256 `f9a4ede3446432b652e4eff3b8497f05246b902828a7386919f630962091459b`）均保留。r2 仍能证明其当时73文件哈希，但其无阻断结论不完整，不能认证修复后新图。当前源审见 [independent-source-review-r3.json](independent-source-review-r3.json)。本 reviewer 未构建、执行测试、启动游戏或修改 src/tests。

## Evidence

| ID | 来源与固定内容 | SHA256 / 复核位置 |
|---|---|---|
| E-001 | 主控 r2 `5-judgements.log`：第9组 PartialScalars PASS；第10组 PinAndPoststoreWithdrawal FAIL；断言为 `Array poststore withdrawal continued exports instead of known temporary cleanup only.`，Faults.cs:124。 | `219677fc86bafe36c73b59088191a7b0310923d960ebc8c5c041ad1fe0902a56`；`D:/riz-build-cache-next/canvas-cache-20261112/root-r2/iteration-r2/5-judgements.log` |
| E-002 | 主控 r2 `result.json`：passed=false；Judgements exitCode=4294967295；843 inputsUnchanged=true；没有完整汇总。stop 记录确认 PID/cmd 后停止剩余项，remainingCasesNotAccepted=true。 | result `c8c6078efaaaa58241732048109021325225900e97533960a7e2d8d2029b9e74`；stop `a3fd7dc99b1309ea8dea2b2c39599d5624f88e8175b996742c5cba17d917a3ed` |
| E-003 | r2 源码：write 的 raw store 后进入 Storage→Metadata→VerifyGeometrySingleArrayClass，helper 首两句连续 `_classType(arrayClass)`、`_classType(single)`，整段条件内无 guard。r2 原哈希仍在独立 JSON。 | write `d26fa1175164e3bbd0702a00fa224a0613e5435379b287d7869ebe9a46b5d3c3`；helper `2c438e1872e56cb7a1c7fa073811bc356efbf43447add26761f320d19c693c4c` |
| E-004 | fixture 将每个导出包装为 TryFinally(export, CanvasCacheObserveStorage)。首个导出结束时观察真实写入并执行 AfterCanvasCacheObservedStore；回调撤销并清日志后，OnlyCleanup 只允许 il2cpp_gchandle_free。 | FakeNative.CanvasCaches.cs:31–42、106–125；Faults.cs:107–126；Checks.cs:172–173。测试文件相对r2完全未改。 |
| E-005 | r3 最小修复：helper optional Action? guard=null，Observe 在每个 primitive / metadata probe 前后执行 guard；write传入guard；reader前后两处均转传已有guard。 | write `c65834ce1171920bc77f0819ae1f1528bd7f953f617c723878835be53181e386`；helper `24ec1e7d6efc1b5655f69df3515f855108dfd47c93bd2a31810036c55c24f2cb`；当前73 hash均匹配 `D:/riz-build-cache-next/canvas-cache-20261112/root-r3/final-r1/inputs-before.json`。 |

`E-001/E-002` 是读取主控已生成的文件，不是本 reviewer 新执行的证据。`E-003` 的旧源码由已读取内容与精确逆向当前两处增量重建得到，重建 SHA256 与 r2 清单完全相同。可使用 `Get-Content -LiteralPath '<已列本地路径>'` 复查既有文件；本轮不重跑测试。

## F-001：array class helper 在撤销后继续导出

- severity: medium（本批必要正确性阻断；无真实游戏可利用性声明）
- category: other
- status: validated-in-r2-source-and-parent-failure; fixed-in-r3-reviewed-source; execution-unverified
- evidence_ids: [E-001, E-002, E-003, E-004, E-005]
- location: NativeRuntime.HoldGeometryReadContainers.cs:66；NativeRuntime.CanvasCacheArrayWrite.cs:29、68–69。
- confidence: high
- impact: native store 已登记进入后，撤销可发生在 poststore 第一个 `_classType(arrayClass)` 返回时。旧 helper 直接进入第二个 `_classType(single)`，随后继续 `_byRef/_typeCode/_typeClass` 等检查，外层 guard 太晚。首槽 pending/owner 不会因此自动丢失，但不满足撤销后仅允许已知临时 cleanup 的合同。
- remediation: 仅给现有 helper 增加 optional guard，并在每个 primitive / metadata probe 前后检查；在 typed writer 及 reader 两处传入现有生命周期 guard。旧直接调用可省略参数。保留所有原类型、rank、元素大小和名字验证，保留 MetadataTypeName 的已知字符串 finally free 与 NativePublicationCall 的 pin finally cleanup，不放宽 OnlyCleanup。

## P-001：失败与修复后的调用边界

path_type=callflow；start=首个已登记的四字节缓存 store；goal=撤销后的下一次 native inspection 被拒绝。

1. E-003/E-004：managed admission 已记录 pending owner，Marshal.WriteInt32 后进入 Storage→Metadata→helper。
2. E-004：首个 `_classType(arrayClass)` 的 export finally 观察变化，触发回调撤销并开始审计。
3. E-001/E-003：旧 helper 未检查 guard 即继续第二个 `_classType(single)`；F-001 对应 OnlyCleanup 失败。
4. E-005：r3 的 Observe 在第一个 primitive 返回后执行 guard；失效即抛出，不能进入第二次检查，后续沿原 catch/finally 保持已知 pin cleanup 与 pending owner。

## 本次修复复核边界

两个 Runtime 文件的变化可在内存中完整逆向回 r2 原 SHA256，确认没有额外修改。r2 其余71文件保持原哈希；原16组、tail selector、所有断言、owner journal及 cleanup 机制未改。`InspectCanvasCacheField` 的原 helper 直接调用继续使用默认 null；未将本次修复扩展成新的通用元数据架构。

主控新全图位于 `D:/riz-build-cache-next/canvas-cache-20261112/root-r3/final-r1`。本记录只核输入关联，不宣称该图执行完成、300/51/6/1001 通过、游戏实际观察或完整 Seek 准入。r2 失败和人工停止均不能充当自然零退出或完整尾组通过。

核对项：原两份JSON未改；当前73文件与r3输入一致；最小差异可精确重建；无源码/测试编辑；无构建或测试执行；r2遗漏和失败事实已单独保留。
