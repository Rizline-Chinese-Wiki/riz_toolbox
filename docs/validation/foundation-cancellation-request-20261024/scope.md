# 本批范围与证据边界

2026-10-24。用户要求继续完善底层，游戏实测稍后。本批完成生产源码接线、离线构建、自有原生/CLR 子进程验证及归档；未安装新包、启动游戏、操作 UI 或调用游戏/IL2CPP 函数。[构建终态](build-run.json)明确 root 是唯一构建者，定向 `69797` 和完整 `45961` 均以 0 结束。[汇总](build-summary.json)为 25 套、1575/1575；简明进展见 [README.md](README.md)。

## 本批实际做到了哪里

生产源码新增六个固定 Stop 声明及其 metadata/ABI resolver、observer 和注册路径：StopCoroutine(IEnumerator)、StopCoroutine(Coroutine)、StopAllCoroutines、StopCoroutineManaged、StopCoroutineFromEnumeratorManaged、StopCoroutine(string)。公共 wrapper 会内联 icall 分发，不能只观察两个 internal 入口；具体固定文件依据保存在 [research.md](research.md) 和 [stop-abi.json](stop-abi.json)。本批未在真实游戏 metadata 上执行 resolver。

取消账本使用固定 owner/receiver provider 表，只读标量身份。owned 请求先登记上下文再读取 provider；foreign 请求不读取，worker fault 只查看不可变 receiver 表和标量。StopAll 保存同 receiver 的 current 集合；handle 仅在实际返回并确认保活后匹配，owner/槽/代次必须仍一致。正常退休的初始化旧记录跳过，仍发布的 Unknown 记录阻断。匹配、Stop 返回和本地请求 idle 都不产生引擎取消、自然完成或释放权限。

同一 launch 的原子 Pending 在 provider 读取前发布，普通已知请求不因此自动变成 Unknown。coordinator、Ready、三类 tracker 的结束/回收准入和三个后端的 detach/逐根释放边界均重核；活动统计仍记录真实 InFlight。请求在读操作中到达时暂停推进；在部分撤销/释放后到达则保留责任并停止后续释放。22 组新回归直接调用生产 ledger、状态推进/屏障和 tracker；provider、native 调用替身与根动作受控，不反射伪造 Ready，也不执行真实 IL2CPP free。

[Dobby 原生证据](operation-dobby-evidence.json)在自有三类目标上真实 Prepare/Configure/Commit：4 个 Prepare、3 个 Commit、3 次重复 Commit 拒绝；1 个 Configure 被拒后仍保留未发布 prepared 目标。1165 检查、69 次受观察 original 加 1 次未发布原目标调用通过。它没有注入任意部分机器码写入失败，也未验证 native admission gate 或并发首发。

[native 到账本联合证据](operation-ledger-evidence.json)使用实际 helper、生产 bridge/broker、实际 mutation 和取消请求 ledger：5 独立进程、544 检查、15 original；每模式 3 mutation 和 2 个取消请求，所有上下文完成并 Dispose。取消输入来自真实机器帧，provider 身份仍受控；初始化时账本 idle 是受控窗口，随后立即切到实际 binding 活动。没有执行生产对象后端，不能将受控 idle 叫作真实安装空闲或生产 Ready。

## 本批没有证明什么

| 事项 | 明确边界 |
| --- | --- |
| 实际引擎 Stop API 与取消完成 | 联测 original 是自有函数；`productionStopBindingVerified=false`、`engineCancellationExecuted=false`。账本 `EngineCancelled=false`，请求完成不等于 scheduler 已移除协程 |
| 真实 tracker 和 IL2CPP/GC 联合 | 联测 `actualTrackerProviderUsed=false`、`identityProviderControlled=true`、`productionObjectBackendExecuted=false`、`actualIl2CppMetadataResolved=false`；真实根采用/释放仍待游戏端验证 |
| 18 个新入口的生产首次发布 | 12 mutation 加 6 Stop 入口仅完成源码接线与有限自有验证；真实 metadata、发布窗口、并发安全和部分发布失败责任尚未验收。旧九写 listener receipt 不覆盖这 18 个入口 |
| 一条完整游戏调用链 | Dobby fixture 与 CLR 账本 fixture 分开执行；后者不是已 detour 的真实游戏目标，不使用生产 FirstInstall receipt |
| 异常完整透明性 | 原生 code/payload、typed C++ 与有限清理通过；`exceptionFlagsTransparencyVerified=false`，没有全量原始 Record/Context 和所有合法 PC 展开证明 |
| 全部变化和资源退休 | 动态 leaf、序列化 alias、generic/shared、直接字段写、provider 转交和 future producer 尚未全封闭；不改 MutationClosed/Pending 的保守结论，不新增整项验收勾选 |

## 冻结和操作边界

[源码归档](source-archive-check.json)包含 534 项声明输入，与[构建前后清单](build-input-integrity-check.json)和[当前源码](source-integrity-check.json)一致；[暂存包归档](package-archive-check.json)包含 22 项文件，逐项匹配且 `installed=false`。相比上一批新增 12、修改 11、删除 0，见[差异原件](changes-from-listener-mutation.json)。归档不包括外部缓存依赖、工具链、干净环境复现或完整 B19 发布矩阵。

[旧安装](previous-installation-check.json)的 18 项文件及旧归档 326 项输入保持匹配；[游戏原件](original-game-check.json)中 Rizline.exe、GameAssembly.dll、global-metadata.dat 的哈希保持匹配，GameAssembly SHA256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。runner 只创建并按句柄管理自有子进程，有限等待并保留 stdout/stderr，没有枚举终止游戏进程。本文只读正式原件并写说明，不重新构建或修改冻结源码。
