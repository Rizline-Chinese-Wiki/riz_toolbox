# 下一离线批次：Canvas 目标位置正式写入

本批只有实际源捕获和不可变目标计算。`CaptureSeekCanvasTargetSource` 不写 Transform，`CompleteSeekPermitted` 始终为 false。先完成本批新图回归，再开始下述 writer；不要重复相机、N1、纯 Canvas 投影或已经冻结的时钟/Play 批次。

1. 使用当前捕获的实际 `canvasTransforms` 槽和 `Plan.Rows` 配对。保留重复 move/Transform 槽、原始顺序和完整覆盖，不以 `CanvasMove.index` 排序或补造实例。共享 Transform 的目标值冲突必须在任何写入前拒绝。
2. 按现有 HoldGeometry 的精确 Unity 方法证据核对 `Transform.get_localPosition` / `set_localPosition(Vector3)`、当前实际对象身份和 Vector3 ABI；新增 Canvas 专用 writer 合同，不调用 `CanvasControl.SetPosition`、Update 或全局 Unity 更新来替代显式目标。
3. 准备阶段只读取并保留全部实际目标和原始 localPosition；与实际 Canvas 源绑定的独立根贯穿申请者 RunValue 退出。线程、重入、撤销和源过期在任何 getter/setter 前拒绝；setter 回调进入即记录责任。
4. 写前分别核当前源、目标和原始值，写后读回。原始值和恢复比较使用 Single 原始位；不能只比较浮点数值。若发生部分 native 变更、回调改写或读到第三状态，保留精确 owner 与 effects journal，不覆盖外部写入，不把采样一致解释成原子性或 ABA 排除。
5. 如果声明条件恢复，必须覆盖未进入、已经进入但异常、已经确认写入三种状态，逆操作前后均核源与责任。未知 free 不重试，所有已知清理单独尝试；不能用一次 Dispose 清除未证明的引擎副作用。
6. 先写真实 backend TDD，覆盖空数组、重复槽/别名冲突、非有限输入、元数据变化、部分 native、setter 回调撤销、原值 `-0` / NaN 位、第三状态与未知清理。构建和测试只用本批新产物，保留原失败日志。

验收时仍不得签完整 target environment 或完整 Seek。随后还需要 line/ring 几何、缺失 runtime 实例、renderer 延后 consumer/source、Hold 引擎退出和时钟事务组合；B01–B19 继续保持未整体完成。真实游戏验证继续后置，不安装、不启动游戏，不操作 UI 或账号。
