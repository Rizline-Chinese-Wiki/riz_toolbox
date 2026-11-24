# Camera integration 本轮范围

- auth：用户授权继续 riz_toolbox 功能开发；本机为自有测试环境，离线构建/测试。不安装、不启动、不操作真实游戏或 GUI。
- in_scope：把已冻结的相机标量原型接入生产 `RizlineAdapter` 模块注册；把 `RizToolbox.GameCamera.Tests` 加入 `RizToolbox.sln` 和 `scripts/Build.ps1` 托管矩阵；运行解决方案构建、相机专项和整树托管回归。
- out_of_scope：真实游戏执行、安装、渲染次序测量、可见效果、输入投影/玩法影响、相机 hook、生产首次准入和性能优化。
- network_profile：离线本地源码、.NET 构建与受控 native-export 测试；不加载真实 GameAssembly/Unity。
- source_identity：原型静态证据仍引用固定构建 GameAssembly SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；本批只做源码集成和离线回归，未重新读取游戏映像。
- 集成点：`src/RizToolbox.Adapter/RizlineAdapter.cs` 在 `GameAppearanceModule` 之后注册 `GameCameraModule`，共享 `SemanticTargetsModule`、`GameAssemblySha256`、`GameLifecycleSnapshot()` 与 `MatchesCurrentGameplayRun`。
- 验收边界：解决方案 0 error；相机专项 33/33；整树 27 套托管回归 2369/2369。相机能力保持 `experimental` / `modifiesGameplay`；`CameraControl.Update` 每帧覆写 X，实际 update/render 先后、持续可见、输入投影和停止恢复仍待实机验收。
