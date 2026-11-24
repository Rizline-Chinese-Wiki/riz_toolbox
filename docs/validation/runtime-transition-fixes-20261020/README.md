# 666 后续修复与新包验证

本目录记录三个子代理并行实现、主代理串行整合的四项修复。旧 666 包的成功与失败会话仍保留在 `../runtime-visual-scope-cleanup-20261020/`，不把新源码修复反写为旧包已通过。

## 修复范围

- **长按身份**：挑战尾部 VFX 路径会覆盖 RSI，改为在真实 `CheckHold.MoveNext` 入口保留协程身份，退出后释放。Boolean ABI 原样传递一字节返回值；重入、撤回和错误线程不能借用外层身份。定位见 `hold-tail-register-proof.txt`，专项 `hold-identity-tests.log` 为 35/35。
- **结算来源**：入口固定所属局，原始 `ControllerOver` 返回结果独立保根并精确消费；取消同谱猜测。旧谓词在新增回归中 73/77，新谓词 77/77，Hook 43/43、RunModification 17/17。见 `result-origin-summary.md`。
- **视觉过渡**：发现过程中产生 cleanup pending 时本帧撤回服务可用性；尚未失效的视图按暂时不可用处理，写操作拒绝，避免正常场景过渡误停插件。专项红/绿为 56/57、58/58，日志仍位于旧验证目录，记录的是后续源码测试。
- **目标代次**：所有 registry 共享线程安全、单调且不复用的代次；跨 registry 旧句柄不能读取或修改新绑定，溢出拒绝注册且不转移资源所有权。新增跨 registry、并行唯一性、溢出边界三项行为回归。动态目标容量和扫描饥饿问题另见 `visual-target-admission-audit.md`，本轮未改其策略。

## 验证文件

2026-10-21 00:00（UTC+8）完成：**691/691、25 个检查项目通过**，原生 fixture 分别 31,261 / 61,358 项通过，inFlight 均为 0。`build.log` 是完整构建输出；`summarize-build.py` 已实际核对并生成 `build-summary.json`、`source-manifest.json`、`package-manifest.json` 和 `native-build/`。正式安装后同脚本 `--installed` 已核对 **18 个文件哈希一致、247 项源码输入未变**。Scope.Tests/Program.cs:92 有一条可空引用编译警告，保留在摘要中。

用户因电量不足要求加速并行。游戏已正常退出，本轮未重新启动；新包的挑战尾判定、真实结果来源及视觉切场景仍需独立实机验收。之前一次观赏模式返回选曲没有正常完成结果，不能用其补齐正常结算。

`runtime-module-proof-restoration.json` 记录将本次测试开启的临时探针配置恢复为原先不存在的状态；恢复前内容另存。插件启用意愿保持不变。

完整 B01–B19 验收仍以原 SPEC 为准，本轮不新增整项勾选。
