# 材质场景切换清理：独立只读复核

2026-10-21，`battery_code_review`。结论：`attempts=1` 与 Unity 延迟销毁的设计路径一致，单凭该警告不能判为释放故障。后续没有第二次失败，较强支持重试成功；但生产成功路径没有 receipt，当前保存日志仍不足以确认该 clone 的 native 死亡、root 释放和预算回收。应补可关联的成功证据，不应将 probe 的 `CLONE RELEASED` 当作物理释放证明。本次未修改代码、未构建、未控制游戏。

## Evidence

先确认了保存目录中实际存在 `toolbox.log`、`current-session.log`、`events-000001.jsonl`、`status.json`、`session.json`、manifest 等文件。本报告优先使用已截取的本会话 `current-session.log`，排除 toolbox.log 中 10 月 20 日的旧警告。

| ID | 实际观察 | 文件与行 |
| --- | --- | --- |
| E-01 | 04:43:52.074 创建 clone `df28c14472f64707b7b2d0d30ac9afc1` | evidence-snapshot-20261021T044942Z/toolbox.log:988 |
| E-02 | 04:44:34.803 `RESTORE REQUESTED`，04:44:34.816 owned Material 首次 cleanup pending，nextDelay=.25s | evidence-snapshot-20261021T044942Z/current-session.log:574–575 |
| E-03 | 04:44:34.829 `CLONE RELEASED` 仅报告 managedLeaseValidAfterDispose=false；随后资源统计明确写 probe lease counts、SDK 无 native statistics | 同文件:576–577 |
| E-04 | 保存日志后续没有该 clone 的 attempts=2，也没有它的成功 receipt；日志继续记录新场景、run 和输入等事件 | 同文件:590 起；按完整 clone ID 搜索 |

E-02/E-04 文件 SHA-256：`5828FC376361E3A54FB22FBB72D6D04BD7F16516C4558EFDE47DFC0694182D97`。在仓库根目录复核：

```powershell
rg -n 'df28c14472f64707b7b2d0d30ac9afc1|Visual retirement attempt failed|Owned visual|Visual root release failed|Visual budget release failed' docs/validation/runtime-color-content-20261021/evidence-snapshot-20261021T044942Z/current-session.log
```

## Findings 与路径

F-01（静态确认；E-02/E-03）：服务只在根的引用数归零后开始释放，`VisualResourceService.cs:320–342`。`TryReleaseEntry` 先发一次 `DestroyOwned`，设置 DestroyIssued，随后检查 IsAlive。Unity 对象本帧仍活着时返回 false，`VisualResourceRetirement.cs:70–76` 记录首次 pending，并安排 .25 秒后的重试。`NativeVisualBackend.cs:213` 调用的是 Unity `Object.Destroy`；没有在这里强制立即销毁。该流程预期会出现一次 pending。

F-02（静态确认；E-04）：`RizlineAdapter.cs:186–193` 每次 Tick 都调用进程级 `VisualResourceRetirement.TickPending`，不依赖材质模块保持可用。重试工作最多每 host tick 4 项，成功前指数退避；第二次失败一定会记录 attempts=2（`VisualResourceRetirement.cs:72–76`）。保留到 04:49 的日志仅有这次首次警告，因此“该项在下一次/早期重试成功”是合理推断，但不是带对象身份的成功观察。

F-03（证据缺口；E-03/E-04）：成功时 `VisualResourceRetirement.cs:68` 只 Dispose ticket 后返回；`VisualResourceService.cs:344–351` 移除内部 entry、尝试释放 root/budget、释放依赖，但没有成功日志或暴露计数。probe 的 lease 清空与 adapter 的物理清理属于不同层，现有日志不能闭合两者。root/budget Dispose 异常目前只记 warning，receipt 必须区分各阶段，不能只追加一个笼统“全部成功”。

P-01（callflow）：场景事件（E-02）→ probe 撤回 override、Dispose asset（E-03）→ 引用清零 → Destroy 请求 → 本帧 native 仍活着 → retirement pending（E-02，F-01）→ 下一 TickPending 重试（F-02）→ native IsAlive=false 才进入 FinishReleaseEntry → 尝试释放 root/budget/dependency → ticket 完成。目前最后两步没有针对该 clone 的日志观察（F-03）。

## 最小后续修复

1. 为每个 owned 资源输出一次可关联的完成 receipt：asset ID、native identity/kind、DestroyIssued、nativeDeadObserved=true、rootDisposeSucceeded、budgetDisposeSucceeded、依赖释放结果；在真实对应阶段之后发出。保留失败状态与错误信息，不因 lease 已失效宣称 native 已释放。
2. retirement ticket 完成时记录 description、总 attempt 次数、失败次数及完成时间，并可报告进程 pending/reserved 数。只在完成发一次，避免每帧日志。
3. 给已有 “Deferred destruction holds dependencies and budget until native death without repeated Destroy” 用例（Visual.Tests/Program.cs:367 起）补证据断言：首次 pending 不产生完成 receipt；native 死亡后产生恰好一个 receipt，预算归零，Destroy 不重复。再用实机 clone 的相同 ID 验证场景切换闭环。

最小修复属于可观测性闭环；本次未发现足以要求修改重试/Destroy 算法的已证实故障。CPU 的 delayed-destruction fake 用例不能替代真实 Unity 物理释放证明。

## 本轮追加诊断实现（尚未验证）

上文源码行号描述 766 历史包审查时的代码。经主代理后续授权，已只在 `VisualResourceRetirement.cs` 修改生产诊断路径：cleanup 谓词返回 true 后，先 Dispose ticket，再输出一次 Info receipt；包含 description、kind、独立 ticket 总 attempt 数、ownerReserved、ownerPending、globalReserved。Failures 在 31 封顶，不能用 Failures+1 充当总尝试数，因此单独计数。Warning pending 策略和重试/释放策略未改。

receipt 的 `cleanupPredicateCompleted=true` 和 `retirementReservationReleased=true` 仅证明清理谓词完成与退休 ticket 的预约释放；**不宣称资产 Budget/GC root Dispose 成功或 GPU 内存回收**，因为服务内部 root/budget 异常仍可能被捕获。原日志中的 766 包未包含新增 receipt，不能回补或改写历史验收。

在 Visual.Tests 新增一项行为回归：诊断 sink 在 pending 与 completion 都抛异常时，原 cleanup 仍成功重试、先归还容量再调用成功日志、后续 Tick 不重复清理。此代理未构建/运行；主代理需要串行执行：

```powershell
dotnet run --project tests/RizToolbox.Visual.Tests/RizToolbox.Visual.Tests.csproj -c Release
```

随后由主代理完成整包验证和新包实机 receipt；这里不记录未执行的通过结果。
