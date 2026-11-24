# LOCAL 会话与结果隔离基础包

2026-10-22，UTC+8。**895/895、25 套检查通过，已安装。** Runtime92、Hooks61、AudioContent131；原生 helper / Dobby fixture 为 31,261 / 61,358。本轮完整构建为增量构建，日志无编译诊断；前置 AudioContent 定向编译的 14 类 FakeNative CS0649 警告并未通过本批源码修改消除，不据此声称干净重建无警告。

四路协作交付并由主代理串行集成：

| 交付 | 已实现与验证 | 尚未证明 |
|---|---|---|
| 精确结果前置门 | 仅固定构建 `LevelOverControl.Start@3D0B20` 的内部权限；普通入口 original 一次，LOCAL claim 后零次，handler/分类故障不回退；15 组策略、3 组 Cdecl calli 回归；修复先接入的不同 MethodInfo alias 绕过检查 | 当前未接游戏模块；calli 只证明反向 P/Invoke 入口，不代表 Dobby 或 LOCAL 实机 |
| 宿主会话层 | Prepared/PendingCommit/Committed/ResultCapturing/ResultReady/Returning/CleanupUnknown/Closed；精确实例快照、一次结果 claim/cache、进程级唯一与 32 会话上限，16 组回归 | 没有原生 clone/结果调用/场景结束验证器；独立 registry 的 classifier 不自动合并，宿主不得丢弃仍负责旧局的 registry |
| 原版本资源续持 | `LocalPlayRequest.Retain` 保留已加载 song/chart revision；provider/catalog/content 撤回后仍可派生宿主请求，同 ID 更新不会替换旧请求；32 个 prepared 请求含 fork，5 组回归 | 这不是 native 重试或新 Chart/controller 创建 |
| 借用参数工厂 | 精确五参 InsideLevelParams ctor、引用字段/类型/接口/Unity 活性检查；只拥有新参数 root，输入始终借用；观察 speed/offset 原位值及 bit，不写假默认值，10 组准入/清理回归 | 正向 native ctor、五字段读回和默认标量尚无游戏证据；不构成完整 owned play bundle |

会话清理有两种明确边界：结果捕获失败且尚无消费者结束证明时，保留 roots 与精确保护；外部验证器已经证明旧 Unity controller/result screen 销毁、所有旧回调/场景/全局引用结束后，先撤下旧地址分类，再释放 roots。此后的未知 free 继续占用资源责任与配额，不把可能复用的旧地址当 LOCAL 保护身份。实现审查在[结果隔离清单](../local-result-isolation-20261022/REVIEW.md)。

## 可复核证据

- [build.log](build.log) / [build-summary.json](build-summary.json)：最终构建、25 套摘要和两个原生 fixture。日志 SHA-256 `1d230c669e8fe669ed56b3341e5581871992c2f1f287484c110cbd7c1caa5f11`。
- [package-manifest.json](package-manifest.json) / [installation-check.json](installation-check.json)：18 项实际安装哈希一致。
- [source-manifest.json](source-manifest.json) / [source-integrity-check.json](source-integrity-check.json)：290 项源码输入在本轮实测收尾时未变。
- [original-game-before.json](original-game-before.json) / [original-game-after.json](original-game-after.json)：原始 exe、GameAssembly 与 global-metadata 三文件不变。
- [启动实测](../runtime-local-session-smoke-20261022/README.md)：193 条连续事件、0 Error，正常进入菜单并退出；明确仅是宿主 smoke。

复现构建与冻结：

```powershell
./scripts/Build.ps1 -NativeOutputDirectory 'D:\RizToolbox-build-cache\local-session-20261022' *> docs/validation/foundation-local-session-20261022/build.log
python docs/validation/foundation-local-session-20261022/summarize-build.py
./scripts/Install.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Rizline'
python docs/validation/foundation-local-session-20261022/summarize-build.py --installed
```

当前 RequestPlay 仍返回 OperationUnsupported。下一步验证参数工厂的真实构造/GC/释放，再将完整资源 bundle、克隆前身份、结果前置门、自有结果/返回和新图重试接成同一个宿主事务。完整 B01–B19 保持进行中，仅 B03/B18a 已勾选。
