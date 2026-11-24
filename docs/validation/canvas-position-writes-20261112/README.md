# Canvas 实际位置写入：本批离线验收

正式 RunValue 已接入实际 Canvas Transform 的 localPosition 写入、完整读回与原始位条件恢复。新增 16 组真实 backend 检查通过，六组件与本批完整回归完成。**位置写入已经实现；完整 Canvas 缓存、Unity 回调效果闭合和完整 Seek 尚未完成。真实 Speed/Seek 保持关闭。** 所有新执行证据来自受控原生导出和自有夹具，没有安装或启动真实游戏。

## 结果

| 检查 | 结果 |
|---|---|
| 六组件 Release | 成功，0 警告 / 0 错误 |
| 判定默认全套 | 284/284，含本批 16 组位置检查 |
| Runtime ABI | 51/51 |
| 精确静态字段读取 | 6/6 |
| AudioContent 默认完整执行 | 1001/1001 |
| 当前构建输入 | 828 项，执行前后未变 |
| 测试依赖副本 | 24 份 DLL/PDB 与当前六组件对应产物一致 |
| 独立源审 | 69 个实现/依赖/测试文件哈希与验收图一致，无剩余必要阻断 |

实际命令、退出码和产物哈希在 [build-test-result.json](build-test-result.json)，警告数在 [evidence-index.json](evidence-index.json)。Runtime/AudioContent 测试构建分别有 1/27 条既有测试警告，无错误。这些结果只验收本批新图，不代表全解决方案、全 B01–B19 或实机已经完成。

## 现在具备的行为

- `RunValue.PrepareSeekCanvasPositions` 从同一实际 Canvas 来源与数值投影生成完整槽映射；保留数组顺序与重复槽，不依据 CanvasMove.index 重排。相同 Transform 的目标原始位冲突在任何 setter 前拒绝；一致别名只写一次。
- 准备阶段只读取，独立保留完整来源和物理根。申请者 RunValue 正常退出后已有命令仍有效；构造序号把后续取消与已有 owner 分开。
- getter/setter 使用独立生产 profile，精确校验声明、MethodInfo 和当前 Vector3 / Single ABI，热缓存不能掩盖原生宽度、字段或 byref 漂移。官方 wrapper 各 80 字节，getter/setter 都有返回后的准入检查。
- 每次已进入的 setter 记入不可变 effects journal；原值、目标、方向、是否返回和读回结果分别记录。准备与验证覆盖完整槽，恢复只处理已经进入的唯一 Transform，保留 NaN payload 与 -0 原始位。
- 遇到第三态或未尝试槽外改永久拒绝后续恢复，不覆写外部值制造回滚。worker、同步重入、回调撤销在后续 native 访问前拒绝；native 异常与部分写入保留原责任。
- setter 一经进入，Unity 外部效果与根责任保持未闭合，即使位置读回或选定值恢复成功。`Dispose` 不会抹掉这类责任；journal 明确 `CompleteSeekPermitted=false`。
- getter 的临时 receiver/result root 释放失败，即使尚未进入任何 setter，也会保留精确公共 commands owner、原 publication owner 和 cleanup failure。未知 free 不重试，不能靠后续无操作伪造清理成功。

## 证据与调用路径

| Evidence | 内容 | Finding |
|---|---|---|
| E-001：[完整结果](build-test-result.json)、[冻结输入](inputs-before.json) | 本批八步实际子进程均自然退出 0，测试计数与依赖匹配 | F-001：位置 writer 在受控原生导出下通过正式 backend 离线验收 |
| E-002：[独立源审](independent-source-review.json)、[复核状态](source-review-status.json) | exact ABI、构造生命周期、条件恢复与未知责任，当前 69 文件哈希 | F-002：静态复核覆盖本次验收版本；不是实机证据 |
| E-003：[文件核验](position-profile-verification.json)、[静态依据](position-static-evidence.json) | 两完整 wrapper / 160 字节、两入口、三向量字段、4 个指令锚点 | F-003：固定版本方法与 ABI 合同成立；resolved internal-call 效果仍未知 |
| E-004：[首轮构建失败](iteration-r1-result.json)、[首次专项通过](iteration-r2-result.json) | 首轮仅测试局部变量重名 CS0136；改名后16/16 | F-004：失败保留，没有修改或削弱测试断言以通过 |

P-001（callflow）：实际完整源与 Plan → 按槽建立目标与别名门 → 独立 baseline raw bits → 当前 metadata 与 source 校验 → 最后 managed guard / 单次 attempt → 官方 setter → 返回准入 → 实际读回 → 独立 journal / 条件值恢复。F-001 不延伸为 Unity 回调已闭合或完整事务已可开放。

本包依据用户授权的离线 SDK 任务，只修改项目源码与证据，不修改游戏、账号、代理、永久环境、旧 ZIP 或 Git 提交。静态分析范围与 timeline 见 `work/seek-canvas-position-native-20261112/`。未测试 257 个同时未闭合 owner；256 上限只作源码复核，不宣称这项已运行。

## 复现与下一步

在仓库根运行；输出须为不存在的新目录，脚本拒绝覆盖旧结果：

```powershell
C:/Python314/python.exe -B work/canvas-position-validation-20261112/validate_current.py --output D:/riz-build-cache-next/canvas-position-20261112/manual-recheck
```

窄范围加 `--position-only`，执行原 16 组，不能代替完整矩阵。脚本只在子进程设置并核哈希的自有 native 夹具路径，不修改永久环境。实际本批构建目录：`D:\riz-build-cache-next\canvas-position-20261112\root-r1\final-r1`。

下一步见 [Canvas 缓存同步合同](next-canvas-cache-contract.md)：floorPositions / floorSpeeds / xPositions 和 nowTime / speed 仍需正式写入，再继续 line/ring、缺失 runtime、renderer consumer/source、Hold 引擎退出及完整时钟事务。
