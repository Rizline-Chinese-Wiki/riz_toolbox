# 输入采集修订包：实体 F8 的 561 条会话

2026-10-20，所选诊断 session：`20261020T123129566Z-247ebfd445b848c3ac851cdda280c4ed`。对应包25检查项目、644/644，正式安装前17文件哈希一致。前一644包要求已裁剪的 `Application.get_unityVersion`，其失败保留在相邻目录；本包用固定 GameAssembly/globalgamemanagers 内容哈希校验该引擎构建。

从仓库根目录复核冻结日志与哈希：

```powershell
C:/Python314/python.exe docs/validation/runtime-input-capture-fix-20261020/inspect-runtime.py
```

脚本仅检查冻结资料，产生 `runtime-proof.json`，不构建、安装、控制或读取当前游戏。

用户随后确认按实体 F8。`session-physical-key/` 的561条导出保留上面的469条前缀不变；单独执行 `C:/Python314/python.exe docs/validation/runtime-input-capture-fix-20261020/inspect-physical-key.py` 得到 `physical-key-proof.json`。该次真实 native device1 更新时间推进，invocation147711、updateType1、updateCount147715 的 F8 边沿被 Host 帧排空一次，随后 `example.panel` 回调一次。前半零回调的导出不被改写。

## 证据与结论

- `build.log` / `build-summary.json`：25项目644项；Visual55覆盖资源恢复/销毁失败，Hooks27含新的 enum32 ABI，UI/input52含逐输入更新队列，Judgements17是内部身份注册表。主包包含后者内部实现，尚无完整公开 Note 服务。
- `package-manifest.json` / `installation-check.json`：17个实际安装文件，检查早于本次启动。与第一次644包的差异由 verifier 单列。
- `session-before-physical-key/`：469条连续完整事件及导出哈希。after-original `InputManager.InvokeAfterUpdateCallback@12FED70` 绑定、真实键盘采样及 SDK 动作注册成功。没有把“模块active”当成按键成功。
- 原游戏标题页对工具 F8 和 Return 没有可见响应，工具鼠标点击才进入 MainMenu。前半469条导出的原生键盘更新时间保持相同，且没有边沿或回调；随后实体 F8 独立证明真实动作链路。不能把工具的输入限制当成所有物理键盘失效，也不能用一次实体按键替代完整 B14 验收。
- 两个不同的实际 Translucent 材质克隆完成三项 Float 原生读回和赋值成功，分别 manual 与 scene-change Dispose 后两个 lease 失效，探针保留计数归零。实际原版菜单导航进入 LevelList。两次都观察到 Unity 延迟销毁时仍保留根和预算；该包没有最终物理清理完成 receipt，不能把探针计数当作 GPU 或所有原生资源计数。
- `launcher-last-launch.json` 记录本地启动器创建的进程；随后新宿主 session 实际初始化，才构成启动成功证据。启动器8项准备检查和继承环境问题的只读证据在前一目录，启动器源及用法见 `tools/RizToolbox.GameLauncher/`。

Evidence → Finding → Path：冻结的真实 after-input/注册事件 → 输入采样时机已从 Host 帧读取改为处理完原始输入后采集 → 有界复制值到 Host 帧再按上下文/焦点派发，真实动作仍须实体键盘对照。冻结材质创建/读回/释放及延迟告警 → 执行过新的异步清理保留路径 → 下一步补最终物理释放计数、完整故障/双Mod矩阵。

原生判定 helper、12来源身份接线、时间单位、TMP Color、动态128目标、自然完成、四基线/20轮及完整分发矩阵仍缺，不验收完整 B01–B19。游戏留在选曲页；该次未遗留探针覆盖租约。
