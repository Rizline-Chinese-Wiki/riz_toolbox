# 实际 Single delivery 到私有 Play 完成账本的联合验证

本轮把正式 Single reader 与实际 native delivery 连在同一条可执行路径中。native Single producer、TLS `ReadCurrent`、`Bridge.Delivery`、broker physical scope、executing-list/dispatch reader 与生产 Play consumer reader 均实际执行。已证明精确闭 UnityAction 的 Play 副作用进入原 occurrence 账本，重复队列项继续逐项等待；未知 listener 和缺失 native post 不会取得 transfer。证据基底为受控对象及自有代码，真实首次准入和真实游戏对象仍未验证。范围见 [scope.md](scope.md)，过程见 [timeline.md](timeline.md)；本报告采用通用实现报告结构，`flavor = null`。

## 原缺口与实现

上一包 39 项分别覆盖生产 Play reader 的 unmanaged metadata/SEC_IMAGE fixture 与受控 WorkOwner transfer；中间仍由测试直接提供 frame、list、target 或 `NativeTweenTypedPlayConsumer`。新的 fixture 从真实 native OperationContext Entry 开始，native ASM 实际执行 Mid、site 1 Pre、闭委托原调用、Post 与 parent Exit。`ReadCurrent` 只允许本 callback 的实际 TLS 地址，Bridge 在其上创建 live Delivery；复制 DTO 仍不能代替 Delivery。

`NativeTweenSingleDeliveryReader.Observe` 是正式 `NativeLocalTweenBackend.SingleDispatch` 与联合 fixture 共用的读取代码。完整 batch 在 metadata 读取前先采用 root；executing-list 读取后再次检验 known 状态和当前 Delivery，再交给 owner。Returned/Abnormal 只消费已经保留的 Before root。生产 Play consumer reader 不接受 fixture 直接提供的 consumer 结果：它从实际 unmanaged InvokableCall、UnityAction 与 TweenInfo MethodInfo 读取并验证全部形状。

Bridge 的生产构造仍捕获真实 `NativeFirstInstall.VerifyReady`，`AdoptReady` 仍只采用该 owner。controlled factory 不填生产 singleton，也不创建 FirstInstall；正式 backend 明确调用 `RequireProduction` 拒绝 controlled bridge。identity 进程验证 absent helper、错误 epoch 在 Attach 前拒绝，controlled 创建后 `AdoptReady` 仍失败。controlled 构造与生产构造共享 gate，native callback 的永久保留方式不变。

原 occurrence 移交算法没有重做。本轮把已有 child 的具体 Pending 原因送入 dispatch snapshot；source Unknown 导致既有 transfer 失效后，row 会显示非空原因，缺失 post 也显示实际 abnormal/missing-post 原因。

## 新增实际执行

| 进程 | 实际 native originals/listeners/Play | 验证行为 |
|---|---:|---|
| identity | 0 / 0 / 0 | controlled 与生产 authority 隔离；无错签 Ready、无执行或 root 释放 |
| instant | 1 / 1 / 1 | 实际原闭委托调用 Play；一个 InstantWork 正常移交后，原账本可借完成区间 |
| duplicates | 1 / 1 / 1 | 一个 InstantWork 加两个相同指针队列 occurrence；第一次退出仍等待，第二次退出才通过 |
| withdraw | 2 / 2 / 2 | listener 内撤回后仍交付原 child Post 与 parent Exit/Dispose；后续原调用不再借这个 run，重复队列仍逐项等待 |
| unknown | 1 / 1 / 1 | 未分类 listener 即使真实原调用产生 Play，也不发 transfer，owner death 后仍 Pending |
| missing-post | 1 / 1 / 1 | native Close 发实际 Abnormal/MissingPost；不进行 Returned consumer 重读，不发 transfer |
| copied-read | 1 / 1 / 1 | 独立进程先完成正常路径，再验证复制 frame 的 native ReadCurrent 拒绝与粘性 Unknown；source fault 使旧 transfer 失效且保留 Pending |

除 identity 外，每进程实际收到 3 次同步 Delivery（Mid/Before/Returned 或 Abnormal）。正常精确 consumer 读两次，unknown 与 missing-post 只读 Before 一次。所有 stale Delivery 在 callback 外均拒绝。正常模式 native 父/子 entered/exited 配对、in-flight 为 0、Unknown 为 0；missing-post 与 copied-read 单独记录其预期 Unknown。私有 transfer 没有执行 root free 或 admission Unregister。

