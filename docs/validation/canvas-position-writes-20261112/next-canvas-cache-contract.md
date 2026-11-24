# 下一离线批次：Canvas 缓存与目标 basis 完整同步

位置命令只处理实际 `canvasTransforms` 的 `localPosition`。它还没有更新 CanvasControl 的计算缓存，不能把成功读回位置当成目标 Canvas 环境已完整重建。开始前以本批完整新图和独立源码复核为准。

1. 用当前固定 GetInformations / SetPosition / Update 全 body 证据确认 `floorPositions@0x38`、`floorSpeeds@0x40`、`xPositions@0x48` 三个实际 Single[]，以及 `nowTime@0x50` / `speed@0x54` 两个 Single。不调用这三个 producer 来代替显式目标写入。
2. 从同一 `NativeSeekCanvasTargetCapture.Plan` 生成每实际槽的 FloorPosition / FloorSpeed / XPosition，时间与速率来自 Request。保留完整实际数组槽、重复身份与空图语义；数组容量、length、header、byteLength、元素 ABI 和 source 当前身份必须实际验证。
3. 准备阶段保留独立实际源和三个缓存数组，读取原始 Single 位；目标必须有限，原值允许 NaN / -0。给出有界、精确 typed、pin 根责任先登记的数组槽写接口，不能把未验证的地址或 dump 偏移直接作为可写凭证。
4. 多缓存引用同一数组时先检查每物理槽目标一致性；冲突在任何写入前拒绝。写入前核当前原值与源，进入即记录 attempt，写后完整读回。支持部分写入的条件恢复；第三态与未尝试槽外改保持 sticky，不能覆盖它们制造回滚。
5. 同步时序必须与实际依赖匹配：完整缓存、time/speed、位置命令的条件和责任分别记录。Unity setter 已进入的外部效果仍由原位置命令 owner 持有，缓存回滚不能清除它；完整 transaction 尚未授权开放。
6. TDD 覆盖实际 backend、每完整槽、多别名、空数组、非有限值、ABI/metadata漂移、pin/字段回调撤销、partial native、raw-bit恢复、第三态、worker/reentry、未知free和精确owner；所有验收使用本批新源码产物。

之后继续 line/ring basis、缺失 runtime 实例、renderer 延后 consumer/source、Hold 引擎退出与完整时钟事务。B01–B19 未整体完成；不安装/启动真实游戏，不操作 UI/账号，不重跑相机、N1、退休或已冻结的 Play/时钟批次。