最终原件：`D:/riz-build-cache-next/single-play-joined-frozen-r3-20261030/`。native 严格构建 exit 0；managed 构建 exit 0，0 error、26 个原 fixture warning；7/7 新进程实际 exit 0 且各自严格 JSON 合同通过。631 个固定基底输入、649 个最终源码文件的前后 hash，以及 25 条 owned overlay 记录均复核不变。

## Evidence → Finding → Path

| Evidence | 原始来源 | SHA-256 / 定位 | 关联工作项 |
|---|---|---|---|
| E-001 | 最终 `test-result.json`，命令与每进程 exit/合同 | `899f44e481a9402f7cbfcc0fb35e135a44121e56efbda21e71f5fa29d9b5da00` | WI-SINGLE-JOINED |
| E-002 | 最终自有 native DLL | `b12e02d7f25bf78297cc00aaa0af69ddd9f4ab9d0a75e71cd9b91051c0d95956` | WI-SINGLE-JOINED |
| E-003 | 最终 source-snapshot.zip | `5ea28f083cbc16eadc107e39340befadb1ac8a9d9a40e860bc49c227a1dbfd35` | WI-SINGLE-JOINED |
| E-004 | `overlay-inputs.json` 的逐条 production/fixture/test hash | `c748b943465fb960714ea95fa90d7782c714f5dee786c87c088a1cff48f93822` | WI-SINGLE-JOINED |

E-001 的新进程 stdout 原始字段包括 `actualNativeTlsReadCurrentVerified=true`、`productionFirstInstallVerified=false`；duplicates/withdraw 的 `WaitedForFirstDuplicateExit=true` 与 `CompletedSecondDuplicateExit=true`。所有 Evidence 的复制件与完整文件信息见 [evidence-index.json](evidence-index.json)，可复现命令为下节统一命令；观察时间取 E-001 的 `observedAtUtc`，不把报告写入时间当成运行时间。

F-001（validated，confidence=high，category=other，severity=n/a_re，evidence_ids=[E-001,E-002,E-003,E-004]）：实际 native Single delivery 到正式 reader 和原 occurrence 账本的联合路径成立，范围限于本报告的自有 native producer/metadata 与 controlled Play/source/admission/death fixture。定位：`NativeTweenSingleDeliveryReader.Observe` 与 `NativeLocalTweenWorkOwner.CompleteTypedListenerEffect`。影响：移交成功不会合并同指针工作、替代 occurrence 退出或开放 root 释放。

F-002（validated，confidence=high，category=other，severity=n/a_re，evidence_ids=[E-001,E-003,E-004]）：controlled bridge 不能成为 production Ready；未知、abnormal/missing-post 和后续 source fault 均没有成功 effect 完成凭据。定位：`NativeTweenSingleEventBridge.RequireProduction` 与 `NativeLocalTweenWorkOwner.ReadTypedListenerEffects`。

P-001（path_type=callflow）：自有 native Single Operation Entry → actual broker Capture/loan → native Mid/Pre TLS delivery → `Bridge.Delivery`/physical scope → 正式 Single reader → production Play consumer reader → native 闭委托调用自有 Play entry → controlled actual Play/source capture → native Returned → 私有 transfer → 各 occurrence 原账本退出 → 完成区间。每步对应 E-001/E-002 的实际调用计数和 E-003/E-004 的固定实现；结论为 F-001/F-002。

## 复现与剩余边界

```powershell
C:/Python314/python.exe docs/validation/foundation-single-play-joined-20261030/run-checks.py --output D:/riz-build-cache-next/single-play-joined-replay-20261030
```

输出必须不存在。需要本机已安装的 Python、.NET 8 和 VS 2019 BuildTools `vcvars64.bat`；runner 内含实际绝对工具路径和 `/W4 /WX` 构建命令，不安装依赖、不访问账号、不启动 GUI。新的 fixed source archive 和全部前后/owned 输入清单会与原始 stdout/stderr 一同写入输出。

native helper 链接未修改的 Single producer 源并启用明确的 fixture publication 宏；生成的 SEC_IMAGE 只含自有 Invoke/Play 跳转，不含真实游戏代码。实际 production Play operation hook、production source journal/admission/death、生产首次准入、真实游戏对象及可见/可闻效果均未验证。本包没有重跑此前 39 项、65 托管/8 native 订阅或 42 模式 native 基底，也不把旧全树/相机结果当成这批源码的完整验收。未知 listener、engine setter/coroutine effects、真实非空全局恢复和完整 LOCAL 正向关闭仍需各自合同与证据。
